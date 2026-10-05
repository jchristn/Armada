namespace Armada.Tui.Screens.Activity
{
    /// <summary>
    /// The optional mission id some signal responses carry beyond the core <c>Signal</c> model (the dashboard's
    /// <c>SignalWithMission</c>).
    /// </summary>
    public class SignalMissionLink
    {
        #region Public-Members

        /// <summary>
        /// Related mission id, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        #endregion
    }
}
