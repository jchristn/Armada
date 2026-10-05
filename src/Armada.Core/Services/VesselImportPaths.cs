namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Path and repository URL normalization shared by vessel discovery and import. Paths compare case-insensitively
    /// on Windows and macOS and case-sensitively on Linux. Thread-safe (stateless).
    /// </summary>
    public static class VesselImportPaths
    {
        #region Public-Members

        /// <summary>
        /// Comparer for filesystem paths on the current platform: ordinal on Linux, ordinal ignore-case elsewhere.
        /// </summary>
        public static StringComparer PathComparer { get; } = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// String comparison matching <see cref="PathComparer"/>.
        /// </summary>
        public static StringComparison PathComparison { get; } = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize a user-supplied absolute path: trim, expand a leading ~ to the user profile, collapse . and ..
        /// segments, resolve symbolic links in existing segments, repair on-disk casing, and remove trailing
        /// separators.
        /// </summary>
        /// <param name="path">Absolute path, optionally starting with ~.</param>
        /// <returns>The normalized absolute path.</returns>
        /// <exception cref="ArgumentException">Thrown when the path is empty, relative, or malformed.</exception>
        public static string NormalizeInputPath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path must not be empty.", nameof(path));
            string trimmed = path.Trim();

            if (trimmed == "~" || trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith("~\\", StringComparison.Ordinal))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                trimmed = trimmed.Length <= 2 ? home : Path.Combine(home, trimmed.Substring(2));
            }

            if (!Path.IsPathFullyQualified(trimmed))
                throw new ArgumentException("Path must be absolute: " + trimmed, nameof(path));

            string full;
            try
            {
                full = Path.GetFullPath(trimmed);
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                throw new ArgumentException("Path is not valid: " + trimmed, nameof(path), ex);
            }

            string canonical = PathCanonicalizer.Canonicalize(full);
            return ResolveOnDiskCasing(canonical);
        }

        /// <summary>
        /// Return the path with each existing segment spelled as it is on disk (relevant on case-insensitive file
        /// systems). Segments that do not exist, or that contain wildcard characters, are kept as given.
        /// </summary>
        /// <param name="path">Absolute path.</param>
        /// <returns>The path with on-disk casing.</returns>
        public static string ResolveOnDiskCasing(string path)
        {
            if (String.IsNullOrEmpty(path)) return path;
            string? root = Path.GetPathRoot(path);
            if (String.IsNullOrEmpty(root)) return path;

            string[] segments = path.Substring(root.Length).Split(
                new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            string current = root;
            bool exists = true;
            foreach (string segment in segments)
            {
                string next = segment;
                if (exists && segment.IndexOfAny(new char[] { '*', '?' }) < 0)
                {
                    try
                    {
                        DirectoryInfo parent = new DirectoryInfo(current);
                        List<FileSystemInfo> matches = parent
                            .EnumerateFileSystemInfos(segment, new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive })
                            .Take(16)
                            .ToList();
                        string composed = segment.Normalize(NormalizationForm.FormC);
                        FileSystemInfo? match = matches.FirstOrDefault(i => String.Equals(i.Name, segment, StringComparison.Ordinal))
                            ?? matches.FirstOrDefault(i => String.Equals(i.Name, segment, PathComparison))
                            ?? matches.FirstOrDefault(i => String.Equals(i.Name.Normalize(NormalizationForm.FormC), composed, PathComparison));
                        if (match != null) next = match.Name;
                        else exists = false;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                    {
                        exists = false;
                    }
                }

                current = Path.Combine(current, next);
            }

            return current;
        }

        /// <summary>
        /// Whether <paramref name="path"/> equals <paramref name="root"/> or lies beneath it. Both must already be
        /// normalized.
        /// </summary>
        /// <param name="path">Normalized path.</param>
        /// <param name="root">Normalized root.</param>
        /// <returns>True when the path is the root or inside it.</returns>
        public static bool IsSameOrUnder(string path, string root)
        {
            if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(root)) return false;
            if (String.Equals(path, root, PathComparison)) return true;
            return PathContainment.IsInside(root, path, allowRoot: true);
        }

        /// <summary>
        /// Normalize a stored directory (for example a vessel WorkingDirectory) for comparison: full path, symbolic
        /// links resolved, trailing separators removed. Returns null for empty or malformed values.
        /// </summary>
        /// <param name="path">Directory path, or null.</param>
        /// <returns>The normalized path, or null.</returns>
        public static string? NormalizeStoredPath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (!Path.IsPathFullyQualified(path.Trim())) return null;
                return PathCanonicalizer.Canonicalize(path.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is IOException || ex is System.Security.SecurityException)
            {
                return null;
            }
        }

        /// <summary>
        /// Normalize a repository URL into a comparison key so equivalent spellings match. HTTPS, SSH, and scp-style
        /// (user@host:path) URLs reduce to "host/path" with a lower-case host, without credentials, port, trailing
        /// slash, or trailing .git, compared case-insensitively. file:// URLs and local paths reduce to their
        /// normalized path prefixed with "file:". Returns null for empty input.
        /// </summary>
        /// <param name="url">Repository URL or local path, or null.</param>
        /// <returns>The comparison key, or null.</returns>
        public static string? NormalizeRepoUrl(string? url)
        {
            if (String.IsNullOrWhiteSpace(url)) return null;
            string value = url.Trim();

            if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string? local = NormalizeStoredPath(new Uri(value).LocalPath);
                    return local == null ? null : "file:" + StripGitSuffix(local);
                }
                catch (UriFormatException)
                {
                    return null;
                }
            }

            if (Path.IsPathFullyQualified(value) && !value.Contains("://", StringComparison.Ordinal))
            {
                string? local = NormalizeStoredPath(value);
                return local == null ? null : "file:" + StripGitSuffix(local);
            }

            string host;
            string pathPart;
            int scheme = value.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0)
            {
                string rest = value.Substring(scheme + 3);
                int slash = rest.IndexOf('/');
                string authority = slash < 0 ? rest : rest.Substring(0, slash);
                pathPart = slash < 0 ? String.Empty : rest.Substring(slash + 1);
                int at = authority.LastIndexOf('@');
                if (at >= 0) authority = authority.Substring(at + 1);
                int colon = authority.IndexOf(':');
                if (colon >= 0) authority = authority.Substring(0, colon);
                host = authority;
            }
            else
            {
                // scp-like syntax: [user@]host:path
                int colon = value.IndexOf(':');
                if (colon <= 0) return StripGitSuffix(value).ToLowerInvariant();
                string authority = value.Substring(0, colon);
                pathPart = value.Substring(colon + 1);
                int at = authority.LastIndexOf('@');
                if (at >= 0) authority = authority.Substring(at + 1);
                host = authority;
            }

            pathPart = StripGitSuffix(pathPart.Trim('/'));
            return (host.ToLowerInvariant() + "/" + pathPart).ToLowerInvariant();
        }

        /// <summary>
        /// Distinct, normalized absolute paths from a list, dropping empty entries. Order is preserved.
        /// </summary>
        /// <param name="paths">Paths.</param>
        /// <returns>Distinct normalized paths.</returns>
        /// <exception cref="ArgumentException">Thrown when any non-empty path is relative or malformed.</exception>
        public static List<string> NormalizeDistinct(IEnumerable<string> paths)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(PathComparer);
            foreach (string path in paths)
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                string normalized = NormalizeInputPath(path);
                if (seen.Add(normalized)) result.Add(normalized);
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static string StripGitSuffix(string value)
        {
            string result = value.TrimEnd('/', '\\');
            while (result.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - 4).TrimEnd('/', '\\');
            }

            return result;
        }

        #endregion
    }
}
