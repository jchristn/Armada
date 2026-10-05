namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Armada.Client;

    /// <summary>
    /// Persists All Activity saved views in <c>tui-activity-views.json</c> next to the TUI preferences file (the
    /// dashboard keeps them in browser local storage). Newest first. Read and write failures are ignored, like the
    /// dashboard. Not thread-safe.
    /// </summary>
    public class ActivitySavedViewStore
    {
        #region Public-Members

        /// <summary>
        /// File path.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Saved views, newest first. Never null.
        /// </summary>
        public IReadOnlyList<ActivitySavedView> Views
        {
            get { return _Views; }
        }

        #endregion

        #region Private-Members

        private List<ActivitySavedView> _Views = new List<ActivitySavedView>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load.
        /// </summary>
        /// <param name="filePath">File path.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null or empty.</exception>
        public ActivitySavedViewStore(string filePath)
        {
            if (String.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            FilePath = filePath;
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Path of the saved views file for a preferences file.
        /// </summary>
        /// <param name="preferencesPath">Preferences file path.</param>
        /// <returns>Path.</returns>
        public static string PathFor(string preferencesPath)
        {
            return Path.Combine(Path.GetDirectoryName(preferencesPath) ?? ".", "tui-activity-views.json");
        }

        /// <summary>
        /// Reload from disk.
        /// </summary>
        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    _Views = new List<ActivitySavedView>();
                    return;
                }

                List<ActivitySavedView>? views = ArmadaJson.Deserialize<List<ActivitySavedView>>(File.ReadAllText(FilePath));
                _Views = views ?? new List<ActivitySavedView>();
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                _Views = new List<ActivitySavedView>();
            }
        }

        /// <summary>
        /// Add a view at the front and save.
        /// </summary>
        /// <param name="view">View.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="view"/> is null.</exception>
        public void Add(ActivitySavedView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            _Views.Insert(0, view);
            Save();
        }

        /// <summary>
        /// Remove a view by id and save.
        /// </summary>
        /// <param name="id">View id.</param>
        /// <returns>True when removed.</returns>
        public bool Remove(string id)
        {
            int removed = _Views.RemoveAll(v => v.Id == id);
            if (removed > 0) Save();
            return removed > 0;
        }

        #endregion

        #region Private-Methods

        private void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);
                File.WriteAllText(FilePath, ArmadaJson.Serialize(_Views.ToList()));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Ignored like the dashboard's storage failures.
            }
        }

        #endregion
    }
}
