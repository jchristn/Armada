// TODO: MCP is currently unauthenticated and uses the default tenant context for all operations.
// MCP authentication and per-tenant scoping is planned for a future phase.
namespace Armada.Server.Mcp
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Server;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server.Mcp.Tools;
    using SyslogLogging;

    /// <summary>
    /// Delegate matching the RegisterTool signature shared by McpHttpServer and McpServer.
    /// </summary>
    public delegate void RegisterToolDelegate(
        string name,
        string description,
        object inputSchema,
        Func<JsonElement?, Task<object>> handler);

    /// <summary>
    /// Registers all Armada MCP tools on any MCP server transport.
    /// Shared between the HTTP MCP server and the stdio MCP server.
    /// </summary>
    public static class McpToolRegistrar
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Register all Armada tools using the provided registration delegate.
        /// </summary>
        /// <param name="register">Tool registration delegate (works with both McpHttpServer and McpServer).</param>
        /// <param name="database">Database driver for direct queries.</param>
        /// <param name="admiral">Admiral service for orchestration operations.</param>
        /// <param name="settings">Application settings for log/diff paths.</param>
        /// <param name="git">Git service for diff operations.</param>
        /// <param name="mergeQueue">Merge queue service.</param>
        /// <param name="dockService">Dock service for dock management.</param>
        /// <param name="landingService">Landing service for retry landing operations.</param>
        /// <param name="checkRunService">Optional structured check-run service for Delivery checks.</param>
        /// <param name="objectiveService">Optional objective service for scope capture workflows.</param>
        /// <param name="planningSessionCoordinator">Optional planning session coordinator for scope readiness workflows.</param>
        /// <param name="objectiveRefinementCoordinator">Optional refinement coordinator for captain-backed backlog refinement workflows.</param>
        /// <param name="releaseService">Optional release service for Delivery release workflows.</param>
        /// <param name="deploymentService">Optional deployment service for Delivery deployment workflows.</param>
        /// <param name="runbookService">Optional runbook service for guided operational workflows.</param>
        /// <param name="onStop">Callback to stop the server.</param>
        /// <param name="onStopCaptain">Callback to kill a captain's agent process by captain ID. Called before RecallCaptainAsync.</param>
        /// <param name="agentLifecycle">Agent lifecycle handler used for captain model validation.</param>
        /// <param name="templateService">Prompt template service for template operations.</param>
        /// <param name="logging">Logging module for tools that need validation services.</param>
        /// <param name="captainToolService">Captain tool availability service for captain tool discovery.</param>
        public static void RegisterAll(
            RegisterToolDelegate register,
            DatabaseDriver database,
            IAdmiralService admiral,
            ArmadaSettings? settings = null,
            IGitService? git = null,
            IMergeQueueService? mergeQueue = null,
            IDockService? dockService = null,
            ILandingService? landingService = null,
            CheckRunService? checkRunService = null,
            ObjectiveService? objectiveService = null,
            PlanningSessionCoordinator? planningSessionCoordinator = null,
            ObjectiveRefinementCoordinator? objectiveRefinementCoordinator = null,
            ReleaseService? releaseService = null,
            DeploymentService? deploymentService = null,
            RunbookService? runbookService = null,
            Action? onStop = null,
            Func<string, Task>? onStopCaptain = null,
            AgentLifecycleHandler? agentLifecycle = null,
            IPromptTemplateService? templateService = null,
            LoggingModule? logging = null,
            CaptainToolService? captainToolService = null,
            ModelEndpointService? modelEndpointService = null)
        {
            McpStatusTools.Register(register, admiral, onStop);
            if (logging != null) McpInboxTools.Register(register, database, logging);
            McpEnumerateTools.Register(register, database, mergeQueue);
            McpFleetTools.Register(register, database);
            McpVesselTools.Register(register, database, dockService);
            McpVoyageTools.Register(register, database, admiral, settings);
            McpMissionTools.Register(register, database, admiral, settings, git, landingService);
            McpCaptainTools.Register(register, database, admiral, settings, onStopCaptain, agentLifecycle, captainToolService);
            McpSignalTools.Register(register, database);
            McpEventTools.Register(register, database);
            McpPapercutTools.Register(register, database);
            McpTokenUsageTools.Register(register, database);
            McpDockTools.Register(register, database, dockService);
            if (logging != null) McpPlaybookTools.Register(register, database, logging);
            if (mergeQueue != null) McpMergeQueueTools.Register(register, mergeQueue);
            if (checkRunService != null) McpCheckRunTools.Register(register, database, checkRunService);
            if (objectiveService != null) McpObjectiveTools.Register(register, database, objectiveService, planningSessionCoordinator, objectiveRefinementCoordinator);
            if (releaseService != null) McpReleaseTools.Register(register, releaseService);
            if (deploymentService != null) McpDeploymentTools.Register(register, deploymentService);
            if (runbookService != null) McpRunbookTools.Register(register, runbookService);
            if (templateService != null) McpPromptTemplateTools.Register(register, database, templateService);
            McpPersonaTools.Register(register, database);
            McpPipelineTools.Register(register, database);
            if (settings != null) McpBackupTools.Register(register, database, settings);
            if (modelEndpointService != null) McpModelEndpointTools.Register(register, modelEndpointService);
        }

        /// <summary>
        /// Describe the Armada MCP tool catalog that would be registered for the supplied services.
        /// </summary>
        /// <returns>Tool summaries ordered by name.</returns>
        public static List<CaptainToolSummary> DescribeAll(
            DatabaseDriver database,
            IAdmiralService admiral,
            ArmadaSettings? settings = null,
            IGitService? git = null,
            IMergeQueueService? mergeQueue = null,
            IDockService? dockService = null,
            ILandingService? landingService = null,
            CheckRunService? checkRunService = null,
            ObjectiveService? objectiveService = null,
            PlanningSessionCoordinator? planningSessionCoordinator = null,
            ObjectiveRefinementCoordinator? objectiveRefinementCoordinator = null,
            ReleaseService? releaseService = null,
            DeploymentService? deploymentService = null,
            RunbookService? runbookService = null,
            Action? onStop = null,
            Func<string, Task>? onStopCaptain = null,
            AgentLifecycleHandler? agentLifecycle = null,
            IPromptTemplateService? templateService = null,
            LoggingModule? logging = null,
            ModelEndpointService? modelEndpointService = null)
        {
            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();

            void RegisterCatalogGroup(string registrationSource, Action<RegisterToolDelegate> registerGroup)
            {
                registerGroup(
                    (name, description, inputSchema, handler) =>
                    {
                        tools.Add(new CaptainToolSummary
                        {
                            Name = name,
                            Description = description,
                            InputSchemaJson = inputSchema == null ? null : JsonSerializer.Serialize(inputSchema, _JsonOptions),
                            RegistrationSource = registrationSource
                        });
                    });
            }

            RegisterCatalogGroup("Armada MCP / Status", register => McpStatusTools.Register(register, admiral, onStop));
            if (logging != null) RegisterCatalogGroup("Armada MCP / Inbox", register => McpInboxTools.Register(register, database, logging));
            RegisterCatalogGroup("Armada MCP / Enumeration", register => McpEnumerateTools.Register(register, database, mergeQueue));
            RegisterCatalogGroup("Armada MCP / Fleets", register => McpFleetTools.Register(register, database));
            RegisterCatalogGroup("Armada MCP / Vessels", register => McpVesselTools.Register(register, database, dockService));
            RegisterCatalogGroup("Armada MCP / Voyages", register => McpVoyageTools.Register(register, database, admiral, settings));
            RegisterCatalogGroup("Armada MCP / Missions", register => McpMissionTools.Register(register, database, admiral, settings, git, landingService));
            RegisterCatalogGroup("Armada MCP / Captains", register => McpCaptainTools.Register(register, database, admiral, settings, onStopCaptain, agentLifecycle));
            RegisterCatalogGroup("Armada MCP / Signals", register => McpSignalTools.Register(register, database));
            RegisterCatalogGroup("Armada MCP / Events", register => McpEventTools.Register(register, database));
            RegisterCatalogGroup("Armada MCP / Papercuts", register => McpPapercutTools.Register(register, database));
            RegisterCatalogGroup("Armada MCP / TokenUsage", register => McpTokenUsageTools.Register(register, database));
            RegisterCatalogGroup("Armada MCP / Docks", register => McpDockTools.Register(register, database, dockService));

            if (logging != null) RegisterCatalogGroup("Armada MCP / Playbooks", register => McpPlaybookTools.Register(register, database, logging));
            if (mergeQueue != null) RegisterCatalogGroup("Armada MCP / Merge Queue", register => McpMergeQueueTools.Register(register, mergeQueue));
            if (checkRunService != null) RegisterCatalogGroup("Armada MCP / Check Runs", register => McpCheckRunTools.Register(register, database, checkRunService));
            if (objectiveService != null) RegisterCatalogGroup("Armada MCP / Objectives", register => McpObjectiveTools.Register(register, database, objectiveService, planningSessionCoordinator, objectiveRefinementCoordinator));
            if (releaseService != null) RegisterCatalogGroup("Armada MCP / Releases", register => McpReleaseTools.Register(register, releaseService));
            if (deploymentService != null) RegisterCatalogGroup("Armada MCP / Deployments", register => McpDeploymentTools.Register(register, deploymentService));
            if (runbookService != null) RegisterCatalogGroup("Armada MCP / Runbooks", register => McpRunbookTools.Register(register, runbookService));
            if (templateService != null) RegisterCatalogGroup("Armada MCP / Prompt Templates", register => McpPromptTemplateTools.Register(register, database, templateService));
            RegisterCatalogGroup("Armada MCP / Personas", register => McpPersonaTools.Register(register, database));
            RegisterCatalogGroup("Armada MCP / Pipelines", register => McpPipelineTools.Register(register, database));
            if (settings != null) RegisterCatalogGroup("Armada MCP / Backup", register => McpBackupTools.Register(register, database, settings));
            if (modelEndpointService != null) RegisterCatalogGroup("Armada MCP / Model Endpoints", register => McpModelEndpointTools.Register(register, modelEndpointService));

            return tools
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
