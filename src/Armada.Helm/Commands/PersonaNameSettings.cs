namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for persona commands that take a persona name.
    /// </summary>
    public class PersonaNameSettings : BaseSettings
    {
        /// <summary>
        /// Persona name (for example Worker or Judge).
        /// </summary>
        [Description("Persona name (for example Worker or Judge)")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;
    }
}
