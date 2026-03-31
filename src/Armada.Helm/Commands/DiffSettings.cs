namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Settings for diff command.
    /// </summary>
    public class DiffSettings : BaseSettings
    {
        /// <summary>
        /// Mission identifier or title substring.
        /// </summary>
        [Description("Mission ID or title")]
        [CommandArgument(0, "[mission]")]
        public string? Id { get; set; }
    }
}
