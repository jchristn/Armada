namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Tui.Routing;

    /// <summary>
    /// Pre-fill handoffs between screens. The dashboard passes these as React Router state; the TUI carries the same
    /// fields in the route query so they survive Back and Forward the same way router state does, and deep links can
    /// use them. <c>from</c> names the source (the dashboard's <c>fromPlanning</c>, <c>fromWorkspace</c>,
    /// <c>fromIncident</c>, <c>fromObjective</c>, <c>fromSetupWizard</c>, <c>fromVessel</c> flags); the other keys match the dashboard's
    /// state fields. Dispatch reads <c>/dispatch?from=...&amp;vesselId=&amp;pipelineName=&amp;prompt=&amp;voyageTitle=&amp;
    /// objectiveId=&amp;playbooks=</c> (playbooks as JSON); Planning reads <c>/planning?from=...&amp;captainId=&amp;
    /// objectiveId=&amp;vesselId=&amp;fleetId=&amp;pipelineId=&amp;title=&amp;initialPrompt=</c>.
    /// </summary>
    public static class OpsHandoff
    {
        #region Public-Members

        /// <summary>
        /// Source: Planning.
        /// </summary>
        public const string FromPlanning = "planning";

        /// <summary>
        /// Source: Workspace.
        /// </summary>
        public const string FromWorkspace = "workspace";

        /// <summary>
        /// Source: Incident.
        /// </summary>
        public const string FromIncident = "incident";

        /// <summary>
        /// Source: Backlog item (objective).
        /// </summary>
        public const string FromObjective = "objective";

        /// <summary>
        /// Source: Setup Wizard.
        /// </summary>
        public const string FromSetupWizard = "setup";

        /// <summary>
        /// Source: a vessel (Vessels list or vessel detail); pre-selects the vessel only.
        /// </summary>
        public const string FromVessel = "vessel";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a Dispatch pre-fill route.
        /// </summary>
        /// <param name="from">Source.</param>
        /// <param name="vesselId">Vessel id, or null.</param>
        /// <param name="pipelineName">Pipeline name, or null.</param>
        /// <param name="prompt">Description, or null.</param>
        /// <param name="voyageTitle">Voyage title, or null.</param>
        /// <param name="objectiveId">Backlog item id, or null.</param>
        /// <param name="playbooks">Selected playbooks, or null.</param>
        /// <returns>Route path and query.</returns>
        public static string Dispatch(string from, string? vesselId, string? pipelineName, string? prompt, string? voyageTitle, string? objectiveId, List<SelectedPlaybook>? playbooks)
        {
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["from"] = from ?? "";
            Put(q, "vesselId", vesselId);
            Put(q, "pipelineName", pipelineName);
            Put(q, "prompt", prompt);
            Put(q, "voyageTitle", voyageTitle);
            Put(q, "objectiveId", objectiveId);
            if (playbooks != null && playbooks.Count > 0) q["playbooks"] = ArmadaJson.Serialize(playbooks);
            return "/dispatch" + RouteMatch.BuildQuery(q);
        }

        /// <summary>
        /// Build a Planning pre-fill route.
        /// </summary>
        /// <param name="from">Source, or null for a captain-only handoff.</param>
        /// <param name="captainId">Captain id, or null.</param>
        /// <param name="objectiveId">Backlog item id, or null.</param>
        /// <param name="vesselId">Vessel id, or null.</param>
        /// <param name="fleetId">Fleet id, or null.</param>
        /// <param name="pipelineId">Pipeline id, or null.</param>
        /// <param name="title">Title, or null.</param>
        /// <param name="initialPrompt">First message, or null.</param>
        /// <returns>Route path and query.</returns>
        public static string Planning(string? from, string? captainId, string? objectiveId, string? vesselId, string? fleetId, string? pipelineId, string? title, string? initialPrompt)
        {
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            Put(q, "from", from);
            Put(q, "captainId", captainId);
            Put(q, "objectiveId", objectiveId);
            Put(q, "vesselId", vesselId);
            Put(q, "fleetId", fleetId);
            Put(q, "pipelineId", pipelineId);
            Put(q, "title", title);
            Put(q, "initialPrompt", initialPrompt);
            return "/planning" + RouteMatch.BuildQuery(q);
        }

        /// <summary>
        /// A query value, or null when absent or empty.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="key">Key.</param>
        /// <returns>Value or null.</returns>
        public static string? Get(RouteMatch route, string key)
        {
            if (route == null) return null;
            return route.Query.TryGetValue(key, out string? v) && !String.IsNullOrEmpty(v) ? v : null;
        }

        /// <summary>
        /// The playbooks of a Dispatch handoff, or an empty list.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <returns>Playbooks.</returns>
        public static List<SelectedPlaybook> Playbooks(RouteMatch route)
        {
            string? json = Get(route, "playbooks");
            if (json == null) return new List<SelectedPlaybook>();
            try { return ArmadaJson.Deserialize<List<SelectedPlaybook>>(json) ?? new List<SelectedPlaybook>(); }
            catch (Exception) { return new List<SelectedPlaybook>(); }
        }

        #endregion

        #region Private-Methods

        private static void Put(Dictionary<string, string> q, string key, string? value)
        {
            if (!String.IsNullOrEmpty(value)) q[key] = value!;
        }

        #endregion
    }
}
