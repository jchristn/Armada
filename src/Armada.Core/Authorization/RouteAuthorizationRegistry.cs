namespace Armada.Core.Authorization
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The single source of truth for what every REST route requires: one explicit
    /// <see cref="AuthorizationRequirement"/> per HTTP method and route template. The Admiral checks it centrally before
    /// any route handler runs (matching the request to the template Watson routes it to), and route handlers check it
    /// again through <see cref="Services.Interfaces.IAuthorizationService"/>. A route registered without a declaration
    /// here is treated as <see cref="PermissionLevel.AdminOnly"/> (fail closed) and is reported by
    /// <c>AuthorizationCoverageSuite</c>, which fails when any registered route or MCP tool is undeclared.
    /// When adding a route, add its line here in the same change.
    /// </summary>
    public static class RouteAuthorizationRegistry
    {
        #region Private-Members

        private static readonly Dictionary<string, AuthorizationRequirement> _Requirements = new Dictionary<string, AuthorizationRequirement>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<RouteAuthorizationEntry> _Templates = new List<RouteAuthorizationEntry>();

        #endregion

        #region Constructors-and-Factories

        static RouteAuthorizationRegistry()
        {
            // Non-API documents served by the Admiral (OpenAPI document and Swagger UI)
            Add("GET", "/openapi.json", "ApiDocumentation", ResourceOperationEnum.Read, PermissionLevel.NoAuthRequired);
            Add("GET", "/swagger", "ApiDocumentation", ResourceOperationEnum.Read, PermissionLevel.NoAuthRequired);

            // Account (self-service password change)
            Add("PUT", "/api/v1/account/password", "User", ResourceOperationEnum.Update, PermissionLevel.Authenticated);

            // AskRoutes
            Add("POST", "/api/v1/captains/{id}/chat", "Captain", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/ask/threads/enumerate", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads", "AskThread", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/ask/threads/{id}", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/ask/threads/{id}", "AskThread", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/ask/threads/{id}", "AskThread", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads/{id}/messages/enumerate", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads/{id}/messages", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads/{id}/cancel", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads/{id}/summarize", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads/{id}/read", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads/{id}/actions", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads/{id}/proposals/{pid}/approve", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/ask/threads/{id}/proposals/{pid}/reject", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/ask/threads/{id}/work/{workId}", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/ask/quick-actions", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/ask/threads/{id}/cli-permission-policy", "AskThread", ResourceOperationEnum.Update, PermissionLevel.Authenticated);

            // CLI tool permissions (decisions and visibility are checked per request by CliPermissionAccess)
            Add("GET", "/api/v1/cli-permissions/requests", "CliPermission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/cli-permissions/requests/{id}", "CliPermission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/cli-permissions/requests/{id}/decide", "CliPermission", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/cli-permissions/rules", "CliPermission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/cli-permissions/rules", "CliPermission", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/cli-permissions/rules/{id}", "CliPermission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/cli-permissions/rules/{id}", "CliPermission", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/cli-permissions/rules/{id}", "CliPermission", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // AuthRoutes
            Add("POST", "/api/v1/authenticate", "Session", ResourceOperationEnum.Execute, PermissionLevel.NoAuthRequired);
            Add("GET", "/api/v1/whoami", "Session", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/tenants/lookup", "Session", ResourceOperationEnum.Read, PermissionLevel.NoAuthRequired);
            Add("POST", "/api/v1/onboarding", "User", ResourceOperationEnum.Execute, PermissionLevel.NoAuthRequired);

            // BackupRoutes
            Add("GET", "/api/v1/backup", "Backup", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            Add("POST", "/api/v1/restore", "Backup", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);

            // CaptainRoutes
            Add("GET", "/api/v1/captains", "Captain", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/captains/enumerate", "Captain", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/captains", "Captain", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/captains/{id}", "Captain", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/captains/{id}/tools", "Captain", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/captains/{id}", "Captain", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("PUT", "/api/v1/captains/{id}/cli-permission-policy", "Captain", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/captains/{id}/unquarantine", "Captain", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/captains/{id}/stop", "Captain", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/captains/stop-all", "Captain", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/captains/{id}/log", "Captain", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/captains/{id}", "Captain", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/captains/delete/multiple", "Captain", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // CheckRunRoutes
            Add("GET", "/api/v1/check-runs", "CheckRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/check-runs/enumerate", "CheckRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/check-runs", "CheckRun", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/check-runs/import", "CheckRun", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/check-runs/sync/github-actions", "CheckRun", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/check-runs/{id}", "CheckRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/check-runs/{id}/retry", "CheckRun", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/check-runs/{id}", "CheckRun", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // DeploymentRoutes
            Add("GET", "/api/v1/deployments", "Deployment", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/deployments/enumerate", "Deployment", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/deployments", "Deployment", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/deployments/{id}", "Deployment", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/deployments/{id}", "Deployment", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/deployments/{id}/approve", "Deployment", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/deployments/{id}/deny", "Deployment", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/deployments/{id}/verify", "Deployment", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/deployments/{id}/rollback", "Deployment", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/deployments/{id}", "Deployment", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // DockRoutes
            Add("GET", "/api/v1/docks", "Dock", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/docks/enumerate", "Dock", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/docks/{id}", "Dock", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/docks/{id}", "Dock", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/docks/{id}/purge", "Dock", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/docks/{id}/repair", "Dock", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/docks/{id}/unstick", "Dock", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/docks/delete/multiple", "Dock", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // EnvironmentRoutes
            Add("GET", "/api/v1/environments", "Environment", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/environments/enumerate", "Environment", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/environments", "Environment", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/environments/{id}", "Environment", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/environments/{id}", "Environment", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/environments/{id}", "Environment", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // EventRoutes
            Add("GET", "/api/v1/events", "Event", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/events/enumerate", "Event", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/events/{id}", "Event", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/events/{id}", "Event", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/events/delete/multiple", "Event", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // FleetActionRoutes
            Add("POST", "/api/v1/fleet-actions/enumerate", "FleetAction", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/fleet-actions", "FleetAction", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/fleet-actions/run", "FleetAction", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/fleet-actions/{id}", "FleetAction", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/fleet-actions/{id}", "FleetAction", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/fleet-actions/{id}", "FleetAction", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/fleet-actions/{id}/run", "FleetAction", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/fleet-action-runs/enumerate", "FleetActionRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/fleet-action-runs/{id}", "FleetActionRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/fleet-action-runs/{id}/targets/enumerate", "FleetActionRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/fleet-action-runs/{id}/targets/{targetId}", "FleetActionRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/fleet-action-runs/{id}/cancel", "FleetActionRun", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);

            // FleetRoutes
            Add("GET", "/api/v1/fleets", "Fleet", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/fleets/enumerate", "Fleet", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/fleets", "Fleet", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/fleets/{id}", "Fleet", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/fleets/{id}", "Fleet", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/fleets/{id}", "Fleet", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/fleets/delete/multiple", "Fleet", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // HarborRoutes
            Add("GET", "/api/v1/harbors", "Harbor", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/harbors", "Harbor", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/harbors/{id}", "Harbor", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/harbors/{id}", "Harbor", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/harbors/{id}", "Harbor", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/harbors/{id}/enable", "Harbor", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/harbors/{id}/disable", "Harbor", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/harbors/{id}/probe", "Harbor", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);

            // HistoryRoutes
            Add("GET", "/api/v1/history", "History", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/history/enumerate", "History", ResourceOperationEnum.Read, PermissionLevel.Authenticated);

            // InboxRoutes
            Add("GET", "/api/v1/inbox", "Inbox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);

            // IncidentRoutes
            Add("GET", "/api/v1/incidents", "Incident", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/incidents/enumerate", "Incident", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/incidents/{id}", "Incident", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/incidents", "Incident", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("PUT", "/api/v1/incidents/{id}", "Incident", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/incidents/{id}", "Incident", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // JobRoutes
            Add("GET", "/api/v1/jobs", "Job", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/jobs/{id}", "Job", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/jobs/{id}/cancel", "Job", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);

            // MemoryRoutes
            Add("GET", "/api/v1/memories", "Memory", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/memories", "Memory", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/memories/{id}", "Memory", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/memories/{id}", "Memory", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/memories/{id}", "Memory", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);

            // MergeQueueRoutes
            Add("GET", "/api/v1/merge-queue", "MergeQueue", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/merge-queue/enumerate", "MergeQueue", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/merge-queue", "MergeQueue", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/merge-queue/{id}", "MergeQueue", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/merge-queue/{id}", "MergeQueue", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/merge-queue/{id}/process", "MergeQueue", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/merge-queue/process", "MergeQueue", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/merge-queue/{id}/purge", "MergeQueue", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/merge-queue/purge", "MergeQueue", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // MissionRoutes
            Add("GET", "/api/v1/missions", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/missions/enumerate", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/missions/summaries", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/missions/summaries/enumerate", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/missions/history", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/missions", "Mission", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/missions/{id}", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/missions/{id}/github/pull-request", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/missions/{id}/landing-preview", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/missions/{id}/evaluate-autoland", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/missions/{id}", "Mission", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("PUT", "/api/v1/missions/{id}/status", "Mission", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/missions/{id}/review/approve", "Mission", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/missions/{id}/review/deny", "Mission", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/missions/{id}", "Mission", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/missions/{id}/purge", "Mission", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/missions/delete/multiple", "Mission", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/missions/{id}/restart", "Mission", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/missions/{id}/retry-landing", "Mission", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/missions/{id}/diff", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/missions/{id}/log", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/missions/{id}/instructions", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);

            // ModelEndpointRoutes
            Add("GET", "/api/v1/model-endpoints", "ModelEndpoint", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/model-endpoints", "ModelEndpoint", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/model-endpoints/{id}", "ModelEndpoint", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/model-endpoints/{id}", "ModelEndpoint", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/model-endpoints/{id}", "ModelEndpoint", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/model-endpoints/{id}/validate", "ModelEndpoint", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/model-endpoints/health-check", "ModelEndpoint", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);

            // ObjectiveRefinementRoutes
            Add("GET", "/api/v1/objectives/{id}/refinement-sessions", "ObjectiveRefinementSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/objectives/{id}/refinement-sessions", "ObjectiveRefinementSession", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/backlog/{id}/refinement-sessions", "ObjectiveRefinementSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/backlog/{id}/refinement-sessions", "ObjectiveRefinementSession", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/objective-refinement-sessions/{id}", "ObjectiveRefinementSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/objective-refinement-sessions/{id}/messages", "ObjectiveRefinementSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/objective-refinement-sessions/{id}/summarize", "ObjectiveRefinementSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/objective-refinement-sessions/{id}/apply", "ObjectiveRefinementSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/objective-refinement-sessions/{id}/stop", "ObjectiveRefinementSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/objective-refinement-sessions/{id}", "ObjectiveRefinementSession", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // ObjectiveRoutes
            Add("GET", "/api/v1/objectives", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/objectives/enumerate", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/objectives/reorder", "Objective", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/objectives/{id}", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/objectives", "Objective", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/objectives/import/github", "Objective", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("PUT", "/api/v1/objectives/{id}", "Objective", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/objectives/{id}", "Objective", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/backlog", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/backlog/enumerate", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/backlog/reorder", "Objective", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/backlog/{id}", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/backlog", "Objective", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("PUT", "/api/v1/backlog/{id}", "Objective", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/backlog/{id}", "Objective", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // PersonaRoutes
            Add("GET", "/api/v1/personas", "Persona", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/personas/enumerate", "Persona", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/personas/{name}", "Persona", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/personas", "Persona", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/personas/{name}", "Persona", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/personas/{name}", "Persona", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);

            // PipelineRoutes
            Add("GET", "/api/v1/pipelines", "Pipeline", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/pipelines/enumerate", "Pipeline", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/pipelines/{name}", "Pipeline", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/pipelines", "Pipeline", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/pipelines/{name}", "Pipeline", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/pipelines/{name}", "Pipeline", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);

            // PlanningSessionRoutes
            Add("GET", "/api/v1/planning-sessions", "PlanningSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/planning-sessions", "PlanningSession", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/planning-sessions/{id}", "PlanningSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/planning-sessions/{id}/messages", "PlanningSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/planning-sessions/{id}/dispatch", "PlanningSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/planning-sessions/{id}/summarize", "PlanningSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/planning-sessions/{id}/stop", "PlanningSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/planning-sessions/{id}/stop-turn", "PlanningSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/planning-sessions/{id}", "PlanningSession", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // PlaybookRoutes
            Add("GET", "/api/v1/playbooks", "Playbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/playbooks/enumerate", "Playbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/playbooks", "Playbook", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/playbooks/{id}", "Playbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/playbooks/{id}", "Playbook", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/playbooks/{id}", "Playbook", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // ProjectProfileRoutes
            Add("GET", "/api/v1/project-profiles", "ProjectProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/project-profiles/enumerate", "ProjectProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/project-profiles/validate", "ProjectProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/project-profiles/resolve/vessels/{vesselId}", "ProjectProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/project-profiles/{id}/persona-preview/{persona}", "ProjectProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/project-profiles", "ProjectProfile", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/project-profiles/{id}", "ProjectProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/project-profiles/{id}", "ProjectProfile", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/project-profiles/{id}", "ProjectProfile", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);

            // PromptTemplateRoutes
            Add("GET", "/api/v1/prompt-templates", "PromptTemplate", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/prompt-templates/enumerate", "PromptTemplate", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/prompt-templates", "PromptTemplate", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/prompt-templates/{name}", "PromptTemplate", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/prompt-templates/{name}", "PromptTemplate", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/prompt-templates/{name}/reset", "PromptTemplate", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);

            // ReleaseRoutes
            Add("GET", "/api/v1/releases", "Release", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/releases/enumerate", "Release", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/releases", "Release", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/releases/{id}", "Release", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/releases/{id}/github/pull-requests", "Release", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/releases/{id}", "Release", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/releases/{id}/refresh", "Release", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/releases/{id}", "Release", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // RequestHistoryRoutes
            Add("GET", "/api/v1/request-history", "RequestHistory", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/request-history/summary", "RequestHistory", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/request-history/{id}", "RequestHistory", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/request-history/{id}", "RequestHistory", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/request-history/delete/multiple", "RequestHistory", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/request-history/delete/by-filter", "RequestHistory", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // RunbookRoutes
            Add("GET", "/api/v1/runbooks", "Runbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/runbooks/enumerate", "Runbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/runbooks/{id}", "Runbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/runbooks", "Runbook", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("PUT", "/api/v1/runbooks/{id}", "Runbook", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/runbooks/{id}", "Runbook", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/runbook-executions", "RunbookExecution", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/runbook-executions/enumerate", "RunbookExecution", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/runbook-executions/{id}", "RunbookExecution", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/runbooks/{id}/executions", "RunbookExecution", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("PUT", "/api/v1/runbook-executions/{id}", "RunbookExecution", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/runbook-executions/{id}", "RunbookExecution", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // RuntimeRoutes
            Add("GET", "/api/v1/runtimes/mux/endpoints", "Runtime", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/runtimes/mux/endpoints/{name}", "Runtime", ResourceOperationEnum.Read, PermissionLevel.Authenticated);

            // SignalRoutes
            Add("GET", "/api/v1/signals", "Signal", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/signals/enumerate", "Signal", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/signals", "Signal", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/signals/recent", "Signal", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/signals/{id}", "Signal", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/signals/{id}/read", "Signal", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/signals/recipient/{captainId}", "Signal", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/signals/{id}", "Signal", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/signals/delete/multiple", "Signal", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // SkillRoutes
            Add("GET", "/api/v1/skills", "Skill", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/skills/enumerate", "Skill", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/skills", "Skill", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/skills/{id}", "Skill", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/skills/{id}", "Skill", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/skills/{id}", "Skill", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);

            // StatusRoutes
            Add("GET", "/api/v1/status", "Status", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/status/health", "Status", ResourceOperationEnum.Read, PermissionLevel.NoAuthRequired);
            Add("GET", "/api/v1/doctor", "Status", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/server/stop", "Server", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            Add("POST", "/api/v1/server/restart", "Server", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            Add("POST", "/api/v1/server/rebuild", "Server", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            Add("GET", "/api/v1/server/rebuild/status", "Server", ResourceOperationEnum.Read, PermissionLevel.AdminOnly);
            Add("POST", "/api/v1/server/rollback", "Server", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            Add("GET", "/api/v1/settings", "Settings", ResourceOperationEnum.Read, PermissionLevel.AdminOnly);
            Add("PUT", "/api/v1/settings", "Settings", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            Add("POST", "/api/v1/server/reset", "Server", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);

            // TenantRoutes
            Add("GET", "/api/v1/tenants", "Tenant", ResourceOperationEnum.Read, PermissionLevel.AdminOnly);
            Add("POST", "/api/v1/tenants", "Tenant", ResourceOperationEnum.Create, PermissionLevel.AdminOnly);
            Add("GET", "/api/v1/tenants/{id}", "Tenant", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/tenants/{id}", "Tenant", ResourceOperationEnum.Update, PermissionLevel.AdminOnly);
            Add("DELETE", "/api/v1/tenants/{id}", "Tenant", ResourceOperationEnum.Delete, PermissionLevel.AdminOnly);
            Add("GET", "/api/v1/users", "User", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/users", "User", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/users/{id}", "User", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/users/{id}", "User", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/users/{id}", "User", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/credentials", "Credential", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/credentials", "Credential", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/credentials/{id}", "Credential", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/credentials/{id}", "Credential", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/credentials/{id}", "Credential", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);

            // TokenUsageRoutes
            Add("GET", "/api/v1/token-usage/summary", "TokenUsage", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/token-usage", "TokenUsage", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/token-usage/delete/by-filter", "TokenUsage", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);

            // VesselHealthRoutes
            Add("POST", "/api/v1/vessel-health/enumerate", "VesselHealth", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/vessel-health/summary", "VesselHealth", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/vessel-health/evaluate", "VesselHealth", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/vessels/{id}/health", "VesselHealth", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/vessels/{id}/health/overrides/{criterion}", "VesselHealth", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/vessels/{id}/health/overrides/{criterion}", "VesselHealth", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // VesselImportRoutes
            Add("GET", "/api/v1/vessels/import/browse", "VesselImport", ResourceOperationEnum.Read, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/vessels/import/discover", "VesselImport", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/vessels/import", "VesselImport", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/vessels/import/batches/enumerate", "VesselImport", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/vessels/import/batches/{id}", "VesselImport", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/vessels/import/categorization/default-prompt", "VesselImport", ResourceOperationEnum.Read, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/vessels/import/batches/{id}/categorize", "VesselImport", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/vessels/import/batches/{id}/fleet-recommendations/apply", "VesselImport", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);

            // VesselRoutes
            Add("GET", "/api/v1/vessels", "Vessel", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/vessels/enumerate", "Vessel", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/vessels", "Vessel", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/vessels/{id}", "Vessel", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/vessels/{id}", "Vessel", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("PATCH", "/api/v1/vessels/{id}/context", "Vessel", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/vessels/{id}/git-status", "Vessel", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/vessels/{id}/branches", "Vessel", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/vessels/{id}/branches/push", "Vessel", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/vessels/{id}/branches/merge", "Vessel", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/vessels/{id}/readiness", "Vessel", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/vessels/{id}/landing-preview", "Vessel", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/vessels/{id}/build-context", "Vessel", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/vessels/{id}", "Vessel", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/vessels/delete/multiple", "Vessel", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // VoyageRoutes
            Add("GET", "/api/v1/voyages", "Voyage", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/voyages/enumerate", "Voyage", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/voyages", "Voyage", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/voyages/{id}", "Voyage", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/voyages/{id}", "Voyage", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/voyages/{id}/purge", "Voyage", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/voyages/delete/multiple", "Voyage", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // WorkflowProfileRoutes
            Add("GET", "/api/v1/workflow-profiles", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/workflow-profiles/enumerate", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/workflow-profiles/validate", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/workflow-profiles/preview/vessels/{vesselId}", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/workflow-profiles/resolve/vessels/{vesselId}", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/workflow-profiles", "WorkflowProfile", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("GET", "/api/v1/workflow-profiles/{id}", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/workflow-profiles/{id}", "WorkflowProfile", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("DELETE", "/api/v1/workflow-profiles/{id}", "WorkflowProfile", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);

            // WorkspaceRoutes
            Add("GET", "/api/v1/workspace/vessels/{vesselId}/tree", "Workspace", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/workspace/vessels/{vesselId}/diff", "Workspace", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/workspace/vessels/{vesselId}/file", "Workspace", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("PUT", "/api/v1/workspace/vessels/{vesselId}/file", "Workspace", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/workspace/vessels/{vesselId}/exec", "Workspace", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("POST", "/api/v1/workspace/vessels/{vesselId}/directory", "Workspace", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("POST", "/api/v1/workspace/vessels/{vesselId}/rename", "Workspace", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("DELETE", "/api/v1/workspace/vessels/{vesselId}/entry", "Workspace", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/workspace/vessels/{vesselId}/search", "Workspace", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/workspace/vessels/{vesselId}/changes", "Workspace", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("GET", "/api/v1/workspace/vessels/{vesselId}/status", "Workspace", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every declared route as "METHOD template" keys, in declaration order.
        /// </summary>
        /// <returns>Declared route keys.</returns>
        public static List<string> GetDeclaredRoutes()
        {
            List<string> keys = new List<string>();
            foreach (RouteAuthorizationEntry template in _Templates) keys.Add(template.Method + " " + template.Template);
            return keys;
        }

        /// <summary>
        /// Look up the requirement declared for an exact method and route template (as registered with Watson).
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="template">Route template, for example /api/v1/fleets/{id}.</param>
        /// <param name="requirement">The declared requirement, or null.</param>
        /// <returns>True when a requirement is declared.</returns>
        public static bool TryGetByTemplate(string method, string template, out AuthorizationRequirement? requirement)
        {
            requirement = null;
            if (String.IsNullOrEmpty(method) || String.IsNullOrEmpty(template)) return false;
            return _Requirements.TryGetValue(Key(method, template), out requirement);
        }

        /// <summary>
        /// Resolve a concrete request path (no query string) to the requirement of the declared template it matches.
        /// Literal segments win over parameters, so /api/v1/missions/history resolves to its own declaration rather
        /// than /api/v1/missions/{id}.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path.</param>
        /// <param name="requirement">The matched requirement, or null.</param>
        /// <param name="template">The matched template, or null.</param>
        /// <returns>True when a declared template matches.</returns>
        public static bool TryResolvePath(string method, string path, out AuthorizationRequirement? requirement, out string? template)
        {
            requirement = null;
            template = null;
            if (String.IsNullOrEmpty(method) || String.IsNullOrEmpty(path)) return false;

            string[] segments = SplitPath(path);
            RouteAuthorizationEntry? best = null;
            int bestLiterals = -1;
            foreach (RouteAuthorizationEntry candidate in _Templates)
            {
                if (!String.Equals(candidate.Method, method, StringComparison.OrdinalIgnoreCase)) continue;
                if (candidate.Segments.Length != segments.Length) continue;

                bool matched = true;
                int literals = 0;
                for (int i = 0; i < segments.Length; i++)
                {
                    string expected = candidate.Segments[i];
                    if (expected.StartsWith("{", StringComparison.Ordinal) && expected.EndsWith("}", StringComparison.Ordinal))
                    {
                        if (segments[i].Length == 0) { matched = false; break; }
                        continue;
                    }

                    if (!String.Equals(expected, segments[i], StringComparison.OrdinalIgnoreCase)) { matched = false; break; }
                    literals++;
                }

                if (matched && literals > bestLiterals)
                {
                    best = candidate;
                    bestLiterals = literals;
                }
            }

            if (best == null) return false;
            template = best.Template;
            requirement = best.Requirement;
            return true;
        }

        #endregion

        #region Private-Methods

        private static void Add(string method, string template, string resourceType, ResourceOperationEnum operation, PermissionLevel level)
        {
            AuthorizationRequirement requirement = new AuthorizationRequirement(resourceType, operation, level);
            string key = Key(method, template);
            if (_Requirements.ContainsKey(key)) throw new InvalidOperationException("Duplicate route authorization declaration: " + key);
            _Requirements[key] = requirement;
            _Templates.Add(new RouteAuthorizationEntry(method.ToUpperInvariant(), template, SplitPath(template), requirement));
        }

        private static string Key(string method, string template)
        {
            return method.ToUpperInvariant() + " " + NormalizePath(template);
        }

        private static string NormalizePath(string path)
        {
            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal)) return path.TrimEnd('/');
            return path;
        }

        private static string[] SplitPath(string path)
        {
            string normalized = NormalizePath(path).Trim('/');
            if (normalized.Length == 0) return new string[0];
            return normalized.Split('/');
        }

        #endregion
    }
}
