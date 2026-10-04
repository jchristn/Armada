namespace Armada.Tui.Services
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Loads and saves <see cref="TuiPreferences"/> as indented JSON. Writes are atomic (temporary file then move). A
    /// corrupt file is moved aside to <c>.bak</c> and defaults are used. Thread-safe.
    /// </summary>
    public class PreferencesService
    {
        #region Public-Members

        /// <summary>
        /// Preferences file path.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Current preferences. Never null. Mutate on the UI loop thread, then call <see cref="Save"/>.
        /// </summary>
        public TuiPreferences Current { get; private set; } = new TuiPreferences();

        /// <summary>
        /// Last load or save error, or null.
        /// </summary>
        public string? LastError { get; private set; } = null;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Json = CreateOptions();
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a file path.
        /// </summary>
        /// <param name="filePath">Preferences file path; null uses <see cref="TuiPaths.PreferencesFile"/>.</param>
        public PreferencesService(string? filePath = null)
        {
            FilePath = String.IsNullOrWhiteSpace(filePath) ? TuiPaths.PreferencesFile() : Path.GetFullPath(filePath!);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load from disk. A missing file yields defaults; a corrupt file is renamed to <c>.bak</c>.
        /// </summary>
        /// <returns>The loaded preferences.</returns>
        public TuiPreferences Load()
        {
            lock (_Lock)
            {
                LastError = null;
                if (!File.Exists(FilePath))
                {
                    Current = new TuiPreferences();
                    return Current;
                }

                try
                {
                    string json = File.ReadAllText(FilePath);
                    TuiPreferences? loaded = JsonSerializer.Deserialize<TuiPreferences>(json, _Json);
                    Current = Normalize(loaded ?? new TuiPreferences());
                }
                catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    LastError = ex.Message;
                    try { File.Copy(FilePath, FilePath + ".bak", true); } catch (Exception) { }
                    Current = new TuiPreferences();
                }

                return Current;
            }
        }

        /// <summary>
        /// Save atomically. Errors are recorded in <see cref="LastError"/> and returned as false (preferences are a
        /// convenience; a read-only home must not crash the TUI).
        /// </summary>
        /// <returns>True when saved.</returns>
        public bool Save()
        {
            lock (_Lock)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(FilePath);
                    if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(Current, _Json));
                    File.Move(tmp, FilePath, true);
                    LastError = null;
                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    LastError = ex.Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Find a profile by name (case-insensitive).
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>The profile, or null.</returns>
        public ServerProfile? FindProfile(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            return Current.Profiles.FirstOrDefault(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Add or update a profile by name and make it active (does not save).
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="url">Base URL.</param>
        /// <returns>The profile.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public ServerProfile UpsertProfile(string name, string url)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            ServerProfile? profile = FindProfile(name);
            if (profile == null)
            {
                profile = new ServerProfile();
                profile.Name = name.Trim();
                Current.Profiles.Add(profile);
            }

            profile.Url = url.Trim().TrimEnd('/');
            Current.ActiveProfile = profile.Name;
            return profile;
        }

        /// <summary>
        /// Get (creating if needed) the table preferences for a grid.
        /// </summary>
        /// <param name="tableId">Table id.</param>
        /// <returns>Preferences.</returns>
        public TablePreferences Table(string tableId)
        {
            if (!Current.Tables.TryGetValue(tableId, out TablePreferences? prefs) || prefs == null)
            {
                prefs = new TablePreferences();
                Current.Tables[tableId] = prefs;
            }

            return prefs;
        }

        #endregion

        #region Private-Methods

        private static TuiPreferences Normalize(TuiPreferences prefs)
        {
            if (prefs.Profiles == null) prefs.Profiles = new System.Collections.Generic.List<ServerProfile>();
            if (prefs.CollapsedSections == null) prefs.CollapsedSections = new System.Collections.Generic.Dictionary<string, bool>();
            if (prefs.Tables == null) prefs.Tables = new System.Collections.Generic.Dictionary<string, TablePreferences>();
            if (prefs.RefreshIntervals == null) prefs.RefreshIntervals = new System.Collections.Generic.Dictionary<string, int>();
            if (prefs.KeyBindings == null) prefs.KeyBindings = new System.Collections.Generic.Dictionary<string, string>();
            return prefs;
        }

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = true;
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
