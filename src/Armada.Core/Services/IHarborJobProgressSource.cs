namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// A Harbor job runner that also reports what its jobs are doing: each job's activity as its runtime's structured
    /// output reports it, and where each job's log is. The link client shows both in the Running now list and relays the
    /// activity to the Admiral when the launch asked for it. Implemented by runners that can (the Harbor's
    /// <c>LocalHarborJobRunner</c>); a runner without it reports output only.
    /// </summary>
    public interface IHarborJobProgressSource
    {
        /// <summary>
        /// Raised for each activity a job reports: the job identifier and the activity.
        /// </summary>
        event Action<string, RuntimeActivity>? ActivityReported;

        /// <summary>
        /// The log file of a running job on this machine, or null when it has none.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <returns>Full path, or null.</returns>
        string? LogPathOf(string jobId);
    }
}
