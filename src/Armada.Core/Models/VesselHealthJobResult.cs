namespace Armada.Core.Models
{
    /// <summary>
    /// Result summary of a vessel health evaluation job, serialized into the job's ResultJson.
    /// </summary>
    public class VesselHealthJobResult
    {
        #region Public-Members

        /// <summary>
        /// Number of vessels the job set out to evaluate.
        /// </summary>
        public int Requested { get; set; } = 0;

        /// <summary>
        /// Number of vessels evaluated and persisted.
        /// </summary>
        public int Evaluated { get; set; } = 0;

        /// <summary>
        /// Number of vessels whose evaluation failed (each failure is isolated and logged).
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Whether the job forced dependency checks.
        /// </summary>
        public bool Force { get; set; } = false;

        /// <summary>
        /// Whether the job was started by the scheduler rather than a person.
        /// </summary>
        public bool Scheduled { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthJobResult()
        {
        }

        #endregion
    }
}
