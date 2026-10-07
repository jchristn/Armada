namespace Armada.Tui.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Every route of the web dashboard (<c>src/Armada.Dashboard/src/App.tsx</c>), including its redirects, plus the
    /// TUI-only extensions. Screens not built yet render a placeholder naming the route and the workstream that builds
    /// it. Thread-safe (immutable after construction).
    /// </summary>
    public static class RouteTable
    {
        #region Public-Members

        /// <summary>
        /// The route shown for paths that match nothing.
        /// </summary>
        public static RouteDefinition NotFound { get; } = new RouteDefinition("/__not-found", "Not Found", "NotFoundScreen", "W1.12", null, null, true);

        /// <summary>
        /// All routes. Never null.
        /// </summary>
        public static IReadOnlyList<RouteDefinition> All { get; } = Build();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a route by its exact pattern.
        /// </summary>
        /// <param name="pattern">Pattern, for example <c>/missions/:id</c>.</param>
        /// <returns>The route, or null.</returns>
        public static RouteDefinition? Find(string pattern)
        {
            return All.FirstOrDefault(r => String.Equals(r.Pattern, pattern, StringComparison.Ordinal));
        }

        #endregion

        #region Private-Methods

        private static IReadOnlyList<RouteDefinition> Build()
        {
            List<RouteDefinition> r = new List<RouteDefinition>();

            HubDefinition dispatch = new HubDefinition("tab",
                new HubTab("dispatch", "Dispatch", "DispatchScreen", "W3.5"),
                new HubTab("backlog", "Backlog", "BacklogScreen", "W3.6"));
            HubDefinition vessels = new HubDefinition("tab",
                new HubTab("vessels", "Vessels", "VesselsScreen", "W4.1"),
                new HubTab("health", "Health", "VesselHealthScreen", "W4.3"),
                new HubTab("fleets", "Fleets", "FleetsScreen", "W4.5"),
                new HubTab("workspace", "Workspace", "WorkspaceScreen", "W4.6"));
            HubDefinition captains = new HubDefinition("tab",
                new HubTab("captains", "Captains", "CaptainsScreen", "W4.7"),
                new HubTab("docks", "Docks", "DocksScreen", "W4.8"));
            HubDefinition missions = new HubDefinition("tab",
                new HubTab("missions", "Missions", "MissionsScreen", "W3.8"),
                new HubTab("voyages", "Voyages", "VoyagesScreen", "W3.9"),
                new HubTab("merge-queue", "Merge Queue", "MergeQueueScreen", "W3.10"));
            HubDefinition activity = new HubDefinition("source",
                new HubTab("history", "All Activity", "ActivityScreen", "W7.1"),
                new HubTab("requests", "API Requests", "RequestHistoryScreen", "W7.2"),
                new HubTab("events", "Events", "EventsScreen", "W7.3"),
                new HubTab("signals", "Signals", "SignalsScreen", "W7.4"),
                new HubTab("tokens", "Token Usage", "TokenUsageScreen", "W7.5"));
            HubDefinition configuration = new HubDefinition("tab",
                new HubTab("workflow-profiles", "Workflow Profiles", "WorkflowProfilesScreen", "W6.1"),
                new HubTab("project-profiles", "Project Profiles", "ProjectProfilesScreen", "W6.2"),
                new HubTab("skills", "Skills", "SkillsScreen", "W6.3"),
                new HubTab("personas", "Personas", "PersonasScreen", "W6.4"),
                new HubTab("pipelines", "Pipelines", "PipelinesScreen", "W6.5"),
                new HubTab("prompts", "Prompts", "PromptTemplatesScreen", "W6.6"),
                new HubTab("playbooks", "Playbooks", "PlaybooksScreen", "W6.7"),
                new HubTab("endpoints", "Endpoints", "EndpointsScreen", "W6.8"),
                new HubTab("harbors", "Harbors", "HarborsScreen", "W6.9"),
                new HubTab("memory", "Memory", "MemoryScreen", "W6.10"));
            HubDefinition delivery = new HubDefinition("tab",
                new HubTab("deployments", "Deployments", "DeploymentsScreen", "W5.1"),
                new HubTab("environments", "Environments", "EnvironmentsScreen", "W5.2"),
                new HubTab("releases", "Releases", "ReleasesScreen", "W5.3"),
                new HubTab("incidents", "Incidents", "IncidentsScreen", "W5.4"),
                new HubTab("checks", "Checks", "ChecksScreen", "W5.5"),
                new HubTab("runbooks", "Runbooks", "RunbooksScreen", "W5.6"));
            HubDefinition fleetActions = new HubDefinition("tab",
                new HubTab("actions", "Actions", "FleetActionsScreen", "W3.7"),
                new HubTab("runs", "Runs", "FleetActionRunsScreen", "W3.7"));
            HubDefinition server = new HubDefinition("tab",
                new HubTab("server", "Server", "ServerSettingsScreen", "W7.7"),
                new HubTab("diagnostics", "Diagnostics", "DiagnosticsScreen", "W7.8"),
                new HubTab("tenants", "Tenants", "TenantsScreen", "W7.9", true, false),
                new HubTab("users", "Users", "UsersScreen", "W7.9", false, true),
                new HubTab("credentials", "Credentials", "CredentialsScreen", "W7.9", false, true));
            HubDefinition cliPermissions = new HubDefinition("tab",
                new HubTab("requests", "Requests", "CliPermissionRequestsScreen", "W3.3"),
                new HubTab("rules", "Rules", "CliPermissionRulesScreen", "W3.3"));

            r.Add(Screen("/", "Dashboard", "HomeScreen", "W3.1"));
            r.Add(Redirect("/dashboard", "/"));
            r.Add(Screen("/planning", "Planning", "PlanningScreen", "W3.4"));
            r.Add(Screen("/planning/:id", "Planning", "PlanningScreen", "W3.4"));
            r.Add(Hub("/dispatch", "Dispatch", dispatch));
            r.Add(Redirect("/backlog", "/dispatch?tab=backlog"));
            r.Add(Screen("/backlog/:id", "Backlog Item", "BacklogItemScreen", "W3.6"));
            r.Add(Screen("/objectives", "Backlog", "BacklogScreen", "W3.6"));
            r.Add(Screen("/objectives/:id", "Backlog Item", "BacklogItemScreen", "W3.6"));
            r.Add(Redirect("/fleets", "/vessels?tab=fleets"));
            r.Add(Screen("/fleets/:id", "Fleet", "FleetScreen", "W4.5"));
            r.Add(Hub("/vessels", "Vessels", vessels));
            r.Add(Screen("/vessels/import", "Import Repositories", "ImportWizard", "W4.2"));
            r.Add(Hub("/vessels/health", "Vessel Health", vessels, "health"));
            r.Add(Screen("/vessels/:id", "Vessel", "VesselScreen", "W4.4"));
            r.Add(Screen("/vessels/:id/onboarding", "Vessel Onboarding", "VesselOnboardingScreen", "W4.4"));
            r.Add(Screen("/vessels/:id/history", "Vessel History", "VesselHistoryScreen", "W4.4"));
            r.Add(Redirect("/workspace", "/vessels?tab=workspace"));
            r.Add(Screen("/workspace/:vesselId", "Workspace", "WorkspaceScreen", "W4.6"));
            r.Add(Screen("/workspace/:vesselId/:panel", "Workspace", "WorkspaceScreen", "W4.6"));
            r.Add(Hub("/captains", "Captains", captains));
            r.Add(Screen("/captains/:id", "Captain", "CaptainScreen", "W4.7"));
            r.Add(Hub("/missions", "Missions", missions));
            r.Add(Screen("/missions/:id", "Mission", "MissionScreen", "W3.8"));
            r.Add(Redirect("/voyages", "/missions?tab=voyages"));
            r.Add(Screen("/voyages/create", "Create Voyage", "VoyageCreateScreen", "W3.9"));
            r.Add(Screen("/voyages/:id", "Voyage", "VoyageScreen", "W3.9"));
            r.Add(Hub("/activity", "Activity", activity));
            r.Add(Redirect("/signals", "/activity?source=signals"));
            r.Add(Redirect("/history", "/activity?source=history"));
            r.Add(Screen("/signals/:id", "Signal", "SignalScreen", "W7.4"));
            r.Add(Redirect("/events", "/activity?source=events"));
            r.Add(Screen("/events/:id", "Event", "EventScreen", "W7.3"));
            r.Add(Redirect("/docks", "/captains?tab=docks"));
            r.Add(Screen("/docks/:id", "Dock", "DockScreen", "W4.8"));
            r.Add(Redirect("/merge-queue", "/missions?tab=merge-queue"));
            r.Add(Screen("/merge-queue/:id", "Merge Entry", "MergeEntryScreen", "W3.10"));
            r.Add(Screen("/jobs", "Jobs", "JobsScreen", "W3.11"));
            r.Add(Hub("/configuration", "Configuration", configuration));
            r.Add(Redirect("/personas", "/configuration?tab=personas"));
            r.Add(Screen("/personas/:name", "Persona", "PersonaScreen", "W6.4"));
            r.Add(Redirect("/pipelines", "/configuration?tab=pipelines"));
            r.Add(Screen("/pipelines/:name", "Pipeline", "PipelineScreen", "W6.5"));
            r.Add(Redirect("/prompt-templates", "/configuration?tab=prompts"));
            r.Add(Screen("/prompt-templates/create", "Create Prompt Template", "PromptTemplateScreen", "W6.6"));
            r.Add(Screen("/prompt-templates/:name", "Prompt Template", "PromptTemplateScreen", "W6.6"));
            r.Add(Redirect("/playbooks", "/configuration?tab=playbooks"));
            r.Add(Screen("/playbooks/:id", "Playbook", "PlaybookScreen", "W6.7"));
            r.Add(Redirect("/workflow-profiles", "/configuration?tab=workflow-profiles"));
            r.Add(Screen("/workflow-profiles/:id", "Workflow Profile", "WorkflowProfileScreen", "W6.1"));
            r.Add(Redirect("/project-profiles", "/configuration?tab=project-profiles"));
            r.Add(Screen("/project-profiles/:id", "Project Profile", "ProjectProfileScreen", "W6.2"));
            r.Add(Redirect("/skills", "/configuration?tab=skills"));
            r.Add(Screen("/skills/:id", "Skill", "SkillScreen", "W6.3"));
            r.Add(Screen("/ask/:threadId?", "Ask Armada", "AskScreen", "W2"));
            r.Add(Screen("/inbox", "Needs You", "InboxScreen", "W3.2"));
            r.Add(Hub("/delivery", "Delivery", delivery));
            r.Add(Hub("/fleet-actions", "Fleet Actions", fleetActions));
            r.Add(Screen("/fleet-actions/runs/:id", "Fleet Action Run", "FleetActionRunScreen", "W3.7"));
            r.Add(Redirect("/checks", "/delivery?tab=checks"));
            r.Add(Screen("/checks/:id", "Check Run", "CheckRunScreen", "W5.5"));
            r.Add(Redirect("/environments", "/delivery?tab=environments"));
            r.Add(Screen("/environments/:id", "Environment", "EnvironmentScreen", "W5.2"));
            r.Add(Redirect("/deployments", "/delivery?tab=deployments"));
            r.Add(Screen("/deployments/:id", "Deployment", "DeploymentScreen", "W5.1"));
            r.Add(Redirect("/releases", "/delivery?tab=releases"));
            r.Add(Screen("/releases/new", "New Release", "ReleaseScreen", "W5.3"));
            r.Add(Screen("/releases/:id", "Release", "ReleaseScreen", "W5.3"));
            r.Add(Redirect("/incidents", "/delivery?tab=incidents"));
            r.Add(Screen("/incidents/:id", "Incident", "IncidentScreen", "W5.4"));
            r.Add(Redirect("/runbooks", "/delivery?tab=runbooks"));
            r.Add(Screen("/runbooks/:id", "Runbook", "RunbookScreen", "W5.6"));
            r.Add(Redirect("/requests", "/activity?source=requests"));
            r.Add(Screen("/requests/:id", "API Request", "RequestHistoryScreen", "W7.2"));
            r.Add(Screen("/api-explorer", "API Explorer", "ApiExplorerScreen", "W7.6"));
            r.Add(Screen("/api-explorer/:operationId", "API Explorer", "ApiExplorerScreen", "W7.6"));
            r.Add(Redirect("/notifications", "/inbox"));
            r.Add(Hub("/cli-permissions", "CLI Tool Permissions", cliPermissions));
            r.Add(Redirect("/admin/tenants", "/server?tab=tenants"));
            r.Add(Redirect("/admin/users", "/server?tab=users"));
            r.Add(Redirect("/admin/credentials", "/server?tab=credentials"));
            r.Add(Hub("/server", "Settings", server));
            r.Add(Redirect("/doctor", "/server?tab=diagnostics"));
            r.Add(Redirect("/settings", "/server"));

            // TUI extensions beyond the dashboard.
            r.Add(new RouteDefinition("/approvals", "Approvals", "ApprovalsScreen", "W3.3", null, null, true));
            r.Add(new RouteDefinition("/setup", "Setup Wizard", "SetupWizard", "W7.10", null, null, true));
            return r.AsReadOnly();
        }

        private static RouteDefinition Screen(string pattern, string title, string screen, string workstream)
        {
            return new RouteDefinition(pattern, title, screen, workstream);
        }

        private static RouteDefinition Redirect(string pattern, string target)
        {
            return new RouteDefinition(pattern, "", "", "", target);
        }

        private static RouteDefinition Hub(string pattern, string title, HubDefinition hub, string? fixedTab = null)
        {
            return new RouteDefinition(pattern, title, "HubScreen", "W1.2", null, hub, false, fixedTab);
        }

        #endregion
    }
}
