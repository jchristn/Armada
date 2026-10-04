namespace Armada.Core.Settings
{
    /// <summary>
    /// Settings for Ask Armada conversation threads (history window, proposal expiry, work tracking, milestone
    /// narration, and turn timeout). All values apply live.
    /// </summary>
    public class AskSettings
    {
        #region Public-Members

        /// <summary>
        /// Number of most recent messages replayed to the captain as conversation history each turn; older history is
        /// represented by the thread summary. Default 20, minimum 2, maximum 200; out-of-range values are clamped.
        /// </summary>
        public int HistoryTurns
        {
            get => _HistoryTurns;
            set => _HistoryTurns = value < 2 ? 2 : (value > 200 ? 200 : value);
        }

        /// <summary>
        /// Minutes a pending action proposal waits for a decision before it expires (never executed). Default 60,
        /// minimum 1, maximum 1440; out-of-range values are clamped.
        /// </summary>
        public int ProposalExpiryMinutes
        {
            get => _ProposalExpiryMinutes;
            set => _ProposalExpiryMinutes = value < 1 ? 1 : (value > 1440 ? 1440 : value);
        }

        /// <summary>
        /// Seconds between work-tracker sweeps of every active tracked item (change notifications are also handled
        /// immediately). Default 5, minimum 2, maximum 300; out-of-range values are clamped.
        /// </summary>
        public int TrackerIntervalSeconds
        {
            get => _TrackerIntervalSeconds;
            set => _TrackerIntervalSeconds = value < 2 ? 2 : (value > 300 ? 300 : value);
        }

        /// <summary>
        /// When true, milestone messages are written by the thread's captain when it is idle; otherwise (or on timeout
        /// or failure) a deterministic sentence is posted. Default true.
        /// </summary>
        public bool NarrateMilestones { get; set; } = true;

        /// <summary>
        /// When false (the default), Ask thread turns and milestone narrations run CLI captains without their
        /// auto-approve or permission-bypass flags, whatever the captain's own auto-approve setting: Ask turns can be
        /// started by any authenticated user, and Armada's proposal gate covers only Armada's MCP tools, not the CLI's
        /// own shell and file tools. Claude Code then runs with <c>--permission-mode acceptEdits</c> in print mode, where
        /// a tool that would need approval is refused rather than prompted, so a turn never waits for input. Set true
        /// only when every account that can use Ask is trusted with a shell on the Admiral host; the captain's own
        /// setting then applies.
        /// </summary>
        public bool CaptainAutoApprove { get; set; } = false;

        /// <summary>
        /// Seconds a milestone narration may take before the deterministic sentence is used instead. Default 60,
        /// minimum 10, maximum 600; out-of-range values are clamped.
        /// </summary>
        public int NarrationTimeoutSeconds
        {
            get => _NarrationTimeoutSeconds;
            set => _NarrationTimeoutSeconds = value < 10 ? 10 : (value > 600 ? 600 : value);
        }

        /// <summary>
        /// Minutes a captain turn may run before it is stopped and marked failed. Default 15, minimum 1, maximum 120;
        /// out-of-range values are clamped.
        /// </summary>
        public int TurnTimeoutMinutes
        {
            get => _TurnTimeoutMinutes;
            set => _TurnTimeoutMinutes = value < 1 ? 1 : (value > 120 ? 120 : value);
        }

        #endregion

        #region Private-Members

        private int _HistoryTurns = 20;
        private int _ProposalExpiryMinutes = 60;
        private int _TrackerIntervalSeconds = 5;
        private int _NarrationTimeoutSeconds = 60;
        private int _TurnTimeoutMinutes = 15;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public AskSettings()
        {
        }

        #endregion
    }
}
