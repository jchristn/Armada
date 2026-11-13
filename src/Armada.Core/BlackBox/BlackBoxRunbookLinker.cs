namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Runbook linkage (an analysis hook): after the advisor ranks recovery paths, each path is matched against the
    /// active runbooks the entry's owner can see. A runbook names what it handles with a tag line in its description or
    /// overview, <c>blackbox: bbx-land-001.stash-retry, BBX-LAND-001, DirtyCheckout, LandingConflict</c> (a path id, a
    /// rule id, a failure step or a failure kind; strongest first), or by its title containing the rule id or the step
    /// name. A matched path gets RunbookId and RunbookTitle, and a confirmed run of it starts the runbook through
    /// start_runbook_execution instead of running its tool steps (<see cref="RunbookPlan"/>). Thread-safe.
    /// </summary>
    public class BlackBoxRunbookLinker : IBlackBoxAnalysisHook
    {
        #region Public-Members

        /// <summary>
        /// The MCP tool a runbook path runs.
        /// </summary>
        public const string RunbookTool = "start_runbook_execution";

        /// <summary>
        /// Test hook: supplies the runbooks for an entry instead of reading them.
        /// </summary>
        public Func<BlackBoxEntry, List<Runbook>>? RunbooksOverride { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxRunbookLinker] ";
        private readonly RunbookService _Runbooks;
        private readonly LoggingModule _Logging;
        private static readonly Regex _TagLine = new Regex(@"^\s*(?:[-*>]\s*)?(?:<!--\s*)?blackbox\s*:\s*(?<tags>[^\r\n]+?)\s*(?:-->)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxRunbookLinker(DatabaseDriver database, LoggingModule logging)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Runbooks = new RunbookService(database, logging);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task OnAnalyzedAsync(BlackBoxEntry entry, BlackBoxRecord record, BlackBoxAnalysis analysis, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (analysis == null) throw new ArgumentNullException(nameof(analysis));
            if (analysis.RecoveryPaths.Count == 0 || String.IsNullOrEmpty(entry.TenantId)) return;
            List<Runbook> runbooks = await ReadRunbooksAsync(entry, token).ConfigureAwait(false);
            Link(analysis.RecoveryPaths, runbooks, entry.Step, entry.FailureKind);
        }

        /// <summary>
        /// Link paths to runbooks. Pure. Paths keep a link only while a runbook still matches.
        /// </summary>
        /// <param name="paths">Recovery paths (changed in place).</param>
        /// <param name="runbooks">Candidate runbooks.</param>
        /// <param name="step">Failure step.</param>
        /// <param name="kind">Failure kind, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when paths or runbooks is null.</exception>
        public static void Link(List<BlackBoxRecoveryPath> paths, List<Runbook> runbooks, BlackBoxStepEnum step, MissionFailureKindEnum? kind)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (runbooks == null) throw new ArgumentNullException(nameof(runbooks));
            List<Runbook> active = runbooks.Where(r => r.Active).OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
            foreach (BlackBoxRecoveryPath path in paths)
            {
                path.RunbookId = null;
                path.RunbookTitle = null;
                int best = 0;
                Runbook? chosen = null;
                foreach (Runbook runbook in active)
                {
                    int score = Score(runbook, path, step, kind);
                    if (score > best) { best = score; chosen = runbook; }
                }
                if (chosen == null) continue;
                path.RunbookId = chosen.Id;
                path.RunbookTitle = BlackBoxLimits.Clip(chosen.Title, BlackBoxLimits.MaxTitleChars);
            }
        }

        /// <summary>
        /// The plan a runbook-linked path runs: one step that starts the runbook, with the entry and path in its notes.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="path">Path with a RunbookId.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry or path is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the path has no runbook.</exception>
        public static List<BlackBoxRecoveryStep> RunbookPlan(BlackBoxEntry entry, BlackBoxRecoveryPath path)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (String.IsNullOrEmpty(path.RunbookId)) throw new ArgumentException("The path has no runbook.", nameof(path));
            BlackBoxAction action = new BlackBoxAction { Tool = RunbookTool };
            action.Arguments["runbookId"] = path.RunbookId!;
            action.Arguments["title"] = BlackBoxLimits.Clip("Recovery: " + path.Title, 200) ?? "Recovery";
            action.Arguments["notes"] = "Started from black box entry " + entry.Id + (entry.MissionId != null ? " (mission " + entry.MissionId + ")" : String.Empty) + ", recovery path " + path.Id + ".";
            if (!String.IsNullOrEmpty(entry.IncidentId)) action.Arguments["incidentId"] = entry.IncidentId!;
            return new List<BlackBoxRecoveryStep>
            {
                new BlackBoxRecoveryStep
                {
                    Order = 1,
                    Description = "Start runbook " + (path.RunbookTitle ?? path.RunbookId) + " (linked to this recovery path) and follow its steps.",
                    Action = action,
                    McpCommand = BlackBoxRuleTemplate.McpCommand(action),
                    RestCommand = "POST /api/v1/runbooks/" + path.RunbookId + "/executions",
                    DataLossRisk = false
                }
            };
        }

        #endregion

        #region Private-Methods

        private async Task<List<Runbook>> ReadRunbooksAsync(BlackBoxEntry entry, CancellationToken token)
        {
            if (RunbooksOverride != null) return RunbooksOverride(entry);
            try
            {
                AuthContext scope = String.IsNullOrEmpty(entry.UserId)
                    ? AuthContext.Authenticated(entry.TenantId!, "blackbox", false, true, "BlackBox")
                    : AuthContext.Authenticated(entry.TenantId!, entry.UserId!, false, false, "BlackBox");
                EnumerationResult<Runbook> page = await _Runbooks.EnumerateAsync(scope, new RunbookQuery { Active = true, PageNumber = 1, PageSize = BlackBoxInsightLimits.MaxRunbookCandidates }, token).ConfigureAwait(false);
                return page.Objects;
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "could not read runbooks for " + entry.Id + ": " + e.Message);
                return new List<Runbook>();
            }
        }

        private static int Score(Runbook runbook, BlackBoxRecoveryPath path, BlackBoxStepEnum step, MissionFailureKindEnum? kind)
        {
            HashSet<string> tags = Tags(runbook);
            string? ruleId = path.RuleId;
            if (tags.Contains(path.Id)) return 6;
            if (ruleId != null && tags.Contains(ruleId)) return 5;
            if (step != BlackBoxStepEnum.Unknown && tags.Contains(step.ToString())) return 4;
            if (kind.HasValue && tags.Contains(kind.Value.ToString())) return 3;
            string title = runbook.Title ?? String.Empty;
            if (ruleId != null && title.IndexOf(ruleId, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (step != BlackBoxStepEnum.Unknown && Regex.IsMatch(title, @"\b" + Regex.Escape(step.ToString()) + @"\b", RegexOptions.IgnoreCase)) return 1;
            return 0;
        }

        private static HashSet<string> Tags(Runbook runbook)
        {
            HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in new[] { runbook.Description ?? String.Empty, runbook.OverviewMarkdown ?? String.Empty })
            {
                foreach (Match m in _TagLine.Matches(text))
                {
                    foreach (string tag in m.Groups["tags"].Value.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = tag.Trim().Trim('`', '"', '\'');
                        if (t.Length > 0 && tags.Count < 64) tags.Add(t);
                    }
                }
            }
            return tags;
        }

        #endregion
    }
}
