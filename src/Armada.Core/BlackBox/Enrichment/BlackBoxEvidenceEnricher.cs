namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Evidence objects: evidence/agent-output.txt (tail, when capture did not store it), evidence/mission.log (tail of
    /// the mission log file), evidence/check-{n}.txt (check runs, or the captured stage check results),
    /// evidence/verification-{n}.txt (mission.landing_verification receipts), evidence/merge-entry.txt (the merge-queue
    /// entry and its test output), evidence/diff.patch (from the worktree when no diff snapshot exists) with
    /// evidence/diffstat.txt and Evidence.DiffStat, and evidence/events.json with Evidence.RelevantEvents (the newest 50
    /// events of the mission or voyage). Each is bounded here (tails keep the end) and again by the writer, which also
    /// redacts every text object; cuts are listed in the record's truncation section.
    /// </summary>
    public class BlackBoxEvidenceEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "Evidence";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly VesselGitInspector _Inspector;
        private const string _VerificationEvent = "mission.landing_verification";
        private const int _MaxOutputChars = 65536;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings (log directory).</param>
        /// <param name="inspector">Git inspector.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxEvidenceEnricher(DatabaseDriver database, ArmadaSettings settings, VesselGitInspector inspector, LoggingModule logging) : base(logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            BlackBoxRecord record = context.Record;
            record.Evidence ??= new BlackBoxEvidenceIndex();
            Mission? mission = context.Mission;
            if (context.Entry.SubjectType == BlackBoxSubjectTypeEnum.Mission && mission != null)
            {
                AddAgentOutput(context, mission);
                AddMissionLog(context, mission);
                await AddChecksAsync(context, mission, token).ConfigureAwait(false);
                await AddVerificationsAsync(context, mission, token).ConfigureAwait(false);
                await AddMergeEntryAsync(context, mission, token).ConfigureAwait(false);
                await AddDiffAsync(context, mission, token).ConfigureAwait(false);
            }
            await AddEventsAsync(context, token).ConfigureAwait(false);
        }

        private void AddAgentOutput(BlackBoxEnrichmentContext context, Mission mission)
        {
            if (String.IsNullOrEmpty(mission.AgentOutput) || BlackBoxForensics.HasEvidence(context.Record, BlackBoxObjectNames.AgentOutput)) return;
            byte[] tail = BlackBoxForensics.TailUtf8(mission.AgentOutput!, BlackBoxForensics.TailBytes, out long original);
            if (tail.LongLength < original) BlackBoxRecordBounder.AddField(context.Record.Truncation, BlackBoxObjectNames.AgentOutput, original, tail.LongLength, "bytes");
            BlackBoxForensics.PutObject(context, new BlackBoxPendingObject { Name = BlackBoxObjectNames.AgentOutput, ContentType = BlackBoxObjectNames.TextContentType, Data = tail });
        }

        private void AddMissionLog(BlackBoxEnrichmentContext context, Mission mission)
        {
            string path;
            try
            {
                path = new ArmadaLogPaths(_Settings.LogDirectory).MissionLogPath(mission.Id);
            }
            catch (ArgumentException)
            {
                return;
            }
            byte[]? tail = BlackBoxForensics.TailFile(path, BlackBoxForensics.TailBytes, out long original);
            if (tail == null || tail.Length == 0) return;
            if (tail.LongLength < original) BlackBoxRecordBounder.AddField(context.Record.Truncation, BlackBoxObjectNames.MissionLog, original, tail.LongLength, "bytes");
            BlackBoxForensics.PutObject(context, new BlackBoxPendingObject { Name = BlackBoxObjectNames.MissionLog, ContentType = BlackBoxObjectNames.TextContentType, Data = tail });
        }

        private async Task AddChecksAsync(BlackBoxEnrichmentContext context, Mission mission, CancellationToken token)
        {
            EnumerationResult<CheckRun> runs = await _Database.CheckRuns.EnumerateAsync(new CheckRunQuery { MissionId = mission.Id, TenantId = context.Entry.TenantId, PageSize = BlackBoxLimits.MaxCheckResults }, token).ConfigureAwait(false);
            int n = 0;
            foreach (CheckRun run in runs.Objects.OrderBy(r => r.CreatedUtc).Take(BlackBoxLimits.MaxCheckResults))
            {
                n++;
                StringBuilder sb = new StringBuilder();
                sb.Append("Check run ").Append(run.Id).Append('\n');
                Field(sb, "Label", run.Label);
                Field(sb, "Type", run.Type.ToString());
                Field(sb, "Status", run.Status.ToString());
                Field(sb, "Exit code", run.ExitCode?.ToString(CultureInfo.InvariantCulture));
                Field(sb, "Commit", run.CommitHash);
                Field(sb, "Working directory", run.WorkingDirectory);
                Field(sb, "Duration ms", run.DurationMs?.ToString(CultureInfo.InvariantCulture));
                Field(sb, "Summary", run.Summary);
                AppendOutput(context, sb, BlackBoxObjectNames.CheckPrefix + n + ".txt", run.Output);
                BlackBoxForensics.PutObject(context, BlackBoxForensics.TextObject(BlackBoxObjectNames.CheckPrefix + n + ".txt", sb.ToString()));
            }
            if (n > 0) return;

            foreach (StageCheckResult check in (context.Record.Evidence?.CheckResults ?? new List<StageCheckResult>()).Take(BlackBoxLimits.MaxCheckResults))
            {
                n++;
                StringBuilder sb = new StringBuilder();
                sb.Append("Stage check ").Append(n.ToString(CultureInfo.InvariantCulture)).Append('\n');
                Field(sb, "Status", check.Status.ToString());
                Field(sb, "Reason", check.Reason.ToString());
                Field(sb, "Commands", check.Commands.Count == 0 ? null : String.Join(" && ", check.Commands));
                Field(sb, "Failed command", check.FailedCommand);
                Field(sb, "Exit code", check.ExitCode?.ToString(CultureInfo.InvariantCulture));
                Field(sb, "Pre-existing failures", check.PreExistingFailures.Count == 0 ? null : String.Join(", ", check.PreExistingFailures));
                Field(sb, "Duration ms", check.DurationMs.ToString(CultureInfo.InvariantCulture));
                AppendOutput(context, sb, BlackBoxObjectNames.CheckPrefix + n + ".txt", check.FailureMessage);
                BlackBoxForensics.PutObject(context, BlackBoxForensics.TextObject(BlackBoxObjectNames.CheckPrefix + n + ".txt", sb.ToString()));
            }
        }

        private async Task AddVerificationsAsync(BlackBoxEnrichmentContext context, Mission mission, CancellationToken token)
        {
            List<ArmadaEvent> events = await _Database.Events.EnumerateByMissionAsync(mission.Id, 200, token).ConfigureAwait(false);
            List<ArmadaEvent> receipts = events.Where(e => e.EventType == _VerificationEvent && TenantMatches(context, e))
                .OrderBy(e => e.CreatedUtc).ToList();
            if (receipts.Count > BlackBoxLimits.MaxCheckResults)
            {
                BlackBoxRecordBounder.AddField(context.Record.Truncation, BlackBoxObjectNames.VerificationPrefix + "{n}.txt", receipts.Count, BlackBoxLimits.MaxCheckResults, "items");
                receipts = receipts.Skip(receipts.Count - BlackBoxLimits.MaxCheckResults).ToList();
            }
            int n = 0;
            foreach (ArmadaEvent evt in receipts)
            {
                n++;
                BlackBoxVerificationReceipt? receipt = null;
                if (!String.IsNullOrWhiteSpace(evt.Payload))
                {
                    try
                    {
                        receipt = JsonSerializer.Deserialize<BlackBoxVerificationReceipt>(evt.Payload!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException)
                    {
                        receipt = null;
                    }
                }
                string name = BlackBoxObjectNames.VerificationPrefix + n + ".txt";
                StringBuilder sb = new StringBuilder();
                sb.Append("Landing verification ").Append(evt.Id).Append(" at ").Append(Utc(evt.CreatedUtc)).Append('\n');
                Field(sb, "Message", evt.Message);
                if (receipt != null)
                {
                    Field(sb, "Status", receipt.Status);
                    Field(sb, "Commit", receipt.CommitHash);
                    Field(sb, "Command", receipt.Command);
                    Field(sb, "Exit code", receipt.ExitCode?.ToString(CultureInfo.InvariantCulture));
                    Field(sb, "Started", receipt.StartedUtc.HasValue ? Utc(receipt.StartedUtc.Value) : null);
                    Field(sb, "Completed", receipt.CompletedUtc.HasValue ? Utc(receipt.CompletedUtc.Value) : null);
                    AppendOutput(context, sb, name, receipt.Output);
                }
                BlackBoxForensics.PutObject(context, BlackBoxForensics.TextObject(name, sb.ToString()));
            }
        }

        private async Task AddMergeEntryAsync(BlackBoxEnrichmentContext context, Mission mission, CancellationToken token)
        {
            MergeEntry? entry = null;
            if (!String.IsNullOrEmpty(context.Entry.MergeEntryId)) entry = await _Database.MergeEntries.ReadAsync(context.Entry.MergeEntryId!, token).ConfigureAwait(false);
            if (entry == null)
            {
                EnumerationResult<MergeEntry> page = await _Database.MergeEntries.EnumerateAsync(new EnumerationQuery { MissionId = mission.Id, PageSize = 1, Order = EnumerationOrderEnum.CreatedDescending }, token).ConfigureAwait(false);
                entry = page.Objects.FirstOrDefault();
            }
            if (entry == null || (context.Entry.TenantId != null && entry.TenantId != null && entry.TenantId != context.Entry.TenantId)) return;

            StringBuilder sb = new StringBuilder();
            sb.Append("Merge-queue entry ").Append(entry.Id).Append('\n');
            Field(sb, "Status", entry.Status.ToString());
            Field(sb, "Branch", entry.BranchName);
            Field(sb, "Target", entry.TargetBranch);
            Field(sb, "Batch", entry.BatchId);
            Field(sb, "Retries", entry.RetryCount.ToString(CultureInfo.InvariantCulture));
            Field(sb, "Test command", entry.TestCommand);
            Field(sb, "Test exit code", entry.TestExitCode?.ToString(CultureInfo.InvariantCulture));
            Field(sb, "Created", Utc(entry.CreatedUtc));
            Field(sb, "Completed", entry.CompletedUtc.HasValue ? Utc(entry.CompletedUtc.Value) : null);
            AppendOutput(context, sb, BlackBoxObjectNames.MergeEntry, entry.TestOutput);
            BlackBoxForensics.PutObject(context, BlackBoxForensics.TextObject(BlackBoxObjectNames.MergeEntry, sb.ToString()));
        }

        private async Task AddDiffAsync(BlackBoxEnrichmentContext context, Mission mission, CancellationToken token)
        {
            string? diff = mission.DiffSnapshot;
            if (String.IsNullOrEmpty(diff))
            {
                BlackBoxForensicState state = await BlackBoxForensics.EnsureStateAsync(context, _Database, _Inspector, token).ConfigureAwait(false);
                if (state.Host != null && state.WorktreeExists == true && state.Dock?.WorktreePath != null && state.TargetBranch != null && GitRevisionNames.IsSafeBranchName(state.TargetBranch))
                {
                    DockHost host = state.Host;
                    string worktree = state.Dock.WorktreePath;
                    string target = state.TargetBranch;
                    diff = await BlackBoxForensics.TryAsync<string?>(async () => await host.Git.DiffAsync(worktree, target, token).ConfigureAwait(false), null).ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(diff))
                        BlackBoxForensics.PutObject(context, new BlackBoxPendingObject { Name = BlackBoxObjectNames.Diff, ContentType = BlackBoxObjectNames.DiffContentType, Data = Encoding.UTF8.GetBytes(diff!) });
                }
            }
            if (String.IsNullOrEmpty(diff)) return;

            List<BlackBoxDiffFileStat> stats = BlackBoxForensics.DiffFileStats(diff);
            if (stats.Count == 0) return;
            BlackBoxDiffStat summary = new BlackBoxDiffStat
            {
                Files = stats.Count,
                Insertions = stats.Sum(s => s.Insertions),
                Deletions = stats.Sum(s => s.Deletions),
                TopPaths = stats.OrderByDescending(s => s.Insertions + s.Deletions).ThenBy(s => s.Path, StringComparer.Ordinal).Take(BlackBoxLimits.MaxDiffTopPaths).Select(s => s.Path).ToList()
            };
            context.Record.Evidence!.DiffStat = summary;

            StringBuilder sb = new StringBuilder();
            int width = Math.Min(80, stats.Max(s => s.Path.Length));
            foreach (BlackBoxDiffFileStat s in stats)
                sb.Append(' ').Append(s.Path.PadRight(width)).Append(" | +").Append(s.Insertions.ToString(CultureInfo.InvariantCulture)).Append(" -").Append(s.Deletions.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(' ').Append(stats.Count.ToString(CultureInfo.InvariantCulture)).Append(" files changed, ")
              .Append(summary.Insertions.ToString(CultureInfo.InvariantCulture)).Append(" insertions(+), ")
              .Append(summary.Deletions.ToString(CultureInfo.InvariantCulture)).Append(" deletions(-)\n");
            BlackBoxForensics.PutObject(context, BlackBoxForensics.TextObject(BlackBoxObjectNames.DiffStat, sb.ToString()));
        }

        private async Task AddEventsAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            List<ArmadaEvent> events;
            if (context.Entry.SubjectType == BlackBoxSubjectTypeEnum.Voyage && context.Entry.VoyageId != null)
                events = await _Database.Events.EnumerateByVoyageAsync(context.Entry.VoyageId, BlackBoxLimits.MaxRelevantEvents, token).ConfigureAwait(false);
            else if (context.Entry.MissionId != null)
                events = await _Database.Events.EnumerateByMissionAsync(context.Entry.MissionId, BlackBoxLimits.MaxRelevantEvents, token).ConfigureAwait(false);
            else return;

            List<BlackBoxEventRef> refs = events.Where(e => TenantMatches(context, e) && !e.EventType.StartsWith("blackbox.", StringComparison.Ordinal))
                .OrderByDescending(e => e.CreatedUtc).Take(BlackBoxLimits.MaxRelevantEvents)
                .Select(e => new BlackBoxEventRef { EventId = e.Id, EventType = e.EventType, Message = BlackBoxLimits.Clip(e.Message, BlackBoxLimits.MaxEvidenceExcerptChars), CreatedUtc = e.CreatedUtc })
                .ToList();
            if (refs.Count == 0) return;
            context.Record.Evidence!.RelevantEvents = refs;
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(refs, BlackBoxJson.Options);
            BlackBoxForensics.PutObject(context, new BlackBoxPendingObject { Name = BlackBoxObjectNames.Events, ContentType = BlackBoxObjectNames.JsonContentType, Data = json, IsText = true });
        }

        private static bool TenantMatches(BlackBoxEnrichmentContext context, ArmadaEvent e)
        {
            return context.Entry.TenantId == null || e.TenantId == null || e.TenantId == context.Entry.TenantId;
        }

        private static void AppendOutput(BlackBoxEnrichmentContext context, StringBuilder sb, string objectName, string? output)
        {
            if (String.IsNullOrEmpty(output)) return;
            string text = output!;
            if (text.Length > _MaxOutputChars)
            {
                BlackBoxRecordBounder.AddField(context.Record.Truncation, objectName, text.Length, _MaxOutputChars, "chars");
                text = "[... cut by Armada: " + text.Length + " chars originally, tail kept ...]\n" + text.Substring(text.Length - _MaxOutputChars);
            }
            sb.Append("\n--- output ---\n").Append(text);
            if (!text.EndsWith("\n", StringComparison.Ordinal)) sb.Append('\n');
        }

        private static void Field(StringBuilder sb, string label, string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            sb.Append(label).Append(": ").Append(value).Append('\n');
        }

        private static string Utc(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
