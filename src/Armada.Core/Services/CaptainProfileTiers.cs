namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Fills <see cref="Captain.ProfileTier"/> from each captain's model profile (RS-8.5), so every tier decision sees the
    /// tier an administrator set on the profile. A profile in another tenant than its captain is ignored.
    /// </summary>
    public static class CaptainProfileTiers
    {
        #region Public-Methods

        /// <summary>
        /// Fill the profile tier of each captain, reading each distinct profile once.
        /// </summary>
        /// <param name="captains">Captains to fill.</param>
        /// <param name="readProfile">Reads a profile by identifier (null when missing).</param>
        /// <returns>A task.</returns>
        /// <exception cref="ArgumentNullException">The captains or the reader are null.</exception>
        public static async Task ApplyAsync(IEnumerable<Captain> captains, Func<string, Task<ModelProfile?>> readProfile)
        {
            if (captains == null) throw new ArgumentNullException(nameof(captains));
            if (readProfile == null) throw new ArgumentNullException(nameof(readProfile));
            Dictionary<string, ModelProfile?> profiles = new Dictionary<string, ModelProfile?>(StringComparer.Ordinal);
            foreach (Captain captain in captains)
            {
                if (captain == null) continue;
                captain.ProfileTier = null;
                if (String.IsNullOrEmpty(captain.ModelProfileId)) continue;
                if (!profiles.TryGetValue(captain.ModelProfileId, out ModelProfile? profile))
                {
                    profile = await readProfile(captain.ModelProfileId).ConfigureAwait(false);
                    profiles[captain.ModelProfileId] = profile;
                }
                if (profile != null && String.Equals(profile.TenantId, captain.TenantId, StringComparison.Ordinal)) captain.ProfileTier = profile.Tiering.Tier;
            }
        }

        /// <summary>
        /// Fill the profile tier of each captain from the database, reading the profiles in one query.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="captains">Captains to fill.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        /// <exception cref="ArgumentNullException">The database or captains are null.</exception>
        public static async Task ApplyAsync(DatabaseDriver database, IEnumerable<Captain> captains, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (captains == null) throw new ArgumentNullException(nameof(captains));
            List<Captain> list = new List<Captain>(captains);
            // One batched read of every distinct profile.
            Dictionary<string, ModelProfile> profiles = await database.ModelProfiles.ReadManyAsync(list.Select(c => c?.ModelProfileId), token).ConfigureAwait(false);
            await ApplyAsync(list, id => Task.FromResult(profiles.TryGetValue(id, out ModelProfile? profile) ? profile : null)).ConfigureAwait(false);
        }

        #endregion
    }
}
