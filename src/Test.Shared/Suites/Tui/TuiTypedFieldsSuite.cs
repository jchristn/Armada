namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Helm.Commands;
    using Armada.Tui.Approvals;
    using Armada.Tui.Ask;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Screens.Admin;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Fragility remediation R4: client, TUI, and Helm decisions made from typed fields instead of matching text.
    /// Each case reproduces an input the old text-matching code got wrong (a composed status string, a numeric
    /// enum value, an id prefix, a header-looking diff line, a placeholder sentinel) and asserts the typed result.
    /// </summary>
    public sealed class TuiTypedFieldsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Tui.TypedFields";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "deployment_verification_failed_is_error", "A succeeded deployment whose verification failed notifies as an error", () =>
            {
                EntityChangedEvent data = new EntityChangedEvent { Id = "dpl_1", Title = "Prod", Status = "Succeeded", VerificationStatus = "Failed" };
                AssertEqual(NotificationSeverityEnum.Error, NotificationService.SeverityFor(ArmadaEventTypes.DeploymentChanged, data), "typed severity");
                AssertEqual(StatusSeverityEnum.Error, StatusBadge.Severity(DeploymentStatusEnum.Succeeded, DeploymentVerificationStatusEnum.Failed), "badge severity");
                AssertEqual(StatusSeverityEnum.Success, StatusBadge.Severity(DeploymentStatusEnum.Succeeded, DeploymentVerificationStatusEnum.Passed), "passed verification");

                NotificationService svc = new NotificationService(new SystemClock(), new LocalizationService(), null, null, null);
                ArmadaSocketMessage msg = ArmadaSocketMessage.Parse("{\"type\":\"deployment.changed\",\"data\":{\"id\":\"dpl_1\",\"title\":\"Prod\",\"status\":\"Succeeded\",\"verificationStatus\":\"Failed\"}}")!;
                AssertTrue(svc.HandleSocketMessage(msg), "recorded");
                AssertEqual(NotificationSeverityEnum.Error, svc.History[0].Severity, "history severity");
                AssertEqual("Succeeded / Failed", svc.History[0].Status, "display text keeps both values");
            }));

            cases.Add(TuiCase.Sync(Suite, "status_text_matched_exactly", "Status names match exactly; composed or embedded words are not classified", () =>
            {
                AssertEqual(NotificationSeverityEnum.Info, NotificationService.SeverityFor("Succeeded / Failed"), "composed notification text");
                AssertEqual(StatusSeverityEnum.Info, StatusBadge.Severity("Succeeded / Failed"), "composed badge text");
                AssertEqual(StatusSeverityEnum.Info, StatusBadge.Severity("NotFailedYet"), "embedded word");
                AssertEqual(StatusSeverityEnum.Error, StatusBadge.Severity("LandingFailed"), "exact name");
                AssertEqual(StatusSeverityEnum.Error, StatusBadge.Severity(MissionStatusEnum.LandingFailed), "typed mission status");
                AssertEqual(StatusSeverityEnum.Warning, StatusBadge.Severity(CheckRunStatusEnum.Canceled), "enum fallback by exact name");
                AssertEqual("x Fallido", StatusBadge.Label("Failed", "Fallido"), "marker from the status, not the localized display");
            }));

            cases.Add(TuiCase.Sync(Suite, "entity_event_typed_accessors", "Entity change accessors accept defined names only", () =>
            {
                AssertEqual<MissionStatusEnum?>(MissionStatusEnum.Review, new EntityChangedEvent { Status = "Review" }.MissionStatus, "mission");
                AssertNull(new EntityChangedEvent { Status = "6" }.MissionStatus, "numeric rejected");
                AssertNull(new EntityChangedEvent { Status = "Review,Failed" }.MissionStatus, "flag list rejected");
                AssertEqual<CaptainStateEnum?>(CaptainStateEnum.Stalled, new EntityChangedEvent { State = "Stalled", Status = "Idle" }.CaptainState, "captain state preferred");
                AssertEqual<DeploymentStatusEnum?>(DeploymentStatusEnum.PendingApproval, new EntityChangedEvent { Status = "PendingApproval" }.DeploymentStatus, "deployment");
                AssertFalse(EnumNames.TryParse<AgentRuntimeEnum>("1", true, out AgentRuntimeEnum _), "numeric runtime rejected");
                AssertTrue(EnumNames.TryParse<AgentRuntimeEnum>(" mux ", true, out AgentRuntimeEnum mux) && mux == AgentRuntimeEnum.Mux, "name accepted");
            }));

            cases.Add(TuiCase.Sync(Suite, "approval_name_from_entity_name", "Approval items take the entity name from InboxItem.EntityName, not a stripped title", () =>
            {
                InboxItem inbox = new InboxItem { Kind = InboxItemKinds.Review, Title = "Awaiting review - Fix: login", EntityName = "Fix: login", EntityId = "msn_1", Href = "/missions/msn_1" };
                ApprovalItem? item = ApprovalSources.FromInbox(inbox);
                AssertNotNull(item, "mapped");
                AssertEqual(ApprovalKindEnum.MissionReview, item!.Kind, "kind");
                AssertEqual("Fix: login", item.EntityName, "entity name");
                AssertNull(ApprovalSources.FromInbox(new InboxItem { Kind = "Review", EntityId = "msn_2" }), "kind constants are exact");
            }));

            cases.Add(TuiCase.Sync(Suite, "rebuild_status_null_not_none", "Rebuild status is a nullable enum; the legacy \"none\" sentinel reads as null", () =>
            {
                AssertNull(ArmadaJson.Deserialize<RebuildStatus>("{\"Status\":\"none\"}")!.Status, "legacy none");
                AssertNull(ArmadaJson.Deserialize<RebuildStatus>("{\"Status\":null}")!.Status, "null");
                AssertNull(ArmadaJson.Deserialize<RebuildStatus>("{}")!.Status, "absent");
                AssertEqual<ServerRebuildStatusEnum?>(ServerRebuildStatusEnum.RolledBack, ArmadaJson.Deserialize<RebuildStatus>("{\"Status\":\"RolledBack\"}")!.Status, "typed");
                AssertThrows<JsonException>(() => ArmadaJson.Deserialize<RebuildStatus>("{\"Status\":\"Exploded\"}"));
                AssertFalse(ServerSettingsRules.RebuildDone(null), "no rebuild is not done");
                AssertTrue(ServerSettingsRules.RebuildDone(ServerRebuildStatusEnum.CuttingOver), "cutting over stops polling");
                AssertFalse(ServerSettingsRules.RebuildDone(ServerRebuildStatusEnum.Building), "building keeps polling");
                AssertTrue(ServerRebuildStatusEnum.Failed.IsTerminal(), "terminal");
            }));

            cases.Add(TuiCase.Sync(Suite, "timeout_by_code", "A timeout is recognized by its code, not its message", () =>
            {
                ArmadaApiException renamed = new ArmadaApiException("The request took too long", 0, ArmadaApiException.TimeoutCode, null, "req_1", "POST", "/api/v1/planning-sessions", null, null);
                AssertTrue(renamed.IsTimeout, "code decides");
                ArmadaApiException other = new ArmadaApiException("Request timed out", 0, null, null, "req_2", "POST", "/api/v1/planning-sessions", null, null);
                AssertFalse(other.IsTimeout, "message alone does not");
            }));

            cases.Add(TuiCase.Sync(Suite, "form_status_error_flag", "Form status error state is a flag, not a \"! \" prefix", () =>
            {
                FormDialog dialog = new FormDialog("Edit", new FormView(), new QueueDispatcher());
                dialog.SetStatus("! starts with a bang but is informational", false);
                AssertFalse(dialog.StatusIsError, "prefix is not an error");
                dialog.SetStatus("Name is required.", true);
                AssertTrue(dialog.StatusIsError, "flag is an error");
                dialog.SetStatus(null);
                AssertFalse(dialog.StatusIsError, "cleared");
            }));

            cases.Add(TuiCase.Sync(Suite, "home_alerts_typed", "Home alerts carry a severity and keyed message arguments", () =>
            {
                ArmadaStatus status = new ArmadaStatus { StalledCaptains = 2, TotalCaptains = 3, MissionsByStatus = new Dictionary<string, int> { ["Failed"] = 1 } };
                List<HomeAlert> alerts = HomeScreen.ComputeAlerts(status);
                AssertEqual(2, alerts.Count, "two alerts");
                AssertEqual(NotificationSeverityEnum.Error, alerts[0].Severity, "stalled is an error");
                AssertEqual("{{count}} captain(s) stalled -- recovery attempts exhausted.", alerts[0].Message, "keyed template");
                AssertEqual<object?>(2, alerts[0].Args["count"], "argument");
                AssertEqual(NotificationSeverityEnum.Warning, alerts[1].Severity, "failed missions warn");
                AssertEqual("/missions", alerts[1].Route, "route");
            }));

            cases.Add(TuiCase.Sync(Suite, "event_route_by_entity_type", "Event entity links route by EntityType, never by id prefix", () =>
            {
                AssertEqual("/missions/msn_1", ScreenOps.EntityRoute("mission", "msn_1"), "mission");
                AssertEqual("/merge-queue/mrg_1", ScreenOps.EntityRoute("merge-entry", "mrg_1"), "dashed spelling");
                AssertEqual("/merge-queue/mrg_2", ScreenOps.EntityRoute("MergeEntry", "mrg_2"), "pascal spelling");
                AssertEqual("/vessels/msn_odd", ScreenOps.EntityRoute("vessel", "msn_odd"), "id prefix ignored");
                AssertNull(ScreenOps.EntityRoute(null, "msn_1"), "no type, no route");
                AssertNull(ScreenOps.EntityRoute("papercut", "pcx_1"), "unknown type");
            }));

            cases.Add(TuiCase.Sync(Suite, "timeline_delete_by_source_type", "Timeline rows are deletable by SourceType, not a req_ id prefix", () =>
            {
                AssertFalse(ActivityScreen.CanDeleteEntry(new HistoricalTimelineEntry { SourceType = "Mission", SourceId = "req_lookalike" }), "prefix alone is not deletable");
                AssertTrue(ActivityScreen.CanDeleteEntry(new HistoricalTimelineEntry { SourceType = HistoricalTimelineSourceTypes.Request, SourceId = "rqh_1" }), "request rows are");
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_local_flag_and_turn_state", "Optimistic Ask messages use IsLocal; turn state and error text are typed", () =>
            {
                AskMessage persisted = new AskMessage { Id = "local-but-persisted" };
                AssertFalse(AskConversation.IsLocal(persisted), "id prefix alone is not local");
                AskMessage optimistic = new AskMessage { Id = "amg_x", IsLocal = true };
                AssertTrue(AskConversation.IsLocal(optimistic), "flag decides");
                AssertFalse(ArmadaJson.Serialize(optimistic).Contains("IsLocal"), "flag is not serialized");

                AskEvent? failed = AskEventParser.Parse(ArmadaSocketMessage.Parse("{\"type\":\"ask.turn\",\"data\":{\"threadId\":\"ath_1\",\"turnId\":\"t1\",\"state\":\"failed\",\"errorText\":\"boom\"}}"));
                AssertNotNull(failed, "parsed");
                AssertEqual(AskTurnStateEnum.Failed, failed!.State, "state");
                AssertEqual("boom", failed.Error, "error text field");

                AskEvent? tool = AskEventParser.Parse(ArmadaSocketMessage.Parse("{\"type\":\"ask.tool\",\"data\":{\"threadId\":\"ath_1\",\"turnId\":\"t1\",\"phase\":\"completed\",\"id\":\"c1\",\"name\":\"x\",\"ok\":false}}"));
                AssertEqual<ToolCallPhaseEnum?>(ToolCallPhaseEnum.Completed, tool!.ToolPhase, "tool phase");
            }));

            cases.Add(TuiCase.Sync(Suite, "quick_actions_union_by_token", "Quick actions read an array or a wrapper by JSON token type", () =>
            {
                AskQuickActionList? bare = ArmadaJson.Deserialize<AskQuickActionList>("  \n [{\"Name\":\"dispatch\"}]");
                AssertEqual("dispatch", bare!.Actions.Single().Name, "array after whitespace");
                AskQuickActionList? wrapped = ArmadaJson.Deserialize<AskQuickActionList>("{\"Objects\":[{\"Name\":\"status\"}]}");
                AssertEqual("status", wrapped!.Actions.Single().Name, "wrapper");
                AssertThrows<JsonException>(() => ArmadaJson.Deserialize<AskQuickActionList>("\"[not json]\""));
            }));

            cases.Add(TuiCase.Sync(Suite, "openapi_values_by_token", "OpenAPI type and example values are read by token type", () =>
            {
                ApiExplorerSchema? schema = JsonSerializer.Deserialize<ApiExplorerSchema>("{\"type\":[\"null\",\"integer\"],\"example\":\"[1,2]\",\"default\":{\"a\":1},\"enum\":[\"x\",3]}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                AssertEqual("integer", ApiExplorerSpec.TypeName(schema), "first non-null type");
                AssertEqual("[1,2]", ApiExplorerSpec.RawToString(schema!.Example), "string example that looks like an array stays a string");
                AssertEqual("{\"a\":1}", ApiExplorerSpec.RawToString(schema.Default), "object default as JSON");
                AssertEqual("3", ApiExplorerSpec.RawToString(schema.Enum![1]), "number");
            }));

            cases.Add(TuiCase.Sync(Suite, "readiness_provider_from_field", "Readiness provider labels come from InputProvider, not a value prefix", () =>
            {
                AssertEqual(" (File path)", OpsReadiness.ProviderSuffix(WorkflowInputReferenceProviderEnum.FilePath), "file");
                AssertEqual(" (1Password)", OpsReadiness.ProviderSuffix(WorkflowInputReferenceProviderEnum.OnePassword), "provider without a prefix form");
                AssertEqual("", OpsReadiness.ProviderSuffix(null), "a command such as \"env: X=1 make\" gets no provider label");
            }));

            cases.Add(TuiCase.Sync(Suite, "objective_source_number", "GitHub source number is exposed on the objective", () =>
            {
                Objective github = new Objective { SourceProvider = Objective.GitHubSourceProvider, SourceId = "jchristn/armada#42" };
                AssertEqual<int?>(42, github.SourceNumber, "number");
                AssertTrue(ArmadaJson.Serialize(github).Contains("\"SourceNumber\":42"), "serialized for clients");
                AssertNull(new Objective { SourceProvider = "Jira", SourceId = "PROJ#42" }.SourceNumber, "other providers");
                AssertNull(new Objective { SourceProvider = Objective.GitHubSourceProvider, SourceId = "o/r#4a" }.SourceNumber, "not a number");
            }));

            cases.Add(TuiCase.Sync(Suite, "diff_lines_by_structure", "Diff views classify lines from hunk counts, so content that looks like a header stays content", () =>
            {
                string diff = "diff --git a/notes.md b/notes.md\nindex 1..2 100644\n--- a/notes.md\n+++ b/notes.md\n@@ -1,1 +1,3 @@\n context\n+++ looks like a header\n+-- also content\n";
                List<UnifiedDiffFile> files = Armada.Core.Services.UnifiedDiffParser.Parse(diff, out List<UnifiedDiffLineKindEnum> kinds);
                AssertEqual(1, files.Count, "one file");
                AssertEqual(0, files[0].StartLineIndex, "section start");
                AssertEqual(diff.Split('\n').Length, kinds.Count, "one kind per line");
                AssertEqual(UnifiedDiffLineKindEnum.FileHeader, kinds[0], "file header");
                AssertEqual(UnifiedDiffLineKindEnum.Meta, kinds[3], "+++ path header");
                AssertEqual(UnifiedDiffLineKindEnum.HunkHeader, kinds[4], "hunk header");
                AssertEqual(UnifiedDiffLineKindEnum.Added, kinds[6], "+++ inside a hunk is an added line");
                AssertEqual(UnifiedDiffLineKindEnum.Added, kinds[7], "+-- inside a hunk is an added line");
                AssertEqual(2, files[0].AddedLineCount, "added count");

                DiffViewer viewer = new DiffViewer(diff);
                AssertEqual("notes.md", viewer.Files.Single(), "file list from the parser");
            }));

            cases.Add(TuiCase.Sync(Suite, "runtime_decisions_typed", "Runtime decisions use AgentRuntimeEnum", () =>
            {
                AssertTrue(CaptainForm.SupportsAutoApprove(AgentRuntimeEnum.Mux), "mux");
                AssertFalse(CaptainForm.SupportsAutoApprove(AgentRuntimeEnum.ApiEndpoint), "api endpoint");
                AssertFalse(CaptainForm.SupportsAutoApprove(null), "none");
                AssertNull(CaptainForm.RuntimeOptionsJson(AgentRuntimeEnum.Codex, new MuxCaptainOptions(), true), "no mux options for codex");
                AssertTrue(AskController.InstructionsUrl(AgentRuntimeEnum.Mux).EndsWith("INSTRUCTIONS_FOR_MUX.md", StringComparison.Ordinal), "mux docs");
                AssertTrue(AskController.InstructionsUrl(null).EndsWith("MCP_API.md", StringComparison.Ordinal), "fallback docs");

                AssertNull(new HarborLaunchRequest { Runtime = "1" }.RuntimeType, "numeric harbor runtime rejected (was Codex)");
                AssertEqual<AgentRuntimeEnum?>(AgentRuntimeEnum.ClaudeCode, new HarborLaunchRequest { Runtime = "claudecode" }.RuntimeType, "name accepted");

                CaptainToolAccessResult? access = ArmadaJson.Deserialize<CaptainToolAccessResult>("{\"Runtime\":\"ApiEndpoint\",\"Servers\":[{\"Name\":\"armada\",\"SourceKind\":\"McpServer\"}],\"Tools\":[{\"Name\":\"t\",\"SourceKind\":\"RuntimeBuiltIn\"}]}");
                AssertEqual<AgentRuntimeEnum?>(AgentRuntimeEnum.ApiEndpoint, access!.Runtime, "tool access runtime");
                AssertEqual(CaptainToolSourceKindEnum.McpServer, access.Servers.Single().SourceKind, "server source kind");
                AssertEqual(CaptainToolSourceKindEnum.RuntimeBuiltIn, access.Tools.Single().SourceKind, "tool source kind");
            }));

            cases.Add(TuiCase.Sync(Suite, "doctor_status_typed", "Doctor check status is an enum on the wire names", () =>
            {
                List<DoctorCheck>? checks = ArmadaJson.Deserialize<List<DoctorCheck>>("[{\"Name\":\"Git\",\"Status\":\"Pass\"},{\"Name\":\"Database\",\"Status\":\"Warn\"}]");
                AssertEqual(DoctorCheckStatusEnum.Warn, checks![1].Status, "warn");
                AssertEqual("Warnings", DiagnosticsScreen.Verdict(checks), "verdict");
            }));

            cases.Add(TuiCase.Sync(Suite, "watch_colors_by_action_words", "Helm watch colors event types by whole action words", () =>
            {
                AssertEqual("red", WatchCommand.EventTypeColor("mission.landing_failed"), "landing failed");
                AssertEqual("green", WatchCommand.EventTypeColor("mission.completed"), "completed");
                AssertEqual("dim", WatchCommand.EventTypeColor("voyage.incompleted"), "a word containing completed is not completed");
                AssertEqual("dim", WatchCommand.EventTypeColor("terror.raised"), "the domain segment is not the action");
            }));

            cases.Add(TuiCase.Sync(Suite, "relative_time_keyed", "Relative times are keyed templates with arguments", () =>
            {
                LocalizationService loc = new LocalizationService();
                DateTime now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
                AssertEqual("12s ago", loc.FormatRelative(now.AddSeconds(-12), now), "seconds");
                AssertEqual("3h ago", loc.FormatRelative(now.AddHours(-3), now), "hours");
            }));

            return new TestSuiteDescriptor(Suite, "Fragility R4: typed fields in clients, TUI, and Helm", cases);
        }

        #endregion
    }
}
