namespace Armada.Tui.Screens.Admin
{
    /// <summary>
    /// The error body the server returns on a failed request (<c>{ Error, Message }</c>), used by the setup
    /// wizard's raw dispatch call.
    /// </summary>
    public class SetupWizardErrorBody
    {
        #region Public-Members

        /// <summary>
        /// Error category.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Message.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion
    }
}
