namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox backfill</c>.
    /// </summary>
    public class BlackBoxBackfillSettings : BaseSettings
    {
        /// <summary>
        /// Since.
        /// </summary>
        [Description("Backfill failures since: a duration (30d) or a date (default the server's window)")]
        [CommandOption("--since <WHEN>")]
        public string? Since { get; set; }

        /// <summary>
        /// Most subjects.
        /// </summary>
        [Description("Most subjects to scan")]
        [CommandOption("--max <COUNT>")]
        public int? Max { get; set; }
    }
}
