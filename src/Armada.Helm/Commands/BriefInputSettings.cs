namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// The brief options shared by <c>armada vessel overlap-preview</c> and <c>armada mission lint-brief</c>.
    /// </summary>
    public class BriefInputSettings : BaseSettings
    {
        /// <summary>
        /// Brief text.
        /// </summary>
        [Description("The mission brief (description) text")]
        [CommandOption("--brief")]
        public string? Brief { get; set; }

        /// <summary>
        /// Brief file.
        /// </summary>
        [Description("Read the brief from a file (- reads stdin)")]
        [CommandOption("--brief-file")]
        public string? BriefFile { get; set; }

        /// <summary>
        /// Mission title.
        /// </summary>
        [Description("Mission title")]
        [CommandOption("--title")]
        public string? Title { get; set; }

        /// <summary>
        /// Dependency.
        /// </summary>
        [Description("The mission this one would depend on (msn_ id)")]
        [CommandOption("--depends-on")]
        public string? DependsOn { get; set; }
    }
}
