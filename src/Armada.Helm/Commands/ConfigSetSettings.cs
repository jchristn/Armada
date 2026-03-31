namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Text.Json;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Settings for config set command.
    /// </summary>
    public class ConfigSetSettings : BaseSettings
    {
        /// <summary>
        /// Configuration key (e.g. admiralPort, mcpPort, dataDirectory).
        /// </summary>
        [Description("Configuration key")]
        [CommandArgument(0, "<key>")]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Value to set.
        /// </summary>
        [Description("Value to set")]
        [CommandArgument(1, "<value>")]
        public string Value { get; set; } = string.Empty;
    }
}
