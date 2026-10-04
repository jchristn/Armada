namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the action run command.
    /// </summary>
    public class ActionRunSettings : BaseSettings
    {
        /// <summary>
        /// Fleet action ID or name.
        /// </summary>
        [Description("Fleet action ID or name")]
        [CommandArgument(0, "<action>")]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Target vessels (repeatable), by ID or name.
        /// </summary>
        [Description("Target vessel ID or name (repeatable)")]
        [CommandOption("--vessel|-v")]
        public string[]? Vessels { get; set; }

        /// <summary>
        /// Target every vessel in a fleet.
        /// </summary>
        [Description("Target every vessel in this fleet (ID)")]
        [CommandOption("--fleet|-f")]
        public string? Fleet { get; set; }

        /// <summary>
        /// Concurrency for this run.
        /// </summary>
        [Description("Concurrency for this run (1-32)")]
        [CommandOption("--concurrency|-c")]
        public int? Concurrency { get; set; }
    }
}
