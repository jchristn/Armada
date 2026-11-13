namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox download</c>.
    /// </summary>
    public class BlackBoxDownloadSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// Output file.
        /// </summary>
        [Description("Output file (default the server's file name in the current directory)")]
        [CommandOption("-o|--output <FILE>")]
        public string? Output { get; set; }
    }
}
