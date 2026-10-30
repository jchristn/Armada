namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox storage migrate</c>.
    /// </summary>
    public class BlackBoxStorageMigrateSettings : BaseSettings
    {
        /// <summary>
        /// Target.
        /// </summary>
        [Description("Target storage: Database, Disk, AzureBlob, GoogleCloud, Cifs, Nfs")]
        [CommandOption("--to <TARGET>")]
        public string? To { get; set; }

        /// <summary>
        /// Source.
        /// </summary>
        [Description("Source storage (default the current target)")]
        [CommandOption("--from <TARGET>")]
        public string? From { get; set; }

        /// <summary>
        /// Delete the source.
        /// </summary>
        [Description("Delete the source objects after copying")]
        [CommandOption("--delete-source")]
        public bool DeleteSource { get; set; } = false;

        /// <summary>
        /// Batch size.
        /// </summary>
        [Description("Entries per batch")]
        [CommandOption("--batch-size <COUNT>")]
        public int? BatchSize { get; set; }
    }
}
