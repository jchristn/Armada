namespace Armada.Core.Services
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Models;

    /// <summary>Stable conservative window identity without inferring rollover from a moving reset timestamp.</summary>
    public static class UsageBudgetIdentity
    {
        /// <summary>Continuous accounting until a provider proves fixed-reset generation semantics.</summary>
        public const string ContinuousGeneration = "continuous";

        /// <summary>Hash normalized window semantics; reset time is evidence, not an inferred generation.</summary>
        /// <param name="window">Sanitized provider window.</param>
        /// <param name="configurationDiscriminator">Custom executable revision, when identities must remain separate.</param>
        /// <returns>Opaque stable 64-character window discriminator.</returns>
        public static string WindowKey(UsageWindow window, string? configurationDiscriminator = null)
        {
            ArgumentNullException.ThrowIfNull(window);
            string? scope = String.IsNullOrWhiteSpace(window.Scope) ? "account" : UsageSelectionEvidenceSanitizer.Label(window.Scope.Trim(), 4096);
            string? unit = String.IsNullOrWhiteSpace(window.Unit) && window.UsedPercent.HasValue ? "percent" : UsageSelectionEvidenceSanitizer.Label(window.Unit?.Trim().ToLowerInvariant(), 4096);
            string normalized = JsonSerializer.Serialize(new object?[] { UsageSelectionEvidenceSanitizer.Label(window.Name?.Trim().ToLowerInvariant(), 4096),
                UsageSelectionEvidenceSanitizer.Label(window.Model?.Trim().ToLowerInvariant(), 4096), scope, window.WindowMinutes, unit, configurationDiscriminator });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }
    }
}
