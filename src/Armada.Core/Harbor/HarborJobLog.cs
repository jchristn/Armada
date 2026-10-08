namespace Armada.Core.Harbor
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using Armada.Core.Hosting;

    /// <summary>
    /// Writes one job's output to its log on the Harbor's machine (see <see cref="HarborLogPaths.JobLogPath"/>): a
    /// header naming the job, every output line (stderr lines marked), and the exit code. Writing is best effort: a log
    /// that cannot be written never affects the job.
    /// </summary>
    public sealed class HarborJobLog : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The log file.
        /// </summary>
        public string FilePath { get; }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private StreamWriter? _Writer;

        #endregion

        #region Constructors-and-Factories

        private HarborJobLog(string filePath, StreamWriter writer)
        {
            FilePath = filePath;
            _Writer = writer;
        }

        /// <summary>
        /// Open (append to) a job's log and write its header. Prunes old job logs first.
        /// </summary>
        /// <param name="paths">Harbor log layout.</param>
        /// <param name="job">Job.</param>
        /// <param name="workingDirectory">Where the job runs.</param>
        /// <param name="error">Why the log could not be opened, or null.</param>
        /// <returns>The log, or null when it could not be opened.</returns>
        public static HarborJobLog? TryOpen(HarborLogPaths paths, HarborJobInfo job, string workingDirectory, out string? error)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (job == null) throw new ArgumentNullException(nameof(job));
            error = null;

            string path = paths.JobLogPath(job);
            try
            {
                Directory.CreateDirectory(paths.JobsDirectory);
                paths.PruneJobLogs(HarborLogPaths.DefaultJobLogsKept);
                FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.AutoFlush = true;
                HarborJobLog log = new HarborJobLog(path, writer);
                log.Write("=== " + Stamp(job.StartedUtc) + " " + job.Title()
                    + " started (runtime " + (String.IsNullOrEmpty(job.Runtime) ? "unknown" : job.Runtime)
                    + (job.CaptainId != null ? ", captain " + job.CaptainId : "")
                    + ", job " + job.JobId + ") in " + workingDirectory);
                return log;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                error = ex.Message;
                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write one output line.
        /// </summary>
        /// <param name="stream">Stream it came from.</param>
        /// <param name="line">Line.</param>
        public void WriteOutput(HarborOutputStreamEnum stream, string line)
        {
            switch (stream)
            {
                case HarborOutputStreamEnum.Stderr:
                    Write("[stderr] " + (line ?? String.Empty));
                    break;
                case HarborOutputStreamEnum.FinalMessage:
                    Write("=== final message");
                    Write(line ?? String.Empty);
                    break;
                default:
                    Write(line ?? String.Empty);
                    break;
            }
        }

        /// <summary>
        /// Write the closing line with the exit code and run time.
        /// </summary>
        /// <param name="exitCode">Exit code.</param>
        /// <param name="nowUtc">Current time, UTC.</param>
        /// <param name="startedUtc">When the job started, UTC.</param>
        public void WriteExit(int exitCode, DateTime nowUtc, DateTime startedUtc)
        {
            Write("=== " + Stamp(nowUtc) + " exited with code " + exitCode.ToString(CultureInfo.InvariantCulture)
                + " after " + HarborJobInfo.FormatElapsed(nowUtc - startedUtc));
        }

        /// <summary>
        /// Write a line that is not job output (a launch failure).
        /// </summary>
        /// <param name="message">Message.</param>
        public void WriteNote(string message)
        {
            Write("=== " + Stamp(DateTime.UtcNow) + " " + (message ?? String.Empty));
        }

        /// <summary>
        /// Close the log.
        /// </summary>
        public void Dispose()
        {
            lock (_Lock)
            {
                try
                {
                    _Writer?.Dispose();
                }
                catch (IOException)
                {
                }

                _Writer = null;
            }
        }

        #endregion

        #region Private-Methods

        private void Write(string line)
        {
            lock (_Lock)
            {
                if (_Writer == null) return;
                try
                {
                    _Writer.WriteLine(line);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                {
                    // Disk full or the file went away: stop logging this job rather than disturbing it.
                    _Writer = null;
                }
            }
        }

        private static string Stamp(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
