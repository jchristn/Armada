namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Checks submit tool arguments synchronously against the same contract the text parsers enforce
    /// (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2): a plan through <see cref="ArchitectPlanParser"/>, a verdict through the
    /// Judge's lens, affected-case and PASS rules, a result through the persona's required sections and the verifier
    /// block rule. Errors come back in order so the model can fix them in the same loop. Types are never coerced.
    /// </summary>
    public static class StageSubmissionValidator
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check submit tool arguments for a persona.
        /// </summary>
        /// <param name="kind">What is submitted.</param>
        /// <param name="persona">Persona of the submitting mission.</param>
        /// <param name="argumentsJson">Tool arguments as JSON.</param>
        /// <param name="maxHierarchyDepth">Deepest allowed plan parent chain.</param>
        /// <returns>The validated submission or the errors.</returns>
        public static StageSubmissionCheck Check(StageSubmissionKindEnum kind, string? persona, string? argumentsJson, int maxHierarchyDepth)
        {
            StageSubmissionCheck check = new StageSubmissionCheck();
            StageSubmissionKindEnum? expected = StageSubmissionTools.KindFor(persona);
            if (expected == null)
            {
                check.Errors.Add("The " + PersonaName(persona) + " persona has no output contract; it submits nothing.");
                return check;
            }
            if (expected.Value != kind)
            {
                check.Errors.Add(StageSubmissionTools.ToolFor(kind) + " is not for the " + PersonaName(persona) + " persona; call "
                    + StageSubmissionTools.ToolFor(expected.Value) + " instead.");
                return check;
            }
            if (String.IsNullOrWhiteSpace(argumentsJson))
            {
                check.Errors.Add("The arguments must be a JSON object.");
                return check;
            }

            StageSubmissionEnvelope envelope = new StageSubmissionEnvelope { Kind = kind };
            switch (kind)
            {
                case StageSubmissionKindEnum.Plan:
                    envelope.Plan = CheckPlan(argumentsJson!, maxHierarchyDepth, check.Errors);
                    break;
                case StageSubmissionKindEnum.Verdict:
                    envelope.Verdict = Read<StageVerdictSubmission>(argumentsJson!, check.Errors);
                    if (envelope.Verdict != null && CheckBounds(envelope.Verdict, check.Errors)) CheckVerdict(envelope, persona, check.Errors);
                    break;
                case StageSubmissionKindEnum.Result:
                    envelope.Result = Read<StageResultSubmission>(argumentsJson!, check.Errors);
                    if (envelope.Result != null && CheckBounds(envelope.Result, check.Errors)) CheckResult(envelope, persona, check.Errors);
                    break;
            }

            if (check.Errors.Count > StageSubmissionCheck.MaxErrors)
            {
                int more = check.Errors.Count - StageSubmissionCheck.MaxErrors;
                check.Errors = check.Errors.Take(StageSubmissionCheck.MaxErrors).ToList();
                check.Errors.Add(more + " more error(s) not shown.");
            }
            if (check.Errors.Count == 0) check.Submission = envelope;
            return check;
        }

        #endregion

        #region Private-Methods

        private static bool CheckBounds(object submission, List<string> errors)
        {
            try
            {
                CollectionBounds.Validate(submission);
                return true;
            }
            catch (CollectionLimitExceededException ex)
            {
                errors.Add(ex.Message);
                return false;
            }
        }

        private static ArchitectPlan? CheckPlan(string argumentsJson, int maxHierarchyDepth, List<string> errors)
        {
            ArchitectPlanParseResult parsed = ArchitectPlanParser.Parse("```" + ArchitectPlanParser.FenceInfo + "\n" + argumentsJson.Trim() + "\n```", maxHierarchyDepth);
            if (parsed.Status == ArchitectPlanParseStatusEnum.Parsed && parsed.Plan != null)
            {
                if (parsed.Plan.ContractVersion != StageOutputContract.PlanContractVersion)
                {
                    errors.Add("contractVersion must be " + StageOutputContract.PlanContractVersion + ".");
                    return null;
                }
                return parsed.Plan;
            }

            if (parsed.Errors.Count > 0) errors.AddRange(parsed.Errors);
            else errors.Add(parsed.Error ?? "The arguments are not a valid plan.");
            return null;
        }

        private static T? Read<T>(string argumentsJson, List<string> errors) where T : class
        {
            try
            {
                T? value = JsonSerializer.Deserialize<T>(argumentsJson, _Options);
                if (value == null) errors.Add("The arguments must be a JSON object.");
                return value;
            }
            catch (JsonException ex)
            {
                errors.Add(ArchitectPlanParser.DescribeJsonError(ex));
                return null;
            }
        }

        private static void CheckVerdict(StageSubmissionEnvelope envelope, string? persona, List<string> errors)
        {
            StageVerdictSubmission verdict = envelope.Verdict!;
            verdict.Findings ??= new List<StageFinding>();
            verdict.Evidence = Normalize(verdict.Evidence);
            List<string> accepted = StageOutputContract.AcceptedVerdicts.Select(StageOutputContract.VerdictToken).ToList();
            if (!accepted.Contains(verdict.Verdict, StringComparer.Ordinal))
                errors.Add("verdict must be one of " + String.Join(", ", accepted) + " (exact case); got \"" + verdict.Verdict + "\".");
            Required(verdict.Correctness, "correctness", errors);
            Required(verdict.BlastRadius, "blastRadius", errors);
            Required(verdict.SourceFidelity, "sourceFidelity", errors);
            CheckFindings(verdict.Findings, errors);
            CheckEvidence(verdict.Evidence, errors);
            if (errors.Count > 0) return;

            string rendered = StageSubmissionText.Render(envelope, persona);
            bool blocking = verdict.Verdict == "FAIL" || verdict.Verdict == "NEEDS_REVISION";
            if (blocking && !JudgeContract.ExhibitsAffectedCase(rendered))
                errors.Add("A " + verdict.Verdict + " verdict needs at least one blocking finding with a real file path or a concrete scenario of at least 20 characters.");
            if (verdict.Verdict == "SPEC_CONFLICT" && String.IsNullOrWhiteSpace(verdict.SpecConflict))
                errors.Add("A SPEC_CONFLICT verdict needs specConflict: the quoted acceptance criterion and the evidence it cannot be satisfied as written.");
            if (verdict.Verdict == "PASS")
            {
                if (verdict.Findings.Any(f => f.Severity == StageFinding.SeverityBlocking))
                    errors.Add("A PASS verdict cannot carry a blocking finding; use NEEDS_REVISION or FAIL, or mark the finding non_blocking.");
                string narrative = String.Join(" ", verdict.Correctness, verdict.BlastRadius, verdict.SourceFidelity, verdict.Summary ?? String.Empty);
                if (!JudgeContract.ValidatePass(rendered, narrative, out string? reason))
                    errors.Add((reason ?? "PASS is not substantiated") + " (in this tool: explain each lens in a sentence or more, and list evidence.checksRun or evidence.inspectedFiles).");
            }
        }

        private static void CheckResult(StageSubmissionEnvelope envelope, string? persona, List<string> errors)
        {
            StageResultSubmission result = envelope.Result!;
            result.Findings ??= new List<StageFinding>();
            result.Sections ??= new List<StageReportSection>();
            result.Evidence = Normalize(result.Evidence);
            bool blocked = String.Equals(result.Status, StageResultSubmission.StatusBlocked, StringComparison.Ordinal);
            if (!blocked && !String.Equals(result.Status, StageResultSubmission.StatusComplete, StringComparison.Ordinal))
                errors.Add("status must be " + StageResultSubmission.StatusComplete + " or " + StageResultSubmission.StatusBlocked + "; got \"" + result.Status + "\".");
            if (blocked && !VerifierContract.Applies(persona))
                errors.Add("Only a Test Engineer, Linter or Usability Engineer can submit status blocked.");
            Required(result.Summary, "summary", errors);

            for (int i = 0; i < result.Sections.Count; i++)
            {
                StageReportSection section = result.Sections[i];
                if (section == null) { errors.Add("sections[" + i + "] must be an object."); continue; }
                Required(section.Heading, "sections[" + i + "].heading", errors);
                Required(section.Body, "sections[" + i + "].body", errors);
            }
            foreach (string requiredSection in StageOutputContract.RequiredSections(persona))
            {
                bool present = result.Sections.Any(s => s != null
                    && String.Equals(s.Heading?.Trim().TrimStart('#', ' '), requiredSection, StringComparison.OrdinalIgnoreCase)
                    && !String.IsNullOrWhiteSpace(s.Body));
                if (!present) errors.Add("sections is missing \"" + requiredSection + "\" (with text).");
            }
            CheckFindings(result.Findings, errors);
            CheckEvidence(result.Evidence, errors);
            if (errors.Count > 0 || !blocked) return;

            if (!JudgeContract.ExhibitsAffectedCase(StageSubmissionText.Render(envelope, persona)))
                errors.Add("A blocked result needs at least one blocking finding with a real file path or a concrete scenario of at least 20 characters.");
        }

        private static void CheckFindings(List<StageFinding> findings, List<string> errors)
        {
            for (int i = 0; i < findings.Count; i++)
            {
                StageFinding finding = findings[i];
                string at = "findings[" + i + "]";
                if (finding == null) { errors.Add(at + " must be an object."); continue; }
                if (finding.Severity != StageFinding.SeverityBlocking && finding.Severity != StageFinding.SeverityNonBlocking)
                    errors.Add(at + ".severity must be " + StageFinding.SeverityBlocking + " or " + StageFinding.SeverityNonBlocking + "; got \"" + finding.Severity + "\".");
                Required(finding.Scenario, at + ".scenario", errors);
                if (finding.Line.HasValue && finding.Line.Value < 1) errors.Add(at + ".line must be 1 or more, or null.");
                if (finding.Line.HasValue && String.IsNullOrWhiteSpace(finding.File)) errors.Add(at + ".line needs a file.");
            }
        }

        private static StageEvidence Normalize(StageEvidence? evidence)
        {
            StageEvidence normalized = evidence ?? new StageEvidence();
            normalized.ChecksRun ??= new List<string>();
            normalized.InspectedFiles ??= new List<string>();
            return normalized;
        }

        private static void CheckEvidence(StageEvidence evidence, List<string> errors)
        {
            if (evidence.ChecksRun.Any(String.IsNullOrWhiteSpace)) errors.Add("evidence.checksRun must not contain empty entries.");
            if (evidence.InspectedFiles.Any(String.IsNullOrWhiteSpace)) errors.Add("evidence.inspectedFiles must not contain empty entries.");
        }

        private static void Required(string? value, string field, List<string> errors)
        {
            if (String.IsNullOrWhiteSpace(value)) errors.Add(field + " is required.");
        }

        private static string PersonaName(string? persona)
        {
            return String.IsNullOrWhiteSpace(persona) ? PersonaCatalog.Worker : PersonaCatalog.NormalizeName(persona);
        }

        #endregion
    }
}
