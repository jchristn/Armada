namespace Armada.Core.BlackBox
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;

    /// <summary>
    /// Failure-mode fingerprint (docs/BLACKBOX.md, "Fingerprint"): SHA-256 (lowercase hex) of
    /// phase|step|kind|site.method|normalized reason template. The template replaces timestamps, GUIDs, Armada ids, URLs,
    /// paths, SHAs and numbers with placeholders, so entries with the same fingerprint are the same failure mode.
    /// Thread-safe.
    /// </summary>
    public static class BlackBoxFingerprint
    {
        #region Private-Members

        private static readonly RegexOptions _Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private static readonly TimeSpan _Timeout = TimeSpan.FromMilliseconds(250);
        private static readonly Regex _Timestamp = new Regex(@"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+\-]\d{2}:?\d{2})?", _Options, _Timeout);
        private static readonly Regex _Guid = new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", _Options, _Timeout);
        private static readonly Regex _ArmadaId = new Regex(@"\b[a-z]{2,4}_[A-Za-z0-9]{6,}(?:_[A-Za-z0-9]{6,})?\b", _Options, _Timeout);
        private static readonly Regex _Url = new Regex(@"\b[a-zA-Z][a-zA-Z0-9+.\-]*://\S+", _Options, _Timeout);
        private static readonly Regex _Path = new Regex(@"(?:[A-Za-z]:)?(?:[\w.\-]+)?(?:[/\\][\w.\-]+)+[/\\]?", _Options, _Timeout);
        private static readonly Regex _Sha = new Regex(@"\b[0-9a-f]{7,64}\b", _Options, _Timeout);
        private static readonly Regex _Number = new Regex(@"\b\d+(?:\.\d+)?\b", _Options, _Timeout);
        private static readonly Regex _Space = new Regex(@"\s+", _Options, _Timeout);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Most characters of a normalized template.
        /// </summary>
        public const int MaxTemplateChars = 500;

        /// <summary>
        /// Compute a fingerprint.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="step">Step.</param>
        /// <param name="kind">Failure kind, or null.</param>
        /// <param name="method">Failure site method, or null.</param>
        /// <param name="reason">Raw reason, or null.</param>
        /// <returns>64 lowercase hex characters.</returns>
        public static string Compute(BlackBoxPhaseEnum phase, BlackBoxStepEnum step, MissionFailureKindEnum? kind, string? method, string? reason)
        {
            string input = phase + "|" + step + "|" + (kind.HasValue ? kind.Value.ToString() : String.Empty) + "|" + (method ?? String.Empty) + "|" + NormalizeReason(reason);
            using (SHA256 sha = SHA256.Create())
            {
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Normalize a reason into its template: placeholders for timestamps ({time}), GUIDs and Armada ids ({id}), URLs
        /// ({url}), paths ({path}), SHAs ({sha}) and numbers ({n}); whitespace collapsed; at most 500 characters.
        /// </summary>
        /// <param name="reason">Reason, or null.</param>
        /// <returns>The template (empty for null).</returns>
        public static string NormalizeReason(string? reason)
        {
            if (String.IsNullOrWhiteSpace(reason)) return String.Empty;
            string text = reason;
            try
            {
                text = _Timestamp.Replace(text, "{time}");
                text = _Guid.Replace(text, "{id}");
                text = _ArmadaId.Replace(text, "{id}");
                text = _Url.Replace(text, "{url}");
                text = _Path.Replace(text, "{path}");
                text = _Sha.Replace(text, "{sha}");
                text = _Number.Replace(text, "{n}");
                text = _Space.Replace(text, " ").Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                text = text.Trim();
            }
            return text.Length <= MaxTemplateChars ? text : text.Substring(0, MaxTemplateChars);
        }

        #endregion
    }
}
