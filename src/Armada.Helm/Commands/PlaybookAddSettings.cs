namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for playbook add command.
    /// </summary>
    public class PlaybookAddSettings : BaseSettings
    {
        /// <summary>
        /// Markdown file name.
        /// </summary>
        [Description("Playbook markdown file name")]
        [CommandArgument(0, "<file-name>")]
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Optional description.
        /// </summary>
        [Description("Optional description")]
        [CommandOption("--description|-d")]
        public string? Description { get; set; }

        /// <summary>
        /// Inline markdown content.
        /// </summary>
        [Description("Inline markdown content")]
        [CommandOption("--content|-c")]
        public string? Content { get; set; }

        /// <summary>
        /// Path to a markdown file to upload.
        /// </summary>
        [Description("Path to a markdown file to upload")]
        [CommandOption("--from-file|-f")]
        public string? FromFile { get; set; }

        /// <summary>
        /// Create the playbook inactive.
        /// </summary>
        [Description("Create the playbook inactive")]
        [CommandOption("--inactive")]
        public bool Inactive { get; set; }
    }
}
