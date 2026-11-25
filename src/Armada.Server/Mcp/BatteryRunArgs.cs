namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// Arguments of run_battery.
    /// </summary>
    public class BatteryRunArgs
    {
        /// <summary>
        /// Packs to run.
        /// </summary>
        public List<string>? Packs { get; set; } = null;

        /// <summary>
        /// Captain (cpt_ prefix).
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Runs of each task (default 3).
        /// </summary>
        public int? RunCount { get; set; } = null;

        /// <summary>
        /// Seed to record, or null.
        /// </summary>
        public int? Seed { get; set; } = null;

        /// <summary>
        /// Create missing fixture vessels.
        /// </summary>
        public bool? CreateFixtureVessel { get; set; } = null;

        /// <summary>
        /// Persona prompt variant for every task (Current, Lean or Compact), or null for the captain's own setting.
        /// </summary>
        public string? PromptVariant { get; set; } = null;

        /// <summary>
        /// Pipeline to run each Worker task under, or null for the built-in ModelEvaluation pipeline.
        /// </summary>
        public string? Pipeline { get; set; } = null;

        /// <summary>
        /// Captain per persona for the pipeline.
        /// </summary>
        public List<Armada.Core.Models.CaptainAssignmentOverride>? CaptainOverrides { get; set; } = null;
    }
}
