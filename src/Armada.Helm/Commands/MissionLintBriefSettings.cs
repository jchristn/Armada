namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada mission lint-brief</c>.
    /// </summary>
    public class MissionLintBriefSettings : BriefInputSettings
    {
        /// <summary>
        /// Vessel name or id (enables the path checks).
        /// </summary>
        [Description("Vessel name or ID (checks the paths the brief names against it)")]
        [CommandOption("--vessel")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Voyage.
        /// </summary>
        [Description("The voyage the mission would join (vyg_ id)")]
        [CommandOption("--voyage")]
        public string? Voyage { get; set; }
    }
}
