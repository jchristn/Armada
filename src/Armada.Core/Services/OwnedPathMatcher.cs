namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Compares mission path ownership. A path is a file, a directory prefix (trailing <c>/</c>), or a pattern where
    /// <c>*</c> matches within one segment and <c>**</c> across segments. A bare path that names a directory of another
    /// path is treated as a prefix, so <c>src</c> and <c>src/a.cs</c> overlap.
    /// </summary>
    public static class OwnedPathMatcher
    {
        #region Public-Methods

        /// <summary>Normalize a path: forward slashes, no leading <c>./</c> or <c>/</c>, trimmed.</summary>
        /// <param name="path">Path as written.</param>
        /// <returns>The normalized path, or an empty string.</returns>
        public static string Normalize(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return String.Empty;
            string value = path.Trim().Trim('`', '"', '\'').Replace('\\', '/');
            while (value.StartsWith("./", StringComparison.Ordinal)) value = value.Substring(2);
            return value.TrimStart('/');
        }

        /// <summary>True when the two paths could name the same file.</summary>
        /// <param name="a">First path.</param>
        /// <param name="b">Second path.</param>
        /// <returns>True on overlap.</returns>
        public static bool Overlaps(string? a, string? b)
        {
            string x = Normalize(a), y = Normalize(b);
            if (x.Length == 0 || y.Length == 0) return false;
            bool xWild = x.Contains('*'), yWild = y.Contains('*');
            if (!xWild && !yWild) return Prefix(x, y) || Prefix(y, x);
            if (xWild && !yWild) return Matches(x, y);
            if (!xWild) return Matches(y, x);
            // Two patterns: compare the literal parts before the first wildcard, conservatively.
            string px = x.Substring(0, x.IndexOf('*')), py = y.Substring(0, y.IndexOf('*'));
            return px.StartsWith(py, StringComparison.Ordinal) || py.StartsWith(px, StringComparison.Ordinal);
        }

        /// <summary>The first overlapping pair between two path lists, or null.</summary>
        /// <param name="first">First mission's paths.</param>
        /// <param name="second">Second mission's paths.</param>
        /// <returns>A description of the overlap, such as <c>src/Foo.cs</c> or <c>src/ and src/Foo.cs</c>.</returns>
        public static string? FirstOverlap(IEnumerable<string>? first, IEnumerable<string>? second)
        {
            foreach (string a in first ?? Enumerable.Empty<string>())
                foreach (string b in second ?? Enumerable.Empty<string>())
                    if (Overlaps(a, b))
                    {
                        string x = Normalize(a), y = Normalize(b);
                        return x == y ? x : x + " and " + y;
                    }
            return null;
        }

        #endregion

        #region Private-Methods

        private static bool Prefix(string directory, string path)
        {
            if (directory == path) return true;
            string prefix = directory.EndsWith("/", StringComparison.Ordinal) ? directory : directory + "/";
            return path.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static bool Matches(string pattern, string path)
        {
            StringBuilder regex = new StringBuilder("^");
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*') { regex.Append(".*"); i++; }
                else if (c == '*') regex.Append("[^/]*");
                else regex.Append(Regex.Escape(c.ToString()));
            }
            // A pattern ending in / covers everything below it; a path inside a matched directory overlaps too.
            regex.Append(pattern.EndsWith("/", StringComparison.Ordinal) ? ".*$" : "(/.*)?$");
            return Regex.IsMatch(path, regex.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                || Prefix(path, pattern.Substring(0, pattern.IndexOf('*')));
        }

        #endregion
    }
}
