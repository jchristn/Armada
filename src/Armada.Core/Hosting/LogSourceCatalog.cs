namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Which logs Harbor can show, and their files. Harbor's own log and the logs of jobs run on Harbor's machine are
    /// always offered; the Admiral's log groups are offered only when the linked Admiral runs on the same machine (its
    /// files are not reachable otherwise).
    /// </summary>
    public static class LogSourceCatalog
    {
        #region Public-Methods

        /// <summary>
        /// The log sources to offer, in display order.
        /// </summary>
        /// <param name="harbor">Harbor's log layout.</param>
        /// <param name="admiral">Where the linked Admiral's files are, or null while unknown.</param>
        /// <returns>Sources.</returns>
        public static List<LogSource> Discover(HarborLogPaths harbor, LocalAdmiralInfo? admiral)
        {
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));

            List<LogSource> sources = new List<LogSource>();
            sources.Add(new LogSource { Kind = LogSourceEnum.Harbor, Label = "Harbor", Directory = harbor.LogDirectory });
            sources.Add(new LogSource { Kind = LogSourceEnum.HarborJobs, Label = "Jobs on this machine", Directory = harbor.JobsDirectory });

            if (admiral != null && admiral.IsLocal)
            {
                ArmadaLogPaths logs = admiral.Logs;
                foreach (LogCategoryEnum category in Enum.GetValues(typeof(LogCategoryEnum)))
                {
                    sources.Add(new LogSource
                    {
                        Kind = LogSourceEnum.Admiral,
                        AdmiralCategory = category,
                        Label = "Admiral: " + CategoryLabel(category),
                        Directory = logs.DirectoryFor(category)
                    });
                }
            }

            return sources;
        }

        /// <summary>
        /// List a source's files, newest first.
        /// </summary>
        /// <param name="source">Source from <see cref="Discover"/>.</param>
        /// <param name="harbor">Harbor's log layout.</param>
        /// <param name="admiral">Where the linked Admiral's files are, or null.</param>
        /// <param name="nameFilter">Case-insensitive substring the file name must contain, or null for all.</param>
        /// <param name="maxResults">Maximum number of files to return.</param>
        /// <returns>The files; empty for an Admiral source when the Admiral is not local.</returns>
        public static List<LogFileEntry> List(LogSource source, HarborLogPaths harbor, LocalAdmiralInfo? admiral, string? nameFilter = null, int maxResults = 500)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));

            switch (source.Kind)
            {
                case LogSourceEnum.Harbor:
                    return harbor.ListHarborLogs(nameFilter, maxResults);
                case LogSourceEnum.HarborJobs:
                    return harbor.ListJobLogs(nameFilter, maxResults);
                default:
                    if (admiral == null || !admiral.IsLocal || !source.AdmiralCategory.HasValue) return new List<LogFileEntry>();
                    return admiral.Logs.List(source.AdmiralCategory.Value, nameFilter, maxResults);
            }
        }

        /// <summary>
        /// Find a mission's (msn_) or captain's (cpt_) log: the Admiral's when it is local (it holds the whole
        /// transcript), else the job log on this machine. Captain logs are the Admiral's only.
        /// </summary>
        /// <param name="id">Mission or captain identifier.</param>
        /// <param name="harbor">Harbor's log layout.</param>
        /// <param name="admiral">Where the linked Admiral's files are, or null.</param>
        /// <param name="captainId">For a mission, the captain running it (to follow its current log), or null.</param>
        /// <returns>Existing file path, or null.</returns>
        public static string? ResolveLog(string id, HarborLogPaths harbor, LocalAdmiralInfo? admiral, string? captainId = null)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));

            string trimmed = id.Trim();
            bool local = admiral != null && admiral.IsLocal;
            if (trimmed.StartsWith(Constants.CaptainIdPrefix, StringComparison.OrdinalIgnoreCase))
                return local ? admiral!.Logs.ResolveCaptainLog(trimmed) : null;

            if (local)
            {
                string? admiralLog = admiral!.Logs.ResolveMissionLog(trimmed, captainId);
                if (admiralLog != null) return admiralLog;
            }

            return harbor.ResolveMissionLog(trimmed);
        }

        /// <summary>
        /// Plain name of an Admiral log group.
        /// </summary>
        /// <param name="category">Group.</param>
        /// <returns>Name.</returns>
        public static string CategoryLabel(LogCategoryEnum category)
        {
            switch (category)
            {
                case LogCategoryEnum.Admiral: return "Server log";
                case LogCategoryEnum.Missions: return "Missions";
                case LogCategoryEnum.Captains: return "Captains";
                case LogCategoryEnum.Diffs: return "Diffs";
                case LogCategoryEnum.Instructions: return "Instructions";
                case LogCategoryEnum.FinalMessages: return "Final messages";
                case LogCategoryEnum.Docks: return "Docks";
                default: return category.ToString();
            }
        }

        #endregion
    }
}
