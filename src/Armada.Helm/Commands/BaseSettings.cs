namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Client;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Base command settings with shared options.
    /// </summary>
    public class BaseSettings : CommandSettings
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

        /// <summary>
        /// Page number for list commands (1-based).
        /// </summary>
        [Description("Page number (1-based, default: 1)")]
        [CommandOption("--page")]
        public int? Page { get; set; }

        /// <summary>
        /// Page size for list commands.
        /// </summary>
        [Description("Results per page (default: 100, max: 1000)")]
        [CommandOption("--page-size")]
        public int? PageSize { get; set; }
    }
}
