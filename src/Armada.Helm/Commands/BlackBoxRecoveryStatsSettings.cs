namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for <c>armada blackbox recovery-stats</c>.
    /// </summary>
    public class BlackBoxRecoveryStatsSettings : BaseSettings
    {
        /// <summary>
        /// Rule filter.
        /// </summary>
        [Description("Only this rule (for example BBX-LAND-001)")]
        [CommandOption("--rule")]
        public string? Rule { get; set; }

        /// <summary>
        /// Vessel filter.
        /// </summary>
        [Description("Only this vessel (vsl_ id)")]
        [CommandOption("--vessel")]
        public string? Vessel { get; set; }
    }
}
