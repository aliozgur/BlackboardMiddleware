using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Bilgi.Sis.BbMiddleware.Model
{
    public class Endpoint:DataConfigBase
    {
        public DataConfig DataConfig { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }
        public string Url{ get; set; }
        public string FileName { get; set; }

        [JsonIgnore]
        public string ProcessedFolderPath => Path.Combine(DataConfig.ProcessedFolderPath, this.ProcessedFolderName);
        [JsonIgnore]
        public string LogFolderPath => Path.Combine(DataConfig.LogFolderPath, this.LogFolderName);
        [JsonIgnore]
        public string DataSetStatus_FolderPath => Path.Combine(DataConfig.DataSetStatus_FolderPath, this.LogFolderName);


        

    }
}
