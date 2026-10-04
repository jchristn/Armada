namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for commands that take a fleet action run ID.
    /// </summary>
    public class ActionRunIdSettings : BaseSettings
    {
        /// <summary>
        /// Run ID (far_ prefix).
        /// </summary>
        [Description("Fleet action run ID (far_ prefix)")]
        [CommandArgument(0, "<run>")]
        public string RunId { get; set; } = string.Empty;
    }
}
