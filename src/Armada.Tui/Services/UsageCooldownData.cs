namespace Armada.Tui.Services
{
    using System;
    /// <summary>Allowlisted fields in a usage cooldown error envelope.</summary>
    internal class UsageCooldownData
    {
        /// <summary>Next permitted capture time.</summary>
        public DateTime? NextEligibleUtc { get; set; }
        /// <summary>Structured error data, when nested by REST.</summary>
        public UsageCooldownData? Data { get; set; }
    }
}
