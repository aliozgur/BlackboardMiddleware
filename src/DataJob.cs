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
using System.Threading;

namespace Bilgi.Sis.BbMiddleware
{

    [DisallowConcurrentExecution]
    public class DataJob : IJob
    {
        private volatile int _isRunning;
        private readonly ILog _log = LogManager.GetLogger(typeof(DataJob));
        private DataConfig _config;


        public void Execute(IJobExecutionContext context)
        {
            if (!LoadConfig(context))
                return;

            if (Interlocked.Exchange(ref _isRunning, 1) == 1)
            {
                _log.Info("[UPLOAD DATA] SOMEONE ELSE IS IN! I'm just leaving the data upload...");
                return;
            }

            try
            {
                
                if (IsSyncStatusCheckOk())
                {
                    DoExecute();
                }
                else
                {
                    _log.Info("WILL REST | Sync batch has something in progress or check has failed.");
                }
            }
            finally
            {
                _isRunning = 0;
            }

        }


        private bool IsSyncStatusCheckOk()
        {
            return true;
            //TODO : Add your own sync status check implementation code here

            //if (!_config.SyncStatusCheck)
            //    return true;

            //try
            //{
            //    if (String.IsNullOrWhiteSpace(_config.Target))
            //    {
            //        _log.Error("ERROR : Sync batch status check target is null or empty in config file. Will halt processing for this turn.");
            //        return false;
            //    }

            //    return !BilgiSisDbContext.HasDataInProgress(_config.Target);
            //}
            //catch (Exception ex)
            //{
            //    _log.Error("ERROR: Can not check Sync batch status. Will halt processing for this turn.", ex);
            //    return false;
            //}
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
            var movedFiles = MoveQueueFilesToInProc();
            if (!movedFiles)
            {
                _log.Info("WILL REST | Can not move files to in process folder");
            }

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

        private bool MoveQueueFilesToInProc()
        {
            _log.Info("WILL MOVE FILES to In Process folder");
            var queueFolderPath = _config.QueueFolderPath;
            var inProcFolderPath = _config.InProcFolderPath;

            if (!Directory.Exists(queueFolderPath))
            {
                _log.Fatal($"Queue directory not found {queueFolderPath}");
                return false;
            }


            if (!Directory.Exists(inProcFolderPath))
                Directory.CreateDirectory(inProcFolderPath);

            var queueDirInfo = new DirectoryInfo(queueFolderPath);


            _log.Info("Getting files in queue for moving to In Process folder");
            var files = queueDirInfo.GetFiles();
            try
            {
                _log.Info("START MOVE FILES to In Process folder");
                foreach (var fi in files)
                {
                    _log.Debug($"Moving file to In Process folder : {fi.FullName}");
                    File.Move(fi.FullName, Path.Combine(inProcFolderPath, fi.Name));
                }
            }
            catch (Exception ex)
            {
                _log.Fatal($"Can not move files to in process folder.", ex);
                return false;
            }

            return true;
        }

        private string GetNextBatchIdInQueue()
        {
            var partsSeperator = _config.FilePartsSeperator;
            if (String.IsNullOrWhiteSpace(partsSeperator))
                return String.Empty;

            if (!Directory.Exists(_config.InProcFolderPath))
            {
                _log.Fatal($"Queue In Process directory not found {_config.InProcFolderPath}");
                return String.Empty;
            }
            var queueDir = new DirectoryInfo(_config.InProcFolderPath);
            var firstFile = queueDir
                .GetFiles()
                .Where(
                    fi =>
                        //fi.Name.Split(new string[1] { partsSeperator }, StringSplitOptions.RemoveEmptyEntries).Length == 2 && 
                        _config.Endpoints.Any(ep => fi.Name.ToLowerInvariant().EndsWith($"{partsSeperator}{ep.FileName.ToLowerInvariant()}")))
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
            var inProcPath = _config.InProcFolderPath;
            var inProcDir = new DirectoryInfo(inProcPath);

            var filesInBatch = inProcDir.GetFiles($"{batchPrefix}*").Select(fi => fi);

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
                    var uploadResult = _config.DryRun
                        ? DryRunUpload(dataFile)
                        : UploadFile(dataFile);

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
                var timeoutSecs = dataFile.Endpoint.DataConfig.UploadTimeoutInSecs??0;
                if (timeoutSecs > 100)
                {
                    client.Timeout =  TimeSpan.FromSeconds(timeoutSecs);
                }

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

                _log.Info($"Post data set http result [{result.StatusCode}] : {result.Url}");
                return result;
            }
        }

        public DataFileHttpResult DryRunUpload(DataFile dataFile)
        {
            var url = dataFile.Endpoint.Url;
            var uri = new Uri(url);

            var result = new DataFileHttpResult
            {
                StatusCode = HttpStatusCode.OK,
                Content = "Dry run upload content. No files uploaded to Blackboard",
                Url = uri,
                Endpoint = dataFile.Endpoint,
                FilePath = dataFile.FilePath,
                FileName = dataFile.FileName
            };

            _log.Info($"Dry run upload : {url}");
            return result;
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
