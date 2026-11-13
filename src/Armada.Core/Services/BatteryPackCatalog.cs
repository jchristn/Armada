namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Finds the battery directory and loads its packs. Packs are read from disk on every call so an edited manifest is
    /// picked up without a restart.
    /// </summary>
    public class BatteryPackCatalog
    {
        #region Public-Members

        /// <summary>
        /// Name of the battery directory searched for when none is configured.
        /// </summary>
        public const string DirectoryName = "battery";

        /// <summary>
        /// Environment variable naming the battery directory when Evaluation.BatteryDirectory is not set. The server
        /// container image sets it to a writable folder under the data directory, which is seeded from the packs bundled
        /// next to the Admiral (see <see cref="BatteryPackSeeder"/>).
        /// </summary>
        public const string DirectoryEnvironmentVariable = "ARMADA_BATTERY_DIR";

        #endregion

        #region Private-Members

        private readonly Func<string?> _ConfiguredDirectory;
        private readonly Func<string?> _EnvironmentDirectory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="configuredDirectory">Reads the configured battery directory (null or empty to search for one).</param>
        /// <param name="environmentDirectory">Reads the battery directory named by the environment; null reads
        /// <see cref="DirectoryEnvironmentVariable"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when configuredDirectory is null.</exception>
        public BatteryPackCatalog(Func<string?> configuredDirectory, Func<string?>? environmentDirectory = null)
        {
            _ConfiguredDirectory = configuredDirectory ?? throw new ArgumentNullException(nameof(configuredDirectory));
            _EnvironmentDirectory = environmentDirectory ?? (() => Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The battery directory in use: the configured one, else the one named by <see cref="DirectoryEnvironmentVariable"/>,
        /// else a "battery" folder next to the application or in one of its parent folders (a source checkout), else null.
        /// </summary>
        /// <returns>The directory, or null when none is found.</returns>
        public string? ResolveDirectory()
        {
            string? explicitDirectory = ResolveExplicitDirectory();
            if (explicitDirectory != null) return explicitDirectory;

            foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                DirectoryInfo? current = String.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
                for (int depth = 0; current != null && depth < 10; depth++, current = current.Parent)
                {
                    string candidate = Path.Combine(current.FullName, DirectoryName);
                    if (HasPacks(candidate)) return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// One page of packs, valid or not, ordered by pack directory name; only the page's manifests are read.
        /// </summary>
        /// <param name="pageNumber">Page number.</param>
        /// <param name="pageSize">Page size (1 to 1000).</param>
        /// <returns>One page.</returns>
        public EnumerationResult<BatteryPack> LoadPage(int pageNumber, int pageSize)
        {
            return BatteryPackLoader.LoadPage(ResolveDirectory(), pageNumber, pageSize);
        }

        /// <summary>
        /// Every pack, valid or not, ordered by identifier.
        /// </summary>
        /// <returns>Packs.</returns>
        public List<BatteryPack> LoadAll()
        {
            return BatteryPackLoader.LoadAll(ResolveDirectory());
        }

        /// <summary>
        /// One pack by identifier.
        /// </summary>
        /// <param name="id">Pack identifier.</param>
        /// <returns>The pack, or null.</returns>
        public BatteryPack? Find(string? id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            return LoadAll().FirstOrDefault(p => String.Equals(p.Id, id.Trim(), StringComparison.Ordinal));
        }

        /// <summary>
        /// The battery directory named by settings or the environment (settings win), or null when neither names one. An
        /// explicit directory is a managed, writable location that is seeded from the bundled packs.
        /// </summary>
        /// <returns>The full path, or null.</returns>
        public string? ResolveExplicitDirectory()
        {
            string? configured = _ConfiguredDirectory();
            if (!String.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            string? fromEnvironment = _EnvironmentDirectory();
            if (!String.IsNullOrWhiteSpace(fromEnvironment)) return Path.GetFullPath(fromEnvironment);
            return null;
        }

        /// <summary>
        /// The packs shipped with the application: a "battery" folder next to it that holds at least one pack, or null.
        /// </summary>
        /// <param name="baseDirectory">Application directory; null uses AppContext.BaseDirectory.</param>
        /// <returns>The full path, or null.</returns>
        public static string? BundledDirectory(string? baseDirectory = null)
        {
            string root = String.IsNullOrEmpty(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
            if (String.IsNullOrEmpty(root)) return null;
            string candidate = Path.GetFullPath(Path.Combine(root, DirectoryName));
            return HasPacks(candidate) ? candidate : null;
        }

        #endregion

        #region Private-Methods

        private static bool HasPacks(string directory)
        {
            if (!Directory.Exists(directory)) return false;
            return Directory.GetDirectories(directory).Any(d => File.Exists(Path.Combine(d, BatteryPackLoader.ManifestFileName)));
        }

        #endregion
    }
}
