using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bilgi.Sis.BbMiddleware.Model;
using Common.Logging;
using Quartz;
using Quartz.Impl;

namespace Bilgi.Sis.BbMiddleware
{
    public class Service
    {
        private readonly ILog _log = LogManager.GetLogger(typeof(Service));
        private IScheduler _scheduler = null;
        private DataConfig _dataConfig;

        private int _dataProcessInterval = 60 * 60; //  1 hour
        private string _dataProcessCron;

        private int _logProcessInterval = (60 * 60) + (60 * 90); // 1.5 hours
        private string _logProcessCron;

        private string DataConfigFilePath => Path.Combine(Environment.CurrentDirectory, "config.json");

        private void ReadConfiguration()
        {
            if (!File.Exists(DataConfigFilePath))
                throw new ApplicationException($"Configuration file does not exist {DataConfigFilePath}");

            _dataConfig = DataConfig.LoadFromFile(DataConfigFilePath);

            if (_dataConfig.DataIntervalInSeconds > 0)
                _dataProcessInterval = _dataConfig.DataIntervalInSeconds;

            if (!String.IsNullOrWhiteSpace(_dataConfig.DataCronExp))
                _dataProcessCron = _dataConfig.DataCronExp;

            if (_dataConfig.DataSetStatusQueryIntervalInSeconds > 0)
                _logProcessInterval = _dataConfig.DataSetStatusQueryIntervalInSeconds;

            if (!String.IsNullOrWhiteSpace(_dataConfig.DataSetStatusCronExp))
                _logProcessCron = _dataConfig.DataSetStatusCronExp;


        }

        public void Start()
        {
            try
            {
                ReadConfiguration();
                DoStart();
            }
            catch (SchedulerConfigException ex)
            {
                _log.Fatal("Can not start service, Quartz scheduler configuration error", ex);
            }
            catch (SchedulerException ex)
            {
                _log.Fatal("Can not start service, Quartz scheduler error", ex);
            }
            catch (Exception ex)
            {
                _log.Fatal("Can not start service", ex);
            }
        }

        public void Stop()
        {
            DoStop();
        }

        private void DoStart()
        {
            StartScheduler();

           
            PrepareDataJob();

            PrepareDataSetStatusJob();

        }

        private void DoStop()
        {
            if (_scheduler != null && _scheduler.IsStarted)
                _scheduler.Shutdown();
        }

        private void StartScheduler()
        {
            if (_scheduler == null)
                _scheduler = StdSchedulerFactory.GetDefaultScheduler();

            _scheduler.Start();
        }
        private void PrepareDataJob()
        {

            if (!_dataConfig.DataJobEnabled)
                return;

            var jobKey = new JobKey("UploadToBb", "DataUplaodGroup");

            IJobDetail job = JobBuilder.Create<DataJob>()
            .WithIdentity(jobKey)
            .UsingJobData("configFilePath", DataConfigFilePath)
            .Build();


            ITrigger trigger = null;

            if (!String.IsNullOrWhiteSpace(_dataProcessCron))
            {
                trigger = TriggerBuilder.Create()
                    .WithSchedule(
                        CronScheduleBuilder.CronSchedule(_dataProcessCron)
                        .WithMisfireHandlingInstructionIgnoreMisfires()
                    ).Build();
            }
            else
            {
                trigger = TriggerBuilder.Create()
                    .StartNow()
                    .WithSimpleSchedule(x => x
                        .WithIntervalInSeconds(_dataProcessInterval)
                        .WithMisfireHandlingInstructionIgnoreMisfires()
                        .RepeatForever()
                    ).Build();
            }

            _scheduler.ScheduleJob(job, trigger);
            _scheduler.TriggerJob(jobKey);
        }

        private void PrepareDataSetStatusJob()
        {
            if (!_dataConfig.DataSetStatusJobEnabled)
                return;

            var jobKey = new JobKey("DataSetStatusJob", "StatusCheckGroup");

            IJobDetail job = JobBuilder.Create<DataSetStatusJob>()
                .WithIdentity(jobKey)
                .UsingJobData("configFilePath", DataConfigFilePath)
                .Build();

            ITrigger trigger = null;

            if (!String.IsNullOrWhiteSpace(_logProcessCron))
            {
                trigger = TriggerBuilder.Create()
                    .WithSchedule(
                        CronScheduleBuilder.CronSchedule(_logProcessCron)
                        .WithMisfireHandlingInstructionIgnoreMisfires()
                    ).Build();
            }
            else
            {
                trigger = TriggerBuilder.Create()
                    .StartNow()
                    .WithSimpleSchedule(x => x
                        .WithIntervalInSeconds(_logProcessInterval)
                        .WithMisfireHandlingInstructionIgnoreMisfires()
                        .RepeatForever())
                    .Build();
            }

            _scheduler.ScheduleJob(job, trigger);
            _scheduler.TriggerJob(jobKey);
        }


    }
}
