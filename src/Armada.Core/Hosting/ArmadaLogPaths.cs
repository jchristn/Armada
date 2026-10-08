namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// The layout of the Admiral's log directory: where each kind of log lives, how a mission's or captain's log is
    /// found (a captain's .current pointer names the log of the session it is running), and listing the files in
    /// each group. Shared by the CLI and Harbor so they agree on the layout.
    /// </summary>
    public class ArmadaLogPaths
    {
        #region Public-Members

        /// <summary>
        /// The log directory (LogDirectory from settings, by default logs/ in the data directory).
        /// </summary>
        public string LogDirectory { get; }

        /// <summary>
        /// Per-mission session logs.
        /// </summary>
        public string MissionsDirectory
        {
            get { return Path.Combine(LogDirectory, "missions"); }
        }

        /// <summary>
        /// Per-captain logs and .current pointers.
        /// </summary>
        public string CaptainsDirectory
        {
            get { return Path.Combine(LogDirectory, "captains"); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a log directory.
        /// </summary>
        /// <param name="logDirectory">The Admiral's log directory.</param>
        public ArmadaLogPaths(string logDirectory)
        {
            if (String.IsNullOrWhiteSpace(logDirectory)) throw new ArgumentNullException(nameof(logDirectory));
            LogDirectory = logDirectory;
        }

        /// <summary>
        /// The layout under the default data directory (ARMADA_DATA_DIR, else ~/.armada), as the CLI uses it.
        /// </summary>
        /// <returns>The paths.</returns>
        public static ArmadaLogPaths ForDefaultDataDirectory()
        {
            return new ArmadaLogPaths(Path.Combine(Constants.DefaultDataDirectory, "logs"));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Directory that holds a category's files.
        /// </summary>
        /// <param name="category">Category.</param>
        /// <returns>Full path.</returns>
        public string DirectoryFor(LogCategoryEnum category)
        {
            switch (category)
            {
                case LogCategoryEnum.Admiral: return LogDirectory;
                case LogCategoryEnum.Missions: return MissionsDirectory;
                case LogCategoryEnum.Captains: return CaptainsDirectory;
                case LogCategoryEnum.Diffs: return Path.Combine(LogDirectory, "diffs");
                case LogCategoryEnum.Instructions: return Path.Combine(LogDirectory, "instructions");
                case LogCategoryEnum.FinalMessages: return Path.Combine(LogDirectory, "final-messages");
                case LogCategoryEnum.Docks: return Path.Combine(LogDirectory, "docks");
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        /// <summary>
        /// A mission's session log path (whether or not it exists).
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <returns>Full path.</returns>
        public string MissionLogPath(string missionId)
        {
            if (String.IsNullOrWhiteSpace(missionId)) throw new ArgumentNullException(nameof(missionId));
            return Path.Combine(MissionsDirectory, missionId.Trim() + ".log");
        }

        /// <summary>
        /// A captain's current log: the file its .current pointer names when that exists, else its .log file.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <returns>Existing file path, or null.</returns>
        public string? ResolveCaptainLog(string captainId)
        {
            if (String.IsNullOrWhiteSpace(captainId)) throw new ArgumentNullException(nameof(captainId));
            string id = captainId.Trim();

            string pointer = Path.Combine(CaptainsDirectory, id + ".current");
            if (File.Exists(pointer))
            {
                try
                {
                    string target = File.ReadAllText(pointer).Trim();
                    if (target.Length > 0 && File.Exists(target)) return target;
                }
                catch (IOException)
                {
                    // Pointer being rewritten: fall through to the captain's own log.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            string log = Path.Combine(CaptainsDirectory, id + ".log");
            return File.Exists(log) ? log : null;
        }

        /// <summary>
        /// A mission's log: its own session log when it exists, else the current log of the captain running it.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="captainId">Captain identifier, or null.</param>
        /// <returns>Existing file path, or null.</returns>
        public string? ResolveMissionLog(string missionId, string? captainId = null)
        {
            string missionLog = MissionLogPath(missionId);
            if (File.Exists(missionLog)) return missionLog;
            if (!String.IsNullOrWhiteSpace(captainId)) return ResolveCaptainLog(captainId!);
            return null;
        }

        /// <summary>
        /// The mission or captain a log file belongs to, when it is that entity's own log: the path this layout gives
        /// the entity (<see cref="MissionLogPath"/>, or captains/&lt;id&gt;.log) is exactly the file.
        /// </summary>
        /// <param name="category">Category the file was listed under.</param>
        /// <param name="file">Full path.</param>
        /// <returns>The entity ID, or null.</returns>
        public string? EntityIdOf(LogCategoryEnum category, string file)
        {
            if (String.IsNullOrEmpty(file)) return null;
            string stem = System.IO.Path.GetFileNameWithoutExtension(file);
            if (category == LogCategoryEnum.Missions && stem.StartsWith(Constants.MissionIdPrefix, StringComparison.Ordinal)
                && String.Equals(MissionLogPath(stem), file, StringComparison.Ordinal))
                return stem;
            if (category == LogCategoryEnum.Captains && stem.StartsWith(Constants.CaptainIdPrefix, StringComparison.Ordinal)
                && String.Equals(System.IO.Path.Combine(CaptainsDirectory, stem + ".log"), file, StringComparison.Ordinal))
                return stem;
            return null;
        }

        /// <summary>
        /// List a category's files, newest first. Captain .current pointers are not logs and are left out.
        /// </summary>
        /// <param name="category">Category.</param>
        /// <param name="nameFilter">Case-insensitive substring the file name must contain, or null for all.</param>
        /// <param name="maxResults">Maximum number of files to return.</param>
        /// <returns>The files.</returns>
        public List<LogFileEntry> List(LogCategoryEnum category, string? nameFilter = null, int maxResults = 500)
        {
            List<LogFileEntry> entries = new List<LogFileEntry>();
            string directory = DirectoryFor(category);
            if (!Directory.Exists(directory)) return entries;

            string? filter = String.IsNullOrWhiteSpace(nameFilter) ? null : nameFilter!.Trim();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string name = System.IO.Path.GetFileName(file);
                if (category == LogCategoryEnum.Admiral && !IsAdmiralLogName(name)) continue;
                if (category == LogCategoryEnum.Captains && name.EndsWith(".current", StringComparison.OrdinalIgnoreCase)) continue;
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                try
                {
                    FileInfo info = new FileInfo(file);
                    entries.Add(new LogFileEntry
                    {
                        Category = category,
                        Path = file,
                        Name = name,
                        SizeBytes = info.Length,
                        LastWriteUtc = info.LastWriteTimeUtc,
                        EntityId = EntityIdOf(category, file)
                    });
                }
                catch (IOException)
                {
                    // Deleted between listing and stat.
                }
            }

            entries.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
            if (maxResults > 0 && entries.Count > maxResults) entries.RemoveRange(maxResults, entries.Count - maxResults);
            return entries;
        }

        #endregion

        #region Private-Methods

        private static bool IsAdmiralLogName(string name)
        {
            return String.Equals(name, LocalAdmiralInfo.AdmiralLogBaseName, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(LocalAdmiralInfo.AdmiralLogBaseName + ".", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
