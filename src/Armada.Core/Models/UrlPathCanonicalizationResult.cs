namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Result of canonicalizing a URL path with <see cref="Armada.Core.UrlPathCanonicalizer"/>.
    /// </summary>
    public class UrlPathCanonicalizationResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the path was canonicalized. When false, <see cref="Rejection"/> says why.
        /// </summary>
        public bool Success
        {
            get => Rejection == UrlPathRejectionEnum.None;
        }

        /// <summary>
        /// Why the path was rejected, or <see cref="UrlPathRejectionEnum.None"/> on success.
        /// </summary>
        public UrlPathRejectionEnum Rejection { get; set; } = UrlPathRejectionEnum.None;

        /// <summary>
        /// Decoded path segments (no empty, "." or ".." segments). Empty for the root path or on failure.
        /// </summary>
        public List<string> Segments { get; set; } = new List<string>();

        /// <summary>
        /// Canonical path: a leading slash, segments joined by single slashes, no trailing slash, and every segment
        /// re-encoded with <see cref="Uri.EscapeDataString(string)"/>. Empty on failure.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the canonical segments start with the given segments, compared case-insensitively.
        /// </summary>
        /// <param name="prefix">Leading segments to compare.</param>
        /// <returns>True when every prefix segment matches.</returns>
        public bool StartsWithSegments(params string[] prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (!Success || Segments.Count < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (!String.Equals(Segments[i], prefix[i], StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        /// <summary>
        /// Whether the canonical segments are exactly the given segments, compared case-insensitively.
        /// </summary>
        /// <param name="segments">Segments to compare.</param>
        /// <returns>True when the paths match segment for segment.</returns>
        public bool MatchesSegments(params string[] segments)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            return Segments.Count == segments.Length && StartsWithSegments(segments);
        }

        #endregion
    }
}
