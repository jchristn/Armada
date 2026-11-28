namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the battery report command.
    /// </summary>
    public class BatteryReportSettings : BaseSettings
    {
        /// <summary>
        /// Gate report ID (gate- prefix); omit for the latest report.
        /// </summary>
        [Description("Gate report ID (gate- prefix); omit for the latest")]
        [CommandArgument(0, "[report]")]
        public string? ReportId { get; set; }
    }
}
