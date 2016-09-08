using FX.Configuration;

namespace Bilgi.Sis.BbMiddleware.Model
{
    public class ServiceConfig : AppConfiguration
    {

        public string ServiceName { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
 
    }
}
