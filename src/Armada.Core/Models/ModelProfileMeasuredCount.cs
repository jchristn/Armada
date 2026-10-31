namespace Armada.Core.Models
{
    /// <summary>
    /// One count of the profile's measured block: how many attempts of a probe, or of a pack's battery tasks for one
    /// persona, passed. Counts only, never a derived score.
    /// </summary>
    public class ModelProfileMeasuredCount
    {
        #region Public-Members

        /// <summary>
        /// Probe kind (for example OutputContract) or battery pack identifier.
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Attempts that passed.
        /// </summary>
        public int Passed { get; set; } = 0;

        /// <summary>
        /// Attempts that failed.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Attempts that could not run.
        /// </summary>
        public int CouldNotRun { get; set; } = 0;

        /// <summary>
        /// Attempts that ran but wait for a subjective grade (counted as neither passed nor failed).
        /// </summary>
        public int PendingEvaluation { get; set; } = 0;

        /// <summary>
        /// Every attempt: passed, failed, could not run and pending evaluation.
        /// </summary>
        public int Attempted { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileMeasuredCount()
        {
        }

        #endregion
    }
}
