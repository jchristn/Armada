namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// One landing mode with the dashboard's long label, short label, and explanation (shared by the vessel form, the
    /// landing mode filter, and the grid so the wording stays consistent).
    /// </summary>
    public class LandingModeInfo
    {
        #region Public-Members

        /// <summary>
        /// The landing modes in the dashboard's order; the first (empty value) is the global default.
        /// </summary>
        public static readonly IReadOnlyList<LandingModeInfo> All = new List<LandingModeInfo>
        {
            new LandingModeInfo("", "Default (use global setting)", "global default", "Uses the global default landing mode configured for the Admiral."),
            new LandingModeInfo("LocalMerge", "Local Merge -- into your working directory, no push", "local, no push", "Merges the mission branch into the default branch in your local working directory. Nothing is pushed. Requires the vessel to have a working directory and local path configured."),
            new LandingModeInfo("MergeAndPush", "Merge and Push -- local merge, then push to the remote", "local + push", "Merges the mission branch into the default branch in your local working directory, then pushes it to the working directory's remote. Requires the vessel to have a working directory and local path configured, and the working directory needs a remote."),
            new LandingModeInfo("PullRequest", "Pull Request -- push and open a PR", "opens a PR", "Pushes the mission branch and opens a pull request on the remote. The mission stays open until the PR is merged."),
            new LandingModeInfo("MergeQueue", "Merge Queue -- validated sequential merge", "merge queue", "Enqueues the mission branch for a validated merge. The merge queue runs tests and merges branches one at a time per vessel."),
            new LandingModeInfo("None", "None -- manual integration", "manual only", "No automatic landing. Work stays as WorkProduced and the branch is kept in the repository for you to integrate manually."),
        };

        /// <summary>
        /// Enum value name, or empty for the global default.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// English option label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// English short label.
        /// </summary>
        public string Short { get; }

        /// <summary>
        /// English explanation.
        /// </summary>
        public string Description { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="label">Label.</param>
        /// <param name="shortLabel">Short label.</param>
        /// <param name="description">Explanation.</param>
        public LandingModeInfo(string value, string label, string shortLabel, string description)
        {
            Value = value ?? "";
            Label = label ?? "";
            Short = shortLabel ?? "";
            Description = description ?? "";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The entry for a mode (the global default for null or unknown values).
        /// </summary>
        /// <param name="mode">Mode name, or null.</param>
        /// <returns>Entry.</returns>
        public static LandingModeInfo For(string? mode)
        {
            string value = mode ?? "";
            return All.FirstOrDefault(m => String.Equals(m.Value, value, StringComparison.Ordinal)) ?? All[0];
        }

        #endregion
    }
}
