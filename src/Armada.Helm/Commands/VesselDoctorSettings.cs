namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada vessel doctor</c>.
    /// </summary>
    public class VesselDoctorSettings : VesselIdSettings
    {
        /// <summary>
        /// Window in days.
        /// </summary>
        [Description("How many days back to look (default 30)")]
        [CommandOption("--window-days")]
        public int? WindowDays { get; set; }
    }
}
