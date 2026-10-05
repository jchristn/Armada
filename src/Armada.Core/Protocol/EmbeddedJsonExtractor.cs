namespace Armada.Core.Protocol
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Finds a JSON object embedded in free text (a model reply, or CLI output that may carry a banner) and deserializes
    /// it into a typed class. Fenced <c>```json</c> blocks are tried first; otherwise balanced top-level objects are found
    /// with a string-aware brace scanner (braces inside string literals do not count), and candidates are tried from the
    /// last to the first. A candidate is accepted only when it deserializes into the target type and passes the caller's
    /// acceptance check, so a stray brace in prose or an unrelated object can never be mistaken for the answer.
    /// </summary>
    public static class EmbeddedJsonExtractor
    {
        #region Private-Members

        private static readonly Regex _FencedBlock = new Regex("```(?:json|JSON)?[ \\t]*\\r?\\n(.*?)\\r?\\n[ \\t]*```", RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to extract and deserialize an embedded JSON object of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="text">Free text that may contain the object.</param>
        /// <param name="accept">Acceptance check applied to each deserialized candidate; null accepts any non-null value.</param>
        /// <param name="value">The accepted value, or default.</param>
        /// <returns>True when an acceptable object was found.</returns>
        public static bool TryExtract<T>(string? text, Func<T, bool>? accept, out T? value) where T : class
        {
            return TryExtract<T>(text, accept, out value, out string? _);
        }

        /// <summary>
        /// Try to extract and deserialize an embedded JSON object of type <typeparamref name="T"/>, also returning the
        /// JSON text of the accepted candidate.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="text">Free text that may contain the object.</param>
        /// <param name="accept">Acceptance check applied to each deserialized candidate; null accepts any non-null value.</param>
        /// <param name="value">The accepted value, or default.</param>
        /// <param name="json">The JSON text of the accepted candidate, or null.</param>
        /// <returns>True when an acceptable object was found.</returns>
        public static bool TryExtract<T>(string? text, Func<T, bool>? accept, out T? value, out string? json) where T : class
        {
            value = null;
            json = null;
            if (String.IsNullOrWhiteSpace(text)) return false;

            List<string> candidates = new List<string>();

            // Fenced blocks first (the documented answer format), last block first.
            MatchCollection fences = _FencedBlock.Matches(text!);
            for (int i = fences.Count - 1; i >= 0; i--)
            {
                string body = fences[i].Groups[1].Value.Trim();
                if (body.StartsWith("{", StringComparison.Ordinal)) candidates.Add(body);
            }

            // Then every balanced top-level object in the text, last first.
            List<string> objects = FindObjects(text!);
            for (int i = objects.Count - 1; i >= 0; i--) candidates.Add(objects[i]);

            foreach (string candidate in candidates)
            {
                T? parsed = null;
                try
                {
                    parsed = JsonSerializer.Deserialize<T>(candidate, _Options);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (parsed == null) continue;
                if (accept != null && !accept(parsed)) continue;

                value = parsed;
                json = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Find every balanced top-level JSON object in free text, in order of appearance, using a brace scanner that
        /// ignores braces inside string literals. Objects nested inside a found object are not returned separately.
        /// </summary>
        /// <param name="text">Free text.</param>
        /// <returns>The object texts; empty when none is found.</returns>
        public static List<string> FindObjects(string? text)
        {
            List<string> results = new List<string>();
            if (String.IsNullOrEmpty(text)) return results;

            int start = text!.IndexOf('{');
            while (start >= 0 && start < text.Length)
            {
                int end = FindObjectEnd(text, start);
                if (end > start)
                {
                    results.Add(text.Substring(start, end - start + 1));
                    start = end + 1 < text.Length ? text.IndexOf('{', end + 1) : -1;
                }
                else
                {
                    start = start + 1 < text.Length ? text.IndexOf('{', start + 1) : -1;
                }
            }

            return results;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Return the index of the brace that closes the object opened at <paramref name="start"/>, or -1 when the object
        /// is not closed. String literals (with escapes) are skipped so braces inside them do not count.
        /// </summary>
        private static int FindObjectEnd(string text, int start)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        #endregion
    }
}
