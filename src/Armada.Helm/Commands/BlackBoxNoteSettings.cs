namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox note</c>.
    /// </summary>
    public class BlackBoxNoteSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// Note text.
        /// </summary>
        [Description("Note text")]
        [CommandArgument(1, "<text>")]
        public string Text { get; set; } = string.Empty;
    }
}
