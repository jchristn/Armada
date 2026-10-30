namespace Armada.Core.Authorization
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The single source of truth for what every MCP tool requires: one explicit <see cref="AuthorizationRequirement"/>
    /// per tool name. The Admiral checks it on every tool call (including Ask Armada proposals executed after approval)
    /// against the caller resolved for the MCP request. A tool registered without a declaration here is treated as
    /// <see cref="PermissionLevel.AdminOnly"/> (fail closed) and is reported by <c>AuthorizationCoverageSuite</c>.
    /// When adding a tool, add its line here in the same change.
    /// </summary>
    public static class McpToolAuthorizationRegistry
    {
        #region Private-Members

        private static readonly Dictionary<string, AuthorizationRequirement> _Requirements = new Dictionary<string, AuthorizationRequirement>(StringComparer.Ordinal);
        private static readonly List<string> _Names = new List<string>();

        #endregion

        #region Constructors-and-Factories

        static McpToolAuthorizationRegistry()
        {
            // Reads
            Add("enumerate", "All", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("status", "Status", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_backlog_item", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_backlog_planning_session", "PlanningSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_backlog_planning_session_messages", "PlanningSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_backlog_refinement_session", "ObjectiveRefinementSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_backlog_refinement_session_messages", "ObjectiveRefinementSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_captain_log", "Captain", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_captain_tools", "Captain", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_captain", "Captain", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_check_run", "CheckRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_deployment", "Deployment", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_dock", "Dock", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_fleet", "Fleet", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_harbor", "Harbor", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_harbor_metrics", "Harbor", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_harbor_jobs", "Harbor", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_memory", "Memory", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_merge_entry", "MergeQueue", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_mission_diff", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_mission_log", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_model_endpoint", "ModelEndpoint", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_objective", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_persona", "Persona", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_pipeline", "Pipeline", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_playbook", "Playbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_prompt_template", "PromptTemplate", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_release", "Release", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_runbook_execution", "Runbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_runbook", "Runbook", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_vessel", "Vessel", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_workflow_profile", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("resolve_workflow_profile", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("preview_workflow_profile", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            // Ask Armada conversations are private: the services scope every call to the caller's own conversations.
            Add("list_ask_threads", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_ask_thread", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("create_ask_thread", "AskThread", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("update_ask_thread", "AskThread", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("delete_ask_thread", "AskThread", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("delete_ask_threads", "AskThread", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("list_ask_messages", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("send_ask_message", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("cancel_ask_turn", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("summarize_ask_thread", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("mark_ask_thread_read", "AskThread", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("list_ask_quick_actions", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("run_ask_quick_action", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("approve_ask_proposal", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("reject_ask_proposal", "AskThread", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("get_ask_work", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_ask_work", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_ask_work_missions", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_ask_work_targets", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_ask_proposals", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_ask_tool_calls", "AskThread", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_backlog_refinement_sessions", "ObjectiveRefinementSession", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_backlog", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_objectives", "Objective", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_prompt_templates", "PromptTemplate", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("mission_status", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("papercut_summary", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("search_memory", "Memory", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("token_usage_summary", "TokenUsage", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("vessel_health", "VesselHealth", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_vessel_dependencies", "VesselHealth", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("voyage_status", "Voyage", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_voyage_progress", "Voyage", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("fleet_action_run_status", "FleetActionRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("evaluate_autoland", "Mission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_cli_permission_request", "CliPermission", ResourceOperationEnum.Read, PermissionLevel.Authenticated);

            // Black box prevention, forensics and operations (read-only; BlackBoxInsightService scopes to the caller).
            Add("preview_mission_overlap", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_vessel_reservations", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("lint_mission_brief", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_vessel_blackbox_doctor", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("diff_blackbox_entries", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_blackbox_nearby_successes", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_blackbox_timeline", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_voyage_blackbox_cascade", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_blackbox_recovery_stats", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);

            // CLI tool permissions. The prompt tool is callable only by a captain's scoped session and deciding only by a
            // person (checked in the handlers); rule and policy changes need an admin.
            Add("cli_permission_prompt", "CliPermission", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("decide_cli_permission_request", "CliPermission", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("create_cli_permission_rule", "CliPermission", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_cli_permission_rule", "CliPermission", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_cli_permission_rule", "CliPermission", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("set_captain_cli_permission_policy", "Captain", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);

            // Stage outcomes and failure-cause rules (tenant scoping, global-rule and built-in-rule writes are checked by StageOutcomeAccessService)
            Add("list_stage_outcomes", "StageOutcome", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_stage_outcome", "StageOutcome", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("classify_stage_outcome", "StageOutcome", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("reclassify_stage_outcomes", "StageOutcome", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("get_handoff_artifact", "HandoffArtifact", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_handoff_artifacts", "HandoffArtifact", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("draft_failure_cause_rule", "StageOutcome", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_failure_cause_rules", "FailureCauseRule", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_failure_cause_rule", "FailureCauseRule", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("create_failure_cause_rule", "FailureCauseRule", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("update_failure_cause_rule", "FailureCauseRule", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("delete_failure_cause_rule", "FailureCauseRule", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            // evaluate_captain covers captains (the service requires a global or tenant admin) and model endpoints (read access).
            Add("evaluate_captain", "ModelProbeRun", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("get_model_probe_run", "ModelProbeRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_model_probe_runs", "ModelProbeRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_battery_packs", "BatteryPack", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("run_battery", "BatteryRun", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("get_battery_run", "BatteryRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_battery_results", "BatteryRun", ResourceOperationEnum.Read, PermissionLevel.Authenticated);

            // Mission and voyage black box (entry visibility is checked by IBlackBoxService: an invisible entry is NotFound;
            // recovery and recommendation steps are authorized as the underlying tool)
            Add("get_blackbox", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("read_blackbox_section", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_blackbox_objects", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("read_blackbox_object", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_blackbox_entries", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("search_blackbox", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_blackbox_notes", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_blackbox_bundle", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_blackbox_hotspots", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("list_blackbox_fingerprints", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("get_blackbox_retention", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.TenantAdmin);
            Add("get_blackbox_storage", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.AdminOnly);
            Add("annotate_blackbox", "BlackBox", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("resolve_blackbox", "BlackBox", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("run_blackbox_recovery", "BlackBox", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("apply_blackbox_recommendation", "BlackBox", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("pin_blackbox", "BlackBox", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("delete_blackbox_entries", "BlackBox", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("purge_blackbox", "BlackBox", ResourceOperationEnum.Delete, PermissionLevel.AdminOnly);
            Add("run_blackbox_gc", "BlackBox", ResourceOperationEnum.Execute, PermissionLevel.AdminOnly);
            Add("reanalyze_blackbox", "BlackBox", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("get_blackbox_fingerprint_trend", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.Authenticated);
            Add("export_blackbox_fixture", "BlackBox", ResourceOperationEnum.Read, PermissionLevel.AdminOnly);

            // Writes to caller-owned resources (memories, model endpoints, harbors), scoped to the caller in the tool handlers
            Add("create_memory", "Memory", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("update_memory", "Memory", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("delete_memory", "Memory", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("create_model_endpoint", "ModelEndpoint", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("duplicate_model_endpoint", "ModelEndpoint", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("update_model_endpoint", "ModelEndpoint", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("delete_model_endpoint", "ModelEndpoint", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("validate_model_endpoint", "ModelEndpoint", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("health_check_model_endpoints", "ModelEndpoint", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("create_harbor", "Harbor", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("update_harbor", "Harbor", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("delete_harbor", "Harbor", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("set_harbor_enabled", "Harbor", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("stop_harbor_job", "Harbor", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);

            // Writes and executions, mirroring the REST routes for the same resources
            Add("add_vessel", "Vessel", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_vessel", "Vessel", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_vessel", "Vessel", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("delete_vessels", "Vessel", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("update_vessel_context", "Vessel", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("create_fleet", "Fleet", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_fleet", "Fleet", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_fleet", "Fleet", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("delete_fleets", "Fleet", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("create_captain", "Captain", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_captain", "Captain", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_captain", "Captain", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("delete_captains", "Captain", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("stop_captain", "Captain", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("stop_all", "Captain", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("release_captain", "Captain", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("dispatch", "Voyage", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("create_mission", "Mission", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_mission", "Mission", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("cancel_mission", "Mission", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("restart_mission", "Mission", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("retry_landing", "Mission", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("transition_mission_status", "Mission", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("approve_mission_review", "Mission", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("deny_mission_review", "Mission", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_missions", "Mission", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("purge_mission", "Mission", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("cancel_voyage", "Voyage", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("delete_voyages", "Voyage", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("purge_voyage", "Voyage", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("delete_dock", "Dock", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("delete_docks", "Dock", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("purge_dock", "Dock", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("repair_dock", "Dock", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("unstick_dock", "Dock", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("send_signal", "Signal", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("delete_signals", "Signal", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("delete_event", "Event", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("delete_events", "Event", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("enqueue_merge", "MergeQueue", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("cancel_merge", "MergeQueue", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("delete_merge", "MergeQueue", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("process_merge_entry", "MergeQueue", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("process_merge_queue", "MergeQueue", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("purge_merge_entries", "MergeQueue", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("purge_merge_entry", "MergeQueue", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("purge_merge_queue", "MergeQueue", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("create_objective", "Objective", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_objective", "Objective", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_objective", "Objective", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("reorder_objectives", "Objective", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("create_backlog_item", "Objective", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_backlog_item", "Objective", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_backlog_item", "Objective", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("reorder_backlog_items", "Objective", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("create_backlog_planning_session", "PlanningSession", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("dispatch_backlog_planning_session", "PlanningSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("create_backlog_refinement_session", "ObjectiveRefinementSession", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("send_backlog_refinement_message", "ObjectiveRefinementSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("stop_backlog_refinement_session", "ObjectiveRefinementSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("summarize_backlog_refinement_session", "ObjectiveRefinementSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("apply_backlog_refinement_summary", "ObjectiveRefinementSession", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("create_playbook", "Playbook", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_playbook", "Playbook", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_playbook", "Playbook", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            // Workflow-profile writes match REST: TenantAdmin, with ownership re-checked in WorkflowProfileAccessService
            Add("validate_workflow_profile", "WorkflowProfile", ResourceOperationEnum.Read, PermissionLevel.TenantAdmin);
            Add("create_workflow_profile", "WorkflowProfile", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_workflow_profile", "WorkflowProfile", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_workflow_profile", "WorkflowProfile", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("delete_workflow_profiles", "WorkflowProfile", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            // Persona, pipeline, and prompt-template writes match REST: any authenticated caller, scoped in the tool handlers
            // (a regular user creates only UserSpecific objects and edits only their own).
            Add("create_persona", "Persona", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("update_persona", "Persona", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("delete_persona", "Persona", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("create_pipeline", "Pipeline", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("update_pipeline", "Pipeline", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("delete_pipeline", "Pipeline", ResourceOperationEnum.Delete, PermissionLevel.Authenticated);
            Add("create_prompt_template", "PromptTemplate", ResourceOperationEnum.Create, PermissionLevel.Authenticated);
            Add("update_prompt_template", "PromptTemplate", ResourceOperationEnum.Update, PermissionLevel.Authenticated);
            Add("reset_prompt_template", "PromptTemplate", ResourceOperationEnum.Execute, PermissionLevel.Authenticated);
            Add("create_release", "Release", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("create_deployment", "Deployment", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("approve_deployment", "Deployment", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("verify_deployment", "Deployment", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("rollback_deployment", "Deployment", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("start_runbook_execution", "Runbook", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("run_check", "CheckRun", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("retry_check_run", "CheckRun", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("create_fleet_action", "FleetAction", ResourceOperationEnum.Create, PermissionLevel.TenantAdmin);
            Add("update_fleet_action", "FleetAction", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);
            Add("delete_fleet_action", "FleetAction", ResourceOperationEnum.Delete, PermissionLevel.TenantAdmin);
            Add("run_fleet_action", "FleetAction", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("cancel_fleet_action_run", "FleetActionRun", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("discover_vessels", "VesselImport", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("import_vessels", "VesselImport", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("categorize_vessel_import", "VesselImport", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("apply_fleet_recommendations", "VesselImport", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("evaluate_vessel_health", "VesselHealth", ResourceOperationEnum.Execute, PermissionLevel.TenantAdmin);
            Add("set_vessel_health_override", "VesselHealth", ResourceOperationEnum.Update, PermissionLevel.TenantAdmin);

            // Server lifecycle, backup, restore
            Add("backup", "Backup", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            Add("restore", "Backup", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
            Add("stop_server", "Server", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every declared tool name, in declaration order.
        /// </summary>
        /// <returns>Declared tool names.</returns>
        public static List<string> GetDeclaredTools()
        {
            return new List<string>(_Names);
        }

        /// <summary>
        /// Look up the requirement declared for a tool.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <param name="requirement">The declared requirement, or null.</param>
        /// <returns>True when a requirement is declared.</returns>
        public static bool TryGet(string toolName, out AuthorizationRequirement? requirement)
        {
            requirement = null;
            if (String.IsNullOrEmpty(toolName)) return false;
            return _Requirements.TryGetValue(toolName, out requirement);
        }

        /// <summary>
        /// The requirement for a tool, or an AdminOnly requirement when the tool is undeclared (fail closed).
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <returns>Requirement.</returns>
        public static AuthorizationRequirement GetOrDefault(string toolName)
        {
            if (TryGet(toolName, out AuthorizationRequirement? requirement) && requirement != null) return requirement;
            return new AuthorizationRequirement("Undeclared", ResourceOperationEnum.Admin, PermissionLevel.AdminOnly);
        }

        #endregion

        #region Private-Methods

        private static void Add(string toolName, string resourceType, ResourceOperationEnum operation, PermissionLevel level)
        {
            if (_Requirements.ContainsKey(toolName)) throw new InvalidOperationException("Duplicate MCP tool authorization declaration: " + toolName);
            _Requirements[toolName] = new AuthorizationRequirement(resourceType, operation, level);
            _Names.Add(toolName);
        }

        #endregion
    }
}
