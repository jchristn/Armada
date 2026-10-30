namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox gc</c>.
    /// </summary>
    public class BlackBoxGcSettings : BaseSettings
    {
        /// <summary>
        /// Dry run.
        /// </summary>
        [Description("Report what garbage collection would remove without deleting anything")]
        [CommandOption("--dry-run")]
        public bool DryRun { get; set; } = false;
    }
}
