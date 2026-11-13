namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// Reads battery packs from disk. A pack is a directory holding a battery-pack.json manifest; malformed packs are
    /// returned with their errors instead of throwing, so list_battery_packs can show what is wrong.
    /// </summary>
    public static class BatteryPackLoader
    {
        #region Public-Members

        /// <summary>
        /// File name of a pack manifest.
        /// </summary>
        public const string ManifestFileName = "battery-pack.json";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Json = CreateJsonOptions();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load one page of the packs directly under a battery directory, ordered by pack directory name (ordinal; a
        /// pack's directory is normally named after its identifier). Only the page's manifests are read; TotalRecords
        /// counts every directory that holds a manifest.
        /// </summary>
        /// <param name="batteryDirectory">Battery directory, or null.</param>
        /// <param name="pageNumber">Page number (values below 1 are normalized to 1).</param>
        /// <param name="pageSize">Page size (normalized to 1 to 1000).</param>
        /// <returns>One page of packs; empty when the directory does not exist.</returns>
        public static EnumerationResult<BatteryPack> LoadPage(string? batteryDirectory, int pageNumber, int pageSize)
        {
            int size = pageSize < CollectionLimits.MinPageSize ? CollectionLimits.MinPageSize : (pageSize > CollectionLimits.MaxPageSize ? CollectionLimits.MaxPageSize : pageSize);
            int number = pageNumber < 1 ? 1 : pageNumber;
            List<string> directories = new List<string>();
            if (!String.IsNullOrWhiteSpace(batteryDirectory) && Directory.Exists(batteryDirectory))
            {
                foreach (string directory in Directory.GetDirectories(batteryDirectory))
                {
                    if (File.Exists(Path.Combine(directory, ManifestFileName))) directories.Add(directory);
                }
            }

            directories.Sort((a, b) => String.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));
            List<BatteryPack> page = new List<BatteryPack>();
            foreach (string directory in directories.Skip((number - 1) * size).Take(size))
                page.Add(Load(directory));

            EnumerationResult<BatteryPack> result = new EnumerationResult<BatteryPack>();
            result.PageNumber = number;
            result.PageSize = size;
            result.TotalRecords = directories.Count;
            result.TotalPages = (int)Math.Ceiling((double)directories.Count / size);
            result.Objects = page;
            return result;
        }

        /// <summary>
        /// Load every pack directly under a battery directory, ordered by identifier. For in-process lookups (starting a
        /// run); API reads use <see cref="LoadPage"/>.
        /// </summary>
        /// <param name="batteryDirectory">Battery directory (for example the repository's battery folder).</param>
        /// <returns>The packs; empty when the directory does not exist.</returns>
        public static List<BatteryPack> LoadAll(string? batteryDirectory)
        {
            List<BatteryPack> packs = new List<BatteryPack>();
            if (String.IsNullOrWhiteSpace(batteryDirectory) || !Directory.Exists(batteryDirectory)) return packs;
            foreach (string directory in Directory.GetDirectories(batteryDirectory))
            {
                if (!File.Exists(Path.Combine(directory, ManifestFileName))) continue;
                packs.Add(Load(directory));
            }

            packs.Sort((a, b) => String.CompareOrdinal(a.Id, b.Id));
            return packs;
        }

        /// <summary>
        /// Load and validate one pack directory.
        /// </summary>
        /// <param name="packDirectory">Pack directory.</param>
        /// <returns>The pack, with errors when it is malformed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when packDirectory is null or empty.</exception>
        public static BatteryPack Load(string packDirectory)
        {
            if (String.IsNullOrWhiteSpace(packDirectory)) throw new ArgumentNullException(nameof(packDirectory));
            string directory = Path.GetFullPath(packDirectory);
            string fallbackId = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
                return new BatteryPack(fallbackId, directory, null, null, new List<string> { "The pack has no " + ManifestFileName + "." });

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(manifestPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new BatteryPack(fallbackId, directory, null, null, new List<string> { "The manifest could not be read: " + ex.Message });
            }

            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            BatteryPackManifest? manifest;
            try
            {
                manifest = Parse(bytes);
            }
            catch (JsonException ex)
            {
                return new BatteryPack(fallbackId, directory, null, hash, new List<string> { "The manifest is not valid JSON: " + ex.Message });
            }

            if (manifest == null)
                return new BatteryPack(fallbackId, directory, null, hash, new List<string> { "The manifest is empty." });

            List<string> errors = BatteryPackValidator.Validate(manifest, directory);
            string id = !String.IsNullOrWhiteSpace(manifest.Id) ? manifest.Id!.Trim() : fallbackId;
            return new BatteryPack(id, directory, manifest, hash, errors);
        }

        /// <summary>
        /// Parse manifest bytes (UTF-8, with or without a byte order mark; comments and trailing commas allowed).
        /// </summary>
        /// <param name="bytes">Manifest bytes.</param>
        /// <returns>The manifest, or null for a JSON null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when bytes is null.</exception>
        /// <exception cref="JsonException">Thrown when the bytes are not a valid manifest document.</exception>
        public static BatteryPackManifest? Parse(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            ReadOnlySpan<byte> span = bytes;
            if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF) span = span.Slice(3);
            return JsonSerializer.Deserialize<BatteryPackManifest>(span, _Json);
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            options.ReadCommentHandling = JsonCommentHandling.Skip;
            options.AllowTrailingCommas = true;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
