namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The declared public WebSocket contract of the Admiral: endpoints, client routes, the fields a client message may
    /// carry, the command actions <see cref="WebSocketCommandHandler"/> accepts (it rejects any other action), and the
    /// server-pushed event types with their payload fields. docs/API_SURFACE_1.0.md is generated from this class and the
    /// API contract test compares it to docs/api-surface-1.0.json. When adding an event or command, add it here in the
    /// same change.
    /// </summary>
    public static class WebSocketSurface
    {
        #region Public-Members

        /// <summary>
        /// Payload fields of every generic event (entity events recorded in the event log and broadcast as they happen).
        /// </summary>
        public static readonly IReadOnlyList<string> GenericEventFields = new List<string>
        {
            "entityType", "entityId", "captainId", "missionId", "vesselId", "voyageId"
        };

        /// <summary>
        /// Envelope fields of every server-to-client event message (message is present only on events that carry one).
        /// </summary>
        public static readonly IReadOnlyList<string> EventEnvelopeFields = new List<string>
        {
            "type", "message", "data", "timestamp"
        };

        /// <summary>
        /// Envelope fields of command replies (command.result and command.error).
        /// </summary>
        public static readonly IReadOnlyList<string> CommandReplyFields = new List<string>
        {
            "type", "action", "data", "error"
        };

        /// <summary>
        /// WebSocket endpoints by surface name and path. The Harbor link path is configurable (Harbor.LinkPath).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Endpoints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "dashboard", "/ws" },
            { "harbor-link", "Harbor.LinkPath (default /v1.0/harbor/connect)" }
        };

        /// <summary>
        /// Client-to-server routes (the Route field of a client message).
        /// </summary>
        public static readonly IReadOnlyList<string> Routes = new List<string> { "subscribe", "command" };

        /// <summary>
        /// Fields a client message may carry (matched case-insensitively). data is the typed body of create/update
        /// actions.
        /// </summary>
        public static readonly IReadOnlyList<string> ClientMessageFields = new List<string>
        {
            "route", "allTenants", "action", "id", "status", "captainId", "query", "entityType", "lines", "offset", "filePath", "outputPath", "data"
        };

        /// <summary>
        /// Command actions accepted on the command route, in documentation order.
        /// </summary>
        public static readonly IReadOnlyList<string> CommandActions = new List<string>
        {
            "status", "stop_captain", "stop_all", "stop_server",
            "list_fleets", "get_fleet", "create_fleet", "update_fleet", "delete_fleet",
            "list_vessels", "get_vessel", "create_vessel", "update_vessel", "update_vessel_context", "delete_vessel",
            "list_voyages", "get_voyage", "create_voyage", "cancel_voyage", "purge_voyage",
            "list_missions", "list_missions_summary", "get_mission", "create_mission", "update_mission", "transition_mission_status",
            "cancel_mission", "purge_mission", "restart_mission", "get_mission_diff", "get_mission_log",
            "list_captains", "get_captain", "create_captain", "update_captain", "delete_captain", "get_captain_log",
            "list_signals", "send_signal",
            "list_events",
            "list_docks",
            "list_merge_queue", "get_merge_entry", "enqueue_merge", "cancel_merge", "process_merge_queue",
            "enumerate",
            "backup", "restore",
            "get_persona", "create_persona", "update_persona", "delete_persona",
            "get_prompt_template", "update_prompt_template",
            "get_pipeline", "create_pipeline", "update_pipeline", "delete_pipeline"
        };

        /// <summary>
        /// Server-pushed event types.
        /// </summary>
        public static readonly IReadOnlyList<WebSocketEventDescriptor> Events = BuildEvents();

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _CommandActionSet = new HashSet<string>(CommandActions, StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a command action is part of the declared surface.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>True when declared.</returns>
        public static bool IsCommandAction(string? action)
        {
            if (String.IsNullOrEmpty(action)) return false;
            return _CommandActionSet.Contains(action);
        }

        /// <summary>
        /// Whether an event type is declared.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <returns>True when declared.</returns>
        public static bool IsEvent(string? eventType)
        {
            if (String.IsNullOrEmpty(eventType)) return false;
            return Events.Any(e => String.Equals(e.Type, eventType, StringComparison.Ordinal));
        }

        #endregion

        #region Private-Methods

        private static List<WebSocketEventDescriptor> BuildEvents()
        {
            List<WebSocketEventDescriptor> events = new List<WebSocketEventDescriptor>();

            // Connection
            events.Add(new WebSocketEventDescriptor("status.snapshot", "connection", false, "ArmadaStatus", new[] { "allTenants" }, false));

            // Entity change events with dedicated payloads
            events.Add(new WebSocketEventDescriptor("mission.changed", "tenant", false, null, new[] { "id", "title", "status", "voyageId" }, false));
            events.Add(new WebSocketEventDescriptor("voyage.changed", "tenant", false, null, new[] { "id", "title", "status" }, false));
            events.Add(new WebSocketEventDescriptor("captain.changed", "tenant", false, null, new[] { "id", "name", "state" }, false));
            events.Add(new WebSocketEventDescriptor("check-run.changed", "tenant", false, "CheckRun", null, false));
            events.Add(new WebSocketEventDescriptor("objective.changed", "tenant", false, "Objective", null, false));
            events.Add(new WebSocketEventDescriptor("deployment.changed", "tenant", false, "Deployment", null, false));
            events.Add(new WebSocketEventDescriptor("deployment.progress", "tenant", false, null, new[] { "id", "title", "status", "verificationStatus", "environmentId", "environmentName", "startedUtc", "completedUtc", "lastUpdateUtc" }, false));
            events.Add(new WebSocketEventDescriptor("environment.health", "tenant", false, null, new[] { "environmentId", "environmentName", "id", "title", "status", "verificationStatus", "lastMonitoredUtc", "lastRegressionAlertUtc", "latestMonitoringSummary", "monitoringFailureCount" }, false));
            events.Add(new WebSocketEventDescriptor("incident.changed", "tenant", false, "Incident", null, false));
            events.Add(new WebSocketEventDescriptor("runbook-execution.changed", "tenant", false, "RunbookExecution", null, false));
            events.Add(new WebSocketEventDescriptor("approval-needed", "tenant", false, null, new[] { "entityType", "entityId", "missionId", "title", "status", "vesselId", "voyageId", "reviewRequestedUtc" }, false));

            // Planning sessions
            events.Add(new WebSocketEventDescriptor("planning-session.changed", "tenant", true, null, new[] { "session" }, false));
            events.Add(new WebSocketEventDescriptor("planning-session.message.created", "tenant", true, null, new[] { "sessionId", "message" }, false));
            events.Add(new WebSocketEventDescriptor("planning-session.message.updated", "tenant", true, null, new[] { "sessionId", "message" }, false));
            events.Add(new WebSocketEventDescriptor("planning-session.summary.created", "tenant", true, null, new[] { "sessionId", "messageId", "draft" }, false));
            events.Add(new WebSocketEventDescriptor("planning-session.dispatch.created", "tenant", true, null, new[] { "sessionId", "voyageId", "messageId" }, false));
            events.Add(new WebSocketEventDescriptor("planning-session.deleted", "tenant", true, null, new[] { "sessionId" }, false));
            events.Add(new WebSocketEventDescriptor("planning-session.thinking", "tenant", true, null, new[] { "sessionId", "messageId", "delta" }, false));
            events.Add(new WebSocketEventDescriptor("planning-session.tool", "tenant", true, null, new[] { "sessionId", "messageId", "phase", "id", "name", "arguments", "ok", "elapsedMs", "result" }, false));

            // Objective refinement sessions
            events.Add(new WebSocketEventDescriptor("objective-refinement-session.changed", "tenant", true, null, new[] { "session" }, false));
            events.Add(new WebSocketEventDescriptor("objective-refinement-session.message.created", "tenant", true, null, new[] { "sessionId", "objectiveId", "message" }, false));
            events.Add(new WebSocketEventDescriptor("objective-refinement-session.message.updated", "tenant", true, null, new[] { "sessionId", "objectiveId", "message" }, false));
            events.Add(new WebSocketEventDescriptor("objective-refinement-session.summary.created", "tenant", true, null, new[] { "sessionId", "messageId", "summary" }, false));
            events.Add(new WebSocketEventDescriptor("objective-refinement-session.applied", "tenant", true, null, new[] { "sessionId", "objectiveId", "summary" }, false));
            events.Add(new WebSocketEventDescriptor("objective-refinement-session.deleted", "tenant", true, null, new[] { "sessionId", "objectiveId" }, false));

            // Ask Armada (owner only)
            events.Add(new WebSocketEventDescriptor("ask.turn", "user", false, null, new[] { "threadId", "turnId", "state", "messageId", "error" }, false));
            events.Add(new WebSocketEventDescriptor("ask.chunk", "user", false, null, new[] { "threadId", "turnId", "delta" }, false));
            events.Add(new WebSocketEventDescriptor("ask.thinking", "user", false, null, new[] { "threadId", "turnId", "delta" }, false));
            events.Add(new WebSocketEventDescriptor("ask.tool", "user", false, null, new[] { "threadId", "turnId", "phase", "id", "name", "arguments", "ok", "elapsedMs", "result" }, false));
            events.Add(new WebSocketEventDescriptor("ask.message", "user", false, null, new[] { "threadId", "message" }, false));
            events.Add(new WebSocketEventDescriptor("ask.proposal", "user", false, null, new[] { "threadId", "proposal" }, false));
            events.Add(new WebSocketEventDescriptor("ask.work", "user", false, null, new[] { "threadId", "trackedWorkId", "snapshot", "trackedWork" }, false));
            events.Add(new WebSocketEventDescriptor("ask.thread", "user", false, null, new[] { "threadId", "thread" }, false));

            // Generic events: GenericEventFields payload plus a message.
            string[] generic = new string[]
            {
                "captain.batch_deleted", "captain.launched",
                "dock.batch_deleted", "dock.deleted", "dock.purged", "dock.repaired", "dock.unstuck",
                "event.batch_deleted", "event.deleted",
                "fleet.batch_deleted",
                "merge.batch_purged", "merge.purged",
                "mission.batch_deleted", "mission.completed", "mission.deleted", "mission.landing_failed", "mission.manual_complete_no_dock",
                "mission.pull_request_open", "mission.restarted", "mission.review_approved", "mission.review_denied", "mission.status_changed",
                "mission.work_produced",
                "objective-refinement-session.created", "objective-refinement-session.stopped",
                "planning-session.created", "planning-session.stopped",
                "signal.batch_deleted",
                "vessel.batch_deleted",
                "voyage.batch_deleted", "voyage.deleted"
            };

            foreach (string type in generic)
                events.Add(new WebSocketEventDescriptor(type, "tenant", true, null, GenericEventFields, true));

            return events;
        }

        #endregion
    }
}
