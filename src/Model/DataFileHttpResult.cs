using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Bilgi.Sis.BbMiddleware.Model
{
    public class DataFileHttpResult
    {
        public string Content { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public Uri Url { get; set; }

        public Endpoint Endpoint { get; set; }

        public string FilePath { get; set; }

        public string FileName { get; set; }
    }
}
