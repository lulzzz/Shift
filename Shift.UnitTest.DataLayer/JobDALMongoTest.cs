﻿using System;
using Xunit;
using Shift.Entities;
using System.Collections.Generic;
using Shift.DataLayer;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace Shift.UnitTest.DataLayer
{
     
    public class JobDALMongoTest 
    {
        private static AppSettingsReader appSettingsReader = new AppSettingsReader();
        private const string AppID = "TestAppID";
        private readonly string processID;
        private static string connectionString = appSettingsReader.GetValue("MongoConnectionString", typeof(string)) as string;
        private const string encryptionKey = "";
        JobDALMongo jobDAL;

        public JobDALMongoTest()
        {
            processID = this.ToString();
            jobDAL = new JobDALMongo(connectionString, encryptionKey);
        }

        [Fact]
        public void DeleteTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            Assert.True(!string.IsNullOrWhiteSpace(jobID));

            jobDAL.Delete(new List<string> { jobID });
            var job = jobDAL.GetJob(jobID);

            Assert.Null(job);
        }

        [Fact]
        public void GetJobTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            var job = jobDAL.GetJob(jobID);
            jobDAL.Delete(new List<string> { jobID });

            Assert.IsType<Job>(job);
            Assert.Equal(jobID, job.JobID);
        }

        [Fact]
        public void GetJobViewTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            var job = jobDAL.GetJobView(jobID);
            jobDAL.Delete(new List<string> { jobID });

            Assert.IsType<JobView>(job);
            Assert.Equal(jobID, job.JobID);
        }

        [Fact]
        public void AddTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            jobDAL.Delete(new List<string> { jobID });
            Assert.True(!string.IsNullOrWhiteSpace(jobID));
        }

        [Fact]
        public void UpdateTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            var count = jobDAL.Update(jobID, AppID, "", "", "JobNameUpdated", () => Console.WriteLine("Hello World Test!"));

            var job = jobDAL.GetJob(jobID);
            jobDAL.Delete(new List<string> { jobID });
            Assert.True(count > 0);
            Assert.Equal("JobNameUpdated", job.JobName);
        }

        public async Task StartAsyncJob(string message)
        {
            Console.WriteLine(message);
            await Task.Delay(1000);
        }

        [Fact]
        public void AddAsyncJobTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => StartAsyncJob("Hello World Test!"));
            jobDAL.Delete(new List<string> { jobID });
            Assert.True(!string.IsNullOrWhiteSpace(jobID));
        }

        [Fact]
        public void UpdateAsyncJobTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => StartAsyncJob("Hello World Test!"));
            var count = jobDAL.Update(jobID, AppID, "", "", "JobNameUpdated", () => StartAsyncJob("Updated Hello World Test!"));

            var job = jobDAL.GetJob(jobID);
            jobDAL.Delete(new List<string> { jobID });
            Assert.True(count > 0);
            Assert.Equal("JobNameUpdated", job.JobName);
        }

        //Test auto delete older than 24 hours and Null(not started) status
        [Fact]
        public void DeleteOldJobs_NotStarted()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now.AddHours(-48)
            };
            job = jobDAL.SetJob(job);

            var count = jobDAL.Delete(24, new List<JobStatus?> { null });
            var outJob = jobDAL.GetJob(job.JobID);

            Assert.True(count > 0);
            Assert.Null(outJob);
        }

        //Test auto delete older than 24 hours and with Error or Completed status
        [Fact]
        public void DeleteOldJobs_ErrorAndCompletedTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now.AddHours(-48),
                Status = JobStatus.Error,
                Error = "Test delete old job with status: Error"
            };
            job = jobDAL.SetJob(job);

            var job2 = new Job()
            {
                AppID = AppID,
                Created = DateTime.Now.AddHours(-48),
                Status = JobStatus.Completed,
                Error = "Test delete old job with status: Completed"
            };
            job2 = jobDAL.SetJob(job2);

            var job3 = new Job()
            {
                AppID = AppID,
                Created = DateTime.Now.AddHours(-48),
                Status = null
            };
            job3 = jobDAL.SetJob(job3);

            var count = jobDAL.Delete(24, new List<JobStatus?> { JobStatus.Error, JobStatus.Completed });
            Assert.True(count > 0);

            var outJob = jobDAL.GetJob(job.JobID);
            Assert.Null(outJob);

            var outJob2 = jobDAL.GetJob(job2.JobID);
            Assert.Null(outJob2);

            var outJob3 = jobDAL.GetJob(job3.JobID);
            jobDAL.Delete(new List<string> { job3.JobID });
            Assert.NotNull(outJob3);
            Assert.Equal(job3.JobID, outJob3.JobID);
        }

        //Test auto delete older than 24 hours and don't care about status
        [Fact]
        public void DeleteOldJobs_AnyStatus()
        {
            var job = new Job
            {
                AppID = AppID,
                Status = JobStatus.Completed,
                Created = DateTime.Now.AddHours(-48)
            };
            job = jobDAL.SetJob(job);

            var job2 = new Job()
            {
                AppID = AppID,
                Created = DateTime.Now.AddHours(-48),
                Status = JobStatus.Error,
                Error = "Test delete old job with Any Status"
            };
            job2 = jobDAL.SetJob(job2);

            var count = jobDAL.Delete(24, null);
            Assert.True(count > 0);

            var outJob = jobDAL.GetJob(job.JobID);
            Assert.Null(outJob);

            var outJob2 = jobDAL.GetJob(job2.JobID);
            Assert.Null(outJob2);
        }

        [Fact]
        public void SetCommandStopTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            jobDAL.SetCommandStop(new List<string> { jobID });
            var job = jobDAL.GetJob(jobID);
            jobDAL.Delete(new List<string> { jobID });

            Assert.Equal(JobCommand.Stop, job.Command);
        }

        [Fact]
        public void SetCommandRunNowTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            jobDAL.SetCommandRunNow(new List<string> { jobID });
            var job = jobDAL.GetJob(jobID);
            jobDAL.Delete(new List<string> { jobID });

            Assert.Equal(JobCommand.RunNow, job.Command);
        }

        [Fact]
        public void SetCommandPauseTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            jobDAL.SetToRunning(new List<string> { jobID }); //only RUNNING jobs can be set to pause
            jobDAL.SetCommandPause(new List<string> { jobID });
            var job = jobDAL.GetJob(jobID);
            jobDAL.Delete(new List<string> { jobID });

            Assert.Equal(JobCommand.Pause, job.Command);
        }

        [Fact]
        public void SetCommandContinueTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test!"));
            jobDAL.SetToPaused(new List<string> { jobID }); //only PAUSED jobs can be set to continue
            jobDAL.SetCommandContinue(new List<string> { jobID });
            var job = jobDAL.GetJob(jobID);
            jobDAL.Delete(new List<string> { jobID });

            Assert.Equal(JobCommand.Continue, job.Command);
        }

        [Fact]
        public void ResetTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Command = JobCommand.Stop
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            jobDAL.Reset(new List<string> { job.JobID });
            var outJob = jobDAL.GetJob(job.JobID);
            jobDAL.Delete(new List<string> { job.JobID });

            Assert.NotNull(outJob);
            Assert.True(string.IsNullOrWhiteSpace(outJob.Command));
        }

        [Fact]
        public void SetToStoppedTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Command = JobCommand.Stop
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var count = jobDAL.SetToStopped(new List<string> { job.JobID });
            var outJob = jobDAL.GetJob(job.JobID);
            jobDAL.Delete(new List<string> { job.JobID });

            Assert.NotNull(outJob);
            Assert.True(count == 1);
            Assert.True(string.IsNullOrWhiteSpace(outJob.Command));
            Assert.Equal(JobStatus.Stopped, outJob.Status);
        }

        //Test from Running with cmd:pause to Status:Paused
        [Fact]
        public void SetToPausedTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Command = JobCommand.Pause,
                Status = JobStatus.Running
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var count = jobDAL.SetToPaused(new List<string> { job.JobID });
            var outJob = jobDAL.GetJob(job.JobID);

            jobDAL.SetToStopped(new List<string> { job.JobID });
            jobDAL.Delete(new List<string> { job.JobID });

            Assert.NotNull(outJob);
            Assert.True(count == 1);
            Assert.True(string.IsNullOrWhiteSpace(outJob.Command));
            Assert.Equal(JobStatus.Paused, outJob.Status);
        }

        //Test from Paused with cmd:continue to Status:Running
        [Fact]
        public void SetToContinueRunningTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Command = JobCommand.Continue,
                Status = JobStatus.Paused
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var count = jobDAL.SetToRunning(new List<string> { job.JobID });
            var outJob = jobDAL.GetJob(job.JobID);

            jobDAL.SetToStopped(new List<string> { job.JobID });
            jobDAL.Delete(new List<string> { job.JobID });

            Assert.NotNull(outJob);
            Assert.True(count == 1);
            Assert.True(string.IsNullOrWhiteSpace(outJob.Command));
            Assert.Equal(JobStatus.Running, outJob.Status);
        }

        //Get Multiple jobs
        [Fact]
        public void GetJobsTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test1!"));
            var jobID2 = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test2!"));

            var jobs = jobDAL.GetJobs(new List<string> { jobID, jobID2 });
            jobDAL.Delete(new List<string> { jobID, jobID2 });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.True(jobs.Count == 2);
            Assert.True(jobIDs.Contains(jobID));
            Assert.True(jobIDs.Contains(jobID2));
        }

        [Fact]
        public void GetNonRunningJobsByIDsTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test1!"));
            var jobID2 = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test2!"));

            var jobs = jobDAL.GetNonRunningJobsByIDs(new List<string> { jobID, jobID2 });
            jobDAL.Delete(new List<string> { jobID, jobID2 });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.True(jobs.Count == 2);
            Assert.True(jobIDs.Contains(jobID));
            Assert.True(jobIDs.Contains(jobID2));
        }

        [Fact]
        public void GetJobIdsByProcessAndCommandTest()
        {
            var job = new Job
            {
                AppID = AppID,
                ProcessID = processID,
                Created = DateTime.Now,
                Command = null
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            jobDAL.SetCommandStop(new List<string> { job.JobID });

            var outJobIDs = jobDAL.GetJobIdsByProcessAndCommand(processID, JobCommand.Stop);
            jobDAL.Delete(new List<string> { job.JobID });

            Assert.True(outJobIDs.Contains(job.JobID));
        }

        [Fact]
        public void GetJobsByProcessAndStatusTest()
        {
            var job = new Job
            {
                AppID = AppID,
                ProcessID = processID,
                Created = DateTime.Now,
                Status = null
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            jobDAL.SetToRunning(processID, job.JobID);

            var outJobs = jobDAL.GetJobsByProcessAndStatus(processID, JobStatus.Running);

            jobDAL.SetToStopped(new List<string> { job.JobID });
            jobDAL.Delete(new List<string> { job.JobID });

            var jobIDs = outJobs.Select(j => j.JobID).ToList();
            Assert.True(jobIDs.Contains(job.JobID));
        }

        [Fact]
        public void GetJobViewsTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test1!"));
            var jobID2 = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test2!"));

            var jobs = jobDAL.GetJobViews(1, 10);
            jobDAL.Delete(new List<string> { jobID, jobID2 });

            Assert.True(jobs.Total >= 2);
            var jobIDs = jobs.Items.Select(j => j.JobID).ToList();
            Assert.True(jobIDs.Contains(jobID));
            Assert.True(jobIDs.Contains(jobID2));
        }

        [Fact]
        public void SetToRunningTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = processID
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var count = jobDAL.SetToRunning(job.ProcessID, job.JobID);
            var outJob = jobDAL.GetJob(job.JobID);

            //set to stop before delete
            jobDAL.SetToStopped(new List<string> { job.JobID });
            jobDAL.Delete(new List<string> { job.JobID });

            Assert.NotNull(outJob);
            Assert.True(count == 1);
            Assert.Equal(JobStatus.Running, outJob.Status);
        }

        [Fact]
        public void SetErrorTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = processID
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var error = "Test Error";
            var count = jobDAL.SetError(job.ProcessID, job.JobID, error);
            var outJob = jobDAL.GetJob(job.JobID);

            jobDAL.Delete(new List<string> { job.JobID });

            Assert.NotNull(outJob);
            Assert.True(count == 1);
            Assert.Equal(JobStatus.Error, outJob.Status);
            Assert.Equal(error, outJob.Error);
        }

        [Fact]
        public void SetToCompletedTest()
        {
            var job = new Job();
            job.AppID = AppID;
            job.Created = DateTime.Now;
            job.Status = null;
            job.ProcessID = processID;
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var count = jobDAL.SetToCompleted(job.ProcessID, job.JobID);
            var outJob = jobDAL.GetJob(job.JobID);

            jobDAL.Delete(new List<string> { job.JobID });

            Assert.NotNull(outJob);
            Assert.True(count == 1);
            Assert.Equal(JobStatus.Completed, outJob.Status);
        }

        [Fact]
        public void CountRunningJobsTest()
        {
            var job1 = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = processID
            };
            job1 = jobDAL.SetJob(job1);
            Assert.True(!string.IsNullOrWhiteSpace(job1.JobID));
            jobDAL.SetToRunning(processID, job1.JobID);

            var job2 = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = processID
            };
            job2 = jobDAL.SetJob(job2);
            Assert.True(!string.IsNullOrWhiteSpace(job2.JobID));
            jobDAL.SetToRunning(processID, job2.JobID);

            var count = jobDAL.CountRunningJobs(processID);

            //set to stop before delete
            jobDAL.SetToStopped(new List<string> { job1.JobID, job2.JobID });
            jobDAL.Delete(new List<string> { job1.JobID, job2.JobID });

            Assert.True(count >= 2);
        }

        [Fact]
        public void ClaimJobsToRunByMaxNumTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = null
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var jobs = jobDAL.ClaimJobsToRun(processID, 10);

            jobDAL.Delete(new List<string> { job.JobID });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.True(jobIDs.Contains(job.JobID));
        }

        [Fact]
        public void ClaimJobsToRunTest()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = null
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var jobs = jobDAL.ClaimJobsToRun(processID, new List<Job> { job });
            var outJob = jobDAL.GetJob(job.JobID);

            jobDAL.Delete(new List<string> { job.JobID });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.Equal(processID, outJob.ProcessID);
            Assert.True(jobIDs.Contains(job.JobID));
        }

        //Don't claim running jobs
        [Fact]
        public void ClaimJobsToRunTest2()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = processID + "-someoneElseTest"
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            jobDAL.SetToRunning(processID + "-someoneElseTest", job.JobID);

            var jobs = jobDAL.ClaimJobsToRun(processID, new List<Job> { job });

            jobDAL.SetToStopped(new List<string> {job.JobID});
            jobDAL.Delete(new List<string> { job.JobID });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.True(!jobIDs.Contains(job.JobID));
        }

        //Don't claim jobs already claimed by someone else
        [Fact]
        public void ClaimJobsToRunTest3()
        {
            var job = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = processID + "-someoneElseTest"
            };
            job = jobDAL.SetJob(job);
            Assert.True(!string.IsNullOrWhiteSpace(job.JobID));

            var jobs = jobDAL.ClaimJobsToRun(processID, new List<Job> { job });

            jobDAL.Delete(new List<string> { job.JobID });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.True(!jobIDs.Contains(job.JobID));
        }

        [Fact]
        public void GetJobsToRunTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test1!"));
            Assert.True(!string.IsNullOrWhiteSpace(jobID));

            var jobs = jobDAL.GetJobsToRun(1);

            jobDAL.Delete(new List<string> { jobID });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.True(jobIDs.Contains(jobID));
            Assert.True(jobs.Count == 1);
        }

        //Get run-now job first
        [Fact]
        public void GetJobsToRunTest2()
        {
            var jobID1 = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test1!"));
            Assert.True(!string.IsNullOrWhiteSpace(jobID1));
            var jobID2 = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test2!"));
            Assert.True(!string.IsNullOrWhiteSpace(jobID2));

            jobDAL.SetCommandRunNow(new List<string> { jobID2 });
            var jobs = jobDAL.GetJobsToRun(1);

            jobDAL.Delete(new List<string> { jobID1, jobID2 });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.True(jobIDs.Contains(jobID2));
            Assert.True(jobs.Count == 1);
        }

        //Should return no jobs that was added
        [Fact]
        public void GetJobsToRunTest3()
        {
            //procesID != null
            var job1 = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = processID
            };
            job1 = jobDAL.SetJob(job1);
            Assert.True(!string.IsNullOrWhiteSpace(job1.JobID));

            //status != null
            var job2 = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = JobStatus.Stopped,
                ProcessID = null
            };
            job2 = jobDAL.SetJob(job2);
            Assert.True(!string.IsNullOrWhiteSpace(job2.JobID));

            //command != null
            var job3 = new Job
            {
                AppID = AppID,
                Created = DateTime.Now,
                Status = null,
                ProcessID = null,
                Command = JobCommand.Stop
            };
            job3 = jobDAL.SetJob(job3);
            Assert.True(!string.IsNullOrWhiteSpace(job3.JobID));

            var jobs = jobDAL.GetJobsToRun(3);

            jobDAL.Delete(new List<string> { job1.JobID, job2.JobID, job3.JobID });

            var jobIDs = jobs.Select(j => j.JobID).ToList();
            Assert.True(!jobIDs.Contains(job1.JobID));
            Assert.True(!jobIDs.Contains(job2.JobID));
            Assert.True(!jobIDs.Contains(job3.JobID));
        }

        [Fact]
        public void SetProgressTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test1!"));
            Assert.True(!string.IsNullOrWhiteSpace(jobID));

            var note = "progress note";
            var data = "progress data";
            var percent = 50;
            var count = jobDAL.SetProgress(jobID, percent, note, data);
            var job = jobDAL.GetJobView(jobID);

            jobDAL.Delete(new List<string> { jobID });

            Assert.True(count == 1);
            Assert.Equal(percent, job.Percent);
            Assert.Equal(note, job.Note);
            Assert.Equal(data, job.Data);
        }

        [Fact]
        public void GetProgressTest()
        {
            var jobID = jobDAL.Add(AppID, "", "", "", () => Console.WriteLine("Hello World Test1!"));
            Assert.True(!string.IsNullOrWhiteSpace(jobID));

            var note = "progress note";
            var data = "progress data";
            var percent = 50;
            jobDAL.SetProgress(jobID, percent, note, data);

            var progress = jobDAL.GetProgress(jobID);

            jobDAL.Delete(new List<string> { jobID });

            Assert.Equal(percent, progress.Percent);
            Assert.Equal(note, progress.Note);
            Assert.Equal(data, progress.Data);
        }

        [Fact]
        public void GetJobStatusCountTest()
        {
            var userID = "UserIDTest";
            var job1 = new Job
            {
                AppID = AppID,
                UserID = userID,
                Created = DateTime.Now,
                Status = null
            };
            job1 = jobDAL.SetJob(job1);
            Assert.True(!string.IsNullOrWhiteSpace(job1.JobID));

            //status != null
            var job2 = new Job
            {
                AppID = AppID,
                UserID = userID,
                Created = DateTime.Now,
                Status = JobStatus.Stopped
            };
            job2 = jobDAL.SetJob(job2);
            Assert.True(!string.IsNullOrWhiteSpace(job2.JobID));

            var statusCounts = jobDAL.GetJobStatusCount(null, null);

            jobDAL.Delete(new List<string> { job1.JobID, job2.JobID });

            var jobStatuses = statusCounts.Select(s => s.Status).ToList();
            Assert.True(jobStatuses.Contains(null));
            Assert.True(jobStatuses.Contains(JobStatus.Stopped));
            Assert.True(statusCounts.Count >= 2);
            foreach (var jobStatusCount in statusCounts)
            {
                if (jobStatusCount.Status == null)
                {
                    Assert.True(jobStatusCount.NullCount >= 1);
                }
                if (jobStatusCount.Status == JobStatus.Stopped)
                {
                    Assert.True(jobStatusCount.Count >= 1);
                }
            }
        }

        //Count by AppID and UserID
        [Fact]
        public void GetJobStatusCountTest2()
        {
            var userID = "UserIDTest";
            var job1 = new Job
            {
                AppID = AppID,
                UserID = userID,
                Created = DateTime.Now,
                Status = null
            };
            job1 = jobDAL.SetJob(job1);
            Assert.True(!string.IsNullOrWhiteSpace(job1.JobID));

            //status != null
            var job2 = new Job
            {
                AppID = AppID,
                UserID = userID,
                Created = DateTime.Now,
                Status = JobStatus.Stopped
            };
            job2 = jobDAL.SetJob(job2);
            Assert.True(!string.IsNullOrWhiteSpace(job2.JobID));

            var statusCounts = jobDAL.GetJobStatusCount(AppID, userID);

            jobDAL.Delete(new List<string> { job1.JobID, job2.JobID });

            var jobStatuses = statusCounts.Select(s => s.Status).ToList();
            Assert.True(jobStatuses.Contains(null));
            Assert.True(jobStatuses.Contains(JobStatus.Stopped));
            Assert.True(statusCounts.Count >= 2);
            foreach (var jobStatusCount in statusCounts)
            {
                if (jobStatusCount.Status == null)
                {
                    Assert.True(jobStatusCount.NullCount >= 1);
                }
                if (jobStatusCount.Status == JobStatus.Stopped)
                {
                    Assert.True(jobStatusCount.Count >= 1);
                }
            }
        }

        //Count by AppID 
        [Fact]
        public void GetJobStatusCountTest3()
        {
            var userID = "UserIDTest";
            var job1 = new Job
            {
                AppID = AppID + "-otherAppID",
                UserID = userID,
                Created = DateTime.Now,
                Status = null
            };
            job1 = jobDAL.SetJob(job1);
            Assert.True(!string.IsNullOrWhiteSpace(job1.JobID));

            //status != null
            var job2 = new Job
            {
                AppID = AppID,
                UserID = userID,
                Created = DateTime.Now,
                Status = JobStatus.Stopped
            };
            job2 = jobDAL.SetJob(job2);
            Assert.True(!string.IsNullOrWhiteSpace(job2.JobID));

            var statusCounts = jobDAL.GetJobStatusCount(AppID, null);

            jobDAL.Delete(new List<string> { job1.JobID, job2.JobID });

            var jobStatuses = statusCounts.Select(s => s.Status).ToList();
            Assert.True(jobStatuses.Contains(JobStatus.Stopped));
            Assert.True(statusCounts.Count >= 1);
            foreach (var jobStatusCount in statusCounts)
            {
                if (jobStatusCount.Status == JobStatus.Stopped)
                {
                    Assert.True(jobStatusCount.Count >= 1);
                }
            }
        }

        //Count by UserID 
        [Fact]
        public void GetJobStatusCountTest4()
        {
            var userID = "UserIDTest";
            var job1 = new Job
            {
                AppID = AppID,
                UserID = userID + "-otherUserID",
                Created = DateTime.Now,
                Status = null
            };
            job1 = jobDAL.SetJob(job1);
            Assert.True(!string.IsNullOrWhiteSpace(job1.JobID));

            //status != null
            var job2 = new Job
            {
                AppID = AppID,
                UserID = userID,
                Created = DateTime.Now,
                Status = JobStatus.Stopped
            };
            job2 = jobDAL.SetJob(job2);
            Assert.True(!string.IsNullOrWhiteSpace(job2.JobID));

            var statusCounts = jobDAL.GetJobStatusCount(null, userID);

            jobDAL.Delete(new List<string> { job1.JobID, job2.JobID });

            var jobStatuses = statusCounts.Select(s => s.Status).ToList();
            Assert.True(jobStatuses.Contains(JobStatus.Stopped));
            Assert.True(statusCounts.Count >= 1);
            foreach (var jobStatusCount in statusCounts)
            {
                if (jobStatusCount.Status == JobStatus.Stopped)
                {
                    Assert.True(jobStatusCount.Count >= 1);
                }
            }
        }
    }
}
