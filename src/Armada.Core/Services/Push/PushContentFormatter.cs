namespace Armada.Core.Services.Push
{
    using System;
    using System.Text;

    /// <summary>
    /// Sanitizes push text: secret-shaped values are redacted (<see cref="SecretRedactor"/>), control characters and
    /// line breaks collapse to single spaces (so multi-line content such as code or diffs cannot be carried), and the
    /// result is truncated with "...".
    /// </summary>
    public static class PushContentFormatter
    {
        #region Public-Members

        /// <summary>
        /// Maximum title length.
        /// </summary>
        public const int MaxTitleLength = 64;

        /// <summary>
        /// Maximum body length.
        /// </summary>
        public const int MaxBodyLength = 178;

        /// <summary>
        /// Maximum length of a quoted summary (an entity title, or a CLI permission summary) inside a body.
        /// </summary>
        public const int MaxSummaryLength = 60;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sanitize and truncate text.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <param name="max">Maximum length (at least 4).</param>
        /// <returns>The sanitized text (never null).</returns>
        public static string Clean(string? text, int max)
        {
            if (max < 4) max = 4;
            if (String.IsNullOrEmpty(text)) return String.Empty;
            string redacted = SecretRedactor.Redact(text!);
            StringBuilder sb = new StringBuilder(Math.Min(redacted.Length, max + 8));
            bool lastSpace = true;
            foreach (char c in redacted)
            {
                bool space = Char.IsWhiteSpace(c) || Char.IsControl(c);
                if (space)
                {
                    if (!lastSpace) sb.Append(' ');
                    lastSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastSpace = false;
                }

                if (sb.Length > max + 1) break;
            }

            string collapsed = sb.ToString().Trim();
            if (collapsed.Length <= max) return collapsed;
            return collapsed.Substring(0, max - 3).TrimEnd() + "...";
        }

        /// <summary>
        /// Sanitize a title.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>The title.</returns>
        public static string Title(string? text)
        {
            return Clean(text, MaxTitleLength);
        }

        /// <summary>
        /// Sanitize a body.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>The body.</returns>
        public static string Body(string? text)
        {
            return Clean(text, MaxBodyLength);
        }

        /// <summary>
        /// Sanitize a summary quoted inside a body.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>The summary.</returns>
        public static string Summary(string? text)
        {
            return Clean(text, MaxSummaryLength);
        }

        #endregion
    }
}
