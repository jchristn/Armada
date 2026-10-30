namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada vessel update</c>.
    /// </summary>
    public class VesselUpdateSettings : VesselIdSettings
    {
        /// <summary>
        /// Serialize overlapping missions (true or false), or null to leave it.
        /// </summary>
        [Description("Chain a mission that overlaps an active one after it (true or false)")]
        [CommandOption("--serialize-overlapping-missions <BOOL>")]
        public bool? SerializeOverlappingMissions { get; set; }
    }
}
