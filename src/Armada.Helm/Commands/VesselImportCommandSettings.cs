namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the vessel import command.
    /// </summary>
    public class VesselImportCommandSettings : BaseSettings
    {
        /// <summary>
        /// Explicit directories. A git repository becomes one candidate; any other directory is scanned like a root.
        /// </summary>
        [Description("Directories to import (a non-repository directory is scanned for repositories)")]
        [CommandArgument(0, "[paths]")]
        public string[]? Paths { get; set; }

        /// <summary>
        /// Roots to scan for git repositories (repeatable).
        /// </summary>
        [Description("Root to scan for git repositories (repeatable)")]
        [CommandOption("--root|-r")]
        public string[]? Roots { get; set; }

        /// <summary>
        /// Fleet name or ID to assign imported vessels to.
        /// </summary>
        [Description("Fleet name or ID for the imported vessels")]
        [CommandOption("--fleet|-f")]
        public string? Fleet { get; set; }

        /// <summary>
        /// Maximum scan depth (1-16).
        /// </summary>
        [Description("Maximum scan depth below each root (1-16)")]
        [CommandOption("--depth")]
        public int? Depth { get; set; }

        /// <summary>
        /// Print the candidate table without importing.
        /// </summary>
        [Description("Show the candidates without importing")]
        [CommandOption("--dry-run")]
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Import without prompting for confirmation.
        /// </summary>
        [Description("Import without prompting")]
        [CommandOption("--yes|-y")]
        public bool Yes { get; set; } = false;

        /// <summary>
        /// Have a captain recommend fleets for the imported vessels after the import.
        /// </summary>
        [Description("After the import, have a captain recommend fleets for the imported repositories (requires --captain)")]
        [CommandOption("--categorize")]
        public bool Categorize { get; set; } = false;

        /// <summary>
        /// Captain ID that recommends fleets.
        /// </summary>
        [Description("Captain ID (cpt_) that recommends fleets")]
        [CommandOption("--captain")]
        public string? Captain { get; set; }

        /// <summary>
        /// File with categorization instructions.
        /// </summary>
        [Description("File with categorization instructions (default: the import.fleet_categorization prompt template)")]
        [CommandOption("--prompt-file")]
        public string? PromptFile { get; set; }

        /// <summary>
        /// Apply the recommended fleets automatically.
        /// </summary>
        [Description("Apply the recommended fleets automatically when categorization completes")]
        [CommandOption("--apply")]
        public bool Apply { get; set; } = false;
    }
}
