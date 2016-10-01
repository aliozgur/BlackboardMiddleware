using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Bilgi.Sis.BbMiddleware.Model;
using Quartz;
using Common.Logging;

namespace Bilgi.Sis.BbMiddleware
{
    [DisallowConcurrentExecution]
    public class DataSetStatusJob : IJob
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(DataSetStatusJob));
        private DataConfig _config;
        private int _maxFilesToProcess = 5;
        private volatile int _isRunning;


        public void Execute(IJobExecutionContext context)
        {
            if (!LoadConfig(context))
                return;

            if (Interlocked.Exchange(ref _isRunning, 1) == 1)
            {
                _log.Info("[DATASET STATUS CHECK] SOMEONE ELSE IS IN! I'm just leaving the data status check...");
                return;
            }

            try
            {
                DoExecute();
            }
            finally
            {
                _isRunning = 0;
            }

        }

        private bool LoadConfig(IJobExecutionContext context)
        {
            var dataMap = context.JobDetail.JobDataMap;

            var configFilePath = dataMap.GetString("configFilePath");
            if (String.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
            {
                _log.Fatal($"Configuration file not specified or does not exist '{configFilePath}'");
                return false;
            }

            _config = DataConfig.LoadFromFile(configFilePath);

            if (_config == null)
            {
                _log.Fatal($"Configuration file can not be loaded '{configFilePath}'");
                return false;
            }

            if (_config.DataSetStatusMaxFilesToProcess > 0)
                _maxFilesToProcess = _config.DataSetStatusMaxFilesToProcess;

            return true;
        }

        private void DoExecute()
        {
            var queue = GetFiles();
            if (queue.Count == 0)
            {
                _log.Info("WILL REST | No log files to process");
                return;
            }

            ProcessLogFiles(queue);
        }

        private Queue<DataFile> GetFiles()
        {
            Queue<DataFile> result = new Queue<DataFile>();

            var logFolderPath = _config.LogFolderPath;
            if (!Directory.Exists(logFolderPath))
                return result;

            var partsSeperator = _config.FilePartsSeperator;
            if (String.IsNullOrWhiteSpace(partsSeperator))
                return result;

            var di = new DirectoryInfo(logFolderPath);
            var filesInBatch = di.GetFiles("*.*", SearchOption.AllDirectories)
                .OrderByDescending(fi => fi.CreationTimeUtc)
                .Take(_maxFilesToProcess)
                .Select(fi => fi);

            var endpoints = _config.Endpoints.OrderBy(ep => ep.Order).Select(ep => ep).ToList();
            endpoints.ForEach(ep =>
            {
                var file =
                    filesInBatch.Where(fi => fi.Name.EndsWith($"{partsSeperator}{ep.FileName}"))
                        .Select(fi => new { FullName = fi.FullName, Name = fi.Name, })
                        .FirstOrDefault();

                if (file == null || String.IsNullOrWhiteSpace(file.FullName))
                    return;


                _log.Info($"Add log file to data set status queue {file.FullName}");
                result.Enqueue(new DataFile { FilePath = file.FullName, FileName = file.Name, Endpoint = ep });
            });

            return result;

        }

        private void ProcessLogFiles(Queue<DataFile> queue)
        {
            while (queue.Count > 0)
            {
                var dataFile = queue.Dequeue();
                try
                {
                    var statusResult = GetDataSetStatus(dataFile);
                    ProcessDataSetStatus(statusResult);
                }
                catch (Exception ex)
                {
                    _log.Error($"Can not process  data set status '{dataFile.FilePath}'", ex);
                }
                
            }
        }

        private DataFileHttpResult GetDataSetStatus(DataFile dataFile)
        {

            if (!File.Exists(dataFile.FilePath))
                return null;

            _log.Debug($"Will get log status {dataFile.FilePath}");
            var logFileContent = File.ReadAllText(dataFile.FilePath, Encoding.UTF8);
            var guid = Regex.Match(logFileContent, "[0-9a-f]{32}", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (!guid.Success)
                return null;


            using (var client = new HttpClient())
            {

                var username = dataFile.Endpoint.Username;
                var password = dataFile.Endpoint.Password;
                var rootUrl = _config.DataSetStatusEndpointUrl;
                if (String.IsNullOrWhiteSpace(rootUrl))
                    return null;

                if (!rootUrl.EndsWith("/"))
                    rootUrl += "/";

                var url = rootUrl + guid.Value;
                var uri = new Uri(url);

                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Add("Accept", "text/xml");
 
                var authBasic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authBasic);
                var responseMessage = client.SendAsync(request).Result;


                var result = new DataFileHttpResult
                {
                    StatusCode = responseMessage.StatusCode,
                    Content = responseMessage.Content.ReadAsStringAsync().Result,
                    Url = uri,
                    Endpoint = dataFile.Endpoint,
                    FilePath = dataFile.FilePath,
                    FileName = dataFile.FileName
                };
                _log.Info($"Get data set status http result [{result.StatusCode}] : {result.Url}");
                return result;
            }
        }

        private void ProcessDataSetStatus(DataFileHttpResult statusResult)
        {
            _log.Debug($"Will process log status {statusResult.FilePath}");

            if (!Directory.Exists(_config.DataSetStatus_FolderPath))
                Directory.CreateDirectory(_config.DataSetStatus_FolderPath);

            if (!Directory.Exists(statusResult.Endpoint.DataSetStatus_FolderPath))
                Directory.CreateDirectory(statusResult.Endpoint.DataSetStatus_FolderPath);

            var statusFolderPath = statusResult.Endpoint.DataSetStatus_FolderPath;
            var fileName = statusResult.FileName;

            if (statusResult.StatusCode != System.Net.HttpStatusCode.OK)
            {
                if (!statusResult.Endpoint.DataConfig.DataSetStatusKeepNotOkResponses)
                {
                    File.Delete(statusResult.FilePath);
                    _log.Info($"Delete log file. Data set status can not be queried. Http Status Code is '{statusResult.StatusCode}' for '{statusResult.FilePath}'");
                }
                else
                {
                    File.Move(statusResult.FilePath, Path.Combine(statusFolderPath, fileName));
                    _log.Info($"Archive log file. Data set status can not be queried. Http Status Code is '{statusResult.StatusCode}' for '{statusResult.FilePath}'");
                }
                return;
            }

            XElement root = XElement.Parse(statusResult.Content);
            var isCompleted = root.Elements("queuedCount").Any(el => (string)el.Value == "0");
            var hasError = root.Elements("errorCount").Any(el => (string)el.Value != "0");

            if (isCompleted)
            {
                if (hasError)
                {
                    // Delete log file
                    File.Delete(statusResult.FilePath);
                    _log.Info($"Delete log file. Data set status reported error for '{statusResult.FilePath}'");

                    // Create new status file
                    var statusFilePath = Path.Combine(statusFolderPath, fileName);
                    File.WriteAllText(statusFilePath, statusResult.Content);
                    _log.Info($"Write data set status. Data set status reported error for '{statusFilePath}'");
                    _log.Fatal($"Data set has errors. Please inspect details '{statusFilePath}'");
                }
                else // Delete log file, no errors reported by Bb
                {
                    _log.Info($"Delete log file. Data set status reported success with no errors for '{statusResult.FilePath}'");
                    File.Delete(statusResult.FilePath);
                }
            }
        }
    }
}
