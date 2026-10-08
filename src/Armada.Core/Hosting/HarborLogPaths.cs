namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using Armada.Core.Harbor;

    /// <summary>
    /// The layout of a Harbor's own log directory (~/.armada-harbor/logs): Harbor's daily log (harbor.log.yyyyMMdd) and
    /// one log per job run on this machine under jobs/ (a mission's runs append to msn_*.log; other jobs, such as Ask
    /// turns, get a file each). These logs are on the Harbor's machine, so they are available whether or not the Admiral
    /// is.
    /// </summary>
    public class HarborLogPaths
    {
        #region Public-Members

        /// <summary>
        /// Base name of Harbor's own log file (daily files append a date).
        /// </summary>
        public const string HarborLogBaseName = "harbor.log";

        /// <summary>
        /// Job logs kept by default; older ones are deleted when a job starts.
        /// </summary>
        public const int DefaultJobLogsKept = 200;

        /// <summary>
        /// Harbor's log directory.
        /// </summary>
        public string LogDirectory { get; }

        /// <summary>
        /// Directory holding the logs of jobs run on this machine.
        /// </summary>
        public string JobsDirectory
        {
            get { return Path.Combine(LogDirectory, "jobs"); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a log directory.
        /// </summary>
        /// <param name="logDirectory">Harbor's log directory.</param>
        public HarborLogPaths(string logDirectory)
        {
            if (String.IsNullOrWhiteSpace(logDirectory)) throw new ArgumentNullException(nameof(logDirectory));
            LogDirectory = logDirectory;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The log file for a job: jobs/msn_*.log for a mission (each run appends), otherwise a file of its own named
        /// for what the job is, its captain (or job id), and when it started.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <returns>Full path.</returns>
        public string JobLogPath(HarborJobInfo job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (job.Kind == HarborJobKindEnum.Mission && !String.IsNullOrWhiteSpace(job.MissionId))
                return MissionLogPath(job.MissionId!);

            string who = !String.IsNullOrWhiteSpace(job.CaptainId) ? job.CaptainId! : ShortJobId(job.JobId);
            string name = KindSlug(job.Kind) + "-" + SafeName(who) + "-"
                + job.StartedUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".log";
            return Path.Combine(JobsDirectory, name);
        }

        /// <summary>
        /// A mission's log on this Harbor (whether or not it exists).
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <returns>Full path.</returns>
        public string MissionLogPath(string missionId)
        {
            if (String.IsNullOrWhiteSpace(missionId)) throw new ArgumentNullException(nameof(missionId));
            return Path.Combine(JobsDirectory, SafeName(missionId.Trim()) + ".log");
        }

        /// <summary>
        /// A mission's log on this Harbor when it exists.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <returns>Existing file path, or null.</returns>
        public string? ResolveMissionLog(string missionId)
        {
            string path = MissionLogPath(missionId);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Harbor's own log files (harbor.log and its daily files), newest first.
        /// </summary>
        /// <param name="nameFilter">Case-insensitive substring the file name must contain, or null for all.</param>
        /// <param name="maxResults">Maximum number of files to return.</param>
        /// <returns>The files.</returns>
        public List<LogFileEntry> ListHarborLogs(string? nameFilter = null, int maxResults = 500)
        {
            return ListFiles(LogDirectory, true, nameFilter, maxResults);
        }

        /// <summary>
        /// The logs of jobs run on this machine, newest first.
        /// </summary>
        /// <param name="nameFilter">Case-insensitive substring the file name must contain, or null for all.</param>
        /// <param name="maxResults">Maximum number of files to return.</param>
        /// <returns>The files.</returns>
        public List<LogFileEntry> ListJobLogs(string? nameFilter = null, int maxResults = 500)
        {
            return ListFiles(JobsDirectory, false, nameFilter, maxResults);
        }

        /// <summary>
        /// The newest Harbor log file, or null when there is none yet.
        /// </summary>
        /// <returns>Path, or null.</returns>
        public string? FindLatestHarborLog()
        {
            return LocalAdmiralInfo.FindLatestLog(LogDirectory, HarborLogBaseName);
        }

        /// <summary>
        /// Delete all but the newest <paramref name="keep"/> job logs. Files that cannot be deleted are left.
        /// </summary>
        /// <param name="keep">Number of job logs to keep.</param>
        /// <returns>Number of files deleted.</returns>
        public int PruneJobLogs(int keep = DefaultJobLogsKept)
        {
            if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep));
            List<LogFileEntry> logs = ListFiles(JobsDirectory, false, null, 0);
            int deleted = 0;
            for (int i = keep; i < logs.Count; i++)
            {
                try
                {
                    File.Delete(logs[i].Path);
                    deleted++;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return deleted;
        }

        #endregion

        #region Private-Methods

        private static List<LogFileEntry> ListFiles(string directory, bool harborLogsOnly, string? nameFilter, int maxResults)
        {
            List<LogFileEntry> entries = new List<LogFileEntry>();
            if (!Directory.Exists(directory)) return entries;

            string? filter = String.IsNullOrWhiteSpace(nameFilter) ? null : nameFilter!.Trim();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(file);
                if (harborLogsOnly && !IsHarborLogName(name)) continue;
                if (!harborLogsOnly && !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                try
                {
                    FileInfo info = new FileInfo(file);
                    entries.Add(new LogFileEntry
                    {
                        Path = file,
                        Name = name,
                        SizeBytes = info.Length,
                        LastWriteUtc = info.LastWriteTimeUtc
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

        private static bool IsHarborLogName(string name)
        {
            return String.Equals(name, HarborLogBaseName, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(HarborLogBaseName + ".", StringComparison.OrdinalIgnoreCase);
        }

        private static string KindSlug(HarborJobKindEnum kind)
        {
            switch (kind)
            {
                case HarborJobKindEnum.Mission: return "mission";
                case HarborJobKindEnum.AskTurn: return "ask";
                case HarborJobKindEnum.Planning: return "planning";
                case HarborJobKindEnum.Refinement: return "refinement";
                case HarborJobKindEnum.ContextBuild: return "context";
                default: return "job";
            }
        }

        private static string ShortJobId(string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return "job";
            string trimmed = jobId.Trim();
            return trimmed.Length <= 8 ? trimmed : trimmed.Substring(0, 8);
        }

        private static string SafeName(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
                builder.Append(safe ? c : '_');
            }

            string result = builder.ToString().Trim('.');
            return result.Length == 0 ? "job" : result;
        }

        #endregion
    }
}
