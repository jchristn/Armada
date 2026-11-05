namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Architect plan handling: cloning stage chains, applying plan dependencies, and parsing plan and legacy marker output.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Methods

        private async Task CloneDependentChainAsync(
            List<Mission> voyageMissions,
            Mission templateMission,
            Mission newDependency,
            string parsedTitle,
            string parsedDescription,
            CancellationToken token)
        {
            List<Mission> directDependents = voyageMissions
                .Where(m => m.DependsOnMissionId == templateMission.Id)
                .OrderBy(m => m.CreatedUtc)
                .ToList();

            foreach (Mission templateChild in directDependents)
            {
                string childPersona = PersonaCatalog.NormalizeName(templateChild.Persona);
                if (String.IsNullOrEmpty(childPersona)) childPersona = templateChild.Persona ?? PersonaCatalog.Worker;

                Mission clonedStage = new Mission(
                    parsedTitle + " [" + childPersona + "]",
                    parsedDescription);
                clonedStage.TenantId = templateChild.TenantId;
                clonedStage.UserId = templateChild.UserId;
                clonedStage.VoyageId = templateChild.VoyageId;
                clonedStage.VesselId = templateChild.VesselId;
                clonedStage.IsEvaluation = templateChild.IsEvaluation;
                clonedStage.WorkerEnvelope = templateChild.WorkerEnvelope;
                clonedStage.Persona = childPersona;
                clonedStage.DependsOnMissionId = newDependency.Id;
                clonedStage.RequiresReview = templateChild.RequiresReview;
                clonedStage.ReviewDenyAction = templateChild.ReviewDenyAction;
                clonedStage.BranchName = null;
                clonedStage = await _Database.Missions.CreateAsync(clonedStage, token).ConfigureAwait(false);
                _Logging.Info(_Header + "architect created chained stage " + clonedStage.Id +
                    " (" + clonedStage.Persona + ") depending on " + newDependency.Id);
                await CloneDependentChainAsync(voyageMissions, templateChild, clonedStage, parsedTitle, parsedDescription, token).ConfigureAwait(false);
            }
        }

        private async Task RetitleDependentChainAsync(
            List<Mission> voyageMissions,
            Mission dependency,
            string parsedTitle,
            string parsedDescription,
            CancellationToken token)
        {
            List<Mission> directDependents = voyageMissions
                .Where(m => m.DependsOnMissionId == dependency.Id)
                .OrderBy(m => m.CreatedUtc)
                .ToList();

            foreach (Mission dependent in directDependents)
            {
                string dependentPersona = PersonaCatalog.NormalizeName(dependent.Persona);
                if (String.IsNullOrEmpty(dependentPersona)) dependentPersona = dependent.Persona ?? PersonaCatalog.Worker;

                dependent.Persona = dependentPersona;
                dependent.Title = parsedTitle + " [" + dependentPersona + "]";
                dependent.Description = parsedDescription;
                dependent.BranchName = null;
                dependent.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(dependent, token).ConfigureAwait(false);
                await RetitleDependentChainAsync(voyageMissions, dependent, parsedTitle, parsedDescription, token).ConfigureAwait(false);
            }
        }

        private async Task ApplyArchitectMissionDependenciesAsync(
            Mission architectMission,
            List<ParsedArchitectMission> parsed,
            CancellationToken token)
        {
            if (architectMission == null) throw new ArgumentNullException(nameof(architectMission));
            if (String.IsNullOrEmpty(architectMission.VoyageId)) return;
            if (parsed == null || parsed.Count == 0) return;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(architectMission.VoyageId, token).ConfigureAwait(false);
            Dictionary<int, Mission> workerRootsByIndex = new Dictionary<int, Mission>();
            Dictionary<int, Mission> terminalStagesByIndex = new Dictionary<int, Mission>();
            Dictionary<string, Mission> terminalStagesByTitle = new Dictionary<string, Mission>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> indexByTerminalId = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < parsed.Count; i++)
            {
                string workerTitle = parsed[i].Title + " [Worker]";
                Mission? workerRoot = voyageMissions.FirstOrDefault(m =>
                    PersonaCatalog.Matches(m.Persona, PersonaCatalog.Worker) &&
                    String.Equals(m.Title, workerTitle, StringComparison.OrdinalIgnoreCase));
                if (workerRoot == null) continue;

                Mission terminalStage = FindTerminalPipelineStage(voyageMissions, workerRoot);
                workerRootsByIndex[i + 1] = workerRoot;
                terminalStagesByIndex[i + 1] = terminalStage;
                terminalStagesByTitle[parsed[i].Title] = terminalStage;
                indexByTerminalId[terminalStage.Id] = i + 1;
            }

            Dictionary<int, int> depthByIndex = new Dictionary<int, int>();
            for (int i = 0; i < parsed.Count; i++)
            {
                if (!workerRootsByIndex.TryGetValue(i + 1, out Mission? workerRoot)) continue;
                // Idempotent: only a slice still waiting on the Architect is changed, so a recovery pass never touches one
                // that already started (H11).
                bool waitingOnArchitect = workerRoot.Status == MissionStatusEnum.Pending && workerRoot.DependsOnMissionId == architectMission.Id;
                if (waitingOnArchitect) await ApplySliceOwnershipAsync(voyageMissions, workerRoot, parsed[i], token).ConfigureAwait(false);

                string? dependencyReference = parsed[i].DependsOnReference;
                int? dependencyIndex = parsed[i].DependsOnIndex;
                if (!dependencyIndex.HasValue && String.IsNullOrWhiteSpace(dependencyReference)) continue;
                if (!waitingOnArchitect) continue;

                // Structured plan: the parent is an index. Legacy marker format: resolve the "Parent:" or "Depends on:" text.
                int? parentIndex = null;
                if (dependencyIndex.HasValue)
                {
                    if (terminalStagesByIndex.ContainsKey(dependencyIndex.Value)) parentIndex = dependencyIndex.Value;
                    dependencyReference = "mission " + dependencyIndex.Value;
                }
                else
                {
                    Mission? resolved = ResolveArchitectDependencyTerminalStage(terminalStagesByIndex, terminalStagesByTitle, i + 1, dependencyReference!);
                    if (resolved != null && indexByTerminalId.TryGetValue(resolved.Id, out int found)) parentIndex = found;
                }
                if (!parentIndex.HasValue || !workerRootsByIndex.TryGetValue(parentIndex.Value, out Mission? parentRoot))
                {
                    _Logging.Warn(_Header + "could not resolve architect parent '" + dependencyReference +
                        "' for mission '" + parsed[i].Title + "' -- leaving it after the architect");
                    continue;
                }
                if (parentRoot.Id == workerRoot.Id || parentIndex.Value >= i + 1)
                {
                    _Logging.Warn(_Header + "ignoring architect parent '" + dependencyReference +
                        "' for mission '" + parsed[i].Title + "': a parent must be an earlier mission");
                    continue;
                }
                int depth = (depthByIndex.TryGetValue(parentIndex.Value, out int parentDepth) ? parentDepth : parentRoot.Depth) + 1;
                if (depth > _Settings.Concurrency.MaxHierarchyDepth)
                {
                    _Logging.Warn(_Header + "architect parent for mission '" + parsed[i].Title + "' would be " + depth
                        + " deep (maximum " + _Settings.Concurrency.MaxHierarchyDepth + ") -- it starts after the architect instead");
                    continue;
                }

                // D13/D16: the slice becomes a child of the parent slice's root. It starts once the parent's last stage
                // finishes (gate 2), so it no longer needs its own link to the Architect; the branch rule picks continue
                // for the first child and fork for later ones unless the plan says otherwise.
                workerRoot.ParentMissionId = parentRoot.Id;
                workerRoot.DependsOnMissionId = null;
                workerRoot.Depth = depth;
                workerRoot.BranchStrategy = parsed[i].BranchStrategy == "continue" ? MissionBranchStrategyEnum.ContinueParent
                    : parsed[i].BranchStrategy == "fork" ? MissionBranchStrategyEnum.ForkFromParent
                    : MissionBranchStrategyEnum.New;
                workerRoot.BranchName = null;
                workerRoot.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(workerRoot, token).ConfigureAwait(false);
                depthByIndex[i + 1] = depth;

                _Logging.Debug(_Header + "architect made worker mission " + workerRoot.Id + " a child of " + parentRoot.Id +
                    " from reference '" + dependencyReference + "'");
            }
        }

        // Ownership is copied onto every stage of the slice, so any stage's prompt can name the slice's paths.
        private async Task ApplySliceOwnershipAsync(List<Mission> voyageMissions, Mission workerRoot, ParsedArchitectMission slice, CancellationToken token)
        {
            string? owned = slice.OwnedPaths.Count == 0 ? null : JsonSerializer.Serialize(slice.OwnedPaths);
            string? reads = slice.ReadsPaths.Count == 0 ? null : JsonSerializer.Serialize(slice.ReadsPaths);
            HashSet<string> chain = new HashSet<string>(StringComparer.Ordinal);
            for (Mission? member = workerRoot; member != null && chain.Add(member.Id);
                member = voyageMissions.Where(m => m.DependsOnMissionId == member.Id).OrderBy(m => m.CreatedUtc).FirstOrDefault())
            {
                if (member.OwnedPathsJson == owned && member.ReadsPathsJson == reads) continue;
                member.OwnedPathsJson = owned;
                member.ReadsPathsJson = reads;
                member.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(member, token).ConfigureAwait(false);
            }
        }

        private static Mission FindTerminalPipelineStage(IEnumerable<Mission> voyageMissions, Mission root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            Mission current = root;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);

            while (!String.IsNullOrEmpty(current.Id) && visited.Add(current.Id))
            {
                Mission? next = voyageMissions
                    .Where(m => m.DependsOnMissionId == current.Id)
                    .OrderBy(m => m.CreatedUtc)
                    .FirstOrDefault();
                if (next == null) break;
                current = next;
            }

            return current;
        }

        private async Task<bool> HasDependentPipelineStages(string? voyageId, string missionId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(voyageId)) return false;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            return voyageMissions.Any(m => m.DependsOnMissionId == missionId);
        }

        /// <summary>
        /// Parse mission definitions from an architect's output. The structured armada-plan JSON block
        /// (<see cref="ArchitectPlanParser"/>) is preferred: its dependsOn and waitForOtherMissions fields drive
        /// sequencing. Only when the output has no valid plan block does this fall back to the legacy formats:
        /// [ARMADA:MISSION] markers (with a "Depends on:" line) and then numbered summary lines, read from the agent
        /// output, the diff snapshot, or the description.
        /// </summary>
        private async Task<string> BuildArchitectSliceContextAsync(Mission architectMission, List<ParsedArchitectMission> parsed, int index, CancellationToken token)
        {
            // Each Worker gets one slice of the plan; give it the overall objective and the shape of the whole plan so
            // it neither drifts from the goal nor redoes a sibling's part.
            StringBuilder sb = new StringBuilder();
            sb.Append("\n\n## Full plan reference\nArchitect mission: ").Append(architectMission.Id)
                .Append(". Retrieve its complete output with mission_status or GET /api/v1/missions/")
                .Append(architectMission.Id).Append(" when relevant context is missing; do only your assigned slice.\n");
            Voyage? voyage = String.IsNullOrEmpty(architectMission.VoyageId) ? null : await _Database.Voyages.ReadAsync(architectMission.VoyageId!, token).ConfigureAwait(false);
            string objective = voyage != null && !String.IsNullOrWhiteSpace(voyage.Description) ? voyage.Description! : (voyage?.Title ?? String.Empty);
            if (!String.IsNullOrWhiteSpace(objective))
            {
                const int maxObjective = 2000;
                string trimmed = objective.Trim();
                if (trimmed.Length > maxObjective) trimmed = trimmed.Substring(0, maxObjective) + "\n(truncated)";
                sb.Append("\n\n## Overall objective (context only; do only your part)\n").Append(trimmed);
            }

            if (parsed.Count > 1)
            {
                sb.Append("\n\n## The Architect's plan (your part is marked; other parts are done by other missions)\n");
                for (int i = 0; i < parsed.Count; i++)
                    sb.Append(i + 1).Append(". ").Append(parsed[i].Title).Append(i == index ? "  <- this mission" : String.Empty).Append('\n');
            }

            return sb.ToString();
        }

        private List<ParsedArchitectMission> ParseArchitectOutput(Mission architectMission)
        {
            List<ParsedArchitectMission> results = new List<ParsedArchitectMission>();
            HashSet<string> seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (ArchitectPlanParser.TryParse(architectMission.AgentOutput, out ArchitectPlan? plan, out _, _Settings.Concurrency.MaxHierarchyDepth) && plan != null)
            {
                foreach (string warning in plan.Warnings)
                    _Logging.Warn(_Header + "architect plan of " + architectMission.Id + ": " + warning);
                foreach (ArchitectPlanMission planned in plan.Missions)
                {
                    ParsedArchitectMission parsed = new ParsedArchitectMission();
                    parsed.Title = planned.Title ?? String.Empty;
                    parsed.Description = ArchitectPlanParser.DescribeMission(plan, planned);
                    parsed.DependsOnIndex = planned.Parent ?? planned.DependsOn;
                    parsed.WaitForOtherMissions = planned.WaitForOtherMissions;
                    parsed.WorkerEnvelope = planned.WorkerEnvelope;
                    parsed.OwnedPaths = (planned.OwnedPaths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                    parsed.ReadsPaths = (planned.ReadsPaths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                    parsed.BranchStrategy = planned.BranchStrategy;
                    results.Add(parsed);
                }

                return results;
            }

            // Only what the Architect produced counts: its output, then a plan it committed (the diff). Explicit
            // [ARMADA:MISSION] markers remain supported for customized templates. Numbered narration ("1. Read the
            // README") is never inferred into missions: a plan without a structured block gets a repair turn instead.
            string?[] candidateSources =
            {
                architectMission.AgentOutput,
                architectMission.DiffSnapshot
            };

            for (int i = 0; i < candidateSources.Length; i++)
            {
                string? candidateSource = candidateSources[i];
                if (String.IsNullOrWhiteSpace(candidateSource)) continue;

                string source = candidateSource.Replace("\r\n", "\n");
                ParseArchitectMissionMarkers(source, results, seenTitles);
                if (results.Count > 0) break;
            }

            return results;
        }

        private void ParseArchitectMissionMarkers(
            string source,
            List<ParsedArchitectMission> results,
            HashSet<string> seenTitles)
        {
            if (String.IsNullOrWhiteSpace(source)) return;

            string[] segments = System.Text.RegularExpressions.Regex.Split(source, @"(?m)^\[ARMADA:MISSION\][ \t]*");

            for (int i = 1; i < segments.Length; i++)
            {
                string segment = segments[i].Trim();
                if (String.IsNullOrEmpty(segment)) continue;

                int closingTagIndex = segment.IndexOf("[/ARMADA:MISSION]", StringComparison.Ordinal);
                if (closingTagIndex >= 0)
                    segment = segment.Substring(0, closingTagIndex).Trim();

                if (String.IsNullOrEmpty(segment)) continue;

                int newlineIndex = segment.IndexOf('\n');
                string title;
                string description;

                if (newlineIndex >= 0)
                {
                    title = segment.Substring(0, newlineIndex).Trim();
                    description = segment.Substring(newlineIndex + 1).Trim();
                }
                else
                {
                    title = segment.Trim();
                    description = "";
                }

                TryAddParsedArchitectMission(results, seenTitles, title, description);
            }
        }

        private void TryAddParsedArchitectMission(
            List<ParsedArchitectMission> results,
            HashSet<string> seenTitles,
            string? title,
            string? description)
        {
            if (String.IsNullOrWhiteSpace(title)) return;

            string normalizedTitle = title.Trim();
            string normalizedDescription = NormalizeArchitectDescription(description);
            ArchitectDependencyExtractionResult dependencyExtraction = ExtractArchitectDependencyReference(normalizedDescription);
            normalizedDescription = ExtractOwnedPaths(dependencyExtraction.Description, out List<string> ownedPaths);
            string? dependencyReference = dependencyExtraction.DependsOnReference;
            if (String.IsNullOrWhiteSpace(normalizedDescription))
            {
                // Title-only architect blocks are still actionable; preserve the title as
                // the downstream mission description so worker/test/judge prompts are not empty.
                normalizedDescription = normalizedTitle;
            }

            if (IsArchitectPlaceholderTitle(normalizedTitle))
                return;

            if (IsArchitectPlaceholderDescription(normalizedDescription))
                return;

            if (seenTitles.Add(normalizedTitle))
            {
                ParsedArchitectMission parsed = new ParsedArchitectMission();
                parsed.Title = normalizedTitle;
                parsed.Description = normalizedDescription;
                parsed.DependsOnReference = dependencyReference;
                parsed.OwnedPaths = ownedPaths;
                parsed.WaitForOtherMissions = LegacyDescriptionRequestsDeferral(normalizedDescription);
                results.Add(parsed);
            }
        }

        private static string NormalizeArchitectDescription(string? description)
        {
            if (String.IsNullOrWhiteSpace(description)) return "";

            List<string> descriptionLines = description
                .Split('\n')
                .Select(l => l.Trim('\r'))
                .ToList();

            while (descriptionLines.Count > 0)
            {
                string firstLine = descriptionLines[0].Trim();
                if (IsAgentTelemetryLine(firstLine))
                {
                    descriptionLines.RemoveAt(0);
                    continue;
                }

                break;
            }

            while (descriptionLines.Count > 0)
            {
                string lastLine = descriptionLines[descriptionLines.Count - 1].Trim();
                if (IsAgentTelemetryLine(lastLine))
                {
                    descriptionLines.RemoveAt(descriptionLines.Count - 1);
                    continue;
                }

                break;
            }

            return String.Join("\n", descriptionLines).Trim();
        }

        private static ArchitectDependencyExtractionResult ExtractArchitectDependencyReference(string description)
        {
            if (String.IsNullOrWhiteSpace(description))
            {
                return new ArchitectDependencyExtractionResult();
            }

            List<string> keptLines = new List<string>();
            string? dependencyReference = null;

            foreach (string rawLine in description.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = rawLine.Trim();
                if (TryExtractArchitectDependency(trimmed, out string? dependencyCandidate, out string? remainingDescription))
                {
                    if (String.IsNullOrWhiteSpace(dependencyReference))
                    {
                        dependencyReference = NormalizeArchitectDependencyReference(dependencyCandidate ?? String.Empty);
                    }

                    if (!String.IsNullOrWhiteSpace(remainingDescription))
                    {
                        keptLines.Add(remainingDescription);
                    }

                    continue;
                }

                keptLines.Add(rawLine.TrimEnd('\r'));
            }

            return new ArchitectDependencyExtractionResult
            {
                Description = String.Join("\n", keptLines).Trim(),
                DependsOnReference = String.IsNullOrWhiteSpace(dependencyReference) ? null : dependencyReference
            };
        }

        // Legacy markers: an "Owns: a, b" line names the files the slice may change; it is removed from the description.
        private static string ExtractOwnedPaths(string description, out List<string> ownedPaths)
        {
            ownedPaths = new List<string>();
            if (String.IsNullOrWhiteSpace(description)) return description;
            List<string> kept = new List<string>();
            foreach (string rawLine in description.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("owns:", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string path in trimmed.Substring(5).Split(','))
                    {
                        string normalized = OwnedPathMatcher.Normalize(path);
                        if (normalized.Length > 0 && !ownedPaths.Contains(normalized)) ownedPaths.Add(normalized);
                    }
                    continue;
                }
                kept.Add(rawLine.TrimEnd('\r'));
            }
            return String.Join("\n", kept).Trim();
        }

        private static bool TryExtractArchitectDependency(string line, out string? dependencyReference, out string? remainingDescription)
        {
            dependencyReference = null;
            remainingDescription = null;

            if (String.IsNullOrWhiteSpace(line)) return false;

            string trimmed = line.Trim();
            // "Parent: <n>" (v1.3) and the older "Depends on: <n>" both name the slice this one builds on.
            string keyword = trimmed.StartsWith("parent", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 6 && (trimmed[6] == ':' || trimmed[6] == ' ')
                ? "parent"
                : trimmed.StartsWith("depends on", StringComparison.OrdinalIgnoreCase) ? "depends on" : String.Empty;
            if (keyword.Length == 0)
            {
                return false;
            }

            string remainder = trimmed.Substring(keyword.Length).TrimStart();
            if (remainder.StartsWith(":", StringComparison.Ordinal))
            {
                remainder = remainder.Substring(1).TrimStart();
            }

            if (String.IsNullOrWhiteSpace(remainder)) return false;

            int sentenceBoundary = remainder.IndexOf(". ", StringComparison.Ordinal);
            if (sentenceBoundary >= 0)
            {
                dependencyReference = remainder.Substring(0, sentenceBoundary).Trim().TrimEnd('.');
                remainingDescription = remainder.Substring(sentenceBoundary + 2).Trim();
            }
            else
            {
                dependencyReference = remainder.Trim().TrimEnd('.');
                remainingDescription = "";
            }

            return !String.IsNullOrWhiteSpace(dependencyReference);
        }

        private static string NormalizeArchitectDependencyReference(string dependencyReference)
        {
            if (String.IsNullOrWhiteSpace(dependencyReference)) return "";

            string normalized = dependencyReference.Trim().Trim('"', '\'', '`');
            int commentIndex = normalized.IndexOf(" (", StringComparison.Ordinal);
            if (commentIndex > 0)
            {
                normalized = normalized.Substring(0, commentIndex).Trim();
            }

            return normalized.Trim().TrimEnd('.', ';', ',');
        }

        private static Mission? ResolveArchitectDependencyTerminalStage(
            IReadOnlyDictionary<int, Mission> terminalStagesByIndex,
            IReadOnlyDictionary<string, Mission> terminalStagesByTitle,
            int currentMissionIndex,
            string dependencyReference)
        {
            string normalizedReference = NormalizeArchitectDependencyReference(dependencyReference);
            if (String.IsNullOrWhiteSpace(normalizedReference)) return null;

            System.Text.RegularExpressions.Match numericMatch =
                System.Text.RegularExpressions.Regex.Match(
                    normalizedReference,
                    @"^(?:mission\s+)?(?<index>\d+)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (numericMatch.Success &&
                Int32.TryParse(numericMatch.Groups["index"].Value, out int dependencyIndex) &&
                dependencyIndex > 0 &&
                dependencyIndex < currentMissionIndex &&
                terminalStagesByIndex.TryGetValue(dependencyIndex, out Mission? terminalStage))
            {
                return terminalStage;
            }

            if (terminalStagesByTitle.TryGetValue(normalizedReference, out Mission? byTitle))
            {
                return byTitle;
            }

            return null;
        }

        private static bool IsAgentTelemetryLine(string? line)
        {
            if (String.IsNullOrWhiteSpace(line)) return true;

            string trimmed = line.Trim();
            if (ProgressParser.TryParse(trimmed) != null) return true;

            return trimmed.Equals("tokens used", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\d,]+$");
        }

        private static bool IsArchitectPlaceholderTitle(string? title)
        {
            if (String.IsNullOrWhiteSpace(title)) return true;

            string trimmed = title.Trim();
            if (trimmed.Equals("...", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("goal:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("inputs:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("deliverables:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("dependencies:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("risks:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("done_when:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("status:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("reason:", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool IsArchitectPlaceholderDescription(string? description)
        {
            if (String.IsNullOrWhiteSpace(description)) return false;

            string[] lines = description
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !String.IsNullOrEmpty(l))
                .ToArray();

            if (lines.Length == 0) return false;

            string[] placeholderPrefixes =
            {
                "goal:",
                "inputs:",
                "deliverables:",
                "dependencies:",
                "risks:",
                "done_when:"
            };

            return lines.All(line =>
                placeholderPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                line.Contains("...", StringComparison.Ordinal));
        }

        #endregion
    }
}
