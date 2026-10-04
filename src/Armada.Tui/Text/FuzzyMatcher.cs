namespace Armada.Tui.Text
{
    using System;

    /// <summary>
    /// Subsequence fuzzy matching for the command palette and pickers: every query character must appear in order;
    /// prefix, word-start, and contiguous matches score higher. Case-insensitive. Thread-safe (stateless).
    /// </summary>
    public static class FuzzyMatcher
    {
        #region Public-Methods

        /// <summary>
        /// Score a candidate.
        /// </summary>
        /// <param name="query">Query; empty matches everything with score 0.</param>
        /// <param name="candidate">Candidate text.</param>
        /// <returns>Score (higher is better), or -1 when the query is not a subsequence.</returns>
        public static int Score(string? query, string? candidate)
        {
            if (String.IsNullOrEmpty(query)) return 0;
            if (String.IsNullOrEmpty(candidate)) return -1;
            string q = query!.Trim().ToLowerInvariant();
            if (q.Length == 0) return 0;
            string c = candidate!.ToLowerInvariant();
            int score = 0;
            int ci = 0;
            int lastMatch = -2;
            for (int qi = 0; qi < q.Length; qi++)
            {
                char ch = q[qi];
                if (ch == ' ') continue;
                int found = c.IndexOf(ch, ci);
                if (found < 0) return -1;
                score += 1;
                if (found == 0) score += 8;
                else if (!Char.IsLetterOrDigit(c[found - 1])) score += 5;
                if (found == lastMatch + 1) score += 4;
                lastMatch = found;
                ci = found + 1;
            }

            if (c.StartsWith(q, StringComparison.Ordinal)) score += 20;
            else if (c.Contains(q)) score += 10;
            score -= Math.Min(10, c.Length / 10);
            return score;
        }

        #endregion
    }
}
