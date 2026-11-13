namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox purge</c>: a dry run unless <c>--execute</c>.
    /// </summary>
    public class BlackBoxPurgeSettings : BlackBoxFilterSettings
    {
        /// <summary>
        /// Delete for real.
        /// </summary>
        [Description("Delete the matching entries (asks to confirm; --yes skips the question)")]
        [CommandOption("--execute")]
        public bool Execute { get; set; } = false;

        /// <summary>
        /// Confirm without asking.
        /// </summary>
        [Description("Confirm without asking (required with --execute when not interactive)")]
        [CommandOption("--yes|-y")]
        public bool Yes { get; set; } = false;

        /// <summary>
        /// Include pinned entries.
        /// </summary>
        [Description("Include pinned entries")]
        [CommandOption("--include-pinned")]
        public bool IncludePinned { get; set; } = false;

        /// <summary>
        /// Most entries.
        /// </summary>
        [Description("Most entries to purge in one run (server default 10000)")]
        [CommandOption("--max <COUNT>")]
        public int? Max { get; set; }
    }
}
