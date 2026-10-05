namespace Armada.Tui.Screens.Admin
{
    using Armada.Core.Models;

    /// <summary>
    /// The wrapped form of the dispatch response (<c>POST /api/v1/missions</c>): the server returns
    /// <c>{ Mission, Warning }</c> when the mission was created but no captain could take it yet, and the bare
    /// mission otherwise.
    /// </summary>
    public class SetupWizardDispatchResponse
    {
        #region Public-Members

        /// <summary>
        /// Mission, when the response is wrapped.
        /// </summary>
        public Mission? Mission { get; set; } = null;

        /// <summary>
        /// Warning, when the response is wrapped.
        /// </summary>
        public string? Warning { get; set; } = null;

        #endregion
    }
}
