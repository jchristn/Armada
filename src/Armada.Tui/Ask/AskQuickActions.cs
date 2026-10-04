namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Armada.Core.Models;

    /// <summary>
    /// Quick-action rules shared with the dashboard (<c>lib/askQuickActions.ts</c>): the built-in catalog, merging the
    /// server catalog, which form an action opens, filtering for the <c>/</c> menu, and validating and building the MCP
    /// arguments for the Dispatch and Fleet action forms. Thread-safe (stateless).
    /// </summary>
    public static class AskQuickActions
    {
        #region Public-Members

        /// <summary>
        /// Maximum missions in the Dispatch form (25, like the dashboard).
        /// </summary>
        public const int MaxMissions = 25;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The five built-in quick actions, used when the server catalog is unavailable and to fill its gaps.
        /// </summary>
        /// <returns>New list.</returns>
        public static List<AskQuickAction> Defaults()
        {
            return new List<AskQuickAction>
            {
                Make("dispatch", "/dispatch", "dispatch", "Dispatch", "Start a voyage of one or more missions on a vessel"),
                Make("fleet-action", "/fleet-action", "run_fleet_action", "Fleet action", "Run a saved fleet action across selected vessels"),
                Make("status", "/status", "status", "Status", "Summarize all active work in Armada"),
                Make("health", "/health", "evaluate_vessel_health", "Health", "Evaluate the health of every active vessel"),
                Make("import", "/import", "", "Import", "Discover and import local repositories as vessels")
            };
        }

        /// <summary>
        /// Server catalog first (in its order) with missing built-ins appended; every entry gets a command, and the
        /// built-in's tool fills a missing tool name except for Import.
        /// </summary>
        /// <param name="server">Server catalog, or null.</param>
        /// <returns>Merged list.</returns>
        public static List<AskQuickAction> Merge(IEnumerable<AskQuickAction>? server)
        {
            List<AskQuickAction> defaults = Defaults();
            List<AskQuickAction> result = new List<AskQuickAction>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AskQuickAction action in server ?? Enumerable.Empty<AskQuickAction>())
            {
                if (action == null || String.IsNullOrEmpty(action.Name)) continue;
                string key = action.Name.ToLowerInvariant();
                if (!seen.Add(key)) continue;
                AskQuickAction? builtIn = defaults.FirstOrDefault(d => d.Name == key);
                if (String.IsNullOrEmpty(action.Title) && builtIn != null) action.Title = builtIn.Title;
                if (String.IsNullOrEmpty(action.Description) && builtIn != null) action.Description = builtIn.Description;
                action.Command = CommandOf(action);
                if (String.IsNullOrEmpty(action.ToolName) && builtIn != null) action.ToolName = builtIn.ToolName;
                result.Add(action);
            }

            foreach (AskQuickAction action in defaults)
            {
                if (!seen.Contains(action.Name)) result.Add(action);
            }

            return result;
        }

        /// <summary>
        /// The <c>/command</c> of an action.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>Command with a leading slash.</returns>
        public static string CommandOf(AskQuickAction action)
        {
            string raw = String.IsNullOrEmpty(action.Command) ? "/" + action.Name : action.Command;
            return raw.StartsWith("/", StringComparison.Ordinal) ? raw : "/" + raw;
        }

        /// <summary>
        /// The form an action opens.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>Form.</returns>
        public static AskQuickActionFormEnum FormFor(AskQuickAction action)
        {
            string name = (action.Name ?? "").ToLowerInvariant();
            if (name == "dispatch") return AskQuickActionFormEnum.Dispatch;
            if (name == "fleet-action" || name == "fleetaction" || name == "fleet_action") return AskQuickActionFormEnum.FleetAction;
            if (name == "import") return AskQuickActionFormEnum.Import;
            return AskQuickActionFormEnum.None;
        }

        /// <summary>
        /// Actions matching what the user typed after <c>/</c> (empty unless the input is a single slash word).
        /// </summary>
        /// <param name="actions">Catalog.</param>
        /// <param name="input">Composer text.</param>
        /// <returns>Matches.</returns>
        public static List<AskQuickAction> Filter(IEnumerable<AskQuickAction> actions, string input)
        {
            if (String.IsNullOrEmpty(input) || !input.StartsWith("/", StringComparison.Ordinal)) return new List<AskQuickAction>();
            if (input.IndexOfAny(new char[] { ' ', '\n', '\t' }) >= 0) return new List<AskQuickAction>();
            string typed = input.Substring(1).ToLowerInvariant();
            return actions.Where(a => CommandOf(a).Substring(1).ToLowerInvariant().StartsWith(typed, StringComparison.Ordinal)
                || (a.Name ?? "").ToLowerInvariant().StartsWith(typed, StringComparison.Ordinal)).ToList();
        }

        /// <summary>
        /// Validate a Dispatch draft; returns English errors keyed by field (<c>vessel</c>, <c>missions</c>).
        /// </summary>
        /// <param name="draft">Draft.</param>
        /// <returns>Errors (empty when valid).</returns>
        public static Dictionary<string, string> ValidateDispatch(AskDispatchDraft draft)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (String.IsNullOrEmpty(draft.VesselId)) errors["vessel"] = "Choose a vessel.";
            List<AskDispatchMissionDraft> missions = draft.Missions.Where(m => m.Title.Trim().Length > 0 || m.Description.Trim().Length > 0).ToList();
            if (missions.Count == 0) errors["missions"] = "Add at least one mission.";
            else if (missions.Any(m => m.Title.Trim().Length == 0)) errors["missions"] = "Every mission needs a title.";
            return errors;
        }

        /// <summary>
        /// MCP <c>dispatch</c> arguments: <c>{ title, vesselId, missions: [{ title, description }], pipelineId? }</c>.
        /// </summary>
        /// <param name="draft">Draft.</param>
        /// <returns>Arguments.</returns>
        public static JsonObject BuildDispatchArguments(AskDispatchDraft draft)
        {
            JsonArray missions = new JsonArray();
            string firstTitle = "";
            foreach (AskDispatchMissionDraft m in draft.Missions)
            {
                string title = m.Title.Trim();
                string description = m.Description.Trim();
                if (title.Length == 0 && description.Length == 0) continue;
                if (firstTitle.Length == 0) firstTitle = title;
                JsonObject item = new JsonObject();
                item["title"] = title;
                item["description"] = description.Length > 0 ? description : title;
                missions.Add(item);
            }

            JsonObject args = new JsonObject();
            string voyageTitle = draft.Title.Trim();
            args["title"] = voyageTitle.Length > 0 ? voyageTitle : firstTitle;
            args["vesselId"] = draft.VesselId;
            args["missions"] = missions;
            if (!String.IsNullOrEmpty(draft.PipelineId)) args["pipelineId"] = draft.PipelineId;
            return args;
        }

        /// <summary>
        /// Validate a Fleet action draft; returns English errors keyed by field (<c>action</c>, <c>vessels</c>).
        /// </summary>
        /// <param name="draft">Draft.</param>
        /// <returns>Errors (empty when valid).</returns>
        public static Dictionary<string, string> ValidateFleetAction(AskFleetActionDraft draft)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (String.IsNullOrEmpty(draft.ActionId)) errors["action"] = "Choose an action.";
            if (draft.VesselIds.Count == 0) errors["vessels"] = "Choose at least one vessel.";
            return errors;
        }

        /// <summary>
        /// MCP <c>run_fleet_action</c> arguments: <c>{ actionId, vesselIds }</c>.
        /// </summary>
        /// <param name="draft">Draft.</param>
        /// <returns>Arguments.</returns>
        public static JsonObject BuildFleetActionArguments(AskFleetActionDraft draft)
        {
            JsonArray ids = new JsonArray();
            foreach (string id in draft.VesselIds) ids.Add(id);
            JsonObject args = new JsonObject();
            args["actionId"] = draft.ActionId;
            args["vesselIds"] = ids;
            return args;
        }

        #endregion

        #region Private-Methods

        private static AskQuickAction Make(string name, string command, string tool, string title, string description)
        {
            AskQuickAction a = new AskQuickAction();
            a.Name = name;
            a.Command = command;
            a.ToolName = tool;
            a.Title = title;
            a.Description = description;
            return a;
        }

        #endregion
    }
}
