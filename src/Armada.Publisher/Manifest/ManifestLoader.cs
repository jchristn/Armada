namespace Armada.Publisher.Manifest
{
    using System;
    using System.IO;
    using System.Text.Json;

    /// <summary>
    /// Loads and deserializes publisher.json into a strongly-typed manifest.
    /// </summary>
    public static class ManifestLoader
    {
        #region Public-Members

        /// <summary>
        /// JSON options: case-insensitive property names and string-valued enums.
        /// </summary>
        public static JsonSerializerOptions Options
        {
            get { return _Options; }
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read and validate a manifest from disk.
        /// </summary>
        /// <param name="path">Path to publisher.json.</param>
        /// <returns>The deserialized, validated manifest.</returns>
        public static PublisherManifest Load(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Manifest not found.", path);

            string json = File.ReadAllText(path);
            PublisherManifest? manifest = JsonSerializer.Deserialize<PublisherManifest>(json, _Options);
            if (manifest == null) throw new InvalidOperationException("Manifest at '" + path + "' deserialized to null.");

            manifest.Validate();
            return manifest;
        }

        #endregion
    }
}
