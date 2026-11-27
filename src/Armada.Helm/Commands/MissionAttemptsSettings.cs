namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the mission attempts command.
    /// </summary>
    public class MissionAttemptsSettings : BaseSettings
    {
        /// <summary>
        /// Mission ID (msn_ prefix).
        /// </summary>
        [Description("Mission ID (msn_ prefix)")]
        [CommandArgument(0, "<mission>")]
        public string MissionId { get; set; } = string.Empty;
    }
}
