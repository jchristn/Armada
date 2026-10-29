namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada vessel overlap-preview</c>.
    /// </summary>
    public class VesselOverlapPreviewSettings : BriefInputSettings
    {
        /// <summary>
        /// Vessel name or id.
        /// </summary>
        [Description("Vessel name or ID")]
        [CommandArgument(0, "<vessel>")]
        public string Vessel { get; set; } = string.Empty;

        /// <summary>
        /// Mission to leave out of the comparison.
        /// </summary>
        [Description("Leave this mission out of the comparison (msn_ id, for a mission being edited)")]
        [CommandOption("--exclude-mission")]
        public string? ExcludeMission { get; set; }
    }
}
