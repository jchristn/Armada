namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Reads the structured plan an Architect captain emits as a fenced code block (backticks or tildes) whose info string is
    /// <c>armada-plan</c>:
    /// <code>
    /// ```armada-plan
    /// {"missions":[{"title":"...","description":"...","parent":null,"waitForOtherMissions":false}]}
    /// ```
    /// </code>
    /// The block is deserialized into <see cref="ArchitectPlan"/>. Versioned contracts are validated before fan-out.
    /// Output with no such block has no plan (the legacy <c>[ARMADA:MISSION]</c> markers were removed, D-027).
    /// </summary>
    public static class ArchitectPlanParser
    {
        #region Public-Members

        /// <summary>
        /// The fenced code block info string that marks an Architect plan.
        /// </summary>
        public const string FenceInfo = "armada-plan";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to read an Architect plan from output. The final plan block must be valid (an Architect that revises
        /// its plan emits the final one last). Missions without a title, with a placeholder title such as "&lt;title&gt;", and repeated titles are
        /// dropped; a
        /// <c>parent</c> that names a dropped mission is rejected; a <c>dependsOn</c> is rejected (removed, D-026).
        /// </summary>
        /// <param name="output">Architect output.</param>
        /// <param name="plan">The plan, when found.</param>
        /// <returns>True when a plan with at least one titled mission was found.</returns>
        public static bool TryParse(string? output, out ArchitectPlan? plan)
        {
            return TryParse(output, out plan, out _);
        }

        /// <summary>Parse a plan and report actionable contract errors. Invalid versioned plans never fall back to prose.</summary>
        public static bool TryParse(string? output, out ArchitectPlan? plan, out string? error)
        {
            return TryParse(output, out plan, out error, Constants.DefaultMaxHierarchyDepth);
        }

        /// <summary>
        /// Parse a plan and report actionable contract errors, rejecting a parent chain deeper than
        /// <paramref name="maxHierarchyDepth"/>. Non-fatal findings are returned in <see cref="ArchitectPlan.Warnings"/>.
        /// </summary>
        /// <param name="output">Architect output.</param>
        /// <param name="plan">The plan, when valid.</param>
        /// <param name="error">Why the plan was rejected, or null.</param>
        /// <param name="maxHierarchyDepth">Deepest allowed parent chain (Concurrency.MaxHierarchyDepth).</param>
        /// <returns>True when a valid plan was found.</returns>
        public static bool TryParse(string? output, out ArchitectPlan? plan, out string? error, int maxHierarchyDepth)
        {
            ArchitectPlanParseResult result = Parse(output, maxHierarchyDepth);
            plan = result.Plan;
            error = result.Error;
            return result.Status == ArchitectPlanParseStatusEnum.Parsed;
        }

        /// <summary>
        /// Read an Architect plan from the final <c>armada-plan</c> block (a backtick or tilde fence). The final block must be
        /// valid: an Architect that revises its plan emits the final one last, and a malformed final block never falls back
        /// to an earlier one. Validation stays strict; a value of the wrong type or a wrong-case <c>kind</c> is reported, not
        /// coerced, so the Architect fixes it in a repair turn.
        /// </summary>
        /// <param name="output">Architect output.</param>
        /// <param name="maxHierarchyDepth">Deepest allowed parent chain (Concurrency.MaxHierarchyDepth).</param>
        /// <returns>The status, the plan when valid, and the ordered, capped error list when not.</returns>
        public static ArchitectPlanParseResult Parse(string? output, int maxHierarchyDepth)
        {
            ArchitectPlanParseResult result = new ArchitectPlanParseResult();
            if (String.IsNullOrWhiteSpace(output)) return result;
            foreach (string block in ExtractPlanBlocks(output))
            {
                result = new ArchitectPlanParseResult();
                ArchitectPlan? candidate;
                try { candidate = JsonSerializer.Deserialize<ArchitectPlan>(block, _Options); }
                catch (JsonException ex)
                {
                    // A later malformed revision must not silently dispatch an older plan.
                    string detail = DescribeJsonError(ex);
                    result.Status = ArchitectPlanParseStatusEnum.InvalidJson;
                    result.Errors.Add(detail);
                    result.Error = "Architect plan JSON is invalid: " + detail + ". Emit a complete armada-plan block with numeric dependencies.";
                    continue;
                }
                try { CollectionBounds.Validate(candidate); }
                catch (CollectionLimitExceededException limit)
                {
                    // An oversized plan is rejected (never truncated): the Architect must split the work.
                    result.Status = ArchitectPlanParseStatusEnum.TooLarge;
                    result.Errors.Add(limit.Message);
                    result.Error = "Architect plan rejected: " + limit.Message + " Split the work into smaller plans.";
                    continue;
                }
                if (candidate?.ContractVersion != null)
                {
                    List<string> errors = ArchitectPlanParseResult.Cap(ValidateContract(candidate, maxHierarchyDepth));
                    if (errors.Count == 0)
                    {
                        result.Status = ArchitectPlanParseStatusEnum.Parsed;
                        result.Plan = candidate;
                        continue;
                    }

                    result.Status = ArchitectPlanParseStatusEnum.Rejected;
                    result.Errors = errors;
                    result.Error = "Architect plan contract rejected: " + String.Join("; ", errors)
                        + ". Remediation: correct the plan before dispatching Workers; retain objective requirement IDs and explicit failure-case evidence.";
                }
                else
                {
                    ArchitectPlan? normalized = Normalize(candidate, maxHierarchyDepth, out string? normalizeError);
                    if (normalized != null)
                    {
                        result.Status = ArchitectPlanParseStatusEnum.Parsed;
                        result.Plan = normalized;
                        continue;
                    }

                    result.Status = ArchitectPlanParseStatusEnum.Rejected;
                    result.Error = normalizeError ?? "Architect plan contains no valid missions.";
                    result.Errors.Add(result.Error);
                }
            }
            return result;
        }

        /// <summary>Render validated structured criteria into the persisted slice, so every downstream stage sees the same contract.</summary>
        public static string DescribeMission(ArchitectPlan plan, ArchitectPlanMission mission)
        {
            if (plan.ContractVersion != 2 && plan.ContractVersion != 3 && plan.ContractVersion != 4) return DescribeOwnership(new StringBuilder(mission.Description ?? mission.Title ?? String.Empty), mission).ToString();
            StringBuilder text = new StringBuilder(mission.Description);
            DescribeOwnership(text, mission);
            text.Append("\n\n## Resolved plan decisions\n");
            foreach (int index in mission.DecisionIndexes ?? Enumerable.Range(1, plan.Decisions.Count).ToList())
                text.Append("- D").Append(index).Append(": ").Append(plan.Decisions[index - 1]).Append('\n');
            text.Append("\n## Acceptance evidence contract\n");
            foreach (ArchitectAcceptanceCriterion criterion in mission.AcceptanceCriteria)
            {
                text.Append("- ").Append(criterion.Id).Append(" [").Append(String.Join(", ", criterion.RequirementIds))
                    .Append("]: ").Append(criterion.Scenario).Append("\n  Expected: ").Append(criterion.Expected)
                    .Append("\n  Verify: ").Append(criterion.Verification).Append('\n');
                if (plan.ContractVersion >= 3) text.Append("  Decisions checked: ").Append(criterion.DecisionIndexes!.Count == 0
                    ? "none (general check)" : String.Join(", ", criterion.DecisionIndexes.Select(index => "D" + index))).Append('\n');
                foreach (string negative in criterion.NegativeCases) text.Append("  Failure/boundary: ").Append(negative).Append('\n');
                if (criterion.NegativeCases.Count == 0) text.Append("  Negative cases not applicable: ").Append(criterion.NegativeCasesNotApplicableReason).Append('\n');
            }
            text.Append("\n## Required validation commands\n");
            foreach (string command in mission.ValidationCommands) text.Append("- ").Append(command).Append('\n');
            text.Append("For every acceptance ID and linked decision, report the named check, observed result, and unverified paths. "
                + "Attach request capture before the action and prove it observes requests. Preserve original command exit status; "
                + "test counts and inherited claims do not establish an omitted guarantee.\n");
            return text.ToString();
        }

        private static StringBuilder DescribeOwnership(StringBuilder text, ArchitectPlanMission mission)
        {
            if (mission.OwnedPaths != null && mission.OwnedPaths.Count > 0)
            {
                text.Append("\n\n## Owned paths (change only these)\n");
                foreach (string path in mission.OwnedPaths) text.Append("- ").Append(OwnedPathMatcher.Normalize(path)).Append('\n');
            }
            if (mission.ReadsPaths != null && mission.ReadsPaths.Count > 0)
            {
                text.Append("\n## Read-only context (do not change)\n");
                foreach (string path in mission.ReadsPaths) text.Append("- ").Append(OwnedPathMatcher.Normalize(path)).Append('\n');
            }
            return text;
        }

        // Hierarchy and ownership through the one validator (RS-4.4). Only contract v4 rejects an ownership overlap.
        private static void ValidateHierarchy(ArchitectPlan plan, int maxHierarchyDepth, List<string> errors)
        {
            PlanValidationResult result = MissionPlanValidator.Validate(ToNodes(plan), maxHierarchyDepth, plan.ContractVersion == 4);
            errors.AddRange(result.Errors.Select(e => e.Message));
            plan.Warnings.AddRange(result.Warnings.Select(w => w.Message));
            for (int i = 0; i < plan.Missions.Count; i++)
            {
                ArchitectPlanMission? mission = plan.Missions[i];
                if (mission == null) continue;
                if (result.IsValid)
                {
                    mission.Parent = result.ParentOf(i + 1);
                    mission.DependsOn = null;
                }
                if (plan.ContractVersion == 4 && (mission.OwnedPaths == null || mission.OwnedPaths.Count == 0) && mission.Kind == "implementation")
                    plan.Warnings.Add("mission " + (i + 1) + ": no ownedPaths; give every implementation mission the files it may change");
            }
        }

        private static List<PlanNode> ToNodes(ArchitectPlan plan)
        {
            // dependsOn was removed (D-026): it is passed through so the validator rejects it, naming parent.
            return plan.Missions.Select(m => m == null ? new PlanNode() : new PlanNode
            {
                Parent = m.Parent,
                DependsOn = m.DependsOn,
                BranchStrategy = m.BranchStrategy,
                OwnedPaths = (m.OwnedPaths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).ToList(),
                WaitsForAll = m.WaitForOtherMissions
            }).ToList();
        }

        private static List<string> ValidateContract(ArchitectPlan plan, int maxHierarchyDepth)
        {
            List<string> errors = new List<string>();
            if (plan.ContractVersion != 2 && plan.ContractVersion != 3 && plan.ContractVersion != 4) { errors.Add("unsupported contractVersion (expected 2, 3 or 4)"); return errors; }
            if (plan.Missions == null || plan.Missions.Count == 0) { errors.Add("missions are required"); return errors; }
            if (plan.Requirements == null || plan.Requirements.Count == 0 || plan.Requirements.Any(String.IsNullOrWhiteSpace))
                errors.Add("nonempty objective requirements are required");
            HashSet<string> requirements = new HashSet<string>(plan.Requirements ?? new List<string>(), StringComparer.Ordinal);
            if (requirements.Count != plan.Requirements?.Count) errors.Add("objective requirement IDs must be unique");
            if (plan.Decisions == null || plan.Decisions.Count == 0 || plan.Decisions.Any(String.IsNullOrWhiteSpace))
                errors.Add("resolved design decisions are required (including compatibility and explicit settings)");
            HashSet<int> checkedDecisions = new HashSet<int>();
            HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> unknownRequirements = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < plan.Missions.Count; i++)
            {
                ArchitectPlanMission? mission = plan.Missions[i];
                string prefix = "mission " + (i + 1) + ": ";
                if (mission == null) { errors.Add(prefix + "definition is null"); continue; }
                if (String.IsNullOrWhiteSpace(mission.Title) || IsPlaceholder(mission.Title) || !titles.Add(mission.Title.Trim())) errors.Add(prefix + "real unique title required");
                if (String.IsNullOrWhiteSpace(mission.Description)) errors.Add(prefix + "bounded description required");
                if (mission.Kind != "implementation" && mission.Kind != "investigation" && mission.Kind != "integration") errors.Add(prefix + "kind must be implementation, investigation or integration");
                if (mission.WaitForOtherMissions && i != plan.Missions.Count - 1) errors.Add(prefix + "only final integration may wait for all missions");
                if (mission.DecisionIndexes != null && (mission.DecisionIndexes.Any(index => index < 1 || index > (plan.Decisions?.Count ?? 0))
                    || mission.DecisionIndexes.Distinct().Count() != mission.DecisionIndexes.Count)) errors.Add(prefix + "decisionIndexes must be unique 1-based plan decision indexes");
                if (mission.ValidationCommands == null || mission.ValidationCommands.Count == 0 || mission.ValidationCommands.Any(String.IsNullOrWhiteSpace)) errors.Add(prefix + "validationCommands are required");
                if (mission.AcceptanceCriteria == null || mission.AcceptanceCriteria.Count == 0) { errors.Add(prefix + "acceptanceCriteria are required"); continue; }
                foreach (ArchitectAcceptanceCriterion? criterion in mission.AcceptanceCriteria)
                {
                    if (criterion == null) { errors.Add(prefix + "acceptance criterion is null"); continue; }
                    if (String.IsNullOrWhiteSpace(criterion.Id) || !ids.Add(criterion.Id)) errors.Add(prefix + "acceptance IDs must be nonempty and unique");
                    if (String.IsNullOrWhiteSpace(criterion.Scenario) || String.IsNullOrWhiteSpace(criterion.Expected) || String.IsNullOrWhiteSpace(criterion.Verification)) errors.Add(prefix + "each criterion needs scenario, expected and verification");
                    if (criterion.RequirementIds == null || criterion.RequirementIds.Count == 0) errors.Add(prefix + "each criterion must cover objective requirement IDs");
                    else foreach (string id in criterion.RequirementIds)
                    {
                        if (id == null || !requirements.Contains(id))
                        {
                            string referenced = JsonSerializer.Serialize(id);
                            if (unknownRequirements.Add(referenced))
                                errors.Add(prefix + "criterion " + criterion.Id + " references unknown objective requirement ID " + referenced
                                    + "; requirements contains IDs only (for example \"REQ-1\", not \"REQ-1: explanation\"); requirementIds must match exactly and explanations belong in descriptions");
                        }
                        else covered.Add(id);
                    }
                    if (plan.ContractVersion >= 3)
                    {
                        if (criterion.DecisionIndexes == null) errors.Add(prefix + "criterion " + criterion.Id + " requires decisionIndexes ([] for a general check)");
                        else
                        {
                            if (criterion.DecisionIndexes.Distinct().Count() != criterion.DecisionIndexes.Count)
                                errors.Add(prefix + "criterion " + criterion.Id + " decisionIndexes must be unique");
                            foreach (int index in criterion.DecisionIndexes)
                            {
                                if (index < 1 || index > (plan.Decisions?.Count ?? 0))
                                    errors.Add(prefix + "criterion " + criterion.Id + " decisionIndexes must name 1-based plan decisions");
                                else if (mission.DecisionIndexes != null && !mission.DecisionIndexes.Contains(index))
                                    errors.Add(prefix + "criterion " + criterion.Id + " references D" + index + " outside this slice's decisionIndexes");
                                else checkedDecisions.Add(index);
                            }
                        }
                    }
                    if (criterion.NegativeCases == null || criterion.NegativeCases.Any(String.IsNullOrWhiteSpace)
                        || (criterion.NegativeCases.Count == 0 && String.IsNullOrWhiteSpace(criterion.NegativeCasesNotApplicableReason)))
                        errors.Add(prefix + "negativeCases or a specific non-applicability reason required");
                }
            }
            ValidateHierarchy(plan, maxHierarchyDepth, errors);
            ArchitectPlanMission? final = plan.Missions[plan.Missions.Count - 1];
            if (final?.Kind != "integration" || !final.WaitForOtherMissions) errors.Add("last mission must be integration with waitForOtherMissions=true");
            foreach (string requirement in requirements.Where(r => !covered.Contains(r))) errors.Add("uncovered objective requirement " + requirement);
            if (plan.ContractVersion >= 3)
                for (int index = 1; index <= (plan.Decisions?.Count ?? 0); index++)
                    if (!checkedDecisions.Contains(index)) errors.Add("decision D" + index + " has no acceptance check; link it to a named verification before dispatch");
            return errors;
        }

        #endregion

        #region Private-Methods

        private static List<string> ExtractPlanBlocks(string output)
        {
            List<string> blocks = new List<string>();
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            StringBuilder? current = null;

            string? openFence = null;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (current == null)
                {
                    // The plan block may use a backtick or a tilde fence; it closes with the same fence.
                    if (ProtocolLines.IsFence(line)
                        && String.Equals(line.Substring(3).Trim(), FenceInfo, StringComparison.OrdinalIgnoreCase))
                    {
                        openFence = line.Substring(0, 3);
                        current = new StringBuilder();
                    }

                    continue;
                }

                if (line.StartsWith(openFence!, StringComparison.Ordinal))
                {
                    blocks.Add(current.ToString());
                    current = null;
                    continue;
                }

                current.AppendLine(rawLine);
            }

            return blocks;
        }

        /// <summary>
        /// Where and why JSON failed to read, so a repair turn or a submit tool can point at the exact value: the reason,
        /// the JSON path, and the line and position.
        /// </summary>
        /// <param name="ex">The exception.</param>
        /// <returns>The description.</returns>
        /// <exception cref="ArgumentNullException">Thrown when ex is null.</exception>
        public static string DescribeJsonError(JsonException ex)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            string reason = ex.Message;
            int cut = reason.IndexOf(" Path:", StringComparison.Ordinal);
            if (cut < 0) cut = reason.IndexOf(" LineNumber:", StringComparison.Ordinal);
            if (cut > 0) reason = reason.Substring(0, cut);
            reason = reason.TrimEnd('.', ' ');
            if (reason.StartsWith("The JSON value could not be converted", StringComparison.Ordinal))
                reason = "value has the wrong type (numbers and booleans must not be quoted)";
            StringBuilder text = new StringBuilder(reason);
            if (!String.IsNullOrEmpty(ex.Path)) text.Append(" at ").Append(ex.Path);
            if (ex.LineNumber.HasValue) text.Append(" (line ").Append(ex.LineNumber.Value + 1).Append(", position ").Append(ex.BytePositionInLine ?? 0).Append(')');
            return text.ToString();
        }

        private static bool IsPlaceholder(string title)
        {
            // The prompt's own example uses angle-bracket placeholders such as "<title>"; an echoed example is not a plan.
            string trimmed = title.Trim();
            return trimmed.StartsWith("<", StringComparison.Ordinal) && trimmed.EndsWith(">", StringComparison.Ordinal);
        }

        // Unversioned plans: untitled, placeholder and duplicate-title entries are dropped, but the hierarchy is checked by
        // the one validator against the Architect's own numbering first, so a bad link gets a repair turn (R12).
        private static ArchitectPlan? Normalize(ArchitectPlan? candidate, int maxHierarchyDepth, out string? error)
        {
            error = null;
            if (candidate == null || candidate.Missions == null) return null;

            // Map each original 1-based position to its position after unusable entries are dropped, so a parent keeps
            // naming the mission the Architect meant.
            Dictionary<int, int> positionMap = new Dictionary<int, int>();
            HashSet<string> seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ArchitectPlan normalized = new ArchitectPlan();
            List<ArchitectPlanMission?> kept = new List<ArchitectPlanMission?>();
            for (int i = 0; i < candidate.Missions.Count; i++)
            {
                ArchitectPlanMission? mission = candidate.Missions[i];
                bool usable = mission != null && !String.IsNullOrWhiteSpace(mission.Title) && !IsPlaceholder(mission.Title) && seenTitles.Add(mission.Title.Trim());
                kept.Add(usable ? mission : null);
                if (usable) positionMap[i + 1] = positionMap.Count + 1;
            }

            ArchitectPlan numbering = new ArchitectPlan { Missions = candidate.Missions };
            List<PlanNode> nodes = ToNodes(numbering);
            for (int i = 0; i < nodes.Count; i++)
                if (kept[i] != null && nodes[i].Parent is int parent && parent >= 1 && parent <= i && kept[parent - 1] == null)
                    nodes[i].UnresolvedParent = "mission " + parent + " (untitled, a placeholder, or a duplicate title)";
            for (int i = 0; i < nodes.Count; i++)
                if (kept[i] == null) nodes[i] = new PlanNode();
            PlanValidationResult result = MissionPlanValidator.Validate(nodes, maxHierarchyDepth, false);
            if (!result.IsValid)
            {
                error = "Architect plan rejected: " + result.Describe() + ". Remediation: give each mission an earlier mission's number as its parent, or none.";
                return null;
            }

            for (int i = 0; i < kept.Count; i++)
            {
                ArchitectPlanMission? mission = kept[i];
                if (mission == null) continue;
                ArchitectPlanMission copy = new ArchitectPlanMission();
                copy.Title = mission.Title!.Trim();
                copy.Description = String.IsNullOrWhiteSpace(mission.Description) ? copy.Title : mission.Description.Trim();
                copy.WaitForOtherMissions = mission.WaitForOtherMissions;
                copy.Kind = mission.Kind;
                copy.OwnedPaths = (mission.OwnedPaths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).ToList();
                copy.ReadsPaths = (mission.ReadsPaths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).ToList();
                if (result.ParentOf(i + 1) is int parent)
                {
                    copy.Parent = positionMap[parent];
                    copy.BranchStrategy = mission.BranchStrategy;
                }
                normalized.Missions.Add(copy);
            }

            normalized.Warnings.AddRange(result.Warnings.Select(w => w.Message));
            return normalized.Missions.Count > 0 ? normalized : null;
        }

        #endregion
    }
}
