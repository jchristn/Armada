namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox bundle</c>.
    /// </summary>
    public class BlackBoxBundleSettings : BlackBoxFilterSettings
    {
        /// <summary>
        /// Output file.
        /// </summary>
        [Description("Output file (default stdout)")]
        [CommandOption("-o|--output <FILE>")]
        public string? Output { get; set; }

        /// <summary>
        /// Most entries.
        /// </summary>
        [Description("Most entries (1 to 50, default 50)")]
        [CommandOption("--max <COUNT>")]
        public int? Max { get; set; }

        /// <summary>
        /// Format.
        /// </summary>
        [Description("Format: markdown (default) or json")]
        [CommandOption("--format <FORMAT>")]
        public string? Format { get; set; }

        /// <summary>
        /// Redact host paths.
        /// </summary>
        [Description("Strip host paths")]
        [CommandOption("--redact-hosts")]
        public bool RedactHosts { get; set; } = false;
    }
}
