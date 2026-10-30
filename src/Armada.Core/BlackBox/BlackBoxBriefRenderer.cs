namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Renders the FIX THIS brief of a black box entry as Markdown (brief.md): a self-contained package to paste into a
    /// coding agent, with the triage instruction (decide UserConfig, MissionBrief, Transient, ArmadaDefect or External,
    /// then fix with evidence) and the sections in record order. Renders whatever sections the record has. Size: aims for <see cref="TargetBytes"/> and never
    /// exceeds <see cref="MaxBytes"/>. Trimming order when over budget: list items (50, 25, 10, 5), then long text
    /// (4000, 1500, 500 characters), then whole sections (Evidence, Related, Attempts, Configuration, Status timeline,
    /// Workspace, Scope), then a hard cut with a marker. The output is redacted again and never contains em-dashes.
    /// Thread-safe.
    /// </summary>
    public class BlackBoxBriefRenderer
    {
        #region Public-Members

        /// <summary>
        /// Size the brief aims for, in UTF-8 bytes. Default BlackBoxLimits.BriefTargetBytes (12 KB), minimum 1024.
        /// </summary>
        public int TargetBytes
        {
            get => _TargetBytes;
            set => _TargetBytes = value < 1024 ? 1024 : value;
        }

        /// <summary>
        /// Hard cap in UTF-8 bytes. Default BlackBoxLimits.BriefMaxBytes (40 KB), minimum 2048.
        /// </summary>
        public int MaxBytes
        {
            get => _MaxBytes;
            set => _MaxBytes = value < 2048 ? 2048 : value;
        }

        /// <summary>
        /// Marker appended when the brief is cut at the hard cap.
        /// </summary>
        public const string CutMarker = "[Brief cut at its size cap. Read record.json (get_blackbox format=json) for the rest.]";

        #endregion

        #region Private-Members

        private static readonly int[] _ListLimits = new int[] { 50, 25, 10, 5 };
        private static readonly int[] _TextLimits = new int[] { 4000, 1500, 500 };
        private static readonly string[] _Order = new string[] { "Header", "Summary", "Identity", "Where", "How", "Timeline", "Why", "Conflict", "Workspace", "Scope", "Configuration", "Prevention", "Improvements", "Recovery", "Attempts", "Evidence", "Related", "Footer", "Answer" };
        private static readonly string[] _DropOrder = new string[] { "Evidence", "Related", "Attempts", "Configuration", "Timeline", "Workspace", "Scope" };
        private static readonly Regex _AbsolutePath = new Regex(@"(?:[A-Za-z]:\\|/)(?:Users|home|private|tmp|var|opt|srv|mnt|Volumes)[^\s'""`)\]]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly BlackBoxRedactor _Redactor;
        private int _TargetBytes = BlackBoxLimits.BriefTargetBytes;
        private int _MaxBytes = BlackBoxLimits.BriefMaxBytes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="redactor">Redactor run over the output, or null for a default one.</param>
        public BlackBoxBriefRenderer(BlackBoxRedactor? redactor = null)
        {
            _Redactor = redactor ?? new BlackBoxRedactor();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render a brief.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="record">Record (null renders from the entry alone).</param>
        /// <param name="redactHosts">Strip absolute paths and host names (for sharing outside the installation).</param>
        /// <returns>Markdown, at most MaxBytes UTF-8 bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public string Render(BlackBoxEntry entry, BlackBoxRecord? record, bool redactHosts = false)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            BlackBoxRecord rec = record ?? new BlackBoxRecord { EntryId = entry.Id, CreatedUtc = entry.CreatedUtc };
            string text = String.Empty;
            HashSet<string> dropped = new HashSet<string>(StringComparer.Ordinal);
            foreach (int listLimit in _ListLimits)
            {
                text = Build(entry, rec, redactHosts, listLimit, _TextLimits[0], dropped);
                if (Bytes(text) <= TargetBytes) return text;
            }
            foreach (int textLimit in _TextLimits)
            {
                text = Build(entry, rec, redactHosts, _ListLimits[_ListLimits.Length - 1], textLimit, dropped);
                if (Bytes(text) <= TargetBytes) return text;
            }
            foreach (string section in _DropOrder)
            {
                if (Bytes(text) <= MaxBytes) return text;
                dropped.Add(section);
                text = Build(entry, rec, redactHosts, _ListLimits[_ListLimits.Length - 1], _TextLimits[_TextLimits.Length - 1], dropped);
            }
            if (Bytes(text) <= MaxBytes) return text;
            return Cut(text, MaxBytes);
        }

        /// <summary>
        /// Replace absolute host paths (under Users, home, private, tmp, var, opt, srv, mnt, Volumes) with {path}, as
        /// redactHosts does for briefs.
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>The text with host paths replaced (empty for null).</returns>
        public static string RedactHosts(string? text)
        {
            return String.IsNullOrEmpty(text) ? String.Empty : _AbsolutePath.Replace(text, "{path}");
        }

        #endregion

        #region Private-Methods

        private string Build(BlackBoxEntry entry, BlackBoxRecord r, bool redactHosts, int listLimit, int textLimit, HashSet<string> dropped)
        {
            BlackBoxSubject s = r.Subject ?? new BlackBoxSubject { MissionId = entry.MissionId, VoyageId = entry.VoyageId, Title = entry.MissionTitle, VesselName = entry.VesselName, Attempt = entry.Attempt, SubjectType = entry.SubjectType };
            BlackBoxSite site = r.Site ?? new BlackBoxSite { Phase = entry.Phase, Step = entry.Step, ArmadaVersion = entry.ArmadaVersion, ArmadaCommit = entry.ArmadaCommit };
            string subjectLabel = s.SubjectType == BlackBoxSubjectTypeEnum.Voyage ? "voyage " + (s.VoyageId ?? "?") : "mission " + (s.MissionId ?? "?");
            Dictionary<string, StringBuilder> sections = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
            StringBuilder sb = Begin(sections, "Header");

            sb.Append("# FIX THIS: ").Append(Inline(s.Title ?? entry.MissionTitle ?? "(untitled)")).Append(" (").Append(subjectLabel).Append(")\n\n");

            sb.Append("You are asked to FIX THIS. This brief is a self-contained black box entry from Armada, a multi-agent orchestration system. ");
            sb.Append("It records where, how and why a ").Append(s.SubjectType == BlackBoxSubjectTypeEnum.Voyage ? "voyage" : "mission").Append(" failed, with evidence.\n\n");
            sb.Append("Before changing anything, triage the failure. Decide which one it is, and say why, quoting the evidence below:\n\n");
            sb.Append("1. **UserConfig**: the user's Armada configuration (vessel, captain, persona, landing mode, commands).\n");
            sb.Append("2. **MissionBrief**: the mission brief or dependency ordering.\n");
            sb.Append("3. **Transient**: a transient condition (provider, network, usage limit); retrying is likely to work.\n");
            sb.Append("4. **ArmadaDefect**: a defect in Armada itself.\n");
            sb.Append("5. **External**: an operator action or something outside Armada.\n\n");
            sb.Append("Then act: for UserConfig or MissionBrief, propose the exact change; for Transient, the retry; for ArmadaDefect, locate the failure site");
            if (!String.IsNullOrEmpty(site.SourceFile)) sb.Append(" (").Append(Inline(site.SourceFile!)).Append(site.SourceLine.HasValue ? ":" + site.SourceLine.Value.ToString(CultureInfo.InvariantCulture) : String.Empty).Append(" in ").Append(Inline(site.Method ?? "?")).Append(")");
            sb.Append(" in the Armada source at the build commit below and propose a fix with a test. Quote evidence, not guesses. Use only the recovery paths listed here when you act on the Armada installation, and confirm anything marked data-loss risk with the user first.\n\n");

            // Summary
            sb = Begin(sections, "Summary");
            sb.Append("## Summary\n\n");
            if (r.Analysis != null && !String.IsNullOrWhiteSpace(r.Analysis.Summary))
            {
                sb.Append(Clip(r.Analysis.Summary!, textLimit));
                if (r.Analysis.SummaryIsModelWritten) sb.Append(" (model-written)");
                sb.Append("\n\n");
            }
            sb.Append("- ").Append(Cap(s.SubjectType.ToString())).Append(" failed during **").Append(site.Phase).Append("** at step **").Append(site.Step).Append("**");
            MissionFailureKindEnum? kind = r.Classification?.Kind ?? entry.FailureKind;
            if (kind.HasValue) sb.Append(" (kind ").Append(kind.Value).Append(")");
            sb.Append(".\n");
            string? before = r.Status?.Before ?? entry.StatusBefore;
            string? after = r.Status?.After ?? entry.StatusAfter;
            if (before != null || after != null) sb.Append("- Status: ").Append(before ?? "unknown").Append(" -> ").Append(after ?? "unknown").Append(".\n");
            BlackBoxTriageEnum triage = r.Analysis?.Triage?.Verdict ?? entry.Triage;
            sb.Append("- Triage: ").Append(triage);
            if (r.Analysis?.Triage != null) sb.Append(" (confidence ").Append(r.Analysis.Triage.Confidence).Append(")");
            sb.Append(triage == BlackBoxTriageEnum.Unknown ? ", not decided yet; decide it from the evidence.\n" : ".\n");
            if (r.CaptureMode != BlackBoxCaptureModeEnum.Inline) sb.Append("- Captured by the ").Append(r.CaptureMode.ToString().ToLowerInvariant()).Append(" after the fact: lower fidelity, the failure site's volatile facts (git state, exception) may be missing.\n");
            if (r.State == BlackBoxStateEnum.Captured) sb.Append("- Enrichment is pending: git forensics and analysis are not in this brief yet.\n");
            if (r.Truncation.TimedOut) sb.Append("- Capture ran out of its time budget; this entry is partial.\n");
            sb.Append('\n');

            // Identity
            sb = Begin(sections, "Identity");
            sb.Append("## Identity\n\n");
            Line(sb, "Entry", entry.Id + " (attempt " + s.Attempt.ToString(CultureInfo.InvariantCulture) + (entry.SupersedesId != null ? ", supersedes " + entry.SupersedesId : String.Empty) + ")");
            Line(sb, "Mission", s.MissionId);
            Line(sb, "Voyage", s.VoyageId == null ? null : s.VoyageId + (s.VoyageTitle != null ? " (" + Inline(s.VoyageTitle) + ")" : String.Empty));
            Line(sb, "Vessel", s.VesselName == null && s.VesselId == null ? null : (s.VesselName ?? "?") + (s.VesselId != null ? " (" + s.VesselId + ")" : String.Empty));
            Line(sb, "Repository", s.VesselRepoUrl);
            Line(sb, "Target branch", s.VesselDefaultBranch);
            Line(sb, "Captain", s.CaptainId == null ? null : (s.CaptainName ?? s.CaptainId) + Join(" / ", s.CaptainRuntime, s.CaptainModel));
            Line(sb, "Persona", s.Persona);
            Line(sb, "Harbor", s.HarborId == null ? null : (s.HarborName ?? s.HarborId));
            Line(sb, "Branch", s.BranchName);
            if (s.IsRescue || s.ParentMissionId != null) Line(sb, "Rescue of", s.ParentMissionId ?? "yes");
            Line(sb, "Merge entry", s.MergeEntryId);
            Line(sb, "Incident", s.IncidentId);
            sb.Append('\n');

            // Where
            sb = Begin(sections, "Where");
            sb.Append("## Where it failed\n\n");
            Line(sb, "Phase / step", site.Phase + " / " + site.Step);
            Line(sb, "Host", site.Host ?? site.HostKind.ToString());
            Line(sb, "Site", site.SourceFile == null ? null : site.SourceFile + (site.SourceLine.HasValue ? ":" + site.SourceLine.Value.ToString(CultureInfo.InvariantCulture) : String.Empty) + (site.Method != null ? " in " + (site.Component != null ? site.Component + "." : String.Empty) + site.Method : String.Empty));
            Line(sb, "Armada build", (site.ArmadaVersion ?? entry.ArmadaVersion ?? "unknown") + ((site.ArmadaCommit ?? entry.ArmadaCommit) != null ? " (commit " + (site.ArmadaCommit ?? entry.ArmadaCommit) + ")" : String.Empty));
            sb.Append('\n');

            // How
            sb = Begin(sections, "How");
            BlackBoxMechanism? m = r.Mechanism;
            string? reason = r.Classification?.RawReason ?? entry.ReasonExcerpt;
            sb.Append("## How it failed\n\n");
            if (!String.IsNullOrWhiteSpace(reason)) sb.Append("Reason:\n\n").Append(Fence(Clip(reason!, textLimit))).Append('\n');
            if (m != null)
            {
                Line(sb, "Exception", m.ExceptionType == null ? null : m.ExceptionType + (m.ExceptionMessage != null ? ": " + Inline(Clip(m.ExceptionMessage, Math.Min(textLimit, 1000))) : String.Empty));
                Line(sb, "Exit code", m.ExitCode?.ToString(CultureInfo.InvariantCulture));
                Line(sb, "Runtime failure", m.RuntimeFailureKind?.ToString());
                Line(sb, "Failing command", m.FailingCommand == null ? null : "`" + Inline(m.FailingCommand) + "`" + (m.CommandExitCode.HasValue ? " (exit " + m.CommandExitCode.Value.ToString(CultureInfo.InvariantCulture) + ")" : String.Empty));
                Line(sb, "Git operation", m.GitOperation);
                if (m.StackFrames.Count > 0)
                {
                    sb.Append("\nArmada stack frames:\n\n").Append(Fence(String.Join("\n", m.StackFrames.Take(listLimit))));
                }
                if (!String.IsNullOrWhiteSpace(m.CommandOutputTail)) sb.Append("\nCommand output (tail):\n\n").Append(Fence(Tail(m.CommandOutputTail!, textLimit)));
                if (!String.IsNullOrWhiteSpace(m.GitStderrTail)) sb.Append("\nGit stderr (tail):\n\n").Append(Fence(Tail(m.GitStderrTail!, textLimit)));
            }
            if (r.Classification != null && r.Classification.SuspectedCause != FailureCauseEnum.None) Line(sb, "Suspected cause", r.Classification.SuspectedCause + (r.Classification.CauseRuleId != null ? " (rule " + r.Classification.CauseRuleId + ")" : String.Empty));
            sb.Append('\n');

            // Status timeline
            sb = Begin(sections, "Timeline");
            if (!dropped.Contains("Timeline") && r.Status != null && r.Status.Timeline.Count > 0)
            {
                sb.Append("## Status timeline\n\n");
                foreach (BlackBoxStatusTimelineEntry t in r.Status.Timeline.Take(listLimit)) sb.Append("- ").Append(Utc(t.Utc)).Append(" ").Append(t.Status).Append(t.Source != null ? " (" + Inline(t.Source) + ")" : String.Empty).Append('\n');
                More(sb, r.Status.Timeline.Count, listLimit);
                sb.Append('\n');
            }

            // Why
            sb = Begin(sections, "Why");
            sb.Append("## Why\n\n");
            if (r.Analysis != null && r.Analysis.Why.Count > 0)
            {
                foreach (BlackBoxFinding f in r.Analysis.Why.Take(listLimit))
                {
                    sb.Append("- ").Append(Inline(f.Statement));
                    sb.Append(" (rule ").Append(f.RuleId).Append(", confidence ").Append(f.Confidence).Append(")");
                    if (f.EvidenceRefs.Count > 0) sb.Append(" Evidence: ").Append(String.Join(", ", f.EvidenceRefs.Take(5).Select(Inline)));
                    sb.Append('\n');
                }
                if (r.Analysis.Triage != null && r.Analysis.Triage.Votes.Count > 0)
                    sb.Append("- Triage votes: ").Append(String.Join(", ", r.Analysis.Triage.Votes.Take(listLimit).Select(v => v.RuleId + "=" + v.Verdict + "x" + v.Weight.ToString(CultureInfo.InvariantCulture)))).Append('\n');
            }
            else
            {
                sb.Append("No analysis findings yet. Work from the reason, mechanism and evidence above.\n");
            }
            sb.Append('\n');

            // Conflict
            sb = Begin(sections, "Conflict");
            BlackBoxConflict? c = r.Conflict;
            if (c != null && (c.IsConflict || c.ConflictedFiles.Count > 0 || c.ConflictingMissions.Count > 0 || (c.ConcurrentMissions != null && c.ConcurrentMissions.Count > 0)))
            {
                sb.Append("## Conflict\n\n");
                if (c.ConflictedFiles.Count > 0)
                {
                    sb.Append("Conflicted files (").Append(Math.Max(c.ConflictedFileTotal, c.ConflictedFiles.Count).ToString(CultureInfo.InvariantCulture)).Append("):\n\n");
                    foreach (string f in c.ConflictedFiles.Take(listLimit)) sb.Append("- `").Append(Inline(f)).Append("`\n");
                    More(sb, Math.Max(c.ConflictedFileTotal, c.ConflictedFiles.Count), listLimit);
                }
                if (c.ConflictedDirectories.Count > 0)
                {
                    sb.Append("\nConflicted directories:\n\n");
                    foreach (BlackBoxDirectoryCount d in c.ConflictedDirectories.Take(listLimit)) sb.Append("- `").Append(Inline(d.Directory.Length == 0 ? "/" : d.Directory)).Append("`: ").Append(d.FileCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    if (c.DeepestConflictedDirectory != null) sb.Append("\nDeepest directory with several conflicts: `").Append(Inline(c.DeepestConflictedDirectory)).Append("` (").Append((c.DeepestConflictedDirectoryFileCount ?? 0).ToString(CultureInfo.InvariantCulture)).Append(" files)\n");
                }
                if (c.ConflictingMissions.Count > 0)
                {
                    sb.Append("\nConflicting work:\n\n");
                    foreach (BlackBoxConflictingMission cm in c.ConflictingMissions.Take(listLimit))
                    {
                        sb.Append("- ").Append(cm.IsOutsideArmada ? "Commit outside Armada" + (cm.Author != null ? " by " + Inline(cm.Author) : String.Empty) : (cm.MissionId ?? "?") + (cm.Title != null ? " (" + Inline(cm.Title) + ")" : String.Empty));
                        if (cm.CommitHash != null) sb.Append(", commit ").Append(cm.CommitHash);
                        if (cm.LandedUtc.HasValue) sb.Append(", landed ").Append(Utc(cm.LandedUtc.Value));
                        sb.Append(", ").Append(Math.Max(cm.OverlappingFileTotal, cm.OverlappingFiles.Count).ToString(CultureInfo.InvariantCulture)).Append(" overlapping files\n");
                    }
                }
                if (c.ConcurrentMissions != null && c.ConcurrentMissions.Count > 0)
                {
                    sb.Append("\nConcurrent missions that touched the same files:\n\n");
                    foreach (BlackBoxConflictingMission cm in c.ConcurrentMissions.Take(listLimit))
                        sb.Append("- ").Append(cm.MissionId ?? "?").Append(cm.Title != null ? " (" + Inline(cm.Title) + ")" : String.Empty).Append(", ").Append(Math.Max(cm.OverlappingFileTotal, cm.OverlappingFiles.Count).ToString(CultureInfo.InvariantCulture)).Append(" overlapping files").Append(cm.LandedUtc.HasValue ? ", landed " + Utc(cm.LandedUtc.Value) : ", not landed").Append('\n');
                }
                if (c.TargetCommitsSinceStart.Count > 0)
                {
                    sb.Append("\nTarget commits since the mission started (").Append(Math.Max(c.TargetCommitTotal, c.TargetCommitsSinceStart.Count).ToString(CultureInfo.InvariantCulture)).Append("):\n\n");
                    foreach (BlackBoxCommitRef cr in c.TargetCommitsSinceStart.Take(listLimit)) sb.Append("- ").Append(cr.CommitHash).Append(cr.Subject != null ? " " + Inline(cr.Subject) : String.Empty).Append(cr.MissionId != null ? " (" + cr.MissionId + ")" : String.Empty).Append('\n');
                }
                sb.Append('\n');
            }

            // Workspace
            sb = Begin(sections, "Workspace");
            BlackBoxWorkspace? w = r.Workspace;
            if (w != null && !dropped.Contains("Workspace"))
            {
                sb.Append("## Workspace\n\n");
                Line(sb, "Dock", w.DockId == null ? null : w.DockId + (w.DockState != null ? " (" + w.DockState + ")" : String.Empty));
                Line(sb, "Worktree", w.WorktreePath == null ? null : w.WorktreePath + (w.WorktreeExists.HasValue ? (w.WorktreeExists.Value ? " (exists)" : " (missing)") : String.Empty));
                Line(sb, "Checkout", w.CheckoutPath == null ? null : w.CheckoutPath + (w.CheckoutDirty == true ? " (dirty)" : String.Empty));
                Line(sb, "Branch", w.BranchName == null ? null : w.BranchName + (w.BranchTip != null ? " at " + w.BranchTip : String.Empty));
                Line(sb, "Mission commit", w.MissionCommit);
                Line(sb, "Start commit", w.StartCommit == null ? null : w.StartCommit + (w.StartCommitSource != null ? " (from " + w.StartCommitSource + ")" : String.Empty));
                Line(sb, "Target", w.TargetBranch == null ? null : w.TargetBranch + Join(", ", w.TargetTipAtStart != null ? "tip at start " + w.TargetTipAtStart : null, w.TargetTipAtFailure != null ? "tip at failure " + w.TargetTipAtFailure : null));
                Line(sb, "Behind target", w.CommitsBehindTarget?.ToString(CultureInfo.InvariantCulture));
                Line(sb, "Ahead of target", w.CommitsAheadOfTarget?.ToString(CultureInfo.InvariantCulture));
                Line(sb, "Branch exists", w.BranchExists.HasValue ? (w.BranchExists.Value ? "yes" : "no") : null);
                Line(sb, "Harbor link", w.HarborLinkState);
                Line(sb, "Inspected on", w.InspectedHost);
                Line(sb, "Note", w.InspectionNote);
                Line(sb, "Target movement", w.TargetMovementNote);
                if (w.DirtyPaths.Count > 0)
                {
                    sb.Append("\nDirty paths (").Append(Math.Max(w.DirtyPathTotal, w.DirtyPaths.Count).ToString(CultureInfo.InvariantCulture)).Append("):\n\n");
                    foreach (string p in w.DirtyPaths.Take(listLimit)) sb.Append("- `").Append(Inline(p)).Append("`\n");
                    More(sb, Math.Max(w.DirtyPathTotal, w.DirtyPaths.Count), listLimit);
                }
                sb.Append('\n');
            }

            // Scope
            sb = Begin(sections, "Scope");
            BlackBoxScope? sc = r.Scope;
            if (sc != null && !dropped.Contains("Scope") && (sc.OutOfScopeFiles.Count > 0 || sc.BoundaryFindings.Count > 0 || sc.ScopedFiles.Count > 0))
            {
                sb.Append("## Scope\n\n");
                if (sc.ScopedFiles.Count > 0) sb.Append("Scoped files: ").Append(String.Join(", ", sc.ScopedFiles.Take(listLimit).Select(f => "`" + Inline(f) + "`"))).Append('\n');
                if (sc.OutOfScopeFiles.Count > 0)
                {
                    sb.Append("\nOut of scope (").Append(Math.Max(sc.OutOfScopeFileTotal, sc.OutOfScopeFiles.Count).ToString(CultureInfo.InvariantCulture)).Append("):\n\n");
                    foreach (string f in sc.OutOfScopeFiles.Take(listLimit)) sb.Append("- `").Append(f.Length == 0 ? "(blank entry)" : Inline(f)).Append("`\n");
                }
                if (sc.BoundaryFindings.Count > 0)
                {
                    sb.Append("\nBoundary findings:\n\n");
                    foreach (string f in sc.BoundaryFindings.Take(listLimit)) sb.Append("- ").Append(Inline(f)).Append('\n');
                }
                sb.Append('\n');
            }

            // Configuration
            sb = Begin(sections, "Configuration");
            if (r.Configuration != null && r.Configuration.Values.Count > 0 && !dropped.Contains("Configuration"))
            {
                sb.Append("## Configuration in effect\n\n");
                foreach (BlackBoxConfigValue v in r.Configuration.Values.Take(listLimit)) sb.Append("- ").Append(v.Setting).Append(": ").Append(v.Value == null ? "(unset)" : "`" + Inline(v.Value) + "`").Append(" (").Append(v.Source).Append(")\n");
                sb.Append('\n');
            }

            // Prevention
            sb = Begin(sections, "Prevention");
            if (r.Analysis != null && r.Analysis.ConfigRecommendations.Count > 0)
            {
                sb.Append("## What would have prevented it\n\n");
                foreach (BlackBoxConfigRecommendation rec in r.Analysis.ConfigRecommendations.Take(listLimit))
                {
                    sb.Append("- ").Append(rec.Id).Append(": set ").Append(rec.Setting).Append(" on the ").Append(rec.Scope.ToString().ToLowerInvariant());
                    if (rec.CurrentValue != null || rec.SuggestedValue != null) sb.Append(" from `").Append(Inline(rec.CurrentValue ?? "unset")).Append("` to `").Append(Inline(rec.SuggestedValue ?? "?")).Append('`');
                    if (rec.Rationale != null) sb.Append(". ").Append(Inline(rec.Rationale));
                    if (rec.RuleId != null) sb.Append(" (rule ").Append(rec.RuleId).Append(")");
                    if (rec.HowToApply != null) sb.Append(" How: ").Append(Inline(rec.HowToApply));
                    sb.Append('\n');
                }
                sb.Append('\n');
            }

            // Improvements
            sb = Begin(sections, "Improvements");
            if (r.Analysis != null && r.Analysis.ArmadaImprovements.Count > 0)
            {
                sb.Append("## What Armada could change\n\n");
                foreach (BlackBoxImprovement imp in r.Analysis.ArmadaImprovements.Take(listLimit))
                {
                    sb.Append("- ").Append(imp.Area != null ? "**" + Inline(imp.Area) + "**: " : String.Empty).Append(Inline(imp.Hypothesis)).Append(" (severity ").Append(imp.Severity);
                    if (imp.RuleId != null) sb.Append(", rule ").Append(imp.RuleId);
                    sb.Append(")");
                    if (imp.SourcePointers.Count > 0) sb.Append(" Source: ").Append(String.Join(", ", imp.SourcePointers.Take(5).Select(p => "`" + Inline(p) + "`")));
                    if (imp.KnownIssue != null) sb.Append(" Known issue: ").Append(Inline(imp.KnownIssue));
                    sb.Append('\n');
                }
                sb.Append('\n');
            }

            // Recovery
            sb = Begin(sections, "Recovery");
            sb.Append("## Recovery\n\n");
            if (r.Analysis != null && r.Analysis.RecoveryPaths.Count > 0)
            {
                foreach (BlackBoxRecoveryPath p in r.Analysis.RecoveryPaths.OrderBy(x => x.Rank).Take(listLimit))
                {
                    sb.Append("### ").Append(p.Rank.ToString(CultureInfo.InvariantCulture)).Append(". ").Append(Inline(p.Title)).Append(" (").Append(p.Id).Append(")").Append("\n\n");
                    sb.Append("Risk: ").Append(p.Risk).Append(p.DataLossRisk ? ", data-loss risk (confirm first)" : String.Empty).Append(".");
                    if (p.ExpectedOutcome != null) sb.Append(" Expected: ").Append(Inline(p.ExpectedOutcome));
                    sb.Append("\n\n");
                    foreach (string pre in p.Preconditions.Take(listLimit)) sb.Append("- Precondition: ").Append(Inline(pre)).Append('\n');
                    foreach (BlackBoxRecoveryStep st in p.Steps.OrderBy(x => x.Order).Take(listLimit))
                    {
                        sb.Append(st.Order.ToString(CultureInfo.InvariantCulture)).Append(". ").Append(Inline(st.Description)).Append(st.DataLossRisk ? " (data-loss risk)" : String.Empty).Append('\n');
                        if (st.McpCommand != null) sb.Append("   - MCP: `").Append(Inline(st.McpCommand)).Append("`\n");
                        if (st.CliCommand != null) sb.Append("   - CLI: `").Append(Inline(st.CliCommand)).Append("`\n");
                        if (st.RestCommand != null) sb.Append("   - REST: `").Append(Inline(st.RestCommand)).Append("`\n");
                    }
                    if (p.Action != null) sb.Append("\nAction: `").Append(p.Action.Tool).Append(' ').Append(Inline(ArgumentsText(p.Action))).Append("` (run it with run_blackbox_recovery, dry run first)\n");
                    sb.Append('\n');
                }
            }
            else
            {
                sb.Append("No recovery paths recorded yet.");
                if (s.MissionId != null) sb.Append(" Typical next steps: read the evidence (`read_blackbox_object`), then `restart_mission` or `retry_landing` for ").Append(s.MissionId).Append(" once the cause is fixed.");
                sb.Append("\n\n");
            }

            // Attempts
            sb = Begin(sections, "Attempts");
            if (r.Attempts.Count > 0 && !dropped.Contains("Attempts"))
            {
                sb.Append("## Attempts\n\n");
                foreach (BlackBoxAttempt a in r.Attempts.Take(listLimit))
                {
                    sb.Append("- Attempt ").Append(a.Attempt.ToString(CultureInfo.InvariantCulture));
                    if (a.EntryId != null) sb.Append(" (").Append(a.EntryId).Append(")");
                    if (a.Kind.HasValue) sb.Append(": ").Append(a.Kind.Value);
                    if (a.ReasonExcerpt != null) sb.Append(", ").Append(Inline(Clip(a.ReasonExcerpt, 200)));
                    sb.Append('\n');
                }
                sb.Append('\n');
            }

            // Evidence
            sb = Begin(sections, "Evidence");
            if (!dropped.Contains("Evidence") && r.Evidence != null && (r.Evidence.Objects.Count > 0 || r.Evidence.CheckResults.Count > 0 || r.Evidence.RelevantEvents.Count > 0))
            {
                sb.Append("## Evidence\n\n");
                foreach (BlackBoxEvidenceItem item in r.Evidence.Objects.Take(listLimit))
                {
                    sb.Append("- `").Append(item.Name).Append("` (").Append(item.UncompressedBytes.ToString(CultureInfo.InvariantCulture)).Append(" bytes").Append(item.Truncated ? ", cut" : String.Empty).Append(")");
                    if (item.Excerpt != null) sb.Append(": ").Append(Inline(Clip(item.Excerpt, 200)));
                    sb.Append('\n');
                }
                foreach (StageCheckResult check in r.Evidence.CheckResults.Take(listLimit))
                    sb.Append("- Check ").Append(check.Status).Append(check.FailedCommand != null ? ": `" + Inline(check.FailedCommand) + "`" : String.Empty).Append(check.ExitCode.HasValue ? " exit " + check.ExitCode.Value.ToString(CultureInfo.InvariantCulture) : String.Empty).Append('\n');
                foreach (BlackBoxEventRef ev in r.Evidence.RelevantEvents.Take(Math.Min(listLimit, 20))) sb.Append("- ").Append(Utc(ev.CreatedUtc)).Append(' ').Append(ev.EventType).Append(ev.Message != null ? ": " + Inline(Clip(ev.Message, 200)) : String.Empty).Append('\n');
                sb.Append("\nRead an object with MCP `read_blackbox_object {\"entryId\": \"").Append(entry.Id).Append("\", \"name\": \"<name>\"}` (64 KB chunks) or REST `GET /api/v1/blackbox/").Append(entry.Id).Append("/objects/<name>`.\n");
                sb.Append('\n');
            }

            // Related
            sb = Begin(sections, "Related");
            if (!dropped.Contains("Related") && r.Related != null && (r.Related.SimilarEntries.Count > 0 || r.Related.OccurrencesLast30Days > 0))
            {
                sb.Append("## Related failures\n\n");
                if (r.Related.Fingerprint != null) Line(sb, "Fingerprint", r.Related.Fingerprint);
                Line(sb, "Same failure in the last 30 days", r.Related.OccurrencesLast30Days.ToString(CultureInfo.InvariantCulture));
                foreach (BlackBoxSimilarEntry se in r.Related.SimilarEntries.Take(listLimit))
                    sb.Append("- ").Append(se.EntryId).Append(se.MissionTitle != null ? " (" + Inline(se.MissionTitle) + ")" : String.Empty).Append(", ").Append(Utc(se.CreatedUtc)).Append(", ").Append(se.Resolution).Append(se.ResolutionNote != null ? ": " + Inline(Clip(se.ResolutionNote, 200)) : String.Empty).Append('\n');
                sb.Append('\n');
            }

            // Footer
            sb = Begin(sections, "Footer");
            if (r.Redaction.RedactedCount > 0 || r.Truncation.Fields.Count > 0 || r.Truncation.Notes.Count > 0)
            {
                sb.Append("## Notes on this record\n\n");
                if (r.Redaction.RedactedCount > 0) sb.Append("- Redacted ").Append(r.Redaction.RedactedCount.ToString(CultureInfo.InvariantCulture)).Append(" secret(s): ").Append(String.Join(", ", r.Redaction.RulesApplied.Select(x => x.Rule + " x" + x.Count.ToString(CultureInfo.InvariantCulture)))).Append(".\n");
                if (r.Truncation.Fields.Count > 0) sb.Append("- Cut fields: ").Append(String.Join(", ", r.Truncation.Fields.Take(listLimit).Select(f => f.Field + " (" + f.OriginalSize.ToString(CultureInfo.InvariantCulture) + " " + f.Unit + ")"))).Append(".\n");
                foreach (string note in r.Truncation.Notes.Take(listLimit)) sb.Append("- ").Append(Inline(note)).Append('\n');
                sb.Append('\n');
            }

            sb = Begin(sections, "Answer");
            sb.Append("## Your answer\n\n");
            sb.Append("State the triage verdict (UserConfig, MissionBrief, Transient, ArmadaDefect or External) and the evidence for it, the root cause, the fix, and how to verify it. ");
            sb.Append("If you change the Armada installation, use the recovery paths above and annotate the entry with `annotate_blackbox` when done.\n");

            StringBuilder assembled = new StringBuilder();
            foreach (string name in _Order)
            {
                if (sections.TryGetValue(name, out StringBuilder? part)) assembled.Append(part);
            }
            string text = assembled.ToString();
            text = _Redactor.Redact(text) ?? String.Empty;
            if (redactHosts) text = _AbsolutePath.Replace(text, "{path}");
            return NoDashes(text);
        }

        private static StringBuilder Begin(Dictionary<string, StringBuilder> sections, string name)
        {
            StringBuilder part = new StringBuilder();
            sections[name] = part;
            return part;
        }

        private static void Line(StringBuilder sb, string label, string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            sb.Append("- ").Append(label).Append(": ").Append(Inline(value!)).Append('\n');
        }

        private static void More(StringBuilder sb, int total, int shown)
        {
            if (total > shown) sb.Append("- ... and ").Append((total - shown).ToString(CultureInfo.InvariantCulture)).Append(" more (see record.json)\n");
        }

        private static string Join(string separator, params string?[] parts)
        {
            List<string> present = parts.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p!).ToList();
            return present.Count == 0 ? String.Empty : " (" + String.Join(separator, present) + ")";
        }

        private static string ArgumentsText(BlackBoxAction action)
        {
            return "{" + String.Join(", ", action.Arguments.Select(a => "\"" + a.Key + "\": \"" + a.Value + "\"")) + "}";
        }

        private static string Cap(string value)
        {
            return value.Length == 0 ? value : Char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static string Inline(string value)
        {
            return value.Replace("\r", " ").Replace("\n", " ").Replace("`", "'");
        }

        private static string Fence(string value)
        {
            string body = value.Replace("```", "'''");
            return "```\n" + body.TrimEnd() + "\n```\n";
        }

        private static string Clip(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max) + " ... (" + (value.Length - max).ToString(CultureInfo.InvariantCulture) + " more characters in record.json)";
        }

        private static string Tail(string value, int max)
        {
            return value.Length <= max ? value : "... (" + (value.Length - max).ToString(CultureInfo.InvariantCulture) + " earlier characters in record.json)\n" + value.Substring(value.Length - max);
        }

        private static string Utc(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z";
        }

        private static int Bytes(string text)
        {
            return Encoding.UTF8.GetByteCount(text);
        }

        private static string Cut(string text, int maxBytes)
        {
            string marker = "\n\n" + CutMarker + "\n";
            int budget = maxBytes - Encoding.UTF8.GetByteCount(marker);
            int chars = Math.Min(text.Length, budget);
            while (chars > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, chars)) > budget) chars -= Math.Max(1, (Encoding.UTF8.GetByteCount(text.AsSpan(0, chars)) - budget) / 4);
            if (chars > 0 && Char.IsHighSurrogate(text[chars - 1])) chars--;
            return text.Substring(0, Math.Max(0, chars)) + marker;
        }

        private static string NoDashes(string text)
        {
            return text.Replace('—', '-').Replace('–', '-');
        }

        #endregion
    }
}
