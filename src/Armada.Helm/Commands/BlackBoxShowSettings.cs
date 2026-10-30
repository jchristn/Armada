namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox show</c> and the <c>mission failure</c> and <c>voyage failure</c> aliases.
    /// </summary>
    public class BlackBoxShowSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// Print only the brief.
        /// </summary>
        [Description("Print only the Markdown FIX THIS brief to stdout (pipe it: | pbcopy)")]
        [CommandOption("--brief")]
        public bool Brief { get; set; } = false;

        /// <summary>
        /// Redact host paths.
        /// </summary>
        [Description("Strip host paths from the brief")]
        [CommandOption("--redact-hosts")]
        public bool RedactHosts { get; set; } = false;
    }
}
