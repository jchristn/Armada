namespace Armada.Client.Socket
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// WebSocket event type names the dashboard handles (the plan's 29-event parity checklist), plus the
    /// <c>status.snapshot</c> the server sends after <c>subscribe</c>. Use with <see cref="ArmadaSocketMessage.Type"/>.
    /// </summary>
    public static class ArmadaEventTypes
    {
        #region Public-Members

        /// <summary>mission.changed: a mission's status changed (<see cref="EntityChangedEvent"/>).</summary>
        public const string MissionChanged = "mission.changed";

        /// <summary>voyage.changed: a voyage's status changed (<see cref="EntityChangedEvent"/>).</summary>
        public const string VoyageChanged = "voyage.changed";

        /// <summary>captain.changed: a captain's state changed (<see cref="EntityChangedEvent"/>).</summary>
        public const string CaptainChanged = "captain.changed";

        /// <summary>deployment.changed: a deployment changed (<see cref="EntityChangedEvent"/>).</summary>
        public const string DeploymentChanged = "deployment.changed";

        /// <summary>objective.changed: a backlog item changed (<see cref="EntityChangedEvent"/>).</summary>
        public const string ObjectiveChanged = "objective.changed";

        /// <summary>incident.changed: an incident changed (<see cref="EntityChangedEvent"/>).</summary>
        public const string IncidentChanged = "incident.changed";

        /// <summary>planning-session.changed (<see cref="PlanningSessionEvent"/>).</summary>
        public const string PlanningSessionChanged = "planning-session.changed";

        /// <summary>planning-session.message.created (<see cref="PlanningSessionEvent"/>).</summary>
        public const string PlanningSessionMessageCreated = "planning-session.message.created";

        /// <summary>planning-session.message.updated (<see cref="PlanningSessionEvent"/>).</summary>
        public const string PlanningSessionMessageUpdated = "planning-session.message.updated";

        /// <summary>planning-session.tool (<see cref="PlanningSessionEvent"/>).</summary>
        public const string PlanningSessionTool = "planning-session.tool";

        /// <summary>planning-session.thinking (<see cref="PlanningSessionEvent"/>).</summary>
        public const string PlanningSessionThinking = "planning-session.thinking";

        /// <summary>planning-session.summary.created (<see cref="PlanningSessionEvent"/>).</summary>
        public const string PlanningSessionSummaryCreated = "planning-session.summary.created";

        /// <summary>planning-session.dispatch.created (<see cref="PlanningSessionEvent"/>).</summary>
        public const string PlanningSessionDispatchCreated = "planning-session.dispatch.created";

        /// <summary>planning-session.deleted (<see cref="PlanningSessionEvent"/>).</summary>
        public const string PlanningSessionDeleted = "planning-session.deleted";

        /// <summary>objective-refinement-session.changed (<see cref="RefinementSessionEvent"/>).</summary>
        public const string RefinementSessionChanged = "objective-refinement-session.changed";

        /// <summary>objective-refinement-session.message.created (<see cref="RefinementSessionEvent"/>).</summary>
        public const string RefinementSessionMessageCreated = "objective-refinement-session.message.created";

        /// <summary>objective-refinement-session.message.updated (<see cref="RefinementSessionEvent"/>).</summary>
        public const string RefinementSessionMessageUpdated = "objective-refinement-session.message.updated";

        /// <summary>objective-refinement-session.summary.created (<see cref="RefinementSessionEvent"/>).</summary>
        public const string RefinementSessionSummaryCreated = "objective-refinement-session.summary.created";

        /// <summary>objective-refinement-session.applied (<see cref="RefinementSessionEvent"/>).</summary>
        public const string RefinementSessionApplied = "objective-refinement-session.applied";

        /// <summary>objective-refinement-session.deleted (<see cref="RefinementSessionEvent"/>).</summary>
        public const string RefinementSessionDeleted = "objective-refinement-session.deleted";

        /// <summary>ask.chunk: reply tokens for a running turn (<see cref="AskDeltaEvent"/>).</summary>
        public const string AskChunk = "ask.chunk";

        /// <summary>ask.thinking: thinking tokens for a running turn (<see cref="AskDeltaEvent"/>).</summary>
        public const string AskThinking = "ask.thinking";

        /// <summary>ask.tool: a tool call started or completed (<see cref="AskToolEvent"/>).</summary>
        public const string AskTool = "ask.tool";

        /// <summary>ask.turn: a turn started, completed, failed, or was cancelled (<see cref="AskTurnEvent"/>).</summary>
        public const string AskTurn = "ask.turn";

        /// <summary>ask.message: a message was persisted (<see cref="AskMessageEvent"/>).</summary>
        public const string AskMessage = "ask.message";

        /// <summary>ask.proposal: a proposal was created or changed (<see cref="AskProposalEvent"/>).</summary>
        public const string AskProposal = "ask.proposal";

        /// <summary>ask.work: tracked work changed (<see cref="AskWorkEvent"/>).</summary>
        public const string AskWork = "ask.work";

        /// <summary>ask.thread: a thread changed (<see cref="AskThreadEvent"/>).</summary>
        public const string AskThread = "ask.thread";

        /// <summary>mission.status_changed: a mission's status changed, with typed <c>status</c> and <c>previousStatus</c> (<see cref="MissionStatusChangedEvent"/>).</summary>
        public const string MissionStatusChanged = "mission.status_changed";

        /// <summary>command.error: a WebSocket command failed; fields are top level, read with <see cref="CommandErrorMessage.From"/>.</summary>
        public const string CommandError = "command.error";

        /// <summary>status.snapshot: the status the server sends right after subscribe (<see cref="Armada.Core.Models.ArmadaStatus"/>).</summary>
        public const string StatusSnapshot = "status.snapshot";

        /// <summary>
        /// The 29 event types in the dashboard parity checklist, in plan order. Never null.
        /// </summary>
        public static IReadOnlyList<string> Parity { get; } = new List<string>
        {
            MissionChanged, VoyageChanged, CaptainChanged, DeploymentChanged, ObjectiveChanged, IncidentChanged,
            PlanningSessionChanged, PlanningSessionMessageCreated, PlanningSessionMessageUpdated, PlanningSessionTool,
            PlanningSessionThinking, PlanningSessionSummaryCreated, PlanningSessionDispatchCreated, PlanningSessionDeleted,
            RefinementSessionChanged, RefinementSessionMessageCreated, RefinementSessionMessageUpdated,
            RefinementSessionSummaryCreated, RefinementSessionApplied, RefinementSessionDeleted,
            AskChunk, AskThinking, AskTool, AskTurn, AskMessage, AskProposal, AskWork, AskThread
        }.AsReadOnly();

        /// <summary>
        /// Payload class for each event type (entity changes share <see cref="EntityChangedEvent"/>). Never null.
        /// </summary>
        public static IReadOnlyDictionary<string, Type> PayloadTypes { get; } = BuildPayloadTypes();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The payload class for an event type, or null when the type is unknown.
        /// </summary>
        /// <param name="eventType">Event type.</param>
        /// <returns>The payload type, or null.</returns>
        public static Type? PayloadTypeFor(string? eventType)
        {
            if (String.IsNullOrEmpty(eventType)) return null;
            return PayloadTypes.TryGetValue(eventType!, out Type? type) ? type : null;
        }

        #endregion

        #region Private-Methods

        private static IReadOnlyDictionary<string, Type> BuildPayloadTypes()
        {
            Dictionary<string, Type> map = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (string t in new[] { MissionChanged, VoyageChanged, CaptainChanged, DeploymentChanged, ObjectiveChanged, IncidentChanged })
                map[t] = typeof(EntityChangedEvent);
            foreach (string t in new[] { PlanningSessionChanged, PlanningSessionMessageCreated, PlanningSessionMessageUpdated, PlanningSessionTool, PlanningSessionThinking, PlanningSessionSummaryCreated, PlanningSessionDispatchCreated, PlanningSessionDeleted })
                map[t] = typeof(PlanningSessionEvent);
            foreach (string t in new[] { RefinementSessionChanged, RefinementSessionMessageCreated, RefinementSessionMessageUpdated, RefinementSessionSummaryCreated, RefinementSessionApplied, RefinementSessionDeleted })
                map[t] = typeof(RefinementSessionEvent);
            map[AskChunk] = typeof(AskDeltaEvent);
            map[AskThinking] = typeof(AskDeltaEvent);
            map[AskTool] = typeof(AskToolEvent);
            map[AskTurn] = typeof(AskTurnEvent);
            map[AskMessage] = typeof(AskMessageEvent);
            map[AskProposal] = typeof(AskProposalEvent);
            map[AskWork] = typeof(AskWorkEvent);
            map[AskThread] = typeof(AskThreadEvent);
            map[StatusSnapshot] = typeof(Armada.Core.Models.ArmadaStatus);
            map[MissionStatusChanged] = typeof(MissionStatusChangedEvent);
            return map;
        }

        #endregion
    }
}
