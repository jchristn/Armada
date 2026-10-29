namespace Armada.Server.Ask
{
    /// <summary>
    /// The confirmation arguments of run_blackbox_recovery and apply_blackbox_recommendation, read to decide whether an
    /// approved Ask proposal runs the dry run and then confirms it.
    /// </summary>
    public class BlackBoxConfirmArgs
    {
        #region Public-Members

        /// <summary>
        /// Dry run flag as proposed, or null when left unset.
        /// </summary>
        public bool? DryRun { get; set; } = null;

        /// <summary>
        /// Confirmation token as proposed, or null.
        /// </summary>
        public string? ConfirmToken { get; set; } = null;

        #endregion
    }
}
