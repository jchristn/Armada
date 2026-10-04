namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the health command.
    /// </summary>
    public class HealthSettings : BaseSettings
    {
        /// <summary>
        /// Optional overall status filter (Pass, Warn, Fail, NotApplicable, Unknown).
        /// </summary>
        [Description("Filter by overall status: Pass, Warn, Fail, NotApplicable, or Unknown")]
        [CommandOption("--status|-s")]
        public string? Status { get; set; }

        /// <summary>
        /// Optional fleet filter.
        /// </summary>
        [Description("Filter by fleet ID")]
        [CommandOption("--fleet|-f")]
        public string? Fleet { get; set; }

        /// <summary>
        /// Start an evaluation (of the fleet when --fleet is given, otherwise every active vessel) before listing.
        /// </summary>
        [Description("Start a health evaluation (of --fleet, or all active vessels) instead of only listing")]
        [CommandOption("--evaluate|-e")]
        public bool Evaluate { get; set; } = false;
    }
}
