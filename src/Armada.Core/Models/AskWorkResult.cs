namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of finished tracked work, built from typed data Armada already stores (mission status and failure
    /// reason, the captain's final message, check runs and their test results, landing outcome, diff size, and elapsed
    /// time). Used to write the final milestone of an Ask Armada thread and the context of the captain's report.
    /// </summary>
    public class AskWorkResult
    {
        #region Public-Members

        /// <summary>
        /// Tracked work identifier.
        /// </summary>
        public string TrackedWorkId
        {
            get => _TrackedWorkId;
            set => _TrackedWorkId = value ?? String.Empty;
        }

        /// <summary>
        /// Wall-clock time from the first start to completion in milliseconds, or null when unknown.
        /// </summary>
        public long? ElapsedMs { get; set; } = null;

        /// <summary>
        /// Per-mission results, in the order of the work snapshot.
        /// </summary>
        public List<AskWorkMissionResult> Missions
        {
            get => _Missions;
            set => _Missions = value ?? new List<AskWorkMissionResult>();
        }

        #endregion

        #region Private-Members

        private string _TrackedWorkId = String.Empty;
        private List<AskWorkMissionResult> _Missions = new List<AskWorkMissionResult>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkResult()
        {
        }

        #endregion
    }
}
