namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox timeline</c>.
    /// </summary>
    public class BlackBoxTimelineSettings : BlackBoxIdSettings
    {
        /// <summary>
        /// Newest first instead of oldest first.
        /// </summary>
        [Description("Newest first (default oldest first)")]
        [CommandOption("--newest-first")]
        public bool NewestFirst { get; set; } = false;
    }
}
