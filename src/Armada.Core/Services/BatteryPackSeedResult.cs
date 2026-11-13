namespace Armada.Core.Services
{
    using System.Collections.Generic;

    /// <summary>
    /// What one <see cref="BatteryPackSeeder.Seed"/> call did.
    /// </summary>
    public class BatteryPackSeedResult
    {
        #region Public-Members

        /// <summary>
        /// Directory the bundled packs were read from, or null when the application has none.
        /// </summary>
        public string? BundledDirectory { get; set; } = null;

        /// <summary>
        /// Directory the packs were seeded into, or null when none is configured.
        /// </summary>
        public string? TargetDirectory { get; set; } = null;

        /// <summary>
        /// Why nothing was seeded, or null when the seed ran.
        /// </summary>
        public string? SkippedReason { get; set; } = null;

        /// <summary>
        /// Bundled packs copied into the target in this call.
        /// </summary>
        public List<string> Added { get; set; } = new List<string>();

        /// <summary>
        /// Bundled packs left alone because the target already has a pack (or file) with that name: edited and custom
        /// packs are never overwritten.
        /// </summary>
        public List<string> Preserved { get; set; } = new List<string>();

        /// <summary>
        /// Bundled packs seeded by an earlier call and since deleted by an operator; they are not recreated.
        /// </summary>
        public List<string> Retired { get; set; } = new List<string>();

        /// <summary>
        /// Bundled packs that could not be copied, each with its error.
        /// </summary>
        public List<string> Failed { get; set; } = new List<string>();

        #endregion
    }
}
