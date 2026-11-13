namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Copies the battery packs bundled with the application into a managed (writable, persistent) battery directory.
    /// Safe to run on every start: a pack already in the target is never touched, so edited and custom packs survive
    /// upgrades, and a bundled pack an operator deleted is remembered in a marker file and not recreated. A pack added to
    /// the bundle by a later release is seeded on the first start after the upgrade. A newer bundled version of a pack
    /// already in the target is not merged; delete the target pack (and its marker line) to take the new one.
    /// </summary>
    public static class BatteryPackSeeder
    {
        #region Public-Members

        /// <summary>
        /// File in the target directory listing every pack id seeded so far, one per line.
        /// </summary>
        public const string MarkerFileName = ".armada-seeded-packs";

        #endregion

        #region Private-Members

        private const string StagingPrefix = ".seeding-";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed the target directory with the bundled packs it does not have yet.
        /// </summary>
        /// <param name="bundledDirectory">Directory holding the bundled packs; null or missing skips the seed.</param>
        /// <param name="targetDirectory">Managed battery directory; null or the bundled directory itself skips the seed.</param>
        /// <returns>What was added, preserved, retired or failed. Errors are reported in the result, never thrown.</returns>
        public static BatteryPackSeedResult Seed(string? bundledDirectory, string? targetDirectory)
        {
            BatteryPackSeedResult result = new BatteryPackSeedResult
            {
                BundledDirectory = String.IsNullOrWhiteSpace(bundledDirectory) ? null : Path.GetFullPath(bundledDirectory),
                TargetDirectory = String.IsNullOrWhiteSpace(targetDirectory) ? null : Path.GetFullPath(targetDirectory)
            };
            if (result.TargetDirectory == null) { result.SkippedReason = "No battery directory is configured."; return result; }
            if (result.BundledDirectory == null || !Directory.Exists(result.BundledDirectory)) { result.SkippedReason = "The application has no bundled battery packs."; return result; }
            if (SamePath(result.BundledDirectory, result.TargetDirectory)) { result.SkippedReason = "The battery directory is the bundled one."; return result; }

            try
            {
                Directory.CreateDirectory(result.TargetDirectory);
                RemoveStaleStaging(result.TargetDirectory);
            }
            catch (Exception ex)
            {
                result.SkippedReason = "Cannot prepare " + result.TargetDirectory + ": " + ex.Message;
                return result;
            }

            string markerPath = Path.Combine(result.TargetDirectory, MarkerFileName);
            HashSet<string> seeded = ReadMarker(markerPath);
            bool markerChanged = false;

            foreach (string source in Directory.GetDirectories(result.BundledDirectory).OrderBy(d => d, StringComparer.Ordinal))
            {
                string id = Path.GetFileName(source);
                if (id.StartsWith(".", StringComparison.Ordinal)) continue;
                if (!File.Exists(Path.Combine(source, BatteryPackLoader.ManifestFileName))) continue;

                string destination = Path.Combine(result.TargetDirectory, id);
                if (Directory.Exists(destination) || File.Exists(destination))
                {
                    result.Preserved.Add(id);
                    if (seeded.Add(id)) markerChanged = true;
                    continue;
                }

                if (seeded.Contains(id))
                {
                    result.Retired.Add(id);
                    continue;
                }

                string staging = Path.Combine(result.TargetDirectory, StagingPrefix + id + "-" + Guid.NewGuid().ToString("N"));
                try
                {
                    CopyDirectory(source, staging);
                    Directory.Move(staging, destination);
                    result.Added.Add(id);
                    seeded.Add(id);
                    markerChanged = true;
                }
                catch (Exception ex)
                {
                    result.Failed.Add(id + ": " + ex.Message);
                    TryDelete(staging);
                }
            }

            if (markerChanged)
            {
                try
                {
                    File.WriteAllText(markerPath, String.Join("\n", seeded.OrderBy(s => s, StringComparer.Ordinal)) + "\n", new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    result.Failed.Add(MarkerFileName + ": " + ex.Message);
                }
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static bool SamePath(string a, string b)
        {
            string left = Path.TrimEndingDirectorySeparator(a);
            string right = Path.TrimEndingDirectorySeparator(b);
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return String.Equals(left, right, comparison);
        }

        private static HashSet<string> ReadMarker(string markerPath)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(markerPath)) return ids;
            foreach (string line in File.ReadAllLines(markerPath))
            {
                string id = line.Trim();
                if (id.Length > 0) ids.Add(id);
            }

            return ids;
        }

        private static void RemoveStaleStaging(string targetDirectory)
        {
            foreach (string directory in Directory.GetDirectories(targetDirectory, StagingPrefix + "*"))
                TryDelete(directory);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(directory);
                if (name == "bin" || name == "obj") continue;
                CopyDirectory(directory, Path.Combine(destination, name));
            }
        }

        private static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}
