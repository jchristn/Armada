namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// A loaded battery pack: its directory, its manifest (null when it could not be read) and its validation errors.
    /// </summary>
    public class BatteryPack
    {
        #region Public-Members

        /// <summary>
        /// Pack identifier: the manifest's id, or the directory name when the manifest has none.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Absolute pack directory.
        /// </summary>
        public string Directory { get; }

        /// <summary>
        /// The manifest, or null when it is missing or not valid JSON.
        /// </summary>
        public BatteryPackManifest? Manifest { get; }

        /// <summary>
        /// SHA-256 of the manifest bytes (hex), or null when it could not be read.
        /// </summary>
        public string? ManifestHash { get; }

        /// <summary>
        /// Validation errors; empty when the pack is valid.
        /// </summary>
        public List<string> Errors { get; }

        /// <summary>
        /// Whether the pack can run.
        /// </summary>
        public bool IsValid => Manifest != null && Errors.Count == 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Pack identifier.</param>
        /// <param name="directory">Absolute pack directory.</param>
        /// <param name="manifest">Manifest, or null.</param>
        /// <param name="manifestHash">Manifest hash, or null.</param>
        /// <param name="errors">Validation errors.</param>
        /// <exception cref="ArgumentNullException">Thrown when id or directory is null.</exception>
        public BatteryPack(string id, string directory, BatteryPackManifest? manifest, string? manifestHash, List<string>? errors)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            Manifest = manifest;
            ManifestHash = manifestHash;
            Errors = errors ?? new List<string>();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The pack as list_battery_packs shows it.
        /// </summary>
        /// <returns>Pack info.</returns>
        public BatteryPackInfo ToInfo()
        {
            BatteryPackInfo info = new BatteryPackInfo
            {
                Id = Id,
                Name = Manifest?.Name,
                Version = Manifest?.Version ?? 0,
                SchemaVersion = Manifest?.SchemaVersion ?? 0,
                Domain = Manifest?.Domain,
                Description = Manifest?.Description,
                Valid = IsValid,
                Errors = new List<string>(Errors),
                ManifestHash = ManifestHash
            };
            if (Manifest == null) return info;

            // A manifest over the limits is invalid (its Errors say so); the info still lists at most MaxConfigItems of
            // each and carries the true counts, so nothing is dropped silently.
            List<string> toolchain = Manifest.Toolchain.Where(t => t != null && !String.IsNullOrWhiteSpace(t.Name)).Select(t => t.Name!).ToList();
            info.ToolchainCount = toolchain.Count;
            info.Toolchain = toolchain.Take(CollectionLimits.MaxConfigItems).ToList();
            info.TaskCount = Manifest.Tasks.Count(t => t != null);
            foreach (BatteryTaskDefinition task in Manifest.Tasks.Where(t => t != null).Take(CollectionLimits.MaxConfigItems))
            {
                if (task == null) continue;
                info.Tasks.Add(new BatteryTaskInfo
                {
                    Id = task.Id ?? String.Empty,
                    Title = task.Title,
                    Persona = task.Persona,
                    Grader = task.Grader?.Type,
                    Toolchain = ToolchainFor(task)
                });
            }

            return info;
        }

        /// <summary>
        /// Names of the toolchain entries a task needs: its own list, or every entry of the pack when it has none.
        /// </summary>
        /// <param name="task">Task.</param>
        /// <returns>Toolchain names.</returns>
        /// <exception cref="ArgumentNullException">Thrown when task is null.</exception>
        public List<string> ToolchainFor(BatteryTaskDefinition task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (task.Toolchain != null) return task.Toolchain.Where(n => !String.IsNullOrWhiteSpace(n)).Take(CollectionLimits.MaxConfigItems).ToList();
            if (Manifest == null) return new List<string>();
            return Manifest.Toolchain.Where(t => t != null && !String.IsNullOrWhiteSpace(t.Name)).Select(t => t.Name!).Take(CollectionLimits.MaxConfigItems).ToList();
        }

        /// <summary>
        /// Absolute path of a file or directory named relative to the pack directory.
        /// </summary>
        /// <param name="relativePath">Relative path.</param>
        /// <returns>Absolute path.</returns>
        /// <exception cref="ArgumentException">Thrown when the path is empty, rooted, or leaves the pack directory.</exception>
        public string Resolve(string? relativePath)
        {
            string? resolved = BatteryPackValidator.ResolveInside(Directory, relativePath);
            if (resolved == null) throw new ArgumentException("Path '" + relativePath + "' is not inside pack " + Id + ".", nameof(relativePath));
            return resolved;
        }

        #endregion
    }
}
