namespace Armada.Tui.Screens.Operations
{
    using System;

    /// <summary>
    /// Display values of the open planning session for the Current Session header.
    /// </summary>
    public class PlanningSessionDetailView
    {
        #region Public-Members

        /// <summary>
        /// True when a session is loaded.
        /// </summary>
        public bool HasSession { get; set; } = false;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Captain name.
        /// </summary>
        public string CaptainName { get; set; } = "";

        /// <summary>
        /// Captain runtime.
        /// </summary>
        public string Runtime { get; set; } = "-";

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string VesselName { get; set; } = "";

        /// <summary>
        /// Branch.
        /// </summary>
        public string Branch { get; set; } = "-";

        /// <summary>
        /// Pipeline name.
        /// </summary>
        public string Pipeline { get; set; } = "-";

        /// <summary>
        /// Playbook count.
        /// </summary>
        public int PlaybookCount { get; set; } = 0;

        /// <summary>
        /// Relative update time.
        /// </summary>
        public string Updated { get; set; } = "";

        /// <summary>
        /// Message count.
        /// </summary>
        public int MessageCount { get; set; } = 0;

        /// <summary>
        /// Failure reason, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionDetailView()
        {
        }

        #endregion
    }
}
