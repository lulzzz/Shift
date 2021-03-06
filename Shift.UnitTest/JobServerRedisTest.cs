﻿using System;
using Xunit;
using System.Threading;
using System.Collections.Generic;
using System.Configuration;
using Autofac;
using Autofac.Features.ResolveAnything;

using Shift.Entities;

namespace Shift.UnitTest
{
     
    public class JobServerRedisTest
    {
        JobClient jobClient;
        JobServer jobServer;
        private const string AppID = "TestAppID";

        public JobServerRedisTest()
        {
            var appSettingsReader = new AppSettingsReader();

            //Configure storage connection
            var clientConfig = new ClientConfig();
            clientConfig.DBConnectionString = appSettingsReader.GetValue("RedisConnectionString", typeof(string)) as string;
            clientConfig.StorageMode = "redis";
            jobClient = new JobClient(clientConfig);

            var serverConfig = new ServerConfig();
            serverConfig.DBConnectionString = appSettingsReader.GetValue("RedisConnectionString", typeof(string)) as string;
            serverConfig.StorageMode = "redis";
            serverConfig.ProcessID = this.ToString();
            serverConfig.Workers = 1;
            serverConfig.MaxRunnableJobs = 1;

            serverConfig.ProgressDBInterval = new TimeSpan(0);
            serverConfig.AutoDeletePeriod = null;
            serverConfig.ForceStopServer = true;
            serverConfig.StopServerDelay = 3000;
            jobServer = new JobServer(serverConfig);
        }

        [Fact]
        public void RunJobsSelectedTest()
        {
            var jobID = jobClient.Add(AppID, () => Console.WriteLine("Hello Test"));
            var job = jobClient.GetJob(jobID);

            Assert.NotNull(job);
            Assert.Equal(jobID, job.JobID);

            //run job
            jobServer.RunJobs(new List<string> { jobID });
            Thread.Sleep(5000);

            job = jobClient.GetJob(jobID);
            jobClient.DeleteJobs(new List<string>() { jobID });
            Assert.Equal(JobStatus.Completed, job.Status);
        }


        [Fact]
        public void StopJobsNonRunningTest()
        {
            var jobID = jobClient.Add(AppID, () => Console.WriteLine("Hello Test"));
            jobClient.SetCommandStop(new List<string> { jobID });
            var job = jobClient.GetJob(jobID);

            Assert.NotNull(job);
            Assert.Equal(JobCommand.Stop, job.Command);

            jobServer.StopJobs(); //stop non-running job
            Thread.Sleep(5000);

            job = jobClient.GetJob(jobID);
            jobClient.DeleteJobs(new List<string>() { jobID });
            Assert.Equal(JobStatus.Stopped, job.Status);
        }

        [Fact]
        public void StopJobsRunningTest()
        {
            var jobTest = new TestJob();
            var progress = new SynchronousProgress<ProgressInfo>();
            var cancelToken = (new CancellationTokenSource()).Token;
            var pauseToken = (new PauseTokenSource()).Token;
            var jobID = jobClient.Add(AppID, () => jobTest.Start("Hello World", progress, cancelToken, pauseToken));

            //run job
            jobServer.RunJobs(new List<string> { jobID });
            Thread.Sleep(1000);

            var job = jobClient.GetJob(jobID);
            Assert.NotNull(job);
            Assert.Equal(JobStatus.Running, job.Status);

            jobClient.SetCommandStop(new List<string> { jobID });
            jobServer.StopJobs(); //stop running job
            Thread.Sleep(3000);

            job = jobClient.GetJob(jobID);
            jobClient.DeleteJobs(new List<string>() { jobID });
            Assert.Equal(JobStatus.Stopped, job.Status);
        }

        [Fact]
        public void CleanUpTest()
        {
            //Test StopJobs with CleanUp() calls

            var jobTest = new TestJob();
            var progress = new SynchronousProgress<ProgressInfo>();
            var cancelToken = (new CancellationTokenSource()).Token;
            var pauseToken = (new PauseTokenSource()).Token;
            var jobID = jobClient.Add(AppID, () => jobTest.Start("Hello World", progress, cancelToken, pauseToken));

            //run job
            jobServer.RunJobs(new List<string> { jobID });
            Thread.Sleep(1000);

            var job = jobClient.GetJob(jobID);
            Assert.NotNull(job);
            Assert.Equal(JobStatus.Running, job.Status);

            jobClient.SetCommandStop(new List<string> { jobID });
            jobServer.CleanUp(); 
            Thread.Sleep(3000);

            job = jobClient.GetJob(jobID);
            jobClient.DeleteJobs(new List<string>() { jobID });
            Assert.Equal(JobStatus.Stopped, job.Status);
        }

        [Fact]
        public void PauseJobsRunningTest()
        {
            var jobTest = new TestJob();
            var progress = new SynchronousProgress<ProgressInfo>();
            var cancelToken = (new CancellationTokenSource()).Token;
            var pauseToken = (new PauseTokenSource()).Token;
            var jobID = jobClient.Add(AppID, () => jobTest.Start("Hello World", progress, cancelToken, pauseToken));

            //run job
            jobServer.RunJobs(new List<string> { jobID });
            Thread.Sleep(1000);

            var job = jobClient.GetJob(jobID);
            Assert.NotNull(job);
            Assert.Equal(JobStatus.Running, job.Status);

            jobClient.SetCommandPause(new List<string> { jobID });
            jobServer.PauseJobs(); //pause running job
            Thread.Sleep(3000);

            job = jobClient.GetJob(jobID);
            jobClient.SetCommandStop(new List<string> { jobID });
            jobServer.StopJobs();
            Thread.Sleep(3000);
            jobClient.DeleteJobs(new List<string>() { jobID });

            Assert.Equal(JobStatus.Paused, job.Status);
        }

        [Fact]
        public void ContinueJobsPausedTest()
        {
            var jobTest = new TestJob();
            var progress = new SynchronousProgress<ProgressInfo>();
            var cancelToken = (new CancellationTokenSource()).Token;
            var pauseToken = (new PauseTokenSource()).Token;
            var jobID = jobClient.Add(AppID, () => jobTest.Start("Hello World", progress, cancelToken, pauseToken));

            //run job
            jobServer.RunJobs(new List<string> { jobID });
            Thread.Sleep(1000);

            jobClient.SetCommandPause(new List<string> { jobID });
            jobServer.PauseJobs(); //pause running job
            Thread.Sleep(3000);

            var job = jobClient.GetJob(jobID);
            Assert.NotNull(job);
            Assert.Equal(JobStatus.Paused, job.Status);

            jobClient.SetCommandContinue(new List<string> { jobID });
            jobServer.ContinueJobs(); //continue paused job
            Thread.Sleep(3000);

            job = jobClient.GetJob(jobID);
            jobClient.SetCommandStop(new List<string> { jobID });
            jobServer.StopJobs();
            Thread.Sleep(3000);
            jobClient.DeleteJobs(new List<string>() { jobID });

            Assert.Equal(JobStatus.Running, job.Status);
        }

    }
}
