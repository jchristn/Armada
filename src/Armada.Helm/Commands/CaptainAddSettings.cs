namespace Armada.Helm.Commands
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Settings for captain add command.
    /// </summary>
    public class CaptainAddSettings : BaseSettings
    {
        /// <summary>
        /// Captain name.
        /// </summary>
        [Description("Name of the captain")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Agent runtime type.
        /// </summary>
        [Description("Agent runtime (claude, codex, custom)")]
        [CommandOption("--runtime|-r")]
        public string? Runtime { get; set; }
    }
}
