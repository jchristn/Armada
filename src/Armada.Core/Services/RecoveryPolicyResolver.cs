namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Resolves the recovery policy in effect for a captain (RS-5.5): each global setting, replaced by the captain's model
    /// profile override when one is set. Judge rework rounds take the larger of the two, so a profile never shortens the
    /// FullPipeline rework chain, and Unclassified resumes never exceed the resumes. Thread safe.
    /// </summary>
    public static class RecoveryPolicyResolver
    {
        #region Public-Methods

        /// <summary>
        /// Apply profile overrides to the global settings.
        /// </summary>
        /// <param name="settings">Global settings.</param>
        /// <param name="profile">The captain's model profile, or null.</param>
        /// <returns>The policy in effect.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static RecoveryPolicy Resolve(ArmadaSettings settings, ModelProfile? profile)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ModelProfileRecovery overrides = profile?.Recovery ?? new ModelProfileRecovery();
            RecoveryPolicy policy = new RecoveryPolicy();
            policy.ModelProfileId = profile?.Id;
            policy.MaxResumes = overrides.MaxResumes ?? settings.MaxRecoveryAttempts;
            policy.UnclassifiedResumes = Math.Min(overrides.UnclassifiedResumes ?? settings.UnclassifiedResumeLimit, policy.MaxResumes);
            policy.TransientGraceMinutes = overrides.TransientGraceMinutes ?? settings.TransientOutageGraceMinutes;
            policy.JudgeReworkRounds = Math.Max(overrides.JudgeReworkRounds ?? 0, settings.JudgeReworkRounds);
            policy.OutputRepairAttempts = overrides.OutputRepairAttempts ?? settings.MaxOutputRepairAttempts;
            return policy;
        }

        /// <summary>
        /// Resolve the policy for a captain from its linked model profile. A captain with no profile, or a profile that
        /// cannot be read, gets the global settings.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Global settings.</param>
        /// <param name="captain">The captain, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The policy in effect.</returns>
        /// <exception cref="ArgumentNullException">Thrown when database or settings is null.</exception>
        public static async Task<RecoveryPolicy> ResolveAsync(DatabaseDriver database, ArmadaSettings settings, Captain? captain, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ModelProfile? profile = null;
            if (captain != null && !String.IsNullOrEmpty(captain.ModelProfileId))
                profile = await database.ModelProfiles.ReadAsync(captain.ModelProfileId!, token).ConfigureAwait(false);
            return Resolve(settings, profile);
        }

        /// <summary>
        /// Resolve the policy for the captain with the given id (see <see cref="ResolveAsync(DatabaseDriver, ArmadaSettings, Captain?, CancellationToken)"/>).
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Global settings.</param>
        /// <param name="captainId">Captain id, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The policy in effect.</returns>
        /// <exception cref="ArgumentNullException">Thrown when database or settings is null.</exception>
        public static async Task<RecoveryPolicy> ResolveForCaptainIdAsync(DatabaseDriver database, ArmadaSettings settings, string? captainId, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Captain? captain = String.IsNullOrEmpty(captainId) ? null : await database.Captains.ReadAsync(captainId!, token).ConfigureAwait(false);
            return await ResolveAsync(database, settings, captain, token).ConfigureAwait(false);
        }

        #endregion
    }
}
