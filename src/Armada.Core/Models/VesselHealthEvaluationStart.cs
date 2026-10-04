namespace Armada.Core.Models
{
    /// <summary>
    /// Outcome of a request to start a vessel health evaluation job.
    /// </summary>
    public class VesselHealthEvaluationStart
    {
        #region Public-Members

        /// <summary>
        /// Identifier of the job that was started, or of the job already running for the tenant.
        /// </summary>
        public string JobId { get; set; } = "";

        /// <summary>
        /// True when no job was started because one is already running for the tenant (HTTP 409).
        /// </summary>
        public bool AlreadyRunning { get; set; } = false;

        /// <summary>
        /// Number of vessels the new job will evaluate (0 when AlreadyRunning).
        /// </summary>
        public int VesselCount { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthEvaluationStart()
        {
        }

        #endregion
    }
}
