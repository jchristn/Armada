namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada profile list</c>.
    /// </summary>
    public class ProfileListSettings : CommandSettings
    {
        /// <summary>
        /// Output in JSON format.
        /// </summary>
        [Description("Output in JSON format")]
        [CommandOption("--json")]
        public bool Json { get; set; } = false;
    }
}
