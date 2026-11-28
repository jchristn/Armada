namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the battery cancel command.
    /// </summary>
    public class BatteryCancelSettings : BaseSettings
    {
        /// <summary>
        /// Run ID (bat_ prefix).
        /// </summary>
        [Description("Battery run ID (bat_ prefix)")]
        [CommandArgument(0, "<run>")]
        public string RunId { get; set; } = string.Empty;

        /// <summary>
        /// Reason recorded on the run.
        /// </summary>
        [Description("Reason recorded on the run (at most 500 characters)")]
        [CommandOption("--reason")]
        public string? Reason { get; set; }
    }
}
