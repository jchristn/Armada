namespace Armada.Core.Services.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;

    /// <summary>
    /// Work linking by tool, in one mapping table: which work an executed action created (and should be tracked by the
    /// thread) or affected (and should be refreshed). dispatch links a Voyage; create_mission and restart_mission a
    /// Mission; run_fleet_action a FleetActionRun; evaluate_vessel_health a Job;
    /// import_vessels and discover_vessels a VesselImportBatch plus its Job when one runs in the background; cancel_voyage,
    /// cancel_mission, and cancel_fleet_action_run refresh the existing tracked item. get_blackbox (a read) links the
    /// BlackBox card of the entry it returned; the black box write tools (run_blackbox_recovery,
    /// apply_blackbox_recommendation, resolve_blackbox, annotate_blackbox) refresh it. A result carrying an Error links
    /// nothing.
    /// </summary>
    public static class AskWorkLinker
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly Regex _MissionIdPattern = new Regex(@"(?<![A-Za-z0-9_])" + Constants.MissionIdPrefix + @"[A-Za-z0-9][A-Za-z0-9_]{3,63}(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);

        /// <summary>
        /// Most mission ids one message links to black box cards.
        /// </summary>
        public const int MaxMentionedMissions = 5;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tools that create or affect trackable work.
        /// </summary>
        /// <returns>Tool names.</returns>
        public static IReadOnlyList<string> LinkedTools()
        {
            return new List<string>
            {
                "dispatch", "create_mission", "restart_mission", "run_fleet_action",
                "evaluate_vessel_health", "import_vessels", "discover_vessels", "cancel_voyage", "cancel_mission", "cancel_fleet_action_run",
                "get_blackbox", "run_blackbox_recovery", "apply_blackbox_recommendation", "resolve_blackbox", "annotate_blackbox"
            };
        }

        /// <summary>
        /// Read-only tools whose results link a card (they run without a proposal, so the gate links them itself).
        /// </summary>
        /// <returns>Tool names.</returns>
        public static IReadOnlyList<string> LinkedReadTools()
        {
            return new List<string> { "get_blackbox" };
        }

        /// <summary>
        /// The distinct mission ids (msn_ prefix) a message mentions, in order of first mention, at most
        /// <see cref="MaxMentionedMissions"/>. Used to link the black box cards of mentioned missions.
        /// </summary>
        /// <param name="text">Message text, or null.</param>
        /// <returns>Mission ids.</returns>
        public static List<string> MentionedMissionIds(string? text)
        {
            List<string> ids = new List<string>();
            if (String.IsNullOrEmpty(text)) return ids;
            foreach (Match match in _MissionIdPattern.Matches(text))
            {
                if (ids.Contains(match.Value, StringComparer.Ordinal)) continue;
                ids.Add(match.Value);
                if (ids.Count >= MaxMentionedMissions) break;
            }

            return ids;
        }

        /// <summary>
        /// Resolve the work linked to an executed tool call.
        /// </summary>
        /// <param name="toolName">Tool name (an mcp__armada__ prefix is ignored).</param>
        /// <param name="argumentsJson">Tool arguments as JSON text, or null.</param>
        /// <param name="resultJson">Tool result as JSON text, or null.</param>
        /// <returns>Linked work; empty when the tool links nothing or failed.</returns>
        public static List<AskWorkLink> Resolve(string? toolName, string? argumentsJson, string? resultJson)
        {
            List<AskWorkLink> links = new List<AskWorkLink>();
            string tool = AskToolPolicy.NormalizeToolName(toolName);
            if (tool.Length == 0) return links;

            AskWorkLinkProbe args = Parse(argumentsJson);
            AskWorkLinkProbe result = Parse(resultJson);
            if (!String.IsNullOrWhiteSpace(result.Error)) return links;

            switch (tool)
            {
                case "dispatch":
                    Add(links, AskTrackedEntityTypeEnum.Voyage, FirstWithPrefix(Constants.VoyageIdPrefix, result.Id, result.VoyageId), false);
                    break;

                case "create_mission":
                case "restart_mission":
                    Add(links, AskTrackedEntityTypeEnum.Mission, FirstWithPrefix(Constants.MissionIdPrefix, result.Id, result.MissionId, args.MissionId), false);
                    break;

                case "run_fleet_action":
                    Add(links, AskTrackedEntityTypeEnum.FleetActionRun, FirstWithPrefix(Constants.FleetActionRunIdPrefix, result.RunId, result.Id), false);
                    break;

                case "evaluate_vessel_health":
                    Add(links, AskTrackedEntityTypeEnum.Job, FirstWithPrefix(Constants.JobIdPrefix, result.JobId, result.Id), false);
                    break;

                case "import_vessels":
                    Add(links, AskTrackedEntityTypeEnum.VesselImportBatch, FirstWithPrefix(Constants.VesselImportBatchIdPrefix, args.BatchId, result.BatchId, result.Batch?.Id, result.Id), false);
                    Add(links, AskTrackedEntityTypeEnum.Job, FirstWithPrefix(Constants.JobIdPrefix, result.JobId), false);
                    break;

                case "discover_vessels":
                    Add(links, AskTrackedEntityTypeEnum.VesselImportBatch, FirstWithPrefix(Constants.VesselImportBatchIdPrefix, result.Batch?.Id, result.BatchId, result.Id), false);
                    Add(links, AskTrackedEntityTypeEnum.Job, FirstWithPrefix(Constants.JobIdPrefix, result.JobId), false);
                    break;

                case "cancel_voyage":
                    Add(links, AskTrackedEntityTypeEnum.Voyage, FirstWithPrefix(Constants.VoyageIdPrefix, args.VoyageId, args.Id), true);
                    break;

                case "cancel_mission":
                    Add(links, AskTrackedEntityTypeEnum.Mission, FirstWithPrefix(Constants.MissionIdPrefix, args.MissionId, args.Id), true);
                    break;

                case "cancel_fleet_action_run":
                    Add(links, AskTrackedEntityTypeEnum.FleetActionRun, FirstWithPrefix(Constants.FleetActionRunIdPrefix, args.RunId, args.Id), true);
                    break;

                case "get_blackbox":
                    Add(links, AskTrackedEntityTypeEnum.BlackBox, FirstWithPrefix(Constants.BlackBoxEntryIdPrefix, result.EntryId, args.EntryId), false);
                    break;

                case "run_blackbox_recovery":
                case "apply_blackbox_recommendation":
                case "resolve_blackbox":
                case "annotate_blackbox":
                    Add(links, AskTrackedEntityTypeEnum.BlackBox, FirstWithPrefix(Constants.BlackBoxEntryIdPrefix, result.EntryId, args.EntryId, result.Id), true);
                    break;
            }

            return links;
        }

        #endregion

        #region Private-Methods

        private static AskWorkLinkProbe Parse(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new AskWorkLinkProbe();
            string trimmed = json.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return new AskWorkLinkProbe();
            try
            {
                return JsonSerializer.Deserialize<AskWorkLinkProbe>(json, _Options) ?? new AskWorkLinkProbe();
            }
            catch (JsonException)
            {
                return new AskWorkLinkProbe();
            }
        }

        private static string? FirstWithPrefix(string prefix, params string?[] candidates)
        {
            return candidates.FirstOrDefault(c => !String.IsNullOrWhiteSpace(c) && c!.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static void Add(List<AskWorkLink> links, AskTrackedEntityTypeEnum type, string? id, bool refreshOnly)
        {
            if (String.IsNullOrWhiteSpace(id)) return;
            if (links.Any(l => l.EntityType == type && String.Equals(l.EntityId, id, StringComparison.Ordinal))) return;
            links.Add(new AskWorkLink(type, id!, refreshOnly));
        }

        #endregion
    }
}
