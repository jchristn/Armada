namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the battery runs command.
    /// </summary>
    public class BatteryRunsSettings : BaseSettings
    {
        /// <summary>
        /// Status filter.
        /// </summary>
        [Description("Status filter: Queued, Running, Completed, Failed or Cancelled")]
        [CommandOption("--status")]
        public string? Status { get; set; }

        /// <summary>
        /// Captain filter.
        /// </summary>
        [Description("Captain filter (cpt_ prefix)")]
        [CommandOption("--captain")]
        public string? Captain { get; set; }
    }
}
