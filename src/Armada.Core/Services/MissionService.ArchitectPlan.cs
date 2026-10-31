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
            }

            for (int i = 0; i < parsed.Count; i++)
            {
                string? dependencyReference = parsed[i].DependsOnReference;
                int? dependencyIndex = parsed[i].DependsOnIndex;
                if (!dependencyIndex.HasValue && String.IsNullOrWhiteSpace(dependencyReference)) continue;
                if (!workerRootsByIndex.TryGetValue(i + 1, out Mission? workerRoot)) continue;
                // Idempotent: only a slice still waiting on the Architect is rewired, so a recovery pass never touches one
                // that already started (H11).
                if (workerRoot.Status != MissionStatusEnum.Pending || workerRoot.DependsOnMissionId != architectMission.Id) continue;

                // Structured plan: the dependency is an index. Legacy marker format: resolve the "Depends on:" text.
                Mission? resolvedDependency;
                if (dependencyIndex.HasValue)
                {
                    terminalStagesByIndex.TryGetValue(dependencyIndex.Value, out resolvedDependency);
                    dependencyReference = "mission " + dependencyIndex.Value;
                }
                else
                {
                    resolvedDependency = ResolveArchitectDependencyTerminalStage(
                        terminalStagesByIndex,
                        terminalStagesByTitle,
                        i + 1,
                        dependencyReference!);
                }
                if (resolvedDependency == null)
                {
                    _Logging.Warn(_Header + "could not resolve architect dependency '" + dependencyReference +
                        "' for mission '" + parsed[i].Title + "' -- leaving dependency on architect");
                    continue;
                }

                if (resolvedDependency.Id == workerRoot.Id)
                {
                    _Logging.Warn(_Header + "ignoring self-referential architect dependency '" + dependencyReference +
                        "' for mission '" + parsed[i].Title + "'");
                    continue;
                }

                workerRoot.DependsOnMissionId = resolvedDependency.Id;
                workerRoot.BranchName = null;
                workerRoot.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(workerRoot, token).ConfigureAwait(false);

                _Logging.Debug(_Header + "architect sequenced worker mission " + workerRoot.Id +
                    " to depend on terminal stage " + resolvedDependency.Id +
                    " from reference '" + dependencyReference + "'");
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

            if (ArchitectPlanParser.TryParse(architectMission.AgentOutput, out ArchitectPlan? plan) && plan != null)
            {
                foreach (ArchitectPlanMission planned in plan.Missions)
                {
                    ParsedArchitectMission parsed = new ParsedArchitectMission();
                    parsed.Title = planned.Title ?? String.Empty;
                    parsed.Description = ArchitectPlanParser.DescribeMission(plan, planned);
                    parsed.DependsOnIndex = planned.DependsOn;
                    parsed.WaitForOtherMissions = planned.WaitForOtherMissions;
                    parsed.WorkerEnvelope = planned.WorkerEnvelope;
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
            normalizedDescription = dependencyExtraction.Description;
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

        private static bool TryExtractArchitectDependency(string line, out string? dependencyReference, out string? remainingDescription)
        {
            dependencyReference = null;
            remainingDescription = null;

            if (String.IsNullOrWhiteSpace(line)) return false;

            string trimmed = line.Trim();
            if (!trimmed.StartsWith("depends on", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string remainder = trimmed.Substring("depends on".Length).TrimStart();
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
