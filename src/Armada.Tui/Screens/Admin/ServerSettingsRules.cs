namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using Armada.Tui.Services;

    /// <summary>
    /// Validation and labels shared by the Server settings page, with the dashboard's messages: the "whole number
    /// from min to max" rule of Vessel Import, Fleet Actions, and Data Retention, the "enter a whole number" and
    /// "between min and max" rules of Repository Health, health and tunnel state labels.
    /// </summary>
    public static class ServerSettingsRules
    {
        #region Private-Members

        private static readonly Regex _Integer = new Regex("^-?\\d+$", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a whole number, or null.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Value or null.</returns>
        public static int? ParseInt(string? text)
        {
            string value = (text ?? "").Trim();
            if (!_Integer.IsMatch(value)) return null;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return null;
            return n;
        }

        /// <summary>
        /// Dashboard <c>rangeError</c>: any value that is not a whole number in range gets "Must be a whole number
        /// from {{min}} to {{max}}."
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="text">Text.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="max">Maximum.</param>
        /// <returns>Translated error, or null.</returns>
        public static string? WholeNumberRange(ITextLocalizer loc, string text, int min, int max)
        {
            int? n = ParseInt(text);
            if (n.HasValue && n.Value >= min && n.Value <= max) return null;
            return loc.T("Must be a whole number from {{min}} to {{max}}.", LocalizationArgs.Of("min", loc.FormatNumber(min), "max", loc.FormatNumber(max)));
        }

        /// <summary>
        /// Repository Health <c>validateRange</c>: "Enter a whole number." or "Must be between {{min}} and {{max}}."
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="text">Text.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="max">Maximum.</param>
        /// <returns>Translated error, or null.</returns>
        public static string? Between(ITextLocalizer loc, string text, int min, int max)
        {
            int? n = ParseInt(text);
            if (!n.HasValue) return loc.T("Enter a whole number.");
            if (n.Value < min || n.Value > max) return loc.T("Must be between {{min}} and {{max}}.", LocalizationArgs.Of("min", loc.FormatNumber(min), "max", loc.FormatNumber(max)));
            return null;
        }

        /// <summary>
        /// Minimum-only rule for the main server fields (the dashboard's number inputs with a <c>min</c>).
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="text">Text.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="max">Maximum, or null for none.</param>
        /// <returns>Translated error, or null.</returns>
        public static string? AtLeast(ITextLocalizer loc, string text, int min, int? max)
        {
            if (max.HasValue) return WholeNumberRange(loc, text, min, max.Value);
            int? n = ParseInt(text);
            if (!n.HasValue) return loc.T("Enter a whole number.");
            if (n.Value < min) return loc.T("Must be at least {{min}}.", LocalizationArgs.Of("min", loc.FormatNumber(min)));
            return null;
        }

        /// <summary>
        /// English label for a health status (the dashboard's <c>translateHealthStatus</c>).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>English label (or the raw status).</returns>
        public static string HealthLabel(string? status)
        {
            switch ((status ?? "").ToLowerInvariant())
            {
                case "healthy": return "Healthy";
                case "degraded": return "Degraded";
                case "unhealthy": return "Unhealthy";
                case "error": return "Error";
                case "unknown": return "Unknown";
                default: return String.IsNullOrEmpty(status) ? "-" : status!;
            }
        }

        /// <summary>
        /// English label for the remote tunnel state (the dashboard's <c>getRemoteTunnelIndicator</c>).
        /// </summary>
        /// <param name="enabled">Tunnel enabled.</param>
        /// <param name="state">State name, or null.</param>
        /// <returns>English label.</returns>
        public static string TunnelLabel(bool enabled, string? state)
        {
            if (!enabled) return "Disabled";
            switch ((state ?? "").ToLowerInvariant())
            {
                case "connected": return "Connected";
                case "connecting": return "Connecting";
                case "stopping": return "Stopping";
                case "error": return "Error";
                case "disconnected": return "Disconnected";
                default: return "Checking status";
            }
        }

        /// <summary>
        /// True for a rebuild status that ends polling (terminal, or cutting over while the server restarts).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True when polling should stop.</returns>
        public static bool RebuildDone(string? status)
        {
            return status == "Succeeded" || status == "Failed" || status == "RolledBack" || status == "CuttingOver";
        }

        #endregion
    }
}
