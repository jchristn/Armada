namespace Armada.Core.Services
{
    /// <summary>
    /// Ties a delegated captain job to the Harbor running it and the host process id it reported, so a
    /// later stop or liveness check by process id can be routed to the right Harbor and job.
    /// </summary>
    public class HarborJobHandle
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier running the job.
        /// </summary>
        public string HarborId { get; }

        /// <summary>
        /// Job identifier.
        /// </summary>
        public string JobId { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="jobId">Job identifier.</param>
        public HarborJobHandle(string harborId, string jobId)
        {
            HarborId = harborId;
            JobId = jobId;
        }

        #endregion
    }
}
