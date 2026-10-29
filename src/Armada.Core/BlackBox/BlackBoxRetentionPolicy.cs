namespace Armada.Core.BlackBox
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Computes when a black box entry expires (docs/BLACKBOX.md, "Retention"). Pinned entries never expire; the vessel's
    /// BlackBoxRetentionDays overrides RetentionDays; 0 days keeps entries forever; a resolved entry expires
    /// ResolvedRetentionDays after resolution when that is sooner. Thread-safe (stateless).
    /// </summary>
    public static class BlackBoxRetentionPolicy
    {
        /// <summary>
        /// Compute an entry's expiry.
        /// </summary>
        /// <param name="entry">Entry (Pinned, Resolution, ResolvedUtc and CreatedUtc are read).</param>
        /// <param name="policy">Retention policy.</param>
        /// <param name="vesselRetentionDays">The vessel's override, or null.</param>
        /// <returns>The expiry (UTC), or null for never.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry or policy is null.</exception>
        public static DateTime? ComputeExpiry(BlackBoxEntry entry, BlackBoxRetentionSettings policy, int? vesselRetentionDays)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (entry.Pinned) return null;
            int days = vesselRetentionDays ?? policy.RetentionDays;
            DateTime? byAge = days > 0 ? entry.CreatedUtc.AddDays(days) : (DateTime?)null;
            bool resolved = entry.Resolution != BlackBoxResolutionEnum.Open && entry.Resolution != BlackBoxResolutionEnum.Unknown;
            if (resolved && policy.ResolvedRetentionDays > 0)
            {
                DateTime byResolution = (entry.ResolvedUtc ?? DateTime.UtcNow).AddDays(policy.ResolvedRetentionDays);
                if (!byAge.HasValue || byResolution < byAge.Value) return byResolution;
            }
            return byAge;
        }
    }
}
