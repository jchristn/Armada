namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox recover</c>: a dry run unless <c>--execute</c>.
    /// </summary>
    public class BlackBoxRecoverSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// Recovery path id.
        /// </summary>
        [Description("Recovery path id (see armada blackbox show)")]
        [CommandArgument(1, "<pathId>")]
        public string PathId { get; set; } = string.Empty;

        /// <summary>
        /// Run the path.
        /// </summary>
        [Description("Run the path after the dry run (asks to confirm; --yes skips the question)")]
        [CommandOption("--execute")]
        public bool Execute { get; set; } = false;

        /// <summary>
        /// Confirm without asking.
        /// </summary>
        [Description("Confirm without asking (required with --execute when not interactive)")]
        [CommandOption("--yes|-y")]
        public bool Yes { get; set; } = false;

        /// <summary>
        /// Acknowledge the data-loss risk.
        /// </summary>
        [Description("Acknowledge that the path can lose work (required for a path marked dataLossRisk)")]
        [CommandOption("--acknowledge-data-loss")]
        public bool AcknowledgeDataLoss { get; set; } = false;
    }
}
