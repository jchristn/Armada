namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox export-fixture</c>.
    /// </summary>
    public class BlackBoxExportFixtureSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// Fixture name.
        /// </summary>
        [Description("Fixture name (default derived from the entry)")]
        [CommandOption("--name <NAME>")]
        public string? Name { get; set; }

        /// <summary>
        /// Output directory.
        /// </summary>
        [Description("Output directory (default src/Test.Shared/Fixtures/BlackBox when run inside the Armada repository)")]
        [CommandOption("-o|--output <DIR>")]
        public string? Output { get; set; }
    }
}
