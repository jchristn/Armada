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
    /// Settings for captain remove command.
    /// </summary>
    public class CaptainRemoveSettings : BaseSettings
    {
        /// <summary>
        /// Captain identifier or name.
        /// </summary>
        [Description("Captain name or ID")]
        [CommandArgument(0, "<captain>")]
        public string Id { get; set; } = string.Empty;
    }
}
