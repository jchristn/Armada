namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Compares git remote URLs by the repository they name rather than by spelling. The https, http, git, and ssh URL
    /// forms and the scp-like form (git@host:owner/repo) of one repository all normalize to the same key
    /// (host/owner/repo): the scheme, user, password, and port are dropped, a trailing ".git" and slashes are removed,
    /// and the result is lower-cased.
    /// </summary>
    public static class GitRemoteUrl
    {
        #region Public-Methods

        /// <summary>
        /// The comparison key for a remote URL (for example "github.com/owner/repo"), or null when the value is empty.
        /// A local path or file:// URL normalizes to its full path, lower-cased.
        /// </summary>
        /// <param name="url">Remote URL.</param>
        /// <returns>The key, or null.</returns>
        public static string? Normalize(string? url)
        {
            if (String.IsNullOrWhiteSpace(url)) return null;
            string value = url.Trim();

            string host;
            string path;
            if (TrySplitUri(value, out string uriHost, out string uriPath))
            {
                host = uriHost;
                path = uriPath;
            }
            else if (TrySplitScpLike(value, out string scpHost, out string scpPath))
            {
                host = scpHost;
                path = scpPath;
            }
            else
            {
                // A local path: compare full paths.
                host = String.Empty;
                path = value.Replace('\\', '/');
            }

            path = path.Replace('\\', '/').Trim('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4);
            path = path.TrimEnd('/');

            string key = host.Length > 0 ? host + "/" + path : "/" + path;
            return key.ToLowerInvariant();
        }

        /// <summary>
        /// Whether two remote URLs name the same repository.
        /// </summary>
        /// <param name="a">First URL.</param>
        /// <param name="b">Second URL.</param>
        /// <returns>True when both normalize to the same non-empty key.</returns>
        public static bool SameRepository(string? a, string? b)
        {
            string? left = Normalize(a);
            string? right = Normalize(b);
            return left != null && right != null && String.Equals(left, right, StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static bool TrySplitUri(string value, out string host, out string path)
        {
            host = String.Empty;
            path = String.Empty;
            int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0) return false;

            string scheme = value.Substring(0, schemeEnd).ToLowerInvariant();
            string rest = value.Substring(schemeEnd + 3);
            if (scheme == "file")
            {
                path = rest;
                return true;
            }

            int slash = rest.IndexOf('/');
            string authority = slash < 0 ? rest : rest.Substring(0, slash);
            path = slash < 0 ? String.Empty : rest.Substring(slash + 1);

            int at = authority.LastIndexOf('@');
            if (at >= 0) authority = authority.Substring(at + 1);
            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                int close = authority.IndexOf(']');
                if (close > 0) authority = authority.Substring(0, close + 1);
            }
            else
            {
                int colon = authority.IndexOf(':');
                if (colon >= 0) authority = authority.Substring(0, colon);
            }

            host = authority;
            return host.Length > 0;
        }

        private static bool TrySplitScpLike(string value, out string host, out string path)
        {
            // git's scp-like syntax: [user@]host:path, where the part before the first colon has no slash. A Windows
            // drive path (C:\repo, C:/repo) is a local path, not a one-letter host.
            host = String.Empty;
            path = String.Empty;
            int colon = value.IndexOf(':');
            if (colon <= 0) return false;

            string before = value.Substring(0, colon);
            if (before.IndexOf('/') >= 0 || before.IndexOf('\\') >= 0) return false;
            if (before.Length == 1 && Char.IsLetter(before[0])) return false;

            int at = before.LastIndexOf('@');
            host = at >= 0 ? before.Substring(at + 1) : before;
            path = value.Substring(colon + 1);
            return host.Length > 0;
        }

        #endregion
    }
}
