namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// package.json contributes: commands, views per container, and configuration.
    /// </summary>
    public sealed class VsCodeContributions
    {
        /// <summary>Commands.</summary>
        public List<VsCodeCommandContribution> Commands { get; set; } = new List<VsCodeCommandContribution>();

        /// <summary>Views by container id (armada, explorer).</summary>
        public Dictionary<string, List<VsCodeViewContribution>> Views { get; set; } = new Dictionary<string, List<VsCodeViewContribution>>();

        /// <summary>Configuration (settings).</summary>
        public VsCodeConfigurationContribution Configuration { get; set; } = new VsCodeConfigurationContribution();
    }
}
