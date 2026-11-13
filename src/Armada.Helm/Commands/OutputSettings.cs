namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Output options shared by every Admiral command (<c>--json</c>, <c>--verbose</c>). Commands that page results use
    /// <see cref="BaseSettings"/>, which adds <c>--page</c> and <c>--page-size</c>.
    /// </summary>
    public class OutputSettings : TargetSettings
    {
        /// <summary>
        /// Output in JSON format for machine consumption.
        /// </summary>
        [Description("Output in JSON format")]
        [CommandOption("--json")]
        public bool Json { get; set; } = false;

        /// <summary>
        /// Verbose output.
        /// </summary>
        [Description("Verbose output")]
        [CommandOption("--verbose")]
        public bool Verbose { get; set; } = false;
    }
}
