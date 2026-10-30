namespace Armada.Core.Services.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The Ask Armada approval policy in one place: the explicit allowlist of read-only MCP tools that a thread's captain
    /// may call without approval. Every tool not on this list is treated as state-changing: with the thread's
    /// auto-approve off it becomes a proposal the user must approve; with auto-approve on it runs and is recorded.
    /// New tools are therefore gated until they are added here deliberately.
    /// </summary>
    public static class AskToolPolicy
    {
        #region Public-Members

        /// <summary>
        /// Prefix Claude Code puts in front of tools of the MCP server registered as "armada".
        /// </summary>
        public const string ClaudeToolPrefix = "mcp__armada__";

        /// <summary>
        /// Read-only tools (never prompt), sorted.
        /// </summary>
        public static IReadOnlyList<string> ReadOnlyTools => _ReadOnly.OrderBy(t => t, StringComparer.Ordinal).ToList();

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _ReadOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            // Status and enumeration
            "status",
            "enumerate",
            "voyage_status",
            "list_voyage_progress",
            "mission_status",
            "fleet_action_run_status",
            "vessel_health",
            "list_vessel_dependencies",
            "papercut_summary",
            "token_usage_summary",
            "search_memory",
            "evaluate_autoland",
            "get_cli_permission_request",

            // get_* readers
            "get_backlog_item",
            "get_backlog_planning_session",
            "get_backlog_refinement_session",
            "get_captain",
            "get_captain_log",
            "get_captain_tools",
            "get_check_run",
            "get_deployment",
            "get_dock",
            "get_fleet",
            "get_harbor",
            "get_memory",
            "get_merge_entry",
            "get_mission_diff",
            "get_mission_log",
            "get_model_endpoint",
            "get_objective",
            "get_persona",
            "get_pipeline",
            "get_playbook",
            "get_prompt_template",
            "get_release",
            "get_runbook",
            "get_runbook_execution",
            "get_vessel",
            "get_workflow_profile",

            // list_* readers
            "list_backlog",
            "list_backlog_planning_session_messages",
            "list_backlog_refinement_session_messages",
            "list_backlog_refinement_sessions",
            "list_objectives",
            "list_prompt_templates",

            // Workflow profile readers (validate only checks a definition, it saves nothing)
            "validate_workflow_profile",
            "resolve_workflow_profile",
            "preview_workflow_profile",

            // Ask Armada readers
            "list_ask_threads",
            "get_ask_thread",
            "list_ask_messages",
            "list_ask_quick_actions",
            "get_ask_work",
            "list_ask_work",
            "list_ask_work_missions",
            "list_ask_work_targets",
            "list_ask_proposals",
            "list_ask_tool_calls",

            // Evaluate run readers
            "get_model_probe_run",
            "list_model_probe_runs",
            "get_model_profile",
            "list_model_profiles",

            // Fixture battery readers
            "list_battery_packs",
            "get_battery_run",
            "list_battery_results",

            // Black box readers (plan 10.1): every BlackBox tool the MCP authorization registry declares as a Read,
            // including the admin-level ones (get_blackbox_retention TenantAdmin; get_blackbox_storage and
            // export_blackbox_fixture AdminOnly), like validate_workflow_profile: the gate sits outside the registry
            // check, so a thread call still runs as the thread owner and is refused (Forbidden) without the role. The
            // write tools (run_blackbox_recovery, apply_blackbox_recommendation, resolve_blackbox, annotate_blackbox, pin,
            // delete, purge, gc, reanalyze) stay behind the Ask gate as proposals.
            "get_blackbox",
            "read_blackbox_section",
            "list_blackbox_objects",
            "read_blackbox_object",
            "list_blackbox_entries",
            "search_blackbox",
            "list_blackbox_notes",
            "get_blackbox_bundle",
            "list_blackbox_hotspots",
            "list_blackbox_fingerprints",
            "list_blackbox_resilience_gaps",
            "get_blackbox_fingerprint_trend",
            "get_blackbox_retention",
            "get_blackbox_storage",
            "export_blackbox_fixture",

            // Black box prevention, forensics and operations readers
            "preview_mission_overlap",
            "list_vessel_reservations",
            "lint_mission_brief",
            "get_vessel_blackbox_doctor",
            "diff_blackbox_entries",
            "list_blackbox_nearby_successes",
            "get_blackbox_timeline",
            "get_voyage_blackbox_cascade",
            "get_blackbox_recovery_stats"
        };

        private static readonly HashSet<string> _GateExempt = new HashSet<string>(StringComparer.Ordinal)
        {
            // The CLI's own permission prompt: answered by an approver in Armada, never turned into a proposal.
            CliPermissionPolicyResolver.PromptToolName
        };

        private static readonly HashSet<string> _ThreadForbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            // A captain must never decide (or pre-approve) its own permission prompts, not even through a proposal the
            // user approves without reading.
            "decide_cli_permission_request",
            "create_cli_permission_rule",
            "update_cli_permission_rule",
            "delete_cli_permission_rule",
            "set_captain_cli_permission_policy",

            // A captain answering in a conversation must never approve or reject its own proposals, start turns (its
            // own included), or change or delete conversations.
            "create_ask_thread",
            "update_ask_thread",
            "delete_ask_thread",
            "delete_ask_threads",
            "send_ask_message",
            "cancel_ask_turn",
            "summarize_ask_thread",
            "mark_ask_thread_read",
            "run_ask_quick_action",
            "approve_ask_proposal",
            "reject_ask_proposal",

            // ...and never pass a pipeline review gate.
            "approve_mission_review",
            "deny_mission_review"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a tool is on the read-only allowlist.
        /// </summary>
        /// <param name="toolName">Tool name (an MCP-client prefix such as mcp__armada__ is ignored).</param>
        /// <returns>True when the tool never needs approval.</returns>
        public static bool IsReadOnly(string? toolName)
        {
            string normalized = NormalizeToolName(toolName);
            return normalized.Length > 0 && _ReadOnly.Contains(normalized);
        }

        /// <summary>
        /// Whether a thread-scoped call of the tool runs without becoming a proposal and without the read-only check
        /// (the CLI permission prompt tool, which is answered by an approver).
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <returns>True when exempt from the Ask gate.</returns>
        public static bool IsGateExempt(string? toolName)
        {
            string normalized = NormalizeToolName(toolName);
            return normalized.Length > 0 && _GateExempt.Contains(normalized);
        }

        /// <summary>
        /// Whether a thread-scoped call of the tool is refused outright (never proposed): CLI permission decisions,
        /// rules, and policies.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <returns>True when refused in Ask threads.</returns>
        public static bool IsThreadForbidden(string? toolName)
        {
            string normalized = NormalizeToolName(toolName);
            return normalized.Length > 0 && _ThreadForbidden.Contains(normalized);
        }

        /// <summary>
        /// Strip an MCP-client prefix (mcp__armada__) from a tool name.
        /// </summary>
        /// <param name="toolName">Tool name as reported by a runtime.</param>
        /// <returns>The bare Armada tool name; empty when null.</returns>
        public static string NormalizeToolName(string? toolName)
        {
            if (String.IsNullOrWhiteSpace(toolName)) return String.Empty;
            string trimmed = toolName.Trim();
            if (trimmed.StartsWith(ClaudeToolPrefix, StringComparison.Ordinal)) trimmed = trimmed.Substring(ClaudeToolPrefix.Length);
            return trimmed;
        }

        #endregion
    }
}
