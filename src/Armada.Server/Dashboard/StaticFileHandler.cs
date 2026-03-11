namespace Armada.Server.Dashboard
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// Serves embedded static files from the wwwroot directory.
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
            { ".svg", "image/svg+xml" },
            { ".ico", "image/x-icon" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to read an embedded resource for the given URL path.
        /// </summary>
        /// <param name="urlPath">URL path (e.g., "/dashboard/index.html").</param>
        /// <param name="content">File content bytes.</param>
        /// <param name="contentType">MIME content type.</param>
        /// <returns>True if the resource was found.</returns>
        public static bool TryGetFile(string urlPath, out byte[] content, out string contentType)
        {
            content = Array.Empty<byte>();
            contentType = "application/octet-stream";

            if (String.IsNullOrEmpty(urlPath)) return false;

            // Strip leading /dashboard/ prefix
            string relativePath = urlPath;
            if (relativePath.StartsWith("/dashboard/"))
                relativePath = relativePath.Substring("/dashboard/".Length);
            else if (relativePath == "/dashboard")
                relativePath = "index.html";

            if (String.IsNullOrEmpty(relativePath) || relativePath == "/")
                relativePath = "index.html";

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

            // Determine content type from extension
            string ext = Path.GetExtension(relativePath);
            if (!String.IsNullOrEmpty(ext) && _ContentTypes.TryGetValue(ext, out string? ct))
            {
                contentType = ct;
            }

            return true;
        }

        /// <summary>
        /// List all embedded resource names (for debugging).
        /// </summary>
        /// <returns>All embedded resource names.</returns>
        public static string[] ListResources()
        {
            return _Assembly.GetManifestResourceNames();
        }

        #endregion
    }
}
