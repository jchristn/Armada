namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Stable conservative window identity and accounting generation; rollover is never inferred from a moving reset timestamp.</summary>
    public static class UsageBudgetIdentity
    {
        /// <summary>Continuous accounting until a provider proves fixed-reset generation semantics.</summary>
        public const string ContinuousGeneration = "continuous";

        /// <summary>Prefix of a fixed-reset generation; the rest is the reset minute in UTC (yyyyMMddTHHmmZ).</summary>
        public const string FixedGenerationPrefix = "fixed:";

        private const string FixedGenerationFormat = "yyyyMMdd'T'HHmm'Z'";

        /// <summary>
        /// Accounting generation for a window. Only an adapter-declared <see cref="UsageWindowResetKindEnum.Fixed"/>
        /// window with an absolute reset time gets a fixed generation named by its reset minute; every other window,
        /// including rolling, unknown and older-host windows, stays on <see cref="ContinuousGeneration"/>.
        /// </summary>
        /// <param name="window">Sanitized provider window.</param>
        /// <returns>The generation key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="window"/> is null.</exception>
        public static string Generation(UsageWindow window)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (window.ResetKind != UsageWindowResetKindEnum.Fixed || !window.ResetsUtc.HasValue) return ContinuousGeneration;
            DateTime reset = DateTime.SpecifyKind(window.ResetsUtc.Value, DateTimeKind.Utc);
            DateTime minute = new DateTime(reset.Ticks - reset.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
            return FixedGenerationPrefix + minute.ToString(FixedGenerationFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>Read the reset minute of a fixed generation; false for continuous or malformed keys.</summary>
        /// <param name="generation">Generation key.</param>
        /// <param name="resetUtc">Reset minute in UTC when the key is a fixed generation.</param>
        /// <returns>True for a well-formed fixed generation.</returns>
        public static bool TryReadFixedReset(string? generation, out DateTime resetUtc)
        {
            resetUtc = default;
            if (generation == null || !generation.StartsWith(FixedGenerationPrefix, StringComparison.Ordinal)) return false;
            if (!DateTime.TryParseExact(generation.Substring(FixedGenerationPrefix.Length), FixedGenerationFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)) return false;
            resetUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

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
