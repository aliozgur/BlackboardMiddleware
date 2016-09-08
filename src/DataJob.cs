using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bilgi.Sis.BbMiddleware.Model;
using Common.Logging;
using Quartz;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Bilgi.Sis.BbMiddleware
{

    [DisallowConcurrentExecution]
    public class DataJob : IJob
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(DataJob));
        private DataConfig _config;

        public void Execute(IJobExecutionContext context)
        {
            if (!LoadConfig(context))
                return;

            DoExecute();
        }

        private bool LoadConfig(IJobExecutionContext context)
        {
            var configFilePath = context.JobDetail.JobDataMap.GetString("configFilePath");
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

            return true;
        }

        private void DoExecute()
        {
            var batchId = GetNextBatchIdInQueue();
            if (String.IsNullOrWhiteSpace(batchId))
            {
                _log.Info("WILL REST | Get next batch id returned empty result");
                return;
            }

            _log.Info($"Processing batch with id '{batchId}'");

            var queue = GetFilesInBatch(batchId);
            if (queue.Count == 0)
            {
                _log.Info("WILL REST | No data files to process");
                return;

            }
            UploadFiles(queue);
        }

        private string GetNextBatchIdInQueue()
        {
            var partsSeperator = _config.FilePartsSeperator;
            if (String.IsNullOrWhiteSpace(partsSeperator))
                return String.Empty;

            if (!Directory.Exists(_config.QueueFolderPath))
            {
                _log.Fatal($"Queue directory not found {_config.QueueFolderPath}");
                return String.Empty;
            }
            var queueDir = new DirectoryInfo(_config.QueueFolderPath);
            var firstFile = queueDir
                .GetFiles()
                .Where(
                    fi =>
                        fi.Name.Split(new string[1] { partsSeperator }, StringSplitOptions.RemoveEmptyEntries).Length == 2
                        && _config.Endpoints.Any(ep => fi.Name.ToLowerInvariant().EndsWith($"{partsSeperator}{ep.FileName.ToLowerInvariant()}")))
                .OrderBy(fi => fi.Name).FirstOrDefault();

            if (firstFile == null)
                return String.Empty;

            var fileNameParts = firstFile.Name.Split(new string[1] { partsSeperator },
                StringSplitOptions.RemoveEmptyEntries);
            return fileNameParts[0];
        }

        private Queue<DataFile> GetFilesInBatch(string batchId)
        {
            var result = new Queue<DataFile>();

            var partsSeperator = _config.FilePartsSeperator;
            if (String.IsNullOrWhiteSpace(partsSeperator))
                return result;

            var batchPrefix = $"{batchId}{partsSeperator}".ToLowerInvariant();
            var queueDirPath = _config.QueueFolderPath;
            var queueDir = new DirectoryInfo(queueDirPath);

            var filesInBatch = queueDir.GetFiles($"{batchPrefix}*").Select(fi => fi);

            var endpoints = _config.Endpoints.OrderBy(ep => ep.Order).Select(ep => ep).ToList();
            endpoints.ForEach(ep =>
            {
                var file =
                    filesInBatch.Where(fi => fi.Name.ToLowerInvariant() == $"{batchPrefix}{ep.FileName.ToLowerInvariant()}")
                        .Select(fi => new { FullName = fi.FullName, Name = fi.Name, })
                        .FirstOrDefault();

                if (file == null || String.IsNullOrWhiteSpace(file.FullName))
                    return;


                _log.Info($"Add file to queue {file.FullName}");
                result.Enqueue(new DataFile { FilePath = file.FullName, FileName = file.Name, Endpoint = ep });
            });

            return result;


        }

        private void UploadFiles(Queue<DataFile> queue)
        {
            while (queue.Count > 0)
            {
                var dataFile = queue.Dequeue();
                try
                {
                    _log.Info($"PROCESS FILE {dataFile.Endpoint.Name}: {dataFile.FilePath}");
                    var uploadResult = UploadFile(dataFile);
                    if (uploadResult.StatusCode != HttpStatusCode.OK)
                    {

                        _log.Fatal($"Upload rejectd by endpoint. Will halt processing for current batch. Status Code = {uploadResult.StatusCode}");
                        break;
                    }

                    PostProcessUploadedFile(uploadResult);
                }
                catch (Exception ex)
                {
                    _log.Fatal("Upload Error. Will halt processing for current batch", ex);
                    break;
                }
            }
        }

        private DataFileHttpResult UploadFile(DataFile dataFile)
        {

            using (var client = new HttpClient())
            {

                var username = dataFile.Endpoint.Username;
                var password = dataFile.Endpoint.Password;
                var url = dataFile.Endpoint.Url;
                var uri = new Uri(url);

                var request = new HttpRequestMessage(HttpMethod.Post, uri);


                if (dataFile.Endpoint.DataConfig.UploadBinary)
                    request.Content = new ByteArrayContent(File.ReadAllBytes(dataFile.FilePath));
                else
                {
                    var utf8 = new UTF8Encoding(true);
                    var fileContent = File.ReadAllText(dataFile.FilePath, utf8);
                    //if (!Directory.Exists("C:\\tmp")) ;
                    //    Directory.CreateDirectory("C:\\tmp");

                    //File.WriteAllText(Path.Combine("C:\\tmp", dataFile.FileName),fileContent,utf8);
                    request.Content = new StringContent(fileContent, utf8, "text/plain");

                }

                var authBasic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authBasic);
                request.Headers.Add("Accept", "*/*");
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

                _log.Info($"Post data set http result[{result.StatusCode}] : {result.Url}");
                return result;
            }
        }

        private void PostProcessUploadedFile(DataFileHttpResult uploadResult)
        {
            if (uploadResult.StatusCode != HttpStatusCode.OK)
                return;
            PostProcess_Processed(uploadResult);
            PostProcess_Logs(uploadResult);
        }

        private void PostProcess_Processed(DataFileHttpResult uploadResult)
        {
            if (uploadResult.Endpoint.ProcessedBackupEnabled ?? false)
            {
                // Move file to processed folder
                var destinationDir = uploadResult.Endpoint.ProcessedFolderPath;

                if (!Directory.Exists(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                var destinationPath = Path.Combine(destinationDir, uploadResult.FileName);

                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                File.Move(uploadResult.FilePath, destinationPath);
            }
            else
            {
                // Delete data file
                File.Delete(uploadResult.FilePath);
            }

        }

        private void PostProcess_Logs(DataFileHttpResult uploadResult)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

            if (uploadResult.Endpoint.LogEnabled ?? false)
            {
                // Create Log file
                var destinationDir = uploadResult.Endpoint.LogFolderPath;

                if (!Directory.Exists(destinationDir))
                    Directory.CreateDirectory(destinationDir);


                var guid = Regex.Match(uploadResult.Content, "[0-9a-f]{32}", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                var logContent = uploadResult.Content;//guid.Success ? guid.Value : ;
                var destinationPath = Path.Combine(destinationDir, $"Log_{timestamp}_{uploadResult.FileName}");
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                File.WriteAllText(destinationPath, logContent);
            }

        }
    }
}
