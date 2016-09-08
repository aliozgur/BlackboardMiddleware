using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bilgi.Sis.BbMiddleware.Model;
using Common.Logging;
using Common.Logging.Simple;
using Quartz;
using Quartz.Impl;
using Topshelf;
using static System.Console;

namespace Bilgi.Sis.BbMiddleware
{
    class Program
    {
        private static ServiceConfig _svcConfig = null;
        private static readonly ILog _log = LogManager.GetLogger(typeof(Program));

        public static void Main(string[] args)
        {
            PrintInfo();

            _svcConfig = new ServiceConfig();
            
            var displayName = _svcConfig.DisplayName;
            var serviceName = _svcConfig.ServiceName;
            var description = _svcConfig.Description;


            HostFactory.Run(x =>
            {
                x.UseLog4Net();

                x.Service<Service>(s =>
                {
                    s.ConstructUsing(name => new Service());
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });

                x.StartAutomatically();

                if (!String.IsNullOrWhiteSpace(serviceName))
                    x.SetServiceName(serviceName);

                if (!String.IsNullOrWhiteSpace(displayName))
                    x.SetDisplayName(displayName);

                if (!String.IsNullOrWhiteSpace(description))
                    x.SetDescription(description);

                WriteLine("\r\nPress Ctrl+C to quit");
            });


        }

        private static void PrintInfo()
        {
            WriteLine("BİLGİ SIS - BbLearn Integration Middleware Service");
            WriteLine("Version : 0.1");
            WriteLine("Author  : Ali Özgür [ali.ozgur@bilgi.edu.tr]");
        }
    }
}
