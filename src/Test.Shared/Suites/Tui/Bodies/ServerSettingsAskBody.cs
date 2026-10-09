namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Ask group of PUT /api/v1/settings; nullable so an absent field reads as null (the production defaults would mask it).
    /// </summary>
    public class ServerSettingsAskBody
    {
        #region Public-Members

        /// <summary>
        /// Whether the captain reports the outcome of finished work.
        /// </summary>
        public bool? ReportResultsOnCompletion { get; set; } = null;

        /// <summary>
        /// Whether milestones are narrated.
        /// </summary>
        public bool? NarrateMilestones { get; set; } = null;

        /// <summary>
        /// Captain auto-approve (not edited in the TUI; must round-trip).
        /// </summary>
        public bool? CaptainAutoApprove { get; set; } = null;

        /// <summary>
        /// History turns.
        /// </summary>
        public int? HistoryTurns { get; set; } = null;

        /// <summary>
        /// Turn timeout in minutes.
        /// </summary>
        public int? TurnTimeoutMinutes { get; set; } = null;

        #endregion
    }
}
