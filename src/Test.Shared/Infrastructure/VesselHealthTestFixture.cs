namespace Test.Shared.Infrastructure
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Entities seeded by the vessel health database suite: two fleets, five active vessels in the default tenant
    /// (four evaluated, one never evaluated), and one vessel in another tenant.
    /// </summary>
    public sealed class VesselHealthTestFixture
    {
        #region Public-Members

        /// <summary>
        /// Fleet named "Alpha Fleet".
        /// </summary>
        public Fleet FleetAlpha { get; set; } = null!;

        /// <summary>
        /// Fleet named "Beta Fleet".
        /// </summary>
        public Fleet FleetBeta { get; set; } = null!;

        /// <summary>
        /// Vessel "alpha_one" (Fail, ahead only, dirty).
        /// </summary>
        public Vessel Alpha { get; set; } = null!;

        /// <summary>
        /// Vessel "beta" (Warn, behind only).
        /// </summary>
        public Vessel Beta { get; set; } = null!;

        /// <summary>
        /// Vessel "gamma" (Pass, diverged, no fleet).
        /// </summary>
        public Vessel Gamma { get; set; } = null!;

        /// <summary>
        /// Vessel "delta" (Unknown, even).
        /// </summary>
        public Vessel Delta { get; set; } = null!;

        /// <summary>
        /// Vessel "epsilon" (never evaluated).
        /// </summary>
        public Vessel Epsilon { get; set; } = null!;

        /// <summary>
        /// Vessel in another tenant.
        /// </summary>
        public Vessel OtherTenantVessel { get; set; } = null!;

        /// <summary>
        /// Identifier of the other tenant.
        /// </summary>
        public string OtherTenantId { get; set; } = String.Empty;

        #endregion
    }
}
