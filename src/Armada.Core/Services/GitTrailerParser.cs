namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// Parses commit message trailers (<c>Key: value</c> lines in the last paragraph of a message, as git
    /// interpret-trailers reads them): a paragraph counts as a trailer block when every line is a trailer or a
    /// continuation (indented) line. Continuation lines are joined to the previous value with a space.
    /// </summary>
    public static class GitTrailerParser
    {
        #region Private-Members

        private static readonly Regex _TrailerLine = new Regex(@"^([A-Za-z0-9][A-Za-z0-9-]*)[ \t]*:[ \t]*(.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse the trailers of a commit message body (or a full message).
        /// </summary>
        /// <param name="message">Message text, or null.</param>
        /// <param name="max">Most trailers to return (at least 1).</param>
        /// <returns>The trailers in order; empty when the last paragraph is not a trailer block.</returns>
        public static List<GitTrailer> Parse(string? message, int max = GitCommitRange.MaxTrailersPerCommit)
        {
            List<GitTrailer> trailers = new List<GitTrailer>();
            if (String.IsNullOrWhiteSpace(message)) return trailers;
            int limit = Math.Max(1, max);

            string[] lines = message!.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd().Split('\n');
            int end = lines.Length - 1;
            int start = end;
            while (start > 0 && lines[start - 1].Trim().Length > 0) start--;

            for (int i = start; i <= end; i++)
            {
                string line = lines[i];
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                {
                    if (trailers.Count == 0) return new List<GitTrailer>();
                    GitTrailer last = trailers[trailers.Count - 1];
                    last.Value = (last.Value + " " + line.Trim()).Trim();
                    continue;
                }

                Match match = _TrailerLine.Match(line);
                if (!match.Success) return new List<GitTrailer>();
                if (trailers.Count < limit) trailers.Add(new GitTrailer { Key = match.Groups[1].Value, Value = match.Groups[2].Value.Trim() });
            }

            return trailers;
        }

        #endregion
    }
}
