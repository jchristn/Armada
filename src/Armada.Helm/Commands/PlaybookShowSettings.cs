namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for playbook show command.
    /// </summary>
    public class PlaybookShowSettings : BaseSettings
    {
        /// <summary>
        /// Playbook ID or file name.
        /// </summary>
        [Description("Playbook ID or file name")]
        [CommandArgument(0, "<playbook>")]
        public string Id { get; set; } = string.Empty;
    }
}
