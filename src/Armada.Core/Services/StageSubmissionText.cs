namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// How a validated submission appears in a stage's output and what the parsers read from it (RELIABILITY_SIMPLICITY_PLAN.md
    /// RS-6.2). The Admiral appends a submission block (a <see cref="BoundaryMarker"/> line and an
    /// <see cref="FenceInfo"/> fence holding the submission JSON) below the captain's own output. Every decision about the
    /// stage then reads <see cref="Effective"/>: when the output holds a valid block, the canonical text rendered from the
    /// last one, so the submission wins over any marker line, section or plan block the captain also wrote; otherwise
    /// the captain's output with any invalid block removed, so markers remain the fallback.
    /// </summary>
    public static class StageSubmissionText
    {
        #region Public-Members

        /// <summary>
        /// The line that opens a submission block.
        /// </summary>
        public const string BoundaryMarker = "[ARMADA:SUBMISSION]";

        /// <summary>
        /// Info string of the fence that holds the submission JSON.
        /// </summary>
        public const string FenceInfo = "armada-submission";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions _ReadOptions = new JsonSerializerOptions();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The block the Admiral appends to a stage output for a stored submission.
        /// </summary>
        /// <param name="submission">Stored submission.</param>
        /// <returns>Block text, starting with a blank line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when submission is null.</exception>
        public static string Block(StageSubmission submission)
        {
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            return Block(submission.Submission, submission.Id);
        }

        /// <summary>
        /// The block for a validated submission, with an id that names the stored record (or a runtime-local id for a
        /// native submission that is stored at completion).
        /// </summary>
        /// <param name="envelope">Validated submission.</param>
        /// <param name="id">Submission id.</param>
        /// <returns>Block text, starting with a blank line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when envelope is null or id is empty.</exception>
        public static string Block(StageSubmissionEnvelope envelope, string id)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            StringBuilder text = new StringBuilder();
            text.Append("\n\n").Append(BoundaryMarker).Append(' ').Append(envelope.Kind.ToString().ToLowerInvariant()).Append(' ').Append(id)
                .Append(" (submitted through ").Append(StageSubmissionTools.ToolFor(envelope.Kind)).Append("; it replaces any plan, verdict or result line above)\n");
            text.Append("```").Append(FenceInfo).Append('\n');
            text.Append(JsonSerializer.Serialize(envelope, _WriteOptions)).Append('\n');
            text.Append("```\n");
            return text.ToString();
        }

        /// <summary>
        /// Whether a stage output already holds the block of a submission id.
        /// </summary>
        /// <param name="output">Stage output.</param>
        /// <param name="id">Submission id.</param>
        /// <returns>True when present.</returns>
        public static bool ContainsBlock(string? output, string? id)
        {
            if (String.IsNullOrEmpty(output) || String.IsNullOrEmpty(id)) return false;
            return output.Contains(BoundaryMarker + " ", StringComparison.Ordinal) && output.Contains(" " + id + " ", StringComparison.Ordinal);
        }

        /// <summary>
        /// The last valid submission in a stage output, re-checked against the persona's contract, or null.
        /// </summary>
        /// <param name="output">Stage output.</param>
        /// <param name="persona">Persona of the stage.</param>
        /// <returns>The submission, or null.</returns>
        public static StageSubmissionEnvelope? Read(string? output, string? persona)
        {
            return Read(output, persona, out _);
        }

        /// <summary>
        /// The last valid submission in a stage output, re-checked against the persona's contract, or null; also returns
        /// the id its block names.
        /// </summary>
        /// <param name="output">Stage output.</param>
        /// <param name="persona">Persona of the stage.</param>
        /// <param name="id">The id the block names, or null.</param>
        /// <returns>The submission, or null.</returns>
        public static StageSubmissionEnvelope? Read(string? output, string? persona, out string? id)
        {
            id = null;
            if (String.IsNullOrEmpty(output) || !output.Contains(BoundaryMarker, StringComparison.Ordinal)) return null;
            List<StageSubmissionBlock> blocks = FindBlocks(output);
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                StageSubmissionEnvelope? envelope;
                try { envelope = JsonSerializer.Deserialize<StageSubmissionEnvelope>(blocks[i].Json, _ReadOptions); }
                catch (JsonException) { continue; }
                if (envelope == null) continue;
                string? arguments = ArgumentsJson(envelope);
                if (arguments == null) continue;
                StageSubmissionCheck check = StageSubmissionValidator.Check(envelope.Kind, persona, arguments, Constants.DefaultMaxHierarchyDepth);
                if (!check.Accepted) continue;
                id = blocks[i].Id;
                return check.Submission;
            }
            return null;
        }

        /// <summary>
        /// What the parsers read for a stage: the canonical rendering of the last valid submission in the output, or the
        /// output itself with any submission block removed.
        /// </summary>
        /// <param name="output">Stage output.</param>
        /// <param name="persona">Persona of the stage.</param>
        /// <returns>The text to parse.</returns>
        public static string? Effective(string? output, string? persona)
        {
            if (String.IsNullOrEmpty(output) || !output.Contains(BoundaryMarker, StringComparison.Ordinal)) return output;
            StageSubmissionEnvelope? envelope = Read(output, persona);
            if (envelope != null) return Render(envelope, persona);
            return WithoutBlocks(output);
        }

        /// <summary>
        /// What the parsers read for a mission: <see cref="Effective"/> of its output and persona.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <returns>The text to parse.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public static string? EffectiveOutput(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return Effective(mission.AgentOutput, mission.Persona);
        }

        /// <summary>
        /// The output with every submission block removed: the captain's own text.
        /// </summary>
        /// <param name="output">Stage output.</param>
        /// <returns>The text without blocks.</returns>
        public static string? WithoutBlocks(string? output)
        {
            if (String.IsNullOrEmpty(output) || !output.Contains(BoundaryMarker, StringComparison.Ordinal)) return output;
            List<StageSubmissionBlock> blocks = FindBlocks(output);
            if (blocks.Count == 0) return output;
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            HashSet<int> drop = new HashSet<int>();
            foreach (StageSubmissionBlock block in blocks)
                for (int i = block.StartLine; i <= block.EndLine; i++) drop.Add(i);
            List<string> kept = new List<string>();
            for (int i = 0; i < lines.Length; i++) if (!drop.Contains(i)) kept.Add(lines[i]);
            return String.Join("\n", kept).TrimEnd('\n', ' ');
        }

        /// <summary>
        /// The canonical text of a submission, in the form the existing parsers read: the plan in an armada-plan fence, or
        /// the persona's sections, findings, evidence and final verdict or result line.
        /// </summary>
        /// <param name="envelope">Validated submission.</param>
        /// <param name="persona">Persona of the stage.</param>
        /// <returns>Canonical text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when envelope is null.</exception>
        public static string Render(StageSubmissionEnvelope envelope, string? persona)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            StringBuilder text = new StringBuilder();
            switch (envelope.Kind)
            {
                case StageSubmissionKindEnum.Plan:
                    text.Append("```").Append(ArchitectPlanParser.FenceInfo).Append('\n')
                        .Append(JsonSerializer.Serialize(envelope.Plan, _WriteOptions)).Append('\n')
                        .Append("```\n");
                    break;
                case StageSubmissionKindEnum.Verdict:
                    RenderVerdict(envelope.Verdict!, text);
                    break;
                case StageSubmissionKindEnum.Result:
                    RenderResult(envelope.Result!, persona, text);
                    break;
            }
            return text.ToString();
        }

        /// <summary>
        /// The submit tool arguments of a submission (the plan, verdict or result JSON), or null when the part its kind
        /// names is missing.
        /// </summary>
        /// <param name="envelope">Submission.</param>
        /// <returns>Arguments JSON, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when envelope is null.</exception>
        public static string? ArgumentsJson(StageSubmissionEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            switch (envelope.Kind)
            {
                case StageSubmissionKindEnum.Plan: return envelope.Plan == null ? null : JsonSerializer.Serialize(envelope.Plan, _WriteOptions);
                case StageSubmissionKindEnum.Verdict: return envelope.Verdict == null ? null : JsonSerializer.Serialize(envelope.Verdict, _WriteOptions);
                case StageSubmissionKindEnum.Result: return envelope.Result == null ? null : JsonSerializer.Serialize(envelope.Result, _WriteOptions);
                default: return null;
            }
        }

        /// <summary>
        /// The structured findings of a submission as handoff findings, numbered from 1, or null when the submission is
        /// a plan.
        /// </summary>
        /// <param name="envelope">Submission.</param>
        /// <returns>Findings, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when envelope is null.</exception>
        public static List<HandoffFinding>? HandoffFindings(StageSubmissionEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            List<StageFinding>? findings = envelope.Kind == StageSubmissionKindEnum.Verdict ? envelope.Verdict?.Findings
                : envelope.Kind == StageSubmissionKindEnum.Result ? envelope.Result?.Findings
                : null;
            if (findings == null) return null;
            List<HandoffFinding> result = new List<HandoffFinding>();
            foreach (StageFinding finding in findings)
            {
                result.Add(new HandoffFinding
                {
                    Number = result.Count + 1,
                    File = String.IsNullOrWhiteSpace(finding.File) ? null : finding.File!.Trim(),
                    Line = finding.Line,
                    Severity = String.Equals(finding.Severity, StageFinding.SeverityNonBlocking, StringComparison.Ordinal)
                        ? HandoffFindingSeverityEnum.NonBlocking
                        : HandoffFindingSeverityEnum.Blocking,
                    Summary = finding.Scenario.Trim(),
                    RequiredFix = String.IsNullOrWhiteSpace(finding.RequiredFix) ? null : finding.RequiredFix!.Trim()
                });
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private static void RenderVerdict(StageVerdictSubmission verdict, StringBuilder text)
        {
            Section(text, JudgeContract.LensCorrectness, verdict.Correctness);
            Section(text, JudgeContract.LensBlastRadius, verdict.BlastRadius);
            Section(text, JudgeContract.LensSourceFidelity, verdict.SourceFidelity);
            RenderFindings(verdict.Findings, text);
            bool blocking = verdict.Verdict == "FAIL" || verdict.Verdict == "NEEDS_REVISION";
            if (blocking) RenderAffectedCase(verdict.Findings, text);
            if (!String.IsNullOrWhiteSpace(verdict.SpecConflict)) Section(text, JudgeContract.SpecConflictSection, verdict.SpecConflict!);
            RenderEvidence(verdict.Evidence, text);
            text.Append("## Verdict\n");
            if (!String.IsNullOrWhiteSpace(verdict.Summary)) text.Append(verdict.Summary!.Trim()).Append('\n');
            text.Append(StageOutputContract.VerdictMarker).Append(' ').Append(verdict.Verdict).Append('\n');
        }

        private static void RenderResult(StageResultSubmission result, string? persona, StringBuilder text)
        {
            foreach (StageReportSection section in result.Sections) Section(text, section.Heading.Trim(), section.Body);
            RenderFindings(result.Findings, text);
            bool blocked = String.Equals(result.Status, StageResultSubmission.StatusBlocked, StringComparison.Ordinal);
            if (blocked) RenderAffectedCase(result.Findings, text);
            RenderEvidence(result.Evidence, text);
            if (blocked)
            {
                text.Append(result.Summary.Trim()).Append("\n\n");
                text.Append(VerifierContract.BlockedMarker).Append('\n');
                return;
            }
            text.Append(StageOutputContract.ResultCompleteLine).Append('\n');
            text.Append(result.Summary.Trim()).Append('\n');
        }

        private static void Section(StringBuilder text, string heading, string body)
        {
            text.Append("## ").Append(heading).Append('\n').Append((body ?? String.Empty).Trim()).Append("\n\n");
        }

        private static void RenderFindings(List<StageFinding> findings, StringBuilder text)
        {
            if (findings == null || findings.Count == 0) return;
            text.Append("## Findings\n");
            int number = 1;
            foreach (StageFinding finding in findings)
            {
                text.Append(number++).Append(". Severity: ").Append(finding.Severity);
                if (!String.IsNullOrWhiteSpace(finding.File)) text.Append(". File: ").Append(FileReference(finding));
                text.Append(". Scenario: ").Append(finding.Scenario.Trim());
                if (!String.IsNullOrWhiteSpace(finding.RequiredFix)) text.Append(" Required fix: ").Append(finding.RequiredFix!.Trim());
                text.Append('\n');
            }
            text.Append('\n');
        }

        private static void RenderAffectedCase(List<StageFinding> findings, StringBuilder text)
        {
            StageFinding? first = findings?.FirstOrDefault(f => String.Equals(f.Severity, StageFinding.SeverityBlocking, StringComparison.Ordinal));
            if (first == null) return;
            text.Append("## ").Append(JudgeContract.AffectedCaseSection).Append('\n');
            if (!String.IsNullOrWhiteSpace(first.File)) text.Append(JudgeContract.AffectedCaseFileField).Append(": ").Append(FileReference(first)).Append('\n');
            text.Append(JudgeContract.AffectedCaseScenarioField).Append(": ").Append(first.Scenario.Trim()).Append('\n');
            if (!String.IsNullOrWhiteSpace(first.RequiredFix)) text.Append("Required fix: ").Append(first.RequiredFix!.Trim()).Append('\n');
            text.Append('\n');
        }

        private static void RenderEvidence(StageEvidence? evidence, StringBuilder text)
        {
            if (evidence == null || (evidence.ChecksRun.Count == 0 && evidence.InspectedFiles.Count == 0)) return;
            text.Append("## Evidence\n");
            if (evidence.ChecksRun.Count > 0) text.Append("Checks run: ").Append(String.Join("; ", evidence.ChecksRun.Select(c => c.Trim()))).Append('\n');
            if (evidence.InspectedFiles.Count > 0) text.Append("Inspected files: ").Append(String.Join(", ", evidence.InspectedFiles.Select(f => f.Trim()))).Append('\n');
            text.Append('\n');
        }

        private static string FileReference(StageFinding finding)
        {
            string file = finding.File!.Trim();
            return finding.Line.HasValue ? file + ":" + finding.Line.Value : file;
        }

        private static List<StageSubmissionBlock> FindBlocks(string output)
        {
            List<StageSubmissionBlock> blocks = new List<StageSubmissionBlock>();
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith(BoundaryMarker + " ", StringComparison.Ordinal)) continue;
                if (i + 1 >= lines.Length || !String.Equals(lines[i + 1].Trim(), "```" + FenceInfo, StringComparison.Ordinal)) continue;
                int close = -1;
                for (int j = i + 2; j < lines.Length; j++)
                {
                    if (String.Equals(lines[j].Trim(), "```", StringComparison.Ordinal)) { close = j; break; }
                }
                if (close < 0) continue;
                string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                StageSubmissionBlock block = new StageSubmissionBlock
                {
                    StartLine = i,
                    EndLine = close,
                    Id = words.Length > 2 ? words[2] : null,
                    Json = String.Join("\n", lines, i + 2, close - i - 2)
                };
                blocks.Add(block);
                i = close;
            }
            return blocks;
        }

        #endregion
    }
}
