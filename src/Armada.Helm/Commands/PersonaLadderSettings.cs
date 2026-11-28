namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the persona ladder command.
    /// </summary>
    public class PersonaLadderSettings : PersonaNameSettings
    {
        /// <summary>
        /// Escalation ladder rungs in order, each as comma-separated key=value pairs (runtime, model, endpoint, tier).
        /// </summary>
        [Description("Ladder rung as key=value pairs: runtime=, model=, endpoint=, tier= (Economy, Standard, Premium); repeat in order")]
        [CommandOption("--rung <SPEC>")]
        public string[]? Rungs { get; set; }

        /// <summary>
        /// Remove every rung; escalation then uses the next tier up.
        /// </summary>
        [Description("Remove every rung (escalation then uses the next tier up)")]
        [CommandOption("--clear")]
        public bool Clear { get; set; }
    }
}
