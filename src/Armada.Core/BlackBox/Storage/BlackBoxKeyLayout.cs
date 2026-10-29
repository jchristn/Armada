namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Object key layout for external black box targets:
    /// {keyPrefix}/{tenantId}/{vesselId or _}/{yyyy}/{MM}/{entryId}/{objectName}. Segments are sanitized so a key never
    /// escapes its prefix (no empty, "." or ".." segments, no backslashes). Thread-safe.
    /// </summary>
    public static class BlackBoxKeyLayout
    {
        /// <summary>
        /// Placeholder segment for a missing tenant or vessel.
        /// </summary>
        public const string Placeholder = "_";

        /// <summary>
        /// Build an entry's key prefix.
        /// </summary>
        /// <param name="keyPrefix">Configured key prefix (BlackBoxStorageSettings.KeyPrefix).</param>
        /// <param name="tenantId">Tenant, or null.</param>
        /// <param name="vesselId">Vessel, or null.</param>
        /// <param name="createdUtc">Entry creation time (UTC).</param>
        /// <param name="entryId">Entry id.</param>
        /// <returns>The prefix, without a trailing slash.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entryId is null or empty.</exception>
        public static string BuildEntryPrefix(string? keyPrefix, string? tenantId, string? vesselId, DateTime createdUtc, string entryId)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            DateTime utc = createdUtc.Kind == DateTimeKind.Local ? createdUtc.ToUniversalTime() : createdUtc;
            StringBuilder sb = new StringBuilder();
            string prefix = (keyPrefix ?? String.Empty).Trim().Trim('/');
            foreach (string part in prefix.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append(Segment(part)).Append('/');
            }
            sb.Append(Segment(tenantId)).Append('/');
            sb.Append(Segment(vesselId)).Append('/');
            sb.Append(utc.ToString("yyyy", CultureInfo.InvariantCulture)).Append('/');
            sb.Append(utc.ToString("MM", CultureInfo.InvariantCulture)).Append('/');
            sb.Append(Segment(entryId));
            return sb.ToString();
        }

        /// <summary>
        /// Build an object's full key.
        /// </summary>
        /// <param name="entryPrefix">Entry prefix from <see cref="BuildEntryPrefix"/>.</param>
        /// <param name="objectName">Object name (may contain slashes, for example evidence/mission.log).</param>
        /// <returns>The key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public static string BuildKey(string entryPrefix, string objectName)
        {
            if (String.IsNullOrEmpty(entryPrefix)) throw new ArgumentNullException(nameof(entryPrefix));
            if (String.IsNullOrEmpty(objectName)) throw new ArgumentNullException(nameof(objectName));
            StringBuilder sb = new StringBuilder(entryPrefix.TrimEnd('/'));
            foreach (string part in objectName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append('/').Append(Segment(part));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Sanitize one key segment: null or blank becomes the placeholder; "." and ".." become "_"; characters other than
        /// letters, digits, '-', '_' and '.' become '_'.
        /// </summary>
        /// <param name="value">Segment.</param>
        /// <returns>The safe segment.</returns>
        public static string Segment(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return Placeholder;
            string trimmed = value.Trim();
            if (trimmed == "." || trimmed == "..") return Placeholder;
            StringBuilder sb = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                sb.Append(Char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            }
            return sb.ToString();
        }
    }
}
