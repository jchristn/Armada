namespace Armada.Proxy.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Core;
    using Armada.Core.Models;

    /// <summary>
    /// Maps a canonical /dashboard/... request path to a file under the proxy's dashboard directory.
    /// </summary>
    public static class DashboardAssetResolver
    {
        #region Private-Members

        private static readonly char[] _SeparatorChars = new char[]
        {
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
            Path.VolumeSeparatorChar
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the file for a canonical dashboard path. The first segment ("dashboard") is skipped; no further
        /// segments means index.html. Segments containing a directory or volume separator are refused, and the
        /// result must stay inside <paramref name="dashboardDirectory"/> per <see cref="PathContainment"/>.
        /// </summary>
        /// <param name="dashboardDirectory">Dashboard asset directory.</param>
        /// <param name="requestPath">Canonical request path from <see cref="UrlPathCanonicalizer"/>.</param>
        /// <returns>Absolute file path inside the directory, or null when the path is refused.</returns>
        public static string? Resolve(string dashboardDirectory, UrlPathCanonicalizationResult requestPath)
        {
            if (String.IsNullOrEmpty(dashboardDirectory)) throw new ArgumentNullException(nameof(dashboardDirectory));
            if (requestPath == null) throw new ArgumentNullException(nameof(requestPath));
            if (!requestPath.Success) return null;

            List<string> segments = requestPath.Segments.Skip(1).ToList();
            if (segments.Count == 0)
            {
                segments.Add("index.html");
            }

            foreach (string segment in segments)
            {
                if (segment.IndexOfAny(_SeparatorChars) >= 0)
                {
                    return null;
                }
            }

            return PathContainment.ResolveInside(dashboardDirectory, Path.Combine(segments.ToArray()));
        }

        #endregion
    }
}
