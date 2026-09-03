namespace Armada.Core.Services
{
    using System.Text.RegularExpressions;

    /// <summary>
    /// One definition of "secret-shaped value" redaction, shared by the runtime-log formatter and the
    /// request-history capture so both surfaces scrub the same token shapes (Authorization bearer tokens,
    /// OpenAI/GitHub/AWS keys, and labelled api-key/secret/token/password assignments). Pure.
    /// </summary>
    public static class SecretRedactor
    {
        #region Private-Members

        private static readonly (Regex Pattern, string Replacement)[] _Redactions = new (Regex, string)[]
        {
            (new Regex(@"(?i)\bBearer\s+[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled), "Bearer [REDACTED]"),
            (new Regex(@"\bsk-[A-Za-z0-9]{16,}", RegexOptions.Compiled), "sk-[REDACTED]"),
            (new Regex(@"\bgh[pousr]_[A-Za-z0-9]{20,}", RegexOptions.Compiled), "gh_[REDACTED]"),
            (new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled), "AKIA[REDACTED]"),
            (new Regex(@"(?i)(api[_\-]?key|apikey|secret|token|password)(\s*[=:]\s*)([""']?)[^\s""']{6,}", RegexOptions.Compiled), "$1$2$3[REDACTED]"),
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Redact secret-shaped values in the text, reporting whether anything was changed.
        /// </summary>
        /// <param name="text">Input text.</param>
        /// <param name="redacted">Set true when at least one value was redacted.</param>
        /// <returns>The redacted text.</returns>
        public static string Redact(string text, out bool redacted)
        {
            redacted = false;
            if (string.IsNullOrEmpty(text)) return text;

            string current = text;
            foreach ((Regex pattern, string replacement) in _Redactions)
            {
                string next = pattern.Replace(current, replacement);
                if (next != current) redacted = true;
                current = next;
            }
            return current;
        }

        /// <summary>
        /// Redact secret-shaped values in the text.
        /// </summary>
        /// <param name="text">Input text.</param>
        /// <returns>The redacted text.</returns>
        public static string Redact(string text)
        {
            return Redact(text, out _);
        }

        #endregion
    }
}
