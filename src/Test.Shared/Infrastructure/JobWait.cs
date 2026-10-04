namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Waits for a background job to reach a terminal status. Background workers update their batch (import,
    /// discovery, categorization) before they finish the job, so a test that waits on the batch and then reads the job
    /// can see it still Running; the job's terminal status is the completion signal to wait on.
    /// </summary>
    public static class JobWait
    {
        #region Public-Methods

        /// <summary>
        /// Poll until the job is Succeeded, Failed, or Cancelled.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="jobId">Job id.</param>
        /// <param name="timeoutSeconds">Deadline in seconds (default 30).</param>
        /// <returns>The finished job.</returns>
        /// <exception cref="AssertionException">The job did not finish in time.</exception>
        public static async Task<Job> ForTerminalAsync(DatabaseDriver db, string jobId, int timeoutSeconds = 30)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(timeoutSeconds));
            Job? job = null;
            while (!deadline.Passed)
            {
                job = await db.Jobs.ReadAsync(jobId).ConfigureAwait(false);
                if (job != null && (job.Status == JobStatusEnum.Succeeded || job.Status == JobStatusEnum.Failed || job.Status == JobStatusEnum.Cancelled))
                    return job;
                await Task.Delay(25).ConfigureAwait(false);
            }

            throw new AssertionException("Timed out waiting for job " + jobId + " to finish (status " + job?.Status + ")");
        }

        #endregion
    }
}
