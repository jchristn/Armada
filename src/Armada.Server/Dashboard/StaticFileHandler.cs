namespace Armada.Server.Dashboard
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using Armada.Core;

    /// <summary>
    /// Serves static files for the web dashboard.
    /// Checks an external directory first (React build output),
    /// then falls back to embedded wwwroot resources (legacy dashboard, not the React dashboard).
    /// </summary>
    public static class StaticFileHandler
    {
        #region Private-Members

        private static readonly Assembly _Assembly = typeof(StaticFileHandler).Assembly;
        private static readonly string _Prefix = "Armada.Server.wwwroot.";

        private static readonly Dictionary<string, string> _ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" },
            { ".js", "application/javascript; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif", "image/gif" },
            { ".svg", "image/svg+xml" },
            { ".ico", "image/x-icon" },
            { ".woff", "font/woff" },
            { ".woff2", "font/woff2" },
            { ".ttf", "font/ttf" },
            { ".eot", "application/vnd.ms-fontobject" },
            { ".map", "application/json" }
        };

        private static string? _ExternalDashboardPath = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the external dashboard directory path.
        /// When set, files are served from this directory before falling back to embedded legacy resources.
        /// </summary>
        /// <param name="path">Absolute path to the dashboard build output directory (e.g., dist/).</param>
        public static void SetExternalPath(string? path)
        {
            if (!String.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                _ExternalDashboardPath = Path.GetFullPath(path);
            }
            else
            {
                _ExternalDashboardPath = null;
            }
        }

        /// <summary>
        /// Try to read a static file for the given URL path.
        /// Checks external dashboard directory first, then falls back to embedded legacy resources.
        /// </summary>
        /// <param name="urlPath">URL path (e.g., "/dashboard/index.html").</param>
        /// <param name="content">File content bytes.</param>
        /// <param name="contentType">MIME content type.</param>
        /// <returns>True if the file was found.</returns>
        public static bool TryGetFile(string urlPath, out byte[] content, out string contentType)
        {
            content = Array.Empty<byte>();
            contentType = "application/octet-stream";

            if (String.IsNullOrEmpty(urlPath)) return false;
            EnsureExternalPathResolved();

            // Strip leading /dashboard/ prefix
            string relativePath = urlPath;
            if (relativePath.StartsWith("/dashboard/", StringComparison.OrdinalIgnoreCase))
                relativePath = relativePath.Substring("/dashboard/".Length);
            else if (String.Equals(relativePath, "/dashboard", StringComparison.OrdinalIgnoreCase))
                relativePath = "index.html";

            if (String.IsNullOrEmpty(relativePath) || relativePath == "/")
                relativePath = "index.html";

            // Sanitize: prevent directory traversal
            relativePath = relativePath.Replace('\\', '/');
            if (relativePath.Split('/').Any(segment => segment == ".." || segment == ".")) return false;

            // Try external directory first
            if (_ExternalDashboardPath != null)
            {
                if (TryGetExternalFile(relativePath, out content, out contentType))
                    return true;
            }

            // Fall back to embedded resources
            return TryGetEmbeddedFile(relativePath, out content, out contentType);
        }

        /// <summary>
        /// Try to get the SPA index.html fallback for client-side routing.
        /// </summary>
        /// <param name="content">File content bytes.</param>
        /// <param name="contentType">MIME content type.</param>
        /// <returns>True if index.html was found.</returns>
        public static bool TryGetIndex(out byte[] content, out string contentType)
        {
            EnsureExternalPathResolved();
            return TryGetFile("/dashboard/index.html", out content, out contentType);
        }

        /// <summary>
        /// Returns true if an external dashboard directory is configured and exists.
        /// </summary>
        public static bool HasExternalDashboard => _ExternalDashboardPath != null;

        /// <summary>
        /// List all embedded resource names (for debugging).
        /// </summary>
        /// <returns>All embedded resource names.</returns>
        public static string[] ListResources()
        {
            return _Assembly.GetManifestResourceNames();
        }

        #endregion

        #region Private-Methods

        private static void EnsureExternalPathResolved()
        {
            if (_ExternalDashboardPath != null && Directory.Exists(_ExternalDashboardPath))
                return;

            _ExternalDashboardPath = null;

            string dashboardInData = Path.Combine(Constants.DefaultDataDirectory, "dashboard");
            if (Directory.Exists(dashboardInData) && File.Exists(Path.Combine(dashboardInData, "index.html")))
            {
                _ExternalDashboardPath = Path.GetFullPath(dashboardInData);
                return;
            }

            // Assembly.Location is empty in a single-file publish (the .pkg, .deb/.rpm, and .msi servers), so fall
            // back to the application base directory, which is the folder holding the executable there.
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string? exeDir = !String.IsNullOrEmpty(assemblyLocation)
                ? Path.GetDirectoryName(assemblyLocation)
                : AppContext.BaseDirectory;
            if (String.IsNullOrEmpty(exeDir))
                return;

            string dashboardNextToExe = Path.Combine(exeDir, "dashboard");
            if (Directory.Exists(dashboardNextToExe) && File.Exists(Path.Combine(dashboardNextToExe, "index.html")))
            {
                _ExternalDashboardPath = Path.GetFullPath(dashboardNextToExe);
            }
        }

        private static bool TryGetExternalFile(string relativePath, out byte[] content, out string contentType)
        {
            content = Array.Empty<byte>();
            contentType = "application/octet-stream";

            string filePath = Path.Combine(_ExternalDashboardPath!, relativePath.Replace('/', Path.DirectorySeparatorChar));
            filePath = Path.GetFullPath(filePath);

            // Ensure the resolved path is still within the dashboard directory
            if (!PathContainment.IsInside(_ExternalDashboardPath!, filePath))
                return false;

            if (!File.Exists(filePath))
                return false;

            content = File.ReadAllBytes(filePath);

            string ext = Path.GetExtension(filePath);
            if (!String.IsNullOrEmpty(ext) && _ContentTypes.TryGetValue(ext, out string? ct))
            {
                contentType = ct;
            }

            return true;
        }

        private static bool TryGetEmbeddedFile(string relativePath, out byte[] content, out string contentType)
        {
            content = Array.Empty<byte>();
            contentType = "application/octet-stream";

            // Convert URL path to resource name (replace / with .)
            string resourceName = _Prefix + relativePath.Replace('/', '.').Replace('\\', '.');

            using (Stream? stream = _Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return false;

                using (MemoryStream ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    content = ms.ToArray();
                }
            }

            string ext = Path.GetExtension(relativePath);
            if (!String.IsNullOrEmpty(ext) && _ContentTypes.TryGetValue(ext, out string? ct))
            {
                contentType = ct;
            }

            return true;
        }

        #endregion
    }
}
