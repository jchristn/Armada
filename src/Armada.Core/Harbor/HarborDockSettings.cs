namespace Armada.Core.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Where a Harbor finds vessel repositories on its machine and where it creates mission docks. Each Harbor machine
    /// keeps code in different places, so this lives on the Harbor, not on the Admiral.
    /// </summary>
    public class HarborDockSettings
    {
        #region Public-Members

        /// <summary>
        /// Directory the Harbor creates mission docks (git worktrees) in, as &lt;vessel&gt;/&lt;mission&gt;. Never inside a
        /// checkout the Harbor serves.
        /// </summary>
        public string DocksDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Directory the Harbor keeps its own bare clones in (&lt;vessel&gt;.git), for vessels with no checkout on this
        /// machine.
        /// </summary>
        public string ReposDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Checkouts named per vessel. A mapping wins over discovery.
        /// </summary>
        public List<HarborRepositoryMapping> Repositories { get; set; } = new List<HarborRepositoryMapping>();

        /// <summary>
        /// Folders searched for checkouts whose remote matches a vessel's repository URL.
        /// </summary>
        public List<string> RepositoryRoots { get; set; } = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check the settings for values a Harbor cannot serve docks with: relative or missing paths, a mapping without a
        /// vessel or with a path that is not a git checkout, duplicate vessels, and a docks directory inside a checkout.
        /// </summary>
        /// <returns>One message per problem; empty when valid.</returns>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();
            if (String.IsNullOrWhiteSpace(DocksDirectory) || !Path.IsPathFullyQualified(DocksDirectory))
                errors.Add("Docks directory must be an absolute path.");
            if (String.IsNullOrWhiteSpace(ReposDirectory) || !Path.IsPathFullyQualified(ReposDirectory))
                errors.Add("Repositories directory (for the Harbor's own clones) must be an absolute path.");

            HashSet<string> vessels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> checkouts = new List<string>();
            foreach (HarborRepositoryMapping mapping in Repositories ?? new List<HarborRepositoryMapping>())
            {
                string vessel = (mapping?.Vessel ?? String.Empty).Trim();
                string path = (mapping?.Path ?? String.Empty).Trim();
                if (vessel.Length == 0)
                {
                    errors.Add("Repository " + (path.Length > 0 ? path : "(no path)") + " needs a vessel name or ID.");
                    continue;
                }

                if (!vessels.Add(vessel)) errors.Add("Vessel " + vessel + " is mapped more than once.");
                if (path.Length == 0 || !Path.IsPathFullyQualified(path))
                {
                    errors.Add("The path for vessel " + vessel + " must be an absolute path.");
                    continue;
                }

                if (!Directory.Exists(path)) errors.Add("The path for vessel " + vessel + " (" + path + ") does not exist.");
                else if (!Directory.Exists(System.IO.Path.Combine(path, ".git"))) errors.Add("The path for vessel " + vessel + " (" + path + ") is not a git checkout (it has no .git folder).");
                else checkouts.Add(path);
            }

            foreach (string root in RepositoryRoots ?? new List<string>())
            {
                string trimmed = (root ?? String.Empty).Trim();
                if (trimmed.Length == 0 || !Path.IsPathFullyQualified(trimmed)) errors.Add("Root folder " + (trimmed.Length > 0 ? trimmed : "(empty)") + " must be an absolute path.");
                else if (!Directory.Exists(trimmed)) errors.Add("Root folder " + trimmed + " does not exist.");
            }

            if (!String.IsNullOrWhiteSpace(DocksDirectory) && Path.IsPathFullyQualified(DocksDirectory))
            {
                foreach (string checkout in checkouts)
                {
                    if (IsSameOrUnder(DocksDirectory, checkout))
                        errors.Add("The docks directory must not be inside the checkout " + checkout + ".");
                }
            }

            return errors;
        }

        /// <summary>
        /// A deep copy.
        /// </summary>
        /// <returns>The copy.</returns>
        public HarborDockSettings Clone()
        {
            HarborDockSettings copy = new HarborDockSettings();
            copy.DocksDirectory = DocksDirectory;
            copy.ReposDirectory = ReposDirectory;
            foreach (HarborRepositoryMapping mapping in Repositories ?? new List<HarborRepositoryMapping>())
                if (mapping != null) copy.Repositories.Add(new HarborRepositoryMapping(mapping.Vessel, mapping.Path));
            copy.RepositoryRoots = new List<string>(RepositoryRoots ?? new List<string>());
            return copy;
        }

        /// <summary>
        /// Whether a path is a directory or inside it (full paths, compared without regard to case on Windows and macOS).
        /// </summary>
        /// <param name="path">Path to test.</param>
        /// <param name="directory">Directory.</param>
        /// <returns>True when the path is the directory or under it.</returns>
        public static bool IsSameOrUnder(string path, string directory)
        {
            if (String.IsNullOrWhiteSpace(path) || String.IsNullOrWhiteSpace(directory)) return false;
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            StringComparison comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (String.Equals(full, root, comparison)) return true;
            return full.StartsWith(root + Path.DirectorySeparatorChar, comparison)
                || full.StartsWith(root + Path.AltDirectorySeparatorChar, comparison);
        }

        #endregion
    }
}
