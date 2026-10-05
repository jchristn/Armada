namespace Armada.Tui.Screens.Admin
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of the result message shown under a setup wizard step.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SetupWizardResultKindEnum
    {
        /// <summary>
        /// The step succeeded.
        /// </summary>
        Success = 0,

        /// <summary>
        /// The step failed or did not validate.
        /// </summary>
        Error = 1,

        /// <summary>
        /// Informational (for example a dispatch warning).
        /// </summary>
        Info = 2
    }
}
