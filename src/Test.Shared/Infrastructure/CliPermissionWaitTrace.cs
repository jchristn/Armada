namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// What a test saw while it waited for a thread prompt's pending CLI permission request and its Ask card, and the
    /// server-side state when the wait gave up: the polls (count, slowest, last status and rows), the prompts the
    /// server still has in flight with the step each is in, the thread's stored requests and messages, the thread pool,
    /// and the tail of the fixture's warning log. A timeout then names the step that stalled instead of only reporting
    /// that nothing appeared.
    /// </summary>
    public sealed class CliPermissionWaitTrace
    {
        #region Public-Members

        /// <summary>
        /// Number of polls made.
        /// </summary>
        public int Polls { get; private set; } = 0;

        /// <summary>
        /// Slowest poll in milliseconds.
        /// </summary>
        public long SlowestPollMs { get; private set; } = 0;

        /// <summary>
        /// Status of the last poll, or null before the first one returned.
        /// </summary>
        public HttpStatusCode? LastStatus { get; private set; } = null;

        #endregion

        #region Private-Members

        private const int _LogTailLines = 40;
        private readonly object _Lock = new object();
        private string _LastRows = "(none)";
        private string? _LastError = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionWaitTrace()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record one poll of the request list.
        /// </summary>
        /// <param name="status">HTTP status.</param>
        /// <param name="elapsedMs">How long the poll took.</param>
        /// <param name="rows">Rows returned, or null when the poll failed.</param>
        public void RecordPoll(HttpStatusCode status, long elapsedMs, List<CliPermissionRequest>? rows)
        {
            lock (_Lock)
            {
                Polls++;
                if (elapsedMs > SlowestPollMs) SlowestPollMs = elapsedMs;
                LastStatus = status;
                if (rows != null) _LastRows = DescribeRequests(rows);
            }
        }

        /// <summary>
        /// Record a poll that threw.
        /// </summary>
        /// <param name="elapsedMs">How long the poll took.</param>
        /// <param name="error">The exception.</param>
        public void RecordPollError(long elapsedMs, Exception error)
        {
            lock (_Lock)
            {
                Polls++;
                if (elapsedMs > SlowestPollMs) SlowestPollMs = elapsedMs;
                _LastError = error.GetType().Name + ": " + error.Message;
            }
        }

        /// <summary>
        /// Describe the wait and the server-side state of the thread's prompt. Never throws: a part that cannot be read
        /// says why.
        /// </summary>
        /// <param name="fx">The fixture whose server ran the prompt.</param>
        /// <param name="thread">The thread.</param>
        /// <param name="call">The prompt call, or null.</param>
        /// <returns>The description.</returns>
        public async Task<string> DescribeAsync(E2EServerFixture fx, AskThread thread, Task? call)
        {
            if (fx == null) throw new ArgumentNullException(nameof(fx));
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            DateTime now = DateTime.UtcNow;
            StringBuilder text = new StringBuilder();
            lock (_Lock)
            {
                text.AppendLine("polls=" + Polls + " slowestPollMs=" + SlowestPollMs + " lastStatus=" + (LastStatus?.ToString() ?? "-") + (_LastError != null ? " lastError=" + _LastError : ""));
                text.AppendLine("last rows: " + _LastRows);
            }

            text.AppendLine("prompt call: " + (call == null ? "-" : call.Status.ToString()));

            CliPermissionService? service = fx.Server.CliPermissions;
            if (service == null)
            {
                text.AppendLine("server prompts in flight: (no service)");
            }
            else
            {
                List<CliPermissionPromptProgress> inFlight = service.GetInFlightPrompts();
                text.AppendLine("server prompts in flight: " + inFlight.Count + " (waiters " + service.WaitingCount + ")");
                foreach (CliPermissionPromptProgress progress in inFlight) text.AppendLine("  " + progress.Describe(now));
            }

            ThreadPool.GetAvailableThreads(out int availableWorkers, out int availableIo);
            ThreadPool.GetMinThreads(out int minWorkers, out int minIo);
            ThreadPool.GetMaxThreads(out int maxWorkers, out int maxIo);
            text.AppendLine("thread pool: threads=" + ThreadPool.ThreadCount + " pendingWorkItems=" + ThreadPool.PendingWorkItemCount
                + " busyWorkers=" + (maxWorkers - availableWorkers) + " busyIo=" + (maxIo - availableIo) + " minWorkers=" + minWorkers + " minIo=" + minIo);

            await AppendStoredStateAsync(text, fx, thread).ConfigureAwait(false);
            AppendLogTail(text, Path.Combine(fx.TempDir, "fixture-warnings.log"));
            return text.ToString();
        }

        #endregion

        #region Private-Methods

        private static async Task AppendStoredStateAsync(StringBuilder text, E2EServerFixture fx, AskThread thread)
        {
            try
            {
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
                // Read only (no InitializeAsync, which writes), and bounded: a database that is stalled for writers must
                // not hold the report up; the error then says so.
                using (CancellationTokenSource bound = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
                {
                    List<CliPermissionRequest> requests = await db.CliPermissionRequests.EnumerateAsync(new CliPermissionRequestQuery { ThreadId = thread.Id }, bound.Token).ConfigureAwait(false);
                    text.AppendLine("stored requests for " + thread.Id + ": " + DescribeRequests(requests));
                    if (!String.IsNullOrEmpty(thread.TenantId))
                    {
                        AskMessagePage page = await db.AskMessages.EnumerateAsync(thread.TenantId!, thread.Id, null, 50, bound.Token).ConfigureAwait(false);
                        text.AppendLine("stored messages: " + (page.Messages.Count == 0
                            ? "(none)"
                            : String.Join("; ", page.Messages.Select(m => m.Id + " #" + m.Sequence + " " + m.Kind + " " + m.CreatedUtc.ToString("HH:mm:ss.fff")))));
                    }
                }
            }
            catch (Exception ex)
            {
                text.AppendLine("stored state unreadable: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void AppendLogTail(StringBuilder text, string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    text.AppendLine("fixture warnings: (no log)");
                    return;
                }

                List<string> lines = new List<string>();
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Add(line);
                        if (lines.Count > _LogTailLines) lines.RemoveAt(0);
                    }
                }

                text.AppendLine("fixture warnings (last " + lines.Count + " lines):");
                foreach (string line in lines) text.AppendLine("  " + line);
            }
            catch (Exception ex)
            {
                text.AppendLine("fixture warnings unreadable: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string DescribeRequests(List<CliPermissionRequest> rows)
        {
            if (rows.Count == 0) return "(none)";
            return String.Join("; ", rows.Select(r => r.Id + " " + r.Status + " messageId=" + (r.MessageId ?? "-")
                + " created=" + r.CreatedUtc.ToString("HH:mm:ss.fff") + " updated=" + r.LastUpdateUtc.ToString("HH:mm:ss.fff")));
        }

        #endregion
    }
}
