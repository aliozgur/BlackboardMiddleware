using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Linq;

using Newtonsoft.Json;

namespace Bilgi.Sis.BbMiddleware.Model
{

    public class DataConfig : DataConfigBase
    {
        public const string InProcessFolderName = "InProcess";

        public bool DryRun { get; set; } = false;

        public string DataRootPath { get; set; }
        public string FilePartsSeperator { get; set; } = "_";
        public string EndpointUrl { get; set; }
        public int DataIntervalInSeconds { get; set; } = 60*60;
        public string DataCronExp { get; set; }

        public bool DataJobEnabled { get; set; } = true;
        public string DataSetStatusEndpointUrl { get; set; }
        public int DataSetStatusQueryIntervalInSeconds { get; set; } = 90*60;
        public string DataSetStatusCronExp { get; set; }
        public bool DataSetStatusJobEnabled { get; set; } = false;
        public int DataSetStatusMaxFilesToProcess { get; set; } = 5;
        public string DataSetStatusFolderName { get; set; } = "Status";

        public bool DataSetStatusKeepNotOkResponses { get; set; } = true;

        public bool UploadBinary { get; set; } = false;

        public List<Endpoint> Endpoints { get; set; }

        [JsonIgnore]
        public string QueueFolderPath => Path.Combine(DataRootPath, QueueFolderName);
        [JsonIgnore]
        public string InProcFolderPath => Path.Combine(DataRootPath, QueueFolderName, InProcessFolderName);
        [JsonIgnore]
        public string ProcessedFolderPath => Path.Combine(DataRootPath, ProcessedFolderName);
        [JsonIgnore]
        public string LogFolderPath => Path.Combine(DataRootPath, LogFolderName);
        [JsonIgnore]
        public string DataSetStatus_FolderPath => Path.Combine(DataRootPath, DataSetStatusFolderName);
  
        
        public static DataConfig LoadFromFile(string path)
        {
            string json = File.ReadAllText(path);
            var result = JsonConvert.DeserializeObject<DataConfig>(json);
            return result;
        }

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext context)
        {
            ProcessEndpoints();
        }

        private void ProcessEndpoints()
        {
            Endpoints.ForEach(ep =>
            {
                ep.DataConfig = this;
                ep.Username = string.IsNullOrWhiteSpace(ep.Username) ? this.Username : ep.Username;
                ep.Password = string.IsNullOrWhiteSpace(ep.Password) ? this.Password : ep.Password;
                ep.LogEnabled = (ep.LogEnabled ?? this.LogEnabled) ?? false;
                ep.ProcessedBackupEnabled = (ep.ProcessedBackupEnabled ?? this.ProcessedBackupEnabled) ?? false;
                ep.Url = ep.Url.StartsWith("http://") || ep.Url.StartsWith("https://")
                    ? ep.Url
                    : this.EndpointUrl + ep.Url;
            });

            if (!Directory.Exists(LogFolderPath) && Endpoints.Any(ep=> ep.LogEnabled??false))
                Directory.CreateDirectory(LogFolderPath);

            if (!Directory.Exists(ProcessedFolderPath) && Endpoints.Any(ep => ep.ProcessedBackupEnabled ?? false))
                Directory.CreateDirectory(ProcessedFolderPath);

        }
    }
}
