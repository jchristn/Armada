namespace Armada.Core.Models
{
    using System;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Services;

    /// <summary>
    /// Payload of the opaque vessel commit history cursor (VesselCommitPage.NextCursor): the tip commit the first page
    /// resolved, the before bound, and how many commits earlier pages returned. Pinning the tip keeps paging stable
    /// while new commits land on the branch. Serialized as JSON, then base64url without padding.
    /// </summary>
    public class VesselCommitCursor
    {
        #region Public-Members

        /// <summary>
        /// Current payload version.
        /// </summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Longest encoded cursor accepted.
        /// </summary>
        public const int MaxEncodedLength = 2048;

        /// <summary>
        /// Payload version.
        /// </summary>
        public int V { get; set; } = CurrentVersion;

        /// <summary>
        /// Branch the history is read from (reported back as VesselCommitPage.Branch).
        /// </summary>
        public string Branch { get; set; } = String.Empty;

        /// <summary>
        /// Full SHA of the tip commit resolved for the first page.
        /// </summary>
        public string Tip { get; set; } = String.Empty;

        /// <summary>
        /// Inclusive upper bound on commit dates, in Unix seconds (the before filter minus one second), or null for none.
        /// </summary>
        public long? Until { get; set; } = null;

        /// <summary>
        /// Commits to skip (returned by earlier pages).
        /// </summary>
        public int Skip { get; set; } = 0;

        #endregion

        #region Private-Members

        private const long _MinUnixSeconds = -62135596800L;
        private const long _MaxUnixSeconds = 253402300799L;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCommitCursor()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode as the opaque cursor string (JSON, base64url, no padding).
        /// </summary>
        /// <returns>Cursor text.</returns>
        public string Encode()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this));
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Decode and validate an opaque cursor string.
        /// </summary>
        /// <param name="text">Cursor text.</param>
        /// <param name="cursor">The cursor when valid; null otherwise.</param>
        /// <returns>True when the text is a well-formed cursor of the current version with valid fields.</returns>
        public static bool TryDecode(string? text, out VesselCommitCursor? cursor)
        {
            cursor = null;
            if (String.IsNullOrEmpty(text) || text.Length > MaxEncodedLength) return false;
            foreach (char c in text)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return false;
            }

            if (text.Length % 4 == 1) return false;
            string padded = text.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            byte[] buffer = new byte[padded.Length];
            if (!Convert.TryFromBase64String(padded, buffer, out int written)) return false;

            VesselCommitCursor? parsed;
            try
            {
                string json = new UTF8Encoding(false, true).GetString(buffer, 0, written);
                parsed = JsonSerializer.Deserialize<VesselCommitCursor>(json);
            }
            catch (JsonException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                // Invalid UTF-8 (DecoderFallbackException derives from ArgumentException).
                return false;
            }

            if (parsed == null) return false;
            if (parsed.V != CurrentVersion) return false;
            if (!GitRevisionNames.IsSafeBranchName(parsed.Branch)) return false;
            if (!GitRevisionNames.IsFullCommitId(parsed.Tip)) return false;
            if (parsed.Skip < 0) return false;
            if (parsed.Until.HasValue && (parsed.Until.Value < _MinUnixSeconds || parsed.Until.Value > _MaxUnixSeconds)) return false;

            cursor = parsed;
            return true;
        }

        #endregion
    }
}
