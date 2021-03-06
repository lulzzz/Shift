﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.Data.SqlClient;
using System.Linq.Expressions;

using Newtonsoft.Json;
using Shift.Entities;
using Dapper;

namespace Shift.DataLayer
{
    //Async pattern based on Stephen Cleary #4 answer in http://stackoverflow.com/a/32512090/2437862
    // 4) Pass in a flag argument. (to indicate synchronous or async action)

    public class JobDALSql : IJobDAL
    {
        private string connectionString;
        private string encryptionKey;

        public JobDALSql(string connectionString, string encryptionKey)
        {
            this.connectionString = connectionString;
            this.encryptionKey = encryptionKey;
        }

        #region insert/update job
        /// <summary>
        /// Add a new job in to queue.
        /// </summary>
        public string Add(string appID, string userID, string jobType, string jobName, Expression<Action> methodCall)
        {
            return AddAsync(appID, userID, jobType, jobName, methodCall, true).GetAwaiter().GetResult();
        }

        public Task<string> AddAsync(string appID, string userID, string jobType, string jobName, Expression<Action> methodCall) {
            return AddAsync(appID, userID, jobType, jobName, methodCall, false);
        }

        public string Add(string appID, string userID, string jobType, string jobName, Expression<Func<Task>> methodCall)
        {
            return AddAsync(appID, userID, jobType, jobName, methodCall, true).GetAwaiter().GetResult();
        }

        public Task<string> AddAsync(string appID, string userID, string jobType, string jobName, Expression<Func<Task>> methodCall)
        {
            return AddAsync(appID, userID, jobType, jobName, methodCall, false);
        }

        private async Task<string> AddAsync(string appID, string userID, string jobType, string jobName, LambdaExpression methodCall, bool isSync)
        {
            var job = DALHelpers.CreateJobFromExpression(encryptionKey, appID, userID, jobType, jobName, methodCall);

            string jobID = null;
            using (var connection = new SqlConnection(connectionString))
            {
                var query = @"INSERT INTO [Job] ([AppID], [UserID], [JobType], [JobName], [InvokeMeta], [Parameters], [Created]) 
                              VALUES(@AppID, @UserID, @JobType, @JobName, @InvokeMeta, @Parameters, @Created);
                              SELECT CAST(SCOPE_IDENTITY() as int); ";
                if (isSync)
                {
                    jobID = connection.Query<string>(query, job).SingleOrDefault();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<string>(query, job);
                    jobID = taskResult.FirstOrDefault();
                }
            }

            return jobID;
        }

        /// <summary>
        /// Update existing job, reset fields, return updated record count.
        /// </summary>
        public int Update(string jobID, string appID, string userID, string jobType, string jobName, Expression<Action> methodCall)
        {
            return UpdateAsync(jobID, appID, userID, jobType, jobName, methodCall, true).GetAwaiter().GetResult();
        }

        public Task<int> UpdateAsync(string jobID, string appID, string userID, string jobType, string jobName, Expression<Action> methodCall)
        {
            return UpdateAsync(jobID, appID, userID, jobType, jobName, methodCall, false);
        }

        public int Update(string jobID, string appID, string userID, string jobType, string jobName, Expression<Func<Task>> methodCall)
        {
            return UpdateAsync(jobID, appID, userID, jobType, jobName, methodCall, true).GetAwaiter().GetResult();
        }

        public Task<int> UpdateAsync(string jobID, string appID, string userID, string jobType, string jobName, Expression<Func<Task>> methodCall)
        {
            return UpdateAsync(jobID, appID, userID, jobType, jobName, methodCall, false);
        }

        private async Task<int> UpdateAsync(string jobID, string appID, string userID, string jobType, string jobName, LambdaExpression methodCall, bool isSync)
        {
            if (methodCall == null)
                throw new ArgumentNullException("methodCall");

            var callExpression = methodCall.Body as MethodCallExpression;
            if (callExpression == null)
            {
                throw new ArgumentException("Expression body must be 'System.Linq.Expressions.MethodCallExpression' type.", "methodCall");
            }

            Type type;
            if (callExpression.Object != null)
            {
                var value = DALHelpers.GetExpressionValue(callExpression.Object);
                if (value == null)
                    throw new InvalidOperationException("Expression object can not be null.");

                type = value.GetType();
            }
            else
            {
                type = callExpression.Method.DeclaringType;
            }

            var methodInfo = callExpression.Method;
            var args = callExpression.Arguments.Select(DALHelpers.GetExpressionValue).ToArray();

            if (type == null) throw new ArgumentNullException("type");
            if (methodInfo == null) throw new ArgumentNullException("method");
            if (args == null) throw new ArgumentNullException("args");

            DALHelpers.Validate(type, "type", methodInfo, "method", args.Length, "args");

            var invokeMeta = new InvokeMeta(type, methodInfo);

            //Save InvokeMeta and args
            var now = DateTime.Now;
            var values = new DynamicParameters();
            values.Add("JobID", jobID);
            values.Add("AppID", appID);
            values.Add("UserID", userID);
            values.Add("JobType", jobType);
            values.Add("JobName", string.IsNullOrWhiteSpace(jobName) ? type.Name + "." + methodInfo.Name : jobName);
            values.Add("InvokeMeta", JsonConvert.SerializeObject(invokeMeta, SerializerSettings.Settings));
            values.Add("Parameters", Helpers.Encrypt(JsonConvert.SerializeObject(DALHelpers.SerializeArguments(args), SerializerSettings.Settings), encryptionKey)); //ENCRYPT it!!!
            values.Add("Created", now);
            values.Add("Status", JobStatus.Running);

            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var trn = connection.BeginTransaction())
                {
                    //Delete job Progress
                    var query2 = @"DELETE  
                            FROM JobProgress
                            WHERE JobID = @jobID; ";
                    if(isSync)
                    {
                        connection.Execute(query2, new { jobID }, trn);
                    }
                    else
                    {
                        await connection.ExecuteAsync(query2, new { jobID }, trn);
                    }

                    var query = @"
                            UPDATE [Job]
                            SET [AppID] = @AppID
                                ,[UserID] = @UserID
                                ,[ProcessID] = NULL
                                ,[JobType] = @JobType
                                ,[JobName] = @JobName
                                ,[InvokeMeta] = @InvokeMeta
                                ,[Parameters] = @Parameters
                                ,[Command] = NULL
                                ,[Status] = NULL
                                ,[Error] = NULL
                                ,[Start] = NULL
                                ,[End] = NULL
                                ,[Created] = @Created
                            WHERE JobID = @JobID AND (Status != @Status OR Status IS NULL);
                            ";
                    if (isSync)
                    {
                        count = connection.Execute(query, values, trn);
                    }
                    else
                    {
                        count = await connection.ExecuteAsync(query, values, trn);
                    }

                    trn.Commit();
                }
            }

            return count;
        }
        #endregion

        #region UnitTest Helper
        //Used by UnitTest for adding/setting jobs
        public Job SetJob(Job job)
        {
            job = SetJobAsync(job).GetAwaiter().GetResult();
            return job;
        }

        public async Task<Job> SetJobAsync(Job job)
        {
            if (string.IsNullOrWhiteSpace(job.JobID))
            {
                //Insert
                using (var connection = new SqlConnection(connectionString))
                {
                    var query = @"INSERT INTO [Job] ([AppID], [UserID], [ProcessID], [JobType], [JobName], [InvokeMeta], 
                                [Parameters], [Command], [Status], [Error], [Start], [End], [Created]) 
                              VALUES(@AppID, @UserID, @ProcessID, @JobType, @JobName, @InvokeMeta, 
                                @Parameters, @Command, @Status, @Error, @Start, @End, @Created);
                              SELECT CAST(SCOPE_IDENTITY() as int); ";
                    var taskResult = await connection.QueryAsync<string>(query, job);
                    job.JobID = taskResult.FirstOrDefault();
                }
            }
            else
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    var query = @"
                            UPDATE [Job]
                            SET [AppID] = @AppID
                                ,[UserID] = @UserID
                                ,[ProcessID] = @ProcessID
                                ,[JobType] = @JobType
                                ,[JobName] = @JobName
                                ,[InvokeMeta] = @InvokeMeta
                                ,[Parameters] = @Parameters
                                ,[Command] = @Command
                                ,[Status] = @Status
                                ,[Error] = @Error
                                ,[Start] = @Start
                                ,[End] = @End
                                ,[Created] = @Created
                            WHERE JobID = @JobID;
                            ";
                    await connection.ExecuteAsync(query, job);
                }
            }

            return job;
        }
        #endregion

        #region Set Command field
        /// <summary>
        /// Flag jobs with 'stop' command. 
        /// </summary>
        /// <remarks>
        /// This works only for running jobs and jobs with no status. The server will attempt to 'stop' jobs marked as 'stop'.
        /// </remarks>
        public int SetCommandStop(ICollection<string> jobIDs)
        {
            return SetCommandStopAsync(jobIDs, true).GetAwaiter().GetResult();
        }

        public Task<int> SetCommandStopAsync(ICollection<string> jobIDs)
        {
            return SetCommandStopAsync(jobIDs, false);
        }

        private async Task<int> SetCommandStopAsync(ICollection<string> jobIDs, bool isSync)
        {
            if (jobIDs.Count == 0)
                return 0;

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"UPDATE [Job] 
                            SET 
                            Command = @command 
                            WHERE JobID IN @ids 
                            AND (Status = @running OR Status = @paused OR Status IS NULL);
                            ";
                if (isSync)
                {
                    return connection.Execute(sql, new { command = JobCommand.Stop, ids = jobIDs.ToArray(), running = JobStatus.Running, paused = JobStatus.Paused });
                }
                else
                {
                    return await connection.ExecuteAsync(sql, new { command = JobCommand.Stop, ids = jobIDs.ToArray(), running = JobStatus.Running, paused = JobStatus.Paused });
                }
            }
        }

        /// <summary>
        /// Set the Command field to run-now, only works for jobs with no status (ready to run).
        /// </summary>
        /// <remarks>
        /// </remarks>
        public int SetCommandRunNow(ICollection<string> jobIDs)
        {
            return SetCommandRunNowAsync(jobIDs, true).GetAwaiter().GetResult();
        }

        public Task<int> SetCommandRunNowAsync(ICollection<string> jobIDs)
        {
            return SetCommandRunNowAsync(jobIDs, false);
        }

        private async Task<int> SetCommandRunNowAsync(ICollection<string> jobIDs, bool isSync)
        {
            if (jobIDs.Count == 0)
                return 0;

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"UPDATE [Job] 
                            SET 
                            Command = @command 
                            WHERE JobID IN @ids 
                            AND Status IS NULL
                            AND ProcessID IS NULL;
                            ";
                if (isSync)
                {
                    return connection.Execute(sql, new { command = JobCommand.RunNow, ids = jobIDs.ToArray() });
                }
                else
                {
                    return await connection.ExecuteAsync(sql, new { command = JobCommand.RunNow, ids = jobIDs.ToArray() });
                }
            }
        }

        /// <summary>
        /// Flag running jobs with 'pause' command. 
        /// </summary>
        /// <remarks>
        /// This works only for running jobs. The server will attempt to 'stop' jobs marked as 'stop'.
        /// </remarks>
        public int SetCommandPause(ICollection<string> jobIDs)
        {
            return SetCommandPauseAsync(jobIDs, true).GetAwaiter().GetResult();
        }

        public Task<int> SetCommandPauseAsync(ICollection<string> jobIDs)
        {
            return SetCommandPauseAsync(jobIDs, false);
        }

        private async Task<int> SetCommandPauseAsync(ICollection<string> jobIDs, bool isSync)
        {
            if (jobIDs.Count == 0)
                return 0;

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"UPDATE [Job] 
                            SET 
                            Command = @command 
                            WHERE JobID IN @ids 
                            AND Status = @status;
                            ";
                if (isSync)
                {
                    return connection.Execute(sql, new { command = JobCommand.Pause, ids = jobIDs.ToArray(), status = JobStatus.Running });
                }
                else
                {
                    return await connection.ExecuteAsync(sql, new { command = JobCommand.Pause, ids = jobIDs.ToArray(), status = JobStatus.Running });
                }
            }
        }

        /// <summary>
        /// Flag paused jobs with 'continue' command. 
        /// </summary>
        /// <remarks>
        /// This works only for paused jobs. The server will attempt to continue jobs marked as 'continue'.
        /// </remarks>
        public int SetCommandContinue(ICollection<string> jobIDs)
        {
            return SetCommandContinueAsync(jobIDs, true).GetAwaiter().GetResult();
        }

        public Task<int> SetCommandContinueAsync(ICollection<string> jobIDs)
        {
            return SetCommandContinueAsync(jobIDs, false);
        }

        private async Task<int> SetCommandContinueAsync(ICollection<string> jobIDs, bool isSync)
        {
            if (jobIDs.Count == 0)
                return 0;

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"UPDATE [Job] 
                            SET 
                            Command = @command 
                            WHERE JobID IN @ids 
                            AND Status = @status;
                            ";
                if (isSync)
                {
                    return connection.Execute(sql, new { command = JobCommand.Continue, ids = jobIDs.ToArray(), status = JobStatus.Paused });
                }
                else
                {
                    return await connection.ExecuteAsync(sql, new { command = JobCommand.Continue, ids = jobIDs.ToArray(), status = JobStatus.Paused });
                }
            }
        }
        #endregion

        #region Direct Action to Jobs
        /// <summary>
        /// Reset jobs, only affect non-running jobs.
        /// </summary>
        public int Reset(ICollection<string> jobIDs)
        {
            return ResetAsync(jobIDs, true).GetAwaiter().GetResult();
        }

        public Task<int> ResetAsync(ICollection<string> jobIDs)
        {
            return ResetAsync(jobIDs, false);
        }

        private async Task<int> ResetAsync(ICollection<string> jobIDs, bool isSync)
        {
            var count = 0;
            if (jobIDs.Count == 0)
                return count;

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                var sql = @"SELECT j.JobID 
                            FROM Job j
                            WHERE j.JobID IN @ids
                            AND (j.Status != @status OR j.Status IS NULL); ";
                var notRunning = connection.Query<int>(sql, new { ids = jobIDs.ToArray(), status = JobStatus.Running }).ToList<int>();

                using (var trn = connection.BeginTransaction())
                {
                    if (notRunning.Count > 0)
                    {
                        //Reset jobs and progress for NON running jobs
                        sql = @"UPDATE JobProgress 
                            SET 
                            [Percent] = NULL, 
                            Note = NULL,
                            Data = NULL
                            WHERE JobID IN @ids; ";
                        if (isSync)
                        {
                            connection.Execute(sql, new { ids = notRunning.ToArray() }, trn);
                        }
                        else
                        {
                            await connection.ExecuteAsync(sql, new { ids = notRunning.ToArray() }, trn);
                        }

                        sql = @"UPDATE Job 
                        SET 
                        ProcessID = NULL, 
                        Command = NULL, 
                        Status = NULL, 
                        Error = NULL,
                        [Start] = NULL, 
                        [End] = NULL 
                        WHERE JobID IN @ids; ";
                        if (isSync)
                        {
                            count = connection.Execute(sql, new { ids = notRunning.ToArray() }, trn);
                        }
                        else
                        {
                            count = await connection.ExecuteAsync(sql, new { ids = notRunning.ToArray() }, trn);
                        }
                    }

                    trn.Commit();
                }
            }

            return count;
        }

        /// <summary>
        /// Delete jobs, only affect non-running jobs.
        /// </summary>
        public int Delete(ICollection<string> jobIDs)
        {
            return DeleteAsync(jobIDs, true).GetAwaiter().GetResult();
        }

        public Task<int> DeleteAsync(ICollection<string> jobIDs)
        {
            return DeleteAsync(jobIDs, false);
        }

        private async Task<int> DeleteAsync(ICollection<string> jobIDs, bool isSync)
        {
            if (jobIDs.Count == 0)
                return 0;

            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                //Get only the NON running jobs
                var sql = @"SELECT j.JobID 
                            FROM Job j
                            WHERE j.JobID IN @ids
                            AND (j.Status != @status OR j.Status IS NULL); ";
                var notRunning = connection.Query<int>(sql, new { ids = jobIDs.ToArray(), status = JobStatus.Running }).ToList<int>();

                using (var trn = connection.BeginTransaction())
                {
                    if (notRunning.Count > 0)
                    {
                        //TODO: add the not running status and remove the previous not running select, unnecessary!!!

                        //Delete only the NON running jobs
                        //Delete JobProgress
                        sql = @"DELETE  
                            FROM JobProgress
                            WHERE JobID IN @ids; ";
                        if (isSync)
                        {
                            connection.Execute(sql, new { ids = notRunning.ToArray() }, trn);
                        }
                        else
                        {
                            await connection.ExecuteAsync(sql, new { ids = notRunning.ToArray() }, trn);
                        }

                        //Delete Job
                        sql = @"DELETE  
                            FROM Job
                            WHERE JobID IN @ids; ";
                        if (isSync)
                        {
                            count = connection.Execute(sql, new { ids = notRunning.ToArray() }, trn);
                        }
                        else
                        {
                            count = await connection.ExecuteAsync(sql, new { ids = notRunning.ToArray() }, trn);
                        }
                    }

                    trn.Commit();
                }
            }

            return count;
        }

        /// <summary>
        /// Delete past jobs with specified status(es). 
        /// </summary>
        /// <param name="hours">Job create hours in the past</param>
        /// <param name="statusList">A list of job's status to delete. Null job status is valid. Default is JobStatus.Completed.</param>
        public int Delete(int hours, ICollection<JobStatus?> statusList)
        {
            return DeleteAsync(hours, statusList, true).GetAwaiter().GetResult();
        }

        public Task<int> DeleteAsync(int hours, ICollection<JobStatus?> statusList)
        {
            return DeleteAsync(hours, statusList, false);
        }

        private async Task<int> DeleteAsync(int hours, ICollection<JobStatus?> statusList, bool isSync)
        {
            var whereQuery = "j.Created < DATEADD(hour, -@hours, GETDATE())";

            //build where status
            if (statusList != null && statusList.Any())
            {
                var whereStatus = "";
                foreach (var status in statusList)
                {
                    whereStatus += string.IsNullOrWhiteSpace(whereStatus) ? "" : " OR ";
                    if (status == null)
                    {
                        whereStatus += "j.Status IS NULL";
                    }
                    else
                    {
                        whereStatus += "j.Status = " + (int)status;
                    }
                }

                if (!string.IsNullOrWhiteSpace(whereStatus))
                {
                    whereQuery += " AND " + "(" + whereStatus + ")";
                }
            }

            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                //Get only the NON running jobs
                var sql = @"SELECT j.JobID 
                            FROM Job j
                            WHERE  " + whereQuery 
                            + " ORDER BY j.Created, j.JobID; "; // FIFO deletion
                var deleteIDs = new List<string>();
                if (isSync)
                {
                    deleteIDs = connection.Query<string>(sql, new { hours }).ToList();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<string>(sql, new { hours });
                    deleteIDs = taskResult.ToList();
                }

                using (var trn = connection.BeginTransaction())
                {
                    if (deleteIDs.Count > 0)
                    {
                        //Delete JobProgress
                        sql = @"DELETE  
                            FROM JobProgress
                            WHERE JobID IN @ids; ";
                        if (isSync)
                        {
                            connection.Execute(sql, new { ids = deleteIDs.ToArray() }, trn);
                        }
                        else
                        {
                            await connection.ExecuteAsync(sql, new { ids = deleteIDs.ToArray() }, trn);
                        }

                        //Delete Job
                        sql = @"DELETE  
                            FROM Job
                            WHERE JobID IN @ids; ";
                        if (isSync)
                        {
                            count = connection.Execute(sql, new { ids = deleteIDs.ToArray() }, trn);
                        }
                        else
                        {
                            count = await connection.ExecuteAsync(sql, new { ids = deleteIDs.ToArray() }, trn);
                        }
                    }

                    trn.Commit();
                }
            }

            return count;
        }

        /// <summary>
        ///  Mark job status to JobStatus.Stopped. 
        /// </summary>
        public int SetToStopped(ICollection<string> jobIDs)
        {
            return SetToStatusAsync(jobIDs, JobStatus.Stopped, true).GetAwaiter().GetResult();
        }

        public Task<int> SetToStoppedAsync(ICollection<string> jobIDs)
        {
            return SetToStatusAsync(jobIDs, JobStatus.Stopped, false);
        }

        /// <summary>
        ///  Mark job status to JobStatus.Paused. 
        /// </summary>
        public int SetToPaused(ICollection<string> jobIDs)
        {
            return SetToStatusAsync(jobIDs, JobStatus.Paused, true).GetAwaiter().GetResult();
        }

        public Task<int> SetToPausedAsync(ICollection<string> jobIDs)
        {
            return SetToStatusAsync(jobIDs, JobStatus.Paused, false);
        }

        /// <summary>
        ///  Mark job status to JobStatus.Running. 
        ///  Used after pause to continue running job.
        /// </summary>
        public int SetToRunning(ICollection<string> jobIDs)
        {
            return SetToStatusAsync(jobIDs, JobStatus.Running, true).GetAwaiter().GetResult();
        }

        public Task<int> SetToRunningAsync(ICollection<string> jobIDs)
        {
            return SetToStatusAsync(jobIDs, JobStatus.Running, false);
        }

        private async Task<int> SetToStatusAsync(ICollection<string> jobIDs, JobStatus jobStatus, bool isSync)
        {
            if (jobIDs.Count == 0)
                return 0;

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"UPDATE Job 
                            SET 
                            Command = NULL, 
                            Status = @status
                            WHERE JobID IN @ids; ";
                if (isSync)
                {
                    return connection.Execute(sql, new { status = jobStatus, ids = jobIDs.ToArray() });
                }
                else
                {
                    return await connection.ExecuteAsync(sql, new { status = jobStatus, ids = jobIDs.ToArray() });
                }
            }
        }

        #endregion

        #region Count Status

        /// <summary>
        /// Return Job Status Count based on appID and/or userID.
        /// Must use unique appID for multi tenant client apps.
        /// Can return count based on only userID for single tenant client apps.
        /// </summary>
        /// <param name="appID"></param>
        /// <param name="userID"></param>
        /// <returns>JobStatusCount</returns>
        public IReadOnlyCollection<JobStatusCount> GetJobStatusCount(string appID, string userID)
        {
            return GetJobStatusCountAsync(appID, userID, true).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyCollection<JobStatusCount>> GetJobStatusCountAsync(string appID, string userID)
        {
            return GetJobStatusCountAsync(appID, userID, false);
        }

        private async Task<IReadOnlyCollection<JobStatusCount>> GetJobStatusCountAsync(string appID, string userID, bool isSync)
        {
            var countList = new List<JobStatusCount>();
            using (var connection = new SqlConnection(connectionString))
            {
                var sql = "";
                if (!string.IsNullOrWhiteSpace(appID) && !string.IsNullOrWhiteSpace(userID))
                {
                    sql = @"SELECT [Status], Count([Status]) [Count], sum(case when [Status] IS null then 1 else 0 end) NullCount
                          FROM [Job]
                          WHERE [AppID] = @appID AND [UserID] = @userID
                          GROUP BY [Status]; ";
                }
                else if (!string.IsNullOrWhiteSpace(appID) && string.IsNullOrWhiteSpace(userID)) //appID not null, userID is null
                {
                    sql = @"SELECT [Status], Count([Status]) [Count], sum(case when [Status] IS null then 1 else 0 end) NullCount
                          FROM [Job]
                          WHERE [AppID] = @appID
                          GROUP BY [Status]; ";
                }
                else if (string.IsNullOrWhiteSpace(appID) && !string.IsNullOrWhiteSpace(userID)) //appID is null, userID not null
                {
                    //This works okay for single tenant/app, but for multi-tenant, there can be multiple UserID for the different apps
                    //Works okay for multi tenant apps with GUID for UserID
                    sql = @"SELECT [Status], Count([Status]) [Count], sum(case when [Status] IS null then 1 else 0 end) NullCount
                          FROM [Job]
                          WHERE [UserID] = @userID
                          GROUP BY [Status]; ";
                }
                else
                {
                    sql = @"SELECT [Status], Count([Status]) [Count], sum(case when [Status] IS null then 1 else 0 end) NullCount
                          FROM [Job]
                          GROUP BY [Status]; ";
                }
                if (isSync)
                {

                    countList = connection.Query<JobStatusCount>(sql, new { appID, userID }).ToList();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<JobStatusCount>(sql, new { appID, userID });
                    countList = taskResult.ToList();
                }
            }

            return countList;
        }

        #endregion

        #region Various ways to get Jobs
        /// <summary>
        ///  Get Job object by specific jobID.
        /// </summary>
        /// <param name="jobID">The existing unique jobID</param>
        /// <returns>Job</returns>
        public Job GetJob(string jobID)
        {
            return GetJobAsync(jobID, true).GetAwaiter().GetResult();
        }

        public Task<Job> GetJobAsync(string jobID)
        {
            return GetJobAsync(jobID, false);
        }

        private async Task<Job> GetJobAsync(string jobID, bool isSync)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"SELECT *
                            FROM Job 
                            WHERE JobID = @jobID; ";
                if (isSync)
                {
                    return connection.Query<Job>(sql, new { jobID }).FirstOrDefault();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<Job>(sql, new { jobID });
                    return taskResult.FirstOrDefault();
                }

            }
        }

        /// <summary>
        ///  Get Jobs object by a group of jobIDs.
        /// </summary>
        /// <param name="jobIDs">group of jobIDs</param>
        /// <returns>List of Jobs</returns>
        public IReadOnlyCollection<Job> GetJobs(IEnumerable<string> jobIDs)
        {
            return GetJobsAsync(jobIDs, true).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyCollection<Job>> GetJobsAsync(IEnumerable<string> jobIDs)
        {
            return GetJobsAsync(jobIDs, false);
        }

        private async Task<IReadOnlyCollection<Job>> GetJobsAsync(IEnumerable<string> jobIDs, bool isSync)
        {
            var jobList = new List<Job>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"SELECT *
                            FROM Job 
                            WHERE JobID IN @ids; ";
                if (isSync)
                {
                    jobList = connection.Query<Job>(sql, new { ids = jobIDs.ToArray() }).ToList();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<Job>(sql, new { ids = jobIDs.ToArray() });
                    jobList = taskResult.ToList();
                }
            }

            return jobList;
        }

        /// <summary>
        ///  Get JobView by specific jobID.
        /// </summary>
        /// <param name="jobID">The existing unique jobID</param>
        /// <returns>JobView</returns>
        public JobView GetJobView(string jobID)
        {
            return GetJobViewAsync(jobID, true).GetAwaiter().GetResult();
        }

        public Task<JobView>GetJobViewAsync(string jobID)
        {
            return GetJobViewAsync(jobID, false);
        }

        private async Task<JobView> GetJobViewAsync(string jobID, bool isSync)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"SELECT *
                            FROM JobView 
                            WHERE JobID = @jobID; ";
                if (isSync)
                {
                    return connection.Query<JobView>(sql, new { jobID }).FirstOrDefault();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<JobView>(sql, new { jobID });
                    return taskResult.FirstOrDefault();
                }
            }
        }

        /// <summary>
        /// Get ready to run jobs by specified job IDs.
        /// </summary>
        /// <param name="jobIDs"></param>
        /// <returns></returns>
        public IReadOnlyCollection<Job> GetNonRunningJobsByIDs(IEnumerable<string> jobIDs)
        {
            return GetNonRunningJobsByIDsAsync(jobIDs, true).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyCollection<Job>> GetNonRunningJobsByIDsAsync(IEnumerable<string> jobIDs)
        {
            return GetNonRunningJobsByIDsAsync(jobIDs, false);
        }

        private async Task<IReadOnlyCollection<Job>> GetNonRunningJobsByIDsAsync(IEnumerable<string> jobIDs, bool isSync)
        {
            var jobList = new List<Job>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"SELECT * 
                            FROM Job j
                            WHERE j.JobID IN @ids 
                            AND j.Status IS NULL; ";
                if (isSync)
                {
                    jobList = connection.Query<Job>(sql, new { ids = jobIDs.ToArray() }).ToList();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<Job>(sql, new { ids = jobIDs.ToArray() });
                    jobList = taskResult.ToList();
                }
            }
            return jobList;
        }

        /// <summary>
        ///  Return all job IDs by specified command and owned by processID. And all jobs with specified command, but no owner.
        /// </summary>
        /// <param name="processID">The processID owning the jobs</param>
        /// <param name="command">The command specified in JobCommand</param>
        /// <returns>List of JobIDs</returns>
        public IReadOnlyCollection<string> GetJobIdsByProcessAndCommand(string processID, string command)
        {
            return GetJobIdsByProcessAndCommandAsync(processID, command, true).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyCollection<string>> GetJobIdsByProcessAndCommandAsync(string processID, string command)
        {
            return GetJobIdsByProcessAndCommandAsync(processID, command, false);
        }

        private async Task<IReadOnlyCollection<string>> GetJobIdsByProcessAndCommandAsync(string processID, string command, bool isSync)
        {
            var jobIds = new List<string>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"SELECT j.JobID 
                            FROM Job j
                            WHERE (j.ProcessID = @processID OR j.ProcessID IS NULL)
                            AND j.Command = @command; ";
                if (isSync)
                {
                    jobIds = connection.Query<string>(sql, new { processID, command }).ToList();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<string>(sql, new { processID, command });
                    jobIds = taskResult.ToList();
                }
            }

            return jobIds;
        }

        /// <summary>
        /// Return jobs based on owner processID and by job's status.
        /// </summary>
        /// <param name="processID">Owner processID</param>
        /// <param name="status">JobStatus</param>
        /// <returns>List of Jobs</returns>
        public IReadOnlyCollection<Job> GetJobsByProcessAndStatus(string processID, JobStatus status)
        {
            return GetJobsByProcessAndStatusAsync(processID, status, true).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyCollection<Job>> GetJobsByProcessAndStatusAsync(string processID, JobStatus status)
        {
            return GetJobsByProcessAndStatusAsync(processID, status, false);
        }

        private async Task<IReadOnlyCollection<Job>> GetJobsByProcessAndStatusAsync(string processID, JobStatus status, bool isSync)
        {
            var jobList = new List<Job>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"SELECT * 
                            FROM Job j
                            WHERE j.ProcessID = @processID
                            AND j.Status = @status; ";
                if (isSync)
                {
                    jobList = connection.Query<Job>(sql, new { processID, status }).ToList();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<Job>(sql, new { processID, status });
                    jobList = taskResult.ToList();
                }
            }

            return jobList;
        }

        /// <summary>
        /// Return job views based on page index and page size.
        /// </summary>
        /// <param name="pageIndex">Page index</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>Total count of job views and list of JobViews</returns>
        public JobViewList GetJobViews(int? pageIndex, int? pageSize)
        {
            return GetJobViewsAsync(pageIndex, pageSize, true).GetAwaiter().GetResult();
        }

        public Task<JobViewList> GetJobViewsAsync(int? pageIndex, int? pageSize)
        {
            return GetJobViewsAsync(pageIndex, pageSize, false);
        }

        private async Task<JobViewList> GetJobViewsAsync(int? pageIndex, int? pageSize, bool isSync)
        {
            var result = new List<JobView>();
            var totalCount = 0;

            pageIndex = pageIndex == null || pageIndex == 0 ? 1 : pageIndex; //default to 1
            pageSize = pageSize == null || pageSize == 0 ? 10 : pageSize; //default to 10
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var offset = (pageIndex - 1) * pageSize;
                var sqlQuery = @"SELECT COUNT(JobID) FROM JobView;
                                SELECT * 
                                FROM JobView jv 
                                ORDER BY jv.Created, jv.JobID
                                OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY;";
                if (isSync)
                {
                    using (var multiResult = connection.QueryMultiple(sqlQuery))
                    {
                        totalCount = multiResult.Read<int>().Single();
                        result = multiResult.Read<JobView>().ToList();
                    }
                }
                else
                {
                    using (var multiResult = await connection.QueryMultipleAsync(sqlQuery))
                    {
                        totalCount = multiResult.Read<int>().Single();
                        result = multiResult.Read<JobView>().ToList();
                    }
                }
            }

            var jobViewList = new JobViewList();
            jobViewList.Total = totalCount;
            jobViewList.Items = result;

            return jobViewList;
        }
        #endregion

        #region ManageJobs by Server
        /// <summary>
        /// Set job status to running and set start date and time to now.
        /// </summary>
        /// <param name="processID">process ID</param>
        /// <param name="jobID">job ID</param>
        /// <returns>Updated record count, 0 or 1 record updated</returns>
        public int SetToRunning(string processID, string jobID)
        {
            return SetToRunningAsync(processID, jobID, true).GetAwaiter().GetResult();
        }

        public Task<int> SetToRunningAsync(string processID, string jobID)
        {
            return SetToRunningAsync(processID, jobID, false);
        }

        private async Task<int> SetToRunningAsync(string processID, string jobID, bool isSync)
        {
            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = "UPDATE [Job] SET Status = @status, [Start] = @start WHERE JobID = @jobID AND ProcessID = @processID;";
                if (isSync)
                {
                    count = connection.Execute(sql, new { status = JobStatus.Running, start = DateTime.Now, jobID, processID });
                }
                else
                {
                    count = await connection.ExecuteAsync(sql, new { status = JobStatus.Running, start = DateTime.Now, jobID, processID });
                }
            }

            return count;
        }

        /// <summary>
        /// Set job status to error and fill in the error message.
        /// </summary>
        /// <param name="processID">process ID</param>
        /// <param name="jobID">job ID</param>
        /// <param name="error">Error message</param>
        /// <returns>Updated record count, 0 or 1 record updated</returns>
        public int SetError(string processID, string jobID, string error)
        {
            return SetErrorAsync(processID, jobID, error, true).GetAwaiter().GetResult();
        }

        public Task<int> SetErrorAsync(string processID, string jobID, string error)
        {
            return SetErrorAsync(processID, jobID, error, false);
        }

        private async Task<int> SetErrorAsync(string processID, string jobID, string error, bool isSync)
        {
            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                var sql = "UPDATE [Job] SET [Error] = @error, [Status] = @status WHERE JobID = @jobID AND ProcessID = @processID;";
                if (isSync)
                {
                    count = connection.Execute(sql, new { error, status = JobStatus.Error, jobID, processID });
                }
                else
                {
                    count = await connection.ExecuteAsync(sql, new { error, status = JobStatus.Error, jobID, processID });
                }
            }

            return count;
        }

        /// <summary>
        /// Set error message.
        /// </summary>
        /// <param name="processID">process ID</param>
        /// <param name="jobID">job ID</param>
        /// <param name="error">Error message</param>
        /// <returns>Updated record count, 0 or 1 record updated</returns>
        public int SetErrorMessage(string processID, string jobID, string error)
        {
            return SetErrorMessageAsync(processID, jobID, error, true).GetAwaiter().GetResult();
        }

        public Task<int> SetErrorMessageAsync(string processID, string jobID, string error)
        {
            return SetErrorMessageAsync(processID, jobID, error, false);
        }

        private async Task<int> SetErrorMessageAsync(string processID, string jobID, string error, bool isSync)
        {
            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                var sql = "UPDATE [Job] SET [Error] = @error WHERE JobID = @jobID AND ProcessID = @processID;";
                if (isSync)
                {
                    count = connection.Execute(sql, new { error, jobID, processID });
                }
                else
                {
                    count = await connection.ExecuteAsync(sql, new { error, jobID, processID });
                }
            }

            return count;
        }


        /// <summary>
        /// Set job as completed.
        /// </summary>
        /// <param name="processID">process ID</param>
        /// <param name="jobID">job ID</param>
        /// <returns>Updated record count, 0 or 1 record updated</returns>
        public int SetToCompleted(string processID, string jobID)
        {
            return SetToCompletedAsync(processID, jobID, true).GetAwaiter().GetResult();
        }

        public Task<int> SetToCompletedAsync(string processID, string jobID)
        {
            return SetToCompletedAsync(processID, jobID, false);
        }

        private async Task<int> SetToCompletedAsync(string processID, string jobID, bool isSync)
        {
            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = "UPDATE [Job] SET Command = '', Status = @status, [End] = @end WHERE JobID = @jobID AND ProcessID = @processID;";
                if (isSync)
                {
                    count = connection.Execute(sql, new { status = JobStatus.Completed, end = DateTime.Now, jobID, processID });
                }
                else
                {
                    count = await connection.ExecuteAsync(sql, new { status = JobStatus.Completed, end = DateTime.Now, jobID, processID });
                }
            }

            return count;
        }

        /// <summary>
        /// Count how many running jobs owned by processID.
        /// </summary>
        /// <param name="processID">Owner processID</param>
        /// <returns>Total count of running jobs.</returns>
        public int CountRunningJobs(string processID)
        {
            return CountRunningJobsAsync(processID, true).GetAwaiter().GetResult();
        }

        public Task<int> CountRunningJobsAsync(string processID)
        {
            return CountRunningJobsAsync(processID, false);
        }

        private async Task<int> CountRunningJobsAsync(string processID, bool isSync)
        {
            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"SELECT COUNT(j.JobID) 
                            FROM Job j
                            WHERE j.Status = @status 
                            AND j.ProcessID = @processID; ";
                if (isSync)
                {
                    count = connection.Query<int>(sql, new { status = JobStatus.Running, processID }).FirstOrDefault();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<int>(sql, new { status = JobStatus.Running, processID });
                    count = taskResult.FirstOrDefault();
                }
            }

            return count;
        }

        /// <summary>
        /// Claim specific number of ready to run jobs to be owned by processID.
        /// Use Optimistic Concurrency, don't claim job if it's already running.
        /// </summary>
        /// <param name="processID">Owner processID</param>
        /// <param name="maxNum">Number of jobs to claim</param>
        /// <returns>List of jobs claimed by processID</returns>
        public IReadOnlyCollection<Job> ClaimJobsToRun(string processID, int maxNum)
        {
            var jobList = GetJobsToRun(maxNum);
            return ClaimJobsToRun(processID, jobList.ToList());
        }

        public async Task<IReadOnlyCollection<Job>> ClaimJobsToRunAsync(string processID, int maxNum)
        {
            var jobList = await GetJobsToRunAsync(maxNum);
            return await ClaimJobsToRunAsync(processID, jobList.ToList());
        }

        /// <summary>
        /// Attempt to claim specific jobs to be owned by processID.
        /// Use Optimistic Concurrency, don't claim job if it's already running or claimed by someone else.
        /// </summary>
        /// <param name="processID">Owner processID</param>
        /// <param name="jobList">List of jobs to claim</param>
        /// <returns>List of actual jobs claimed by processID</returns>
        public IReadOnlyCollection<Job> ClaimJobsToRun(string processID, ICollection<Job> jobList)
        {
            return ClaimJobsToRunAsync(processID, jobList, true).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyCollection<Job>> ClaimJobsToRunAsync(string processID, ICollection<Job> jobList)
        {
            return ClaimJobsToRunAsync(processID, jobList, false);
        }

        private async Task<IReadOnlyCollection<Job>> ClaimJobsToRunAsync(string processID, ICollection<Job> jobList, bool isSync)
        {
            var claimedJobs = new List<Job>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach(var job in jobList)
                {
                    var count = 0;
                    try
                    {
                        var sql = @"UPDATE [Job]
                                SET ProcessID = @processID 
                                WHERE Status IS NULL 
                                AND ProcessID IS NULL
                                AND [jobID] = @jobID; ";
                        if (isSync)
                        {
                            count = connection.Execute(sql, new { processID, job.JobID });
                        } 
                        else
                        {
                            count = await connection.ExecuteAsync(sql, new { processID, job.JobID });
                        }

                        job.ProcessID = processID; //set it similar to DB record!
                    }
                    catch (Exception exc)
                    {
                        //just mark error, don't stop
                        var error = job.Error + " ClaimJobsToRun error: " + exc.ToString();
                        if (isSync)
                            SetError(processID, job.JobID, error); //set error in storage
                        else
                            await SetErrorAsync(processID, job.JobID, error); 
                        job.Status = JobStatus.Error;
                        job.Error = error;
                        continue;
                    }

                    if (count > 0) //successful update
                        claimedJobs.Add(job);
                }
            }

            return claimedJobs; //it's possible to return less than passed jobIDs, since multiple Shift server might run and already claimed the job(s)
        }

        /// <summary>
        /// Return ready to run or 'run-now' jobs based on a set number, don't return if it's already claimed by other processes.
        /// Sort by inserted date and by 'run-now' command. The jobs with 'run-now' command are given highest priority.
        /// </summary>
        /// <param name="maxNum">Maximum number to return</param>
        /// <returns>List of jobs</returns>
        public IReadOnlyCollection<Job> GetJobsToRun(int maxNum)
        {
            return GetJobsToRunAsync(maxNum, true).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyCollection<Job>> GetJobsToRunAsync(int maxNum)
        {
            return GetJobsToRunAsync(maxNum, false);
        }

        private async Task<IReadOnlyCollection<Job>> GetJobsToRunAsync(int maxNum, bool isSync)
        {
            var jobList = new List<Job>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"SELECT * 
                            FROM Job j
                            WHERE j.Status IS NULL 
                            AND j.ProcessID IS NULL
                            AND (j.Command = @runNow OR j.Command IS NULL)
                            ORDER BY j.Command DESC, j.Created, j.JobID
                            OFFSET 0 ROWS FETCH NEXT @maxNum ROWS ONLY; ";
                if (isSync)
                {
                    jobList = connection.Query<Job>(sql, new { runNow = JobCommand.RunNow, maxNum }).ToList();
                }
                else
                {
                    var taskResult = await connection.QueryAsync<Job>(sql, new { runNow = JobCommand.RunNow, maxNum });
                    jobList = taskResult.ToList();
                }
            }

            return jobList;
        }

        /// <summary>
        /// Set progress for specific job.
        /// </summary>
        /// <param name="jobID">jobID</param>
        /// <param name="percent">% of progress</param>
        /// <param name="note">Any type of note for the progress</param>
        /// <param name="data">Any data for the progress</param>
        /// <returns>0 for no insert/update, 1 for successful insert/update</returns>
        public int SetProgress(string jobID, int? percent, string note, string data)
        {
            return SetProgressAsync(jobID, percent, note, data, true).GetAwaiter().GetResult();
        }

        public Task<int> SetProgressAsync(string jobID, int? percent, string note, string data)
        {
            return SetProgressAsync(jobID, percent, note, data, false);
        }

        private async Task<int> SetProgressAsync(string jobID, int? percent, string note, string data, bool isSync)
        {
            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var found = connection.Query<int>("SELECT COUNT(JobID) FROM JobProgress WHERE JobID = @jobID;", new { jobID }).FirstOrDefault(); ;
                if (found == 0)
                {
                    //INSERT
                    var sql = @"INSERT INTO JobProgress ([JobID], [Percent], [Note], [Data]) 
                                VALUES ( @jobID, @percent, @note, @data ); ";
                    if (isSync)
                    {
                        count = connection.Execute(sql, new { jobID, percent, note, data });
                    }
                    else
                    {
                        count = await connection.ExecuteAsync(sql, new { jobID, percent, note, data });
                    }
                }
                else
                {
                    //UPDATE
                    var sql = @"UPDATE [JobProgress] SET [Percent] = @percent, Note = @note, Data = @data 
                                WHERE JobID = @jobID; ";
                    if (isSync)
                    {
                        count = connection.Execute(sql, new { percent, note, data, jobID });
                    }
                    else
                    {
                        count = await connection.ExecuteAsync(sql, new { percent, note, data, jobID });
                    }
                }
            }


            return count;
        }

        /// <summary>
        /// Update progress, similar to SetProgress() method. 
        /// Higher performance than SetProgress(). This method only uses 1 call to the database storage.
        /// </summary>
        /// <param name="jobID">jobID</param>
        /// <param name="percent">% of progress</param>
        /// <param name="note">Any type of note for the progress</param>
        /// <param name="data">Any data for the progress</param>
        /// <returns>0 for no update, 1 for successful update</returns>
        public async Task<int> UpdateProgressAsync(string jobID, int? percent, string note, string data)
        {
            var count = 0;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var sql = @"UPDATE [JobProgress] SET [Percent] = @percent, Note = @note, Data = @data 
                            WHERE JobID = @jobID; ";
                count = await connection.ExecuteAsync(sql, new { percent, note, data, jobID });
            }

            return count;
        }

        #endregion

        public JobStatusProgress GetProgress(string jobID)
        {
            return GetProgressAsync(jobID, true).GetAwaiter().GetResult();
        }

        public Task<JobStatusProgress> GetProgressAsync(string jobID)
        {
            return GetProgressAsync(jobID, false);
        }

        private async Task<JobStatusProgress> GetProgressAsync(string jobID, bool isSync)
        {
            JobStatusProgress jsProgress = new JobStatusProgress();
            var jobView = isSync ? GetJobView(jobID) : await GetJobViewAsync(jobID);
            if (jobView != null)
            {
                jsProgress.JobID = jobView.JobID;
                jsProgress.Status = jobView.Status;
                jsProgress.Error = jobView.Error;
                jsProgress.Percent = jobView.Percent;
                jsProgress.Note = jobView.Note;
                jsProgress.Data = jobView.Data;
            }
            else
            {
                jsProgress.JobID = jobID;
                jsProgress.ExistsInDB = false;
                jsProgress.Error = "Job progress id: " + jobID + " not found!";
            }

            return jsProgress;
        }

    }
}
