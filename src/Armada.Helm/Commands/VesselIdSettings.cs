namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the vessel commands that take one vessel (name or id).
    /// </summary>
    public class VesselIdSettings : BaseSettings
    {
        /// <summary>
        /// Vessel name or id.
        /// </summary>
        [Description("Vessel name or ID")]
        [CommandArgument(0, "<vessel>")]
        public string Vessel { get; set; } = string.Empty;
    }
}
