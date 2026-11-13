namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds a <see cref="HandoffArtifact"/> from what Armada already parses at the end of a stage: the protocol lines,
    /// the Judge verdict and its affected cases, a verifier's block, the stage check result, the commit and the diff.
    /// Deterministic and free of I/O, so the same stage always yields the same artifact.
    /// </summary>
    public static class HandoffArtifactBuilder
    {
        #region Public-Members

        /// <summary>
        /// Longest stage output stored on an artifact. A longer output stays on the mission and the artifact records its
        /// length with <see cref="HandoffArtifact.OutputComplete"/> false.
        /// </summary>
        public const int MaxStoredOutputChars = 1000000;

        /// <summary>
        /// Longest diff stored on an artifact. A longer diff is left to <see cref="HandoffArtifact.DiffPointer"/>.
        /// </summary>
        public const int MaxStoredDiffChars = 1000000;

        /// <summary>
        /// Most entries stored in each of <see cref="HandoffArtifact.Findings"/>, <see cref="HandoffArtifact.AcceptanceCriteria"/>
        /// and <see cref="HandoffArtifact.CompletionLines"/> (docs/ENUMERATION.md). These come from agent output and the
        /// task description, which cannot be rejected, so a longer list keeps its first entries and ends with one marker
        /// entry that says how many more there were; the full text stays in <see cref="HandoffArtifact.Output"/> and on the
        /// mission.
        /// </summary>
        public const int MaxStoredListItems = CollectionLimits.MaxConfigItems;

        /// <summary>
        /// Prefix of the marker entry that ends a CompletionLines list that was cut to <see cref="MaxStoredListItems"/>.
        /// </summary>
        public const string TruncatedLinePrefix = "[ARMADA:TRUNCATED]";

        #endregion

        #region Private-Members

        private const int _MaxCommitReferences = 20;

        private static readonly string[] _ProtocolPrefixes = new[] { "[ARMADA:RESULT]", "[ARMADA:VERDICT]", "[ARMADA:STAGE-OUTCOME]" };

        private static readonly Regex _Heading = new Regex(@"^\s*#{1,6}\s+(?<title>.+?)\s*#*\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _FindingSectionTitle = new Regex(
            @"^(?:\*\*|__|`)?(?:affected\s+cases?|(?:blocking\s+|unresolved\s+|non-?blocking\s+)?findings|issues)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _Field = new Regex(
            @"^\s*(?:[-*]\s*)?(?:\*\*|__)?(?<label>[A-Za-z][A-Za-z /-]{0,40}?)(?:\*\*|__)?\s*:\s*(?:\*\*|__)?\s*(?<value>.*?)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _ListItem = new Regex(@"^(?:\d+[.)]|[-*])\s+\S", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _FileWithLine = new Regex(
            @"`?(?<path>[A-Za-z0-9_.\-/\\]+\.[A-Za-z0-9]+)(?::(?<line>\d+))?`?",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _CommitMention = new Regex(
            @"\b(?:commit|sha|hash|head)\b[^0-9a-fA-F\n]{0,20}\b(?<sha>[0-9a-f]{7,40})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _FixLabel = new Regex(
            @"^(?:required\s+(?:fix|correction|change|action)|fix|correction|remediation|suggested\s+fix)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the artifact of one stage attempt.
        /// </summary>
        /// <param name="stage">The stage (mission) as it is after Armada handled its exit.</param>
        /// <param name="check">The stage check result of the attempt, or null when none was recorded.</param>
        /// <param name="captain">The captain that ran the attempt, or null.</param>
        /// <param name="stageOutcomeId">The stage outcome of the same attempt, or null.</param>
        /// <param name="attempt">Attempt number on the mission, starting at 1.</param>
        /// <returns>The artifact (not stored).</returns>
        /// <exception cref="ArgumentNullException">Thrown when stage is null.</exception>
        public static HandoffArtifact Build(Mission stage, StageCheckResult? check = null, Captain? captain = null, string? stageOutcomeId = null, int attempt = 1)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));

            string output = stage.AgentOutput ?? String.Empty;
            string persona = String.IsNullOrWhiteSpace(stage.Persona) ? PersonaCatalog.Worker : PersonaCatalog.NormalizeName(stage.Persona);

            HandoffArtifact artifact = new HandoffArtifact
            {
                TenantId = stage.TenantId,
                UserId = stage.UserId,
                MissionId = stage.Id,
                VoyageId = stage.VoyageId,
                VesselId = stage.VesselId,
                StageOutcomeId = stageOutcomeId,
                Persona = persona,
                CaptainId = captain?.Id ?? stage.CaptainId,
                CaptainName = captain?.Name,
                Attempt = attempt < 1 ? 1 : attempt,
                ReworkRound = stage.ReworkRound,
                BranchName = stage.BranchName,
                CommitHash = stage.CommitHash
            };

            artifact.CompletionLines = ExtractCompletionLines(output);
            artifact.Verdict = DecideVerdict(persona, output, artifact.CompletionLines);
            bool blocking = IsBlockingVerdict(persona, output, artifact.Verdict);
            artifact.Findings = ExtractFindings(output, blocking);
            artifact.AcceptanceCriteria = ExtractAcceptanceCriteria(stage.Description);
            if (check != null) artifact.CheckResults.Add(check);
            artifact.CommitReferences = ExtractCommitReferences(stage.CommitHash, output);
            artifact.RequiredActions = BuildRequiredActions(artifact);

            // Bound the agent-derived lists after the verdict and required actions were decided from the full lists.
            artifact.Findings = CapFindings(artifact.Findings);
            artifact.AcceptanceCriteria = CapLines(artifact.AcceptanceCriteria, "acceptance criteria were not stored; read them in the mission description.");
            artifact.CompletionLines = CapLines(artifact.CompletionLines, "protocol lines were not stored; read them in the stage output.", TruncatedLinePrefix + " ");

            artifact.OutputLength = output.Length;
            artifact.OutputComplete = output.Length <= MaxStoredOutputChars;
            artifact.Output = output.Length == 0 || !artifact.OutputComplete ? null : output;

            string diff = stage.DiffSnapshot ?? String.Empty;
            artifact.DiffLength = diff.Length;
            artifact.DiffComplete = diff.Length <= MaxStoredDiffChars;
            artifact.Diff = diff.Length == 0 || !artifact.DiffComplete ? null : diff;
            artifact.DiffPointer = BuildDiffPointer(stage);
            return artifact;
        }

        /// <summary>
        /// The protocol lines in an output ([ARMADA:RESULT], [ARMADA:VERDICT], [ARMADA:STAGE-OUTCOME]), outside code
        /// fences, in order.
        /// </summary>
        /// <param name="output">Stage output, or null.</param>
        /// <returns>The lines, trimmed.</returns>
        public static List<string> ExtractCompletionLines(string? output)
        {
            List<string> lines = new List<string>();
            if (String.IsNullOrEmpty(output)) return lines;
            bool inFence = false;
            foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (inFence) continue;
                foreach (string prefix in _ProtocolPrefixes)
                {
                    if (line.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        lines.Add(line);
                        break;
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// The numbered findings in a stage output: every block of an Affected Case, Findings or Issues section, with its
        /// file, line, severity and required fix. The finding text is kept whole.
        /// </summary>
        /// <param name="output">Stage output, or null.</param>
        /// <param name="blockingVerdict">Whether the stage blocked the work (findings default to Blocking).</param>
        /// <returns>The findings, numbered from 1.</returns>
        public static List<HandoffFinding> ExtractFindings(string? output, bool blockingVerdict)
        {
            List<HandoffFinding> findings = new List<HandoffFinding>();
            if (String.IsNullOrWhiteSpace(output)) return findings;

            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            bool inFence = false;
            bool inSection = false;
            bool sectionNonBlocking = false;
            List<string>? block = null;

            void Flush()
            {
                if (block == null) return;
                HandoffFinding? finding = ToFinding(block, blockingVerdict, sectionNonBlocking, findings.Count + 1);
                if (finding != null) findings.Add(finding);
                block = null;
            }

            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) inFence = !inFence;

                Match heading = inFence ? Match.Empty : _Heading.Match(raw);
                if (heading.Success)
                {
                    Flush();
                    string title = heading.Groups["title"].Value.Trim();
                    inSection = _FindingSectionTitle.IsMatch(title);
                    sectionNonBlocking = inSection && Regex.IsMatch(title, @"non-?blocking|optional", RegexOptions.IgnoreCase);
                    continue;
                }

                if (!inSection) continue;
                if (trimmed.StartsWith("[ARMADA:", StringComparison.Ordinal)) continue;

                if (block != null && !inFence && StartsNewFinding(block, raw)) Flush();
                if (block == null)
                {
                    if (trimmed.Length == 0) continue;
                    block = new List<string>();
                }

                block.Add(raw.TrimEnd());
            }

            Flush();
            return findings;
        }

        /// <summary>
        /// The acceptance criteria written in a task description: the list items under a heading or label that names
        /// acceptance criteria. Text handed over from earlier stages is ignored.
        /// </summary>
        /// <param name="description">Mission description, or null.</param>
        /// <returns>The criteria, in order.</returns>
        public static List<string> ExtractAcceptanceCriteria(string? description)
        {
            List<string> criteria = new List<string>();
            if (String.IsNullOrWhiteSpace(description)) return criteria;

            string text = description!.Replace("\r\n", "\n");
            int cut = text.IndexOf(PipelineHandoffContext.SectionMarker, StringComparison.Ordinal);
            if (cut >= 0) text = text.Substring(0, cut);

            bool inSection = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                string plain = line.TrimStart('#', ' ').Trim('*', '_', ' ');
                bool isHeading = line.StartsWith("#", StringComparison.Ordinal) || (plain.EndsWith(":", StringComparison.Ordinal) && !_ListItem.IsMatch(line));
                if (isHeading)
                {
                    inSection = plain.IndexOf("acceptance", StringComparison.OrdinalIgnoreCase) >= 0;
                    continue;
                }

                if (!inSection) continue;
                if (line.Length == 0)
                {
                    if (criteria.Count > 0) inSection = false;
                    continue;
                }

                if (_ListItem.IsMatch(line))
                    criteria.Add(Regex.Replace(line, @"^(?:\d+[.)]|[-*])\s+(?:\[[ xX]\]\s+)?", String.Empty).Trim());
                else if (criteria.Count > 0)
                    criteria[criteria.Count - 1] = criteria[criteria.Count - 1] + " " + line;
            }

            return criteria;
        }

        #endregion

        #region Private-Methods

        private static string? DecideVerdict(string persona, string output, List<string> completionLines)
        {
            if (PersonaCatalog.Matches(persona, PersonaCatalog.Judge))
            {
                JudgeVerdictEnum verdict = JudgeVerdictParser.Parse(output);
                switch (verdict)
                {
                    case JudgeVerdictEnum.Pass: return "PASS";
                    case JudgeVerdictEnum.Fail: return "FAIL";
                    case JudgeVerdictEnum.NeedsRevision: return "NEEDS_REVISION";
                    case JudgeVerdictEnum.SpecConflict: return "SPEC_CONFLICT";
                    default: return null;
                }
            }

            if (VerifierContract.Applies(persona) && VerifierContract.IsBlocked(output)) return "BLOCKED";

            for (int i = completionLines.Count - 1; i >= 0; i--)
            {
                string line = completionLines[i];
                int close = line.IndexOf(']');
                string value = close >= 0 ? line.Substring(close + 1).Trim() : String.Empty;
                int space = value.IndexOf(' ');
                if (space > 0) value = value.Substring(0, space);
                if (value.Length > 0) return value.ToUpperInvariant();
            }

            return null;
        }

        private static bool IsBlockingVerdict(string persona, string output, string? verdict)
        {
            if (String.Equals(verdict, "NEEDS_REVISION", StringComparison.Ordinal) || String.Equals(verdict, "FAIL", StringComparison.Ordinal)) return true;
            return VerifierContract.Applies(persona) && VerifierContract.IsBlocked(output);
        }

        private static bool StartsNewFinding(List<string> block, string raw)
        {
            string line = raw.TrimEnd();
            if (line.Length == 0) return false;

            // A top-level list item starts a finding.
            if (!Char.IsWhiteSpace(line[0]) && Regex.IsMatch(line, @"^\d+[.)]\s+\S")) return true;

            // A second File: or Scenario: field starts the next affected case.
            Match field = _Field.Match(line);
            if (!field.Success) return false;
            string label = field.Groups["label"].Value.Trim();
            if (!String.Equals(label, JudgeContract.AffectedCaseFileField, StringComparison.OrdinalIgnoreCase)
                && !String.Equals(label, JudgeContract.AffectedCaseScenarioField, StringComparison.OrdinalIgnoreCase)) return false;

            foreach (string existing in block)
            {
                Match prior = _Field.Match(existing);
                if (prior.Success && String.Equals(prior.Groups["label"].Value.Trim(), label, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static HandoffFinding? ToFinding(List<string> block, bool blockingVerdict, bool sectionNonBlocking, int number)
        {
            string summary = String.Join("\n", block).Trim();
            if (summary.Length == 0) return null;
            if (Regex.IsMatch(summary, @"^(?:[-*]\s*)?(?:none|n/?a|no (?:blocking )?findings?)\.?$", RegexOptions.IgnoreCase)) return null;

            HandoffFinding finding = new HandoffFinding { Number = number, Summary = summary };
            string? fileValue = null;
            List<string> fix = new List<string>();
            foreach (string line in block)
            {
                Match field = _Field.Match(line);
                if (!field.Success) continue;
                string label = field.Groups["label"].Value.Trim();
                string value = field.Groups["value"].Value.Trim();
                if (fileValue == null && String.Equals(label, JudgeContract.AffectedCaseFileField, StringComparison.OrdinalIgnoreCase) && value.Length > 0) fileValue = value;
                else if (_FixLabel.IsMatch(label) && value.Length > 0) fix.Add(value);
                else if (String.Equals(label, "Severity", StringComparison.OrdinalIgnoreCase)) finding.Severity = ParseSeverity(value, finding.Severity);
            }

            Match location = _FileWithLine.Match(fileValue ?? summary);
            if (location.Success && location.Groups["path"].Value.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
            {
                finding.File = location.Groups["path"].Value;
                if (location.Groups["line"].Success && Int32.TryParse(location.Groups["line"].Value, out int lineNumber)) finding.Line = lineNumber;
            }

            if (fix.Count > 0) finding.RequiredFix = String.Join(" ", fix);

            if (finding.Severity == HandoffFindingSeverityEnum.Unknown)
            {
                if (sectionNonBlocking || Regex.IsMatch(summary, @"\b(?:non-?blocking|optional|nit)\b", RegexOptions.IgnoreCase))
                    finding.Severity = HandoffFindingSeverityEnum.NonBlocking;
                else if (blockingVerdict || Regex.IsMatch(summary, @"\bblocking\b", RegexOptions.IgnoreCase))
                    finding.Severity = HandoffFindingSeverityEnum.Blocking;
            }

            return finding;
        }

        private static HandoffFindingSeverityEnum ParseSeverity(string value, HandoffFindingSeverityEnum fallback)
        {
            if (Regex.IsMatch(value, @"non-?blocking|optional|low|minor|nit|info", RegexOptions.IgnoreCase)) return HandoffFindingSeverityEnum.NonBlocking;
            if (Regex.IsMatch(value, @"blocking|high|critical|major|medium", RegexOptions.IgnoreCase)) return HandoffFindingSeverityEnum.Blocking;
            return fallback;
        }

        private static List<string> ExtractCommitReferences(string? commitHash, string output)
        {
            List<string> references = new List<string>();
            if (!String.IsNullOrWhiteSpace(commitHash)) references.Add(commitHash!.Trim());
            foreach (Match match in _CommitMention.Matches(output))
            {
                if (references.Count >= _MaxCommitReferences) break;
                string sha = match.Groups["sha"].Value;
                if (references.Any(r => r.StartsWith(sha, StringComparison.OrdinalIgnoreCase) || sha.StartsWith(r, StringComparison.OrdinalIgnoreCase))) continue;
                references.Add(sha);
            }

            return references;
        }

        private static List<string> BuildRequiredActions(HandoffArtifact artifact)
        {
            List<string> actions = new List<string>();
            int blocking = artifact.Findings.Count(f => !f.Resolved && f.Severity == HandoffFindingSeverityEnum.Blocking);
            int other = artifact.Findings.Count(f => !f.Resolved && f.Severity != HandoffFindingSeverityEnum.Blocking);
            if (blocking > 0)
                actions.Add("Resolve the " + blocking + " blocking finding(s) with a regression test each; report every finding as resolved, superseded (why) or unresolved.");
            if (other > 0)
                actions.Add("Check the " + other + " other finding(s); a reproducible violation of an acceptance criterion stays blocking whatever its label.");
            if (String.Equals(artifact.Verdict, "SPEC_CONFLICT", StringComparison.Ordinal))
                actions.Add("The stage reported a specification conflict; a person decides. Do not waive the criterion.");

            foreach (StageCheckResult check in artifact.CheckResults)
            {
                if (check.Status == StageCheckStatusEnum.Failed)
                    actions.Add("Fix the failing check: " + (check.FailedCommand ?? "a configured command") + (check.ExitCode.HasValue ? " (exit " + check.ExitCode.Value + ")" : "") + ".");
                else if (check.Status == StageCheckStatusEnum.CouldNotRun)
                    actions.Add("The stage check could not run (" + check.Reason + "); run the build and tests yourself before relying on the work.");
            }

            return actions;
        }

        private static List<HandoffFinding> CapFindings(List<HandoffFinding> findings)
        {
            if (findings.Count <= MaxStoredListItems) return findings;
            List<HandoffFinding> kept = findings.Take(MaxStoredListItems - 1).ToList();
            int omitted = findings.Count - kept.Count;
            kept.Add(new HandoffFinding
            {
                Number = kept.Count + 1,
                Severity = HandoffFindingSeverityEnum.Unknown,
                Summary = omitted + " more findings (" + (kept.Count + 1) + " to " + findings.Count + " of " + findings.Count
                    + ") were not stored; read them in the stage output."
            });
            return kept;
        }

        private static List<string> CapLines(List<string> lines, string what, string prefix = "")
        {
            if (lines.Count <= MaxStoredListItems) return lines;
            List<string> kept = lines.Take(MaxStoredListItems - 1).ToList();
            int omitted = lines.Count - kept.Count;
            kept.Add(prefix + omitted + " more of " + lines.Count + " " + what);
            return kept;
        }

        private static string BuildDiffPointer(Mission stage)
        {
            StringBuilder pointer = new StringBuilder();
            pointer.Append("Branch ").Append(stage.BranchName ?? "unknown");
            if (!String.IsNullOrEmpty(stage.CommitHash)) pointer.Append(" at commit ").Append(stage.CommitHash);
            pointer.Append("; regenerate with git diff on that branch, MCP get_mission_diff (missionId ").Append(stage.Id)
                .Append("), or GET /api/v1/missions/").Append(stage.Id).Append("/diff.");
            return pointer.ToString();
        }

        #endregion
    }
}
