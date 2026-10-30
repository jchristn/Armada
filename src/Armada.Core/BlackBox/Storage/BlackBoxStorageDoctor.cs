namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// The black box storage doctor check: validates the configured target's settings and runs the store's health check
    /// (Blobject ValidateConnectivity for external targets). Never throws. Thread-safe.
    /// </summary>
    public static class BlackBoxStorageDoctor
    {
        /// <summary>
        /// Run the check.
        /// </summary>
        /// <param name="settings">Black box settings.</param>
        /// <param name="stores">Store factory, or null when the black box is not wired.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static async Task<BlackBoxStorageDoctorResult> CheckAsync(BlackBoxSettings settings, BlackBoxStoreFactory? stores, CancellationToken token = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            BlackBoxStorageTargetEnum target = settings.Storage.Target;
            if (!settings.Enabled) return new BlackBoxStorageDoctorResult { Status = DoctorCheckStatusEnum.Warn, Message = "The black box is disabled (blackBox.enabled is false); storage target " + target + " was not checked." };
            List<string> errors = BlackBoxStorageSettingsValidator.Validate(settings.Storage);
            if (errors.Count > 0)
            {
                return new BlackBoxStorageDoctorResult
                {
                    Status = DoctorCheckStatusEnum.Fail,
                    Message = target + " target is misconfigured: " + String.Join(" ", errors) + Fallback(settings)
                };
            }
            if (stores == null) return new BlackBoxStorageDoctorResult { Status = DoctorCheckStatusEnum.Warn, Message = "The black box runtime is not running; storage target " + target + " was not checked." };
            try
            {
                BlackBoxStoreHealth health = await stores.Get(target).CheckHealthAsync(token).ConfigureAwait(false);
                if (health.Healthy) return new BlackBoxStorageDoctorResult { Status = DoctorCheckStatusEnum.Pass, Message = target + " target healthy (" + health.LatencyMs + " ms): " + health.Message };
                return new BlackBoxStorageDoctorResult { Status = DoctorCheckStatusEnum.Fail, Message = target + " target unhealthy: " + health.Message + Fallback(settings) };
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return new BlackBoxStorageDoctorResult { Status = DoctorCheckStatusEnum.Fail, Message = target + " target unavailable: " + e.Message + Fallback(settings) };
            }
        }

        private static string Fallback(BlackBoxSettings settings)
        {
            return settings.Storage.FallbackToDatabase
                ? " New entries fall back to the database."
                : " Fallback is off: new entries keep record.json in the database and lose their other objects.";
        }
    }
}
