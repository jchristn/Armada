namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Filters shared by <c>armada blackbox list</c>, <c>purge</c>, and <c>bundle</c>.
    /// </summary>
    public class BlackBoxFilterSettings : BaseSettings
    {
        /// <summary>
        /// Vessel id.
        /// </summary>
        [Description("Vessel id")]
        [CommandOption("--vessel <ID>")]
        public string? Vessel { get; set; }

        /// <summary>
        /// Voyage id.
        /// </summary>
        [Description("Voyage id")]
        [CommandOption("--voyage <ID>")]
        public string? Voyage { get; set; }

        /// <summary>
        /// Mission id.
        /// </summary>
        [Description("Mission id")]
        [CommandOption("--mission <ID>")]
        public string? Mission { get; set; }

        /// <summary>
        /// Captain id.
        /// </summary>
        [Description("Captain id")]
        [CommandOption("--captain <ID>")]
        public string? Captain { get; set; }

        /// <summary>
        /// Failure kind.
        /// </summary>
        [Description("Failure kind (for example LandingConflict, TestFail, Timeout)")]
        [CommandOption("--kind <KIND>")]
        public string? Kind { get; set; }

        /// <summary>
        /// Triage verdict.
        /// </summary>
        [Description("Triage: UserConfig, MissionBrief, Transient, ArmadaDefect, External, Unknown")]
        [CommandOption("--triage <TRIAGE>")]
        public string? Triage { get; set; }

        /// <summary>
        /// Phase.
        /// </summary>
        [Description("Phase (for example Landing, Execution, Dispatch)")]
        [CommandOption("--phase <PHASE>")]
        public string? Phase { get; set; }

        /// <summary>
        /// Step.
        /// </summary>
        [Description("Step (for example DirtyCheckout)")]
        [CommandOption("--step <STEP>")]
        public string? Step { get; set; }

        /// <summary>
        /// Resolution.
        /// </summary>
        [Description("Resolution: Open, Resolved, Duplicate, WontFix, ArmadaFixed")]
        [CommandOption("--resolution <RESOLUTION>")]
        public string? Resolution { get; set; }

        /// <summary>
        /// Fingerprint.
        /// </summary>
        [Description("Failure fingerprint")]
        [CommandOption("--fingerprint <FINGERPRINT>")]
        public string? Fingerprint { get; set; }

        /// <summary>
        /// Only pinned entries.
        /// </summary>
        [Description("Only pinned entries")]
        [CommandOption("--pinned")]
        public bool Pinned { get; set; } = false;

        /// <summary>
        /// Captured since.
        /// </summary>
        [Description("Captured since: a duration (30d, 12h, 2w) or a date (2026-09-01)")]
        [CommandOption("--since <WHEN>")]
        public string? Since { get; set; }

        /// <summary>
        /// Captured before.
        /// </summary>
        [Description("Captured before: a duration (30d) or a date (2026-09-01)")]
        [CommandOption("--before <WHEN>")]
        public string? Before { get; set; }
    }
}
