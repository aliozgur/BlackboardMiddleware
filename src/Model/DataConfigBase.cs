using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bilgi.Sis.BbMiddleware.Model
{
    public class DataConfigBase
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string QueueFolderName { get; set; }
        public string ProcessedFolderName { get; set; }
        public bool? ProcessedBackupEnabled { get; set; }
        public string LogFolderName { get; set; }
        public bool? LogEnabled { get; set; }

    }

}
