namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox gaps</c>.
    /// </summary>
    public class BlackBoxGapsSettings : BaseSettings
    {
        /// <summary>
        /// Vessel filter.
        /// </summary>
        [Description("Only this vessel (vsl_ ID)")]
        [CommandOption("--vessel <VESSEL>")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Resolution filter.
        /// </summary>
        [Description("Resolution: Open, Resolved, Duplicate, WontFix, ArmadaFixed")]
        [CommandOption("--resolution <RESOLUTION>")]
        public string? Resolution { get; set; }

        /// <summary>
        /// Phase filter.
        /// </summary>
        [Description("Phase (Handoff, Landing, MergeQueue, Recovery, Provision, Completion, Review)")]
        [CommandOption("--phase <PHASE>")]
        public string? Phase { get; set; }

        /// <summary>
        /// Window start.
        /// </summary>
        [Description("Only failures since a duration (30d, 12h) or a date (2026-09-01)")]
        [CommandOption("--since <WHEN>")]
        public string? Since { get; set; }
    }
}
