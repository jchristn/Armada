namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Resolves a captain's <see cref="TimeoutPolicy"/> for the health check (RS-8.8) without reading its model profile on
    /// every tick: profiles are kept for one minute. The policy itself is resolved from the current settings each call,
    /// so a settings change applies at once. A profile that cannot be read gives the global settings. Thread safe.
    /// </summary>
    internal sealed class TimeoutPolicyCache
    {
        private static readonly TimeSpan _Lifetime = TimeSpan.FromMinutes(1);
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly ConcurrentDictionary<string, TimeoutPolicyCacheEntry> _Profiles = new ConcurrentDictionary<string, TimeoutPolicyCacheEntry>(StringComparer.Ordinal);

        internal TimeoutPolicyCache(DatabaseDriver database, ArmadaSettings settings)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal async Task<TimeoutPolicy> ResolveAsync(Captain? captain, CancellationToken token)
        {
            string? profileId = captain?.ModelProfileId;
            if (String.IsNullOrEmpty(profileId)) return TimeoutPolicyResolver.Resolve(_Settings, null);
            DateTime now = DateTime.UtcNow;
            if (_Profiles.TryGetValue(profileId!, out TimeoutPolicyCacheEntry? cached) && cached.ExpiresUtc > now)
                return TimeoutPolicyResolver.Resolve(_Settings, cached.Profile);

            ModelProfile? profile = null;
            try
            {
                profile = await _Database.ModelProfiles.ReadAsync(profileId!, token).ConfigureAwait(false);
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                profile = cached?.Profile;
            }

            _Profiles[profileId!] = new TimeoutPolicyCacheEntry { Profile = profile, ExpiresUtc = now + _Lifetime };
            return TimeoutPolicyResolver.Resolve(_Settings, profile);
        }
    }
}
