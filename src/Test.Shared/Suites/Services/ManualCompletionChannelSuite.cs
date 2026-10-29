namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server;
    using Armada.Server.Mcp;
    using Armada.Server.Mcp.Tools;
    using Armada.Server.Routes;
    using Armada.Server.WebSocket;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Manual Complete, timestamp, audit, review, and dependent-stage behavior of the REST, MCP, and
    /// WebSocket status operations. The landing callback is the production <c>OnMissionComplete</c> seam.
    /// </summary>
    public sealed class ManualCompletionChannelSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ManualCompletionChannel";

        private const string SampleDiff = "diff --git a/notes.txt b/notes.txt\n--- a/notes.txt\n+++ b/notes.txt\n@@ -1 +1 @@\n-old\n+new\n";

        private static readonly JsonSerializerOptions _ReplyOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for manual completion across REST, MCP, and WebSocket.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("rest_complete_with_active_dock_runs_landing_once", "REST Complete with an active dock runs landing once", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.WorkProduced, true).ConfigureAwait(false);
                    LandingProbe probe = new LandingProbe(testDb.Driver);
                    Mission returned = await fx.TransitionRestAsync(mission.Id, "Complete", probe).ConfigureAwait(false);
                    AssertEqual(1, probe.Calls, "landing callback count");
                    AssertEqual(MissionStatusEnum.WorkProduced, probe.StatusSeen, "status when landing starts");
                    AssertEqual(MissionStatusEnum.PullRequestOpen, returned.Status, "returned row");
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.PullRequestOpen, stored.Status, "stored row");
                }
            }));

            cases.Add(CaseAsync("mcp_complete_with_active_dock_runs_landing_once", "MCP Complete with an active dock runs landing once", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.WorkProduced, true).ConfigureAwait(false);
                    LandingProbe probe = fx.AttachLandingProbe(testDb.Driver);
                    Mission returned = await fx.TransitionMcpAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertEqual(1, probe.Calls, "landing callback count");
                    AssertEqual(MissionStatusEnum.WorkProduced, probe.StatusSeen, "status when landing starts");
                    AssertEqual(MissionStatusEnum.PullRequestOpen, returned.Status, "returned row");
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.PullRequestOpen, stored.Status, "stored row");
                }
            }));

            cases.Add(CaseAsync("websocket_complete_with_active_dock_runs_landing_once", "WebSocket Complete with an active dock runs landing once", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.WorkProduced, true).ConfigureAwait(false);
                    LandingProbe probe = fx.AttachLandingProbe(testDb.Driver);
                    Mission returned = await fx.TransitionWebSocketAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertEqual(1, probe.Calls, "landing callback count");
                    AssertEqual(MissionStatusEnum.WorkProduced, probe.StatusSeen, "status when landing starts");
                    AssertEqual(MissionStatusEnum.PullRequestOpen, returned.Status, "returned row");
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.PullRequestOpen, stored.Status, "stored row");
                }
            }));

            cases.Add(CaseAsync("mcp_complete_without_active_dock_records_audit_event", "MCP Complete without an active dock records the audit event", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.WorkProduced, false, MissionModeEnum.Audit).ConfigureAwait(false);
                    Mission returned = await fx.TransitionMcpAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, returned.Status, "returned row");
                    await AssertNoDockAuditAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertTrue(stored.CompletedUtc != null, "CompletedUtc");
                }
            }));

            cases.Add(CaseAsync("websocket_complete_without_active_dock_records_audit_event", "WebSocket Complete without an active dock records the audit event", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.WorkProduced, false, MissionModeEnum.Audit).ConfigureAwait(false);
                    Mission returned = await fx.TransitionWebSocketAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, returned.Status, "returned row");
                    await AssertNoDockAuditAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertTrue(stored.CompletedUtc != null, "CompletedUtc");
                }
            }));

            cases.Add(CaseAsync("mcp_in_progress_sets_started_utc", "MCP InProgress sets StartedUtc when it is empty", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.Assigned, false).ConfigureAwait(false);
                    await fx.TransitionMcpAsync(mission.Id, "InProgress").ConfigureAwait(false);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, stored.Status, "status");
                    AssertTrue(stored.StartedUtc != null, "StartedUtc");
                }
            }));

            cases.Add(CaseAsync("websocket_in_progress_sets_started_utc", "WebSocket InProgress sets StartedUtc when it is empty", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.Assigned, false).ConfigureAwait(false);
                    await fx.TransitionWebSocketAsync(mission.Id, "InProgress").ConfigureAwait(false);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, stored.Status, "status");
                    AssertTrue(stored.StartedUtc != null, "StartedUtc");
                }
            }));

            cases.Add(CaseAsync("mcp_in_progress_keeps_existing_started_utc", "MCP InProgress keeps an existing StartedUtc", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.Assigned, false).ConfigureAwait(false);
                    mission.StartedUtc = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    Mission before = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    await fx.TransitionMcpAsync(mission.Id, "InProgress").ConfigureAwait(false);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(before.StartedUtc, stored.StartedUtc, "StartedUtc");
                }
            }));

            cases.Add(CaseAsync("websocket_in_progress_keeps_existing_started_utc", "WebSocket InProgress keeps an existing StartedUtc", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.Assigned, false).ConfigureAwait(false);
                    mission.StartedUtc = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    Mission before = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    await fx.TransitionWebSocketAsync(mission.Id, "InProgress").ConfigureAwait(false);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(before.StartedUtc, stored.StartedUtc, "StartedUtc");
                }
            }));

            cases.Add(CaseAsync("mcp_landing_failed_sets_completed_utc", "MCP LandingFailed sets CompletedUtc", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.WorkProduced, false).ConfigureAwait(false);
                    await fx.TransitionMcpAsync(mission.Id, "LandingFailed").ConfigureAwait(false);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, stored.Status, "status");
                    AssertTrue(stored.CompletedUtc != null, "CompletedUtc");
                }
            }));

            cases.Add(CaseAsync("websocket_landing_failed_sets_completed_utc", "WebSocket LandingFailed sets CompletedUtc", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.WorkProduced, false).ConfigureAwait(false);
                    await fx.TransitionWebSocketAsync(mission.Id, "LandingFailed").ConfigureAwait(false);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, stored.Status, "status");
                    AssertTrue(stored.CompletedUtc != null, "CompletedUtc");
                }
            }));

            cases.Add(CaseAsync("websocket_review_complete_is_refused", "WebSocket refuses to leave Review by writing Complete", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.Review, true).ConfigureAwait(false);
                    LandingProbe probe = fx.AttachLandingProbe(testDb.Driver);
                    WebSocketCommandError error = await fx.TransitionWebSocketErrorAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertTrue(error.Error.IndexOf("review gate", StringComparison.Ordinal) >= 0, error.Error);
                    AssertEqual(0, probe.Calls, "landing callback count");
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Review, stored.Status, "status");
                }
            }));

            cases.Add(CaseAsync("mcp_review_complete_is_refused", "MCP refuses to leave Review by writing Complete", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.Review, false).ConfigureAwait(false);
                    McpToolError error = await fx.TransitionMcpErrorAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertTrue(error.Error.IndexOf("review gate", StringComparison.Ordinal) >= 0, error.Error);
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Review, stored.Status, "status");
                }
            }));

            cases.Add(CaseAsync("mcp_invalid_transition_does_not_change_the_mission", "MCP rejects an illegal transition without writing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.Pending, false).ConfigureAwait(false);
                    McpToolError error = await fx.TransitionMcpErrorAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertEqual(McpToolErrorCodeEnum.Conflict, error.ErrorCode, "error code");
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Pending, stored.Status, "status");
                }
            }));

            cases.Add(CaseAsync("websocket_invalid_transition_does_not_change_the_mission", "WebSocket rejects an illegal transition without writing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.Pending, false).ConfigureAwait(false);
                    WebSocketCommandError error = await fx.TransitionWebSocketErrorAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertEqual(WebSocketCommandErrorCodeEnum.Conflict, error.Code, "error code");
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Pending, stored.Status, "status");
                }
            }));

            cases.Add(CaseAsync("rest_review_dependent_stage_that_requires_review_lands_the_mission", "REST Complete of a Review stage that requires review lands it instead of returning it to Review", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    DependentPair pair = await fx.CreateDependentPairAsync(MissionStatusEnum.Review, true, true).ConfigureAwait(false);
                    LandingProbe probe = new LandingProbe(testDb.Driver);
                    Mission returned = await fx.TransitionRestAsync(pair.Parent.Id, "Complete", probe).ConfigureAwait(false);
                    AssertEqual(1, probe.Calls, "landing callback count");
                    AssertEqual(MissionStatusEnum.WorkProduced, probe.StatusSeen, "status when landing starts");
                    AssertEqual(MissionStatusEnum.PullRequestOpen, returned.Status, "returned row");
                    Mission parent = await ReadMissionAsync(testDb.Driver, pair.Parent.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.PullRequestOpen, parent.Status, "parent status");
                    AssertEqual(pair.Parent.ReviewRequestedUtc, parent.ReviewRequestedUtc, "ReviewRequestedUtc");
                    AssertEqual(pair.Parent.ReviewComment, parent.ReviewComment, "ReviewComment");
                    Mission child = await ReadMissionAsync(testDb.Driver, pair.Child.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Pending, child.Status, "dependent status");
                }
            }));

            cases.Add(CaseAsync("rest_review_dependent_stage_without_review_requirement_hands_off", "REST Complete of a Review stage that needs no review hands off without landing", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    DependentPair pair = await fx.CreateDependentPairAsync(MissionStatusEnum.Review).ConfigureAwait(false);
                    LandingProbe probe = new LandingProbe(testDb.Driver);
                    await fx.TransitionRestAsync(pair.Parent.Id, "Complete", probe).ConfigureAwait(false);
                    await AssertHandoffAsync(testDb.Driver, pair, probe).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("rest_dependent_stage_handoff_without_transition_is_a_conflict", "REST reports a conflict and records nothing when the completion handler leaves the mission in place", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    DependentPair pair = await fx.CreateDependentPairAsync(MissionStatusEnum.Review).ConfigureAwait(false);
                    ArmadaEvent unverified = new ArmadaEvent(StageCheckEvents.EventType, "stage check");
                    unverified.MissionId = pair.Parent.Id;
                    unverified.Payload = "{not json";
                    await testDb.Driver.Events.CreateAsync(unverified).ConfigureAwait(false);

                    LandingProbe probe = new LandingProbe(testDb.Driver);
                    string message = await fx.TransitionRestErrorAsync(pair.Parent.Id, "Complete", probe, 409).ConfigureAwait(false);
                    AssertTrue(message.IndexOf("Review", StringComparison.Ordinal) >= 0, message);
                    AssertEqual(0, probe.Calls, "landing callback count");
                    Mission parent = await ReadMissionAsync(testDb.Driver, pair.Parent.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Review, parent.Status, "parent status");
                    Mission child = await ReadMissionAsync(testDb.Driver, pair.Child.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Pending, child.Status, "dependent status");
                    List<ArmadaEvent> changes = await testDb.Driver.Events.EnumerateByTypeAsync("mission.status_changed", 50).ConfigureAwait(false);
                    foreach (ArmadaEvent change in changes)
                        AssertTrue(change.MissionId != pair.Parent.Id, "mission.status_changed was recorded for a mission that did not change");
                }
            }));

            cases.Add(CaseAsync("mcp_running_dependent_stage_is_refused", "MCP refuses Complete of a running dependent stage", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    DependentPair pair = await fx.CreateDependentPairAsync(MissionStatusEnum.InProgress).ConfigureAwait(false);
                    LandingProbe probe = fx.AttachLandingProbe(testDb.Driver);
                    McpToolError error = await fx.TransitionMcpErrorAsync(pair.Parent.Id, "Complete").ConfigureAwait(false);
                    AssertTrue(error.Error.IndexOf("still owns this mission", StringComparison.Ordinal) >= 0, error.Error);
                    await AssertRefusedAsync(testDb.Driver, pair, probe).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("websocket_running_dependent_stage_is_refused", "WebSocket refuses Complete of a running dependent stage", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    DependentPair pair = await fx.CreateDependentPairAsync(MissionStatusEnum.InProgress).ConfigureAwait(false);
                    LandingProbe probe = fx.AttachLandingProbe(testDb.Driver);
                    WebSocketCommandError error = await fx.TransitionWebSocketErrorAsync(pair.Parent.Id, "Complete").ConfigureAwait(false);
                    AssertTrue(error.Error.IndexOf("still owns this mission", StringComparison.Ordinal) >= 0, error.Error);
                    await AssertRefusedAsync(testDb.Driver, pair, probe).ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("websocket_complete_without_dock_takes_the_no_change_path", "WebSocket Complete of a dockless WorkProduced mission already in the target branch completes without landing", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    fx.StubGit.IsAncestorResult = true;
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.WorkProduced, false).ConfigureAwait(false);
                    mission.CommitHash = "0123456789abcdef0123456789abcdef01234567";
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    LandingProbe probe = fx.AttachLandingProbe(testDb.Driver);
                    Mission returned = await fx.TransitionWebSocketAsync(mission.Id, "Complete").ConfigureAwait(false);
                    AssertEqual(0, probe.Calls, "landing callback count");
                    AssertEqual(MissionStatusEnum.Complete, returned.Status, "returned row");
                    Mission stored = await ReadMissionAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, stored.Status, "stored row");
                    AssertTrue(stored.CompletedUtc != null, "CompletedUtc");
                }
            }));

            cases.Add(CaseAsync("mcp_work_produced_request_event_names_the_requested_status", "MCP WorkProduced request records a status event that names WorkProduced, not completion", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ChannelFixture fx = await ChannelFixture.OpenAsync(testDb.Driver).ConfigureAwait(false);
                    Mission mission = await fx.CreateMissionAsync(MissionStatusEnum.LandingFailed, true).ConfigureAwait(false);
                    LandingProbe probe = fx.AttachLandingProbe(testDb.Driver);
                    await fx.TransitionMcpAsync(mission.Id, "WorkProduced").ConfigureAwait(false);
                    AssertEqual(1, probe.Calls, "landing callback count");
                    List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByTypeAsync("mission.status_changed", 50).ConfigureAwait(false);
                    bool found = false;
                    foreach (ArmadaEvent evt in events)
                    {
                        string text = evt.Message ?? "";
                        if (evt.MissionId != mission.Id || text.IndexOf("landed as", StringComparison.Ordinal) < 0) continue;
                        found = true;
                        AssertTrue(text.IndexOf("WorkProduced", StringComparison.Ordinal) >= 0, text);
                        AssertTrue(text.IndexOf("completed", StringComparison.Ordinal) < 0, text);
                    }
                    AssertTrue(found, "landing status event");
                }
            }));

            cases.Add(CaseAsync("mcp_review_broadcasts_approval_needed", "MCP Review broadcasts approval-needed", TestTags.Positive, async () =>
            {
                await AssertApprovalBroadcastAsync(true).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("websocket_review_broadcasts_approval_needed", "WebSocket Review broadcasts approval-needed", TestTags.Positive, async () =>
            {
                await AssertApprovalBroadcastAsync(false).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Manual Completion Channels",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task AssertHandoffAsync(DatabaseDriver database, DependentPair pair, LandingProbe probe)
        {
            AssertEqual(0, probe.Calls, "landing callback count");
            Mission parent = await ReadMissionAsync(database, pair.Parent.Id).ConfigureAwait(false);
            AssertEqual(MissionStatusEnum.WorkProduced, parent.Status, "parent status");
            Mission child = await ReadMissionAsync(database, pair.Child.Id).ConfigureAwait(false);
            string description = child.Description ?? "";
            AssertTrue(description.IndexOf("Prior Stage Output", StringComparison.Ordinal) >= 0, "dependent description");
            AssertTrue(description.IndexOf(pair.Parent.Id, StringComparison.Ordinal) >= 0, "dependent description names the parent");
        }

        private static async Task AssertRefusedAsync(DatabaseDriver database, DependentPair pair, LandingProbe probe)
        {
            AssertEqual(0, probe.Calls, "landing callback count");
            Mission parent = await ReadMissionAsync(database, pair.Parent.Id).ConfigureAwait(false);
            AssertEqual(MissionStatusEnum.InProgress, parent.Status, "parent status");
            Mission child = await ReadMissionAsync(database, pair.Child.Id).ConfigureAwait(false);
            AssertEqual(pair.Child.Description, child.Description, "dependent description");
            AssertEqual(MissionStatusEnum.Pending, child.Status, "dependent status");
        }

        private static async Task AssertNoDockAuditAsync(DatabaseDriver database, string missionId)
        {
            List<ArmadaEvent> events = await database.Events.EnumerateByTypeAsync("mission.manual_complete_no_dock", 50).ConfigureAwait(false);
            bool found = false;
            foreach (ArmadaEvent evt in events)
            {
                if (evt.MissionId == missionId) found = true;
            }
            AssertTrue(found, "mission.manual_complete_no_dock");
        }

        private static async Task<Mission> ReadMissionAsync(DatabaseDriver database, string missionId)
        {
            Mission? mission = await database.Missions.ReadAsync(missionId).ConfigureAwait(false);
            AssertTrue(mission != null, "mission " + missionId);
            return mission!;
        }

        private static async Task AssertApprovalBroadcastAsync(bool mcp)
        {
            E2EServerFixture server = await E2EServerFixture.AcquireAsync(SuiteId).ConfigureAwait(false);
            string missionId = await CreateMissionViaRestAsync(server.AuthClient).ConfigureAwait(false);
            await PutStatusAsync(server.AuthClient, missionId, "Assigned").ConfigureAwait(false);
            await PutStatusAsync(server.AuthClient, missionId, "InProgress").ConfigureAwait(false);

            using (ClientWebSocket socket = await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(server.RestPort).ConfigureAwait(false))
            {
                await SubscribeAsync(socket).ConfigureAwait(false);
                if (mcp)
                {
                    if (!server.McpClient.DefaultRequestHeaders.Contains("X-Api-Key"))
                        server.McpClient.DefaultRequestHeaders.Add("X-Api-Key", server.ApiKey);
                    string body = await McpTestCalls.CallToolAsync(server.McpClient, "transition_mission_status", new { missionId = missionId, status = "Review" }).ConfigureAwait(false);
                    ToolPayload payload = ReadToolPayload(body);
                    AssertTrue(payload.ErrorCode == null, payload.Error ?? "tool error");
                    AssertEqual(MissionStatusEnum.Review, payload.Status, "tool status");
                }
                else
                {
                    await SendReviewCommandAsync(server.RestPort, missionId).ConfigureAwait(false);
                }

                bool saw = false;
                try
                {
                    saw = await WaitForApprovalAsync(socket, missionId, 1500).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    saw = false;
                }

                Mission stored = await GetMissionAsync(server.AuthClient, missionId).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Review, stored.Status, "stored status");
                AssertTrue(saw, "approval-needed was not broadcast");
            }
        }

        private static async Task<string> CreateMissionViaRestAsync(HttpClient client)
        {
            string title = "review-broadcast-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            using (HttpResponseMessage response = await client.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(new { Title = title })).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertTrue(response.IsSuccessStatusCode, body);
                MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                if (wrapper.Mission != null) return wrapper.Mission.Id;
                Mission mission = JsonHelper.Deserialize<Mission>(body);
                return mission.Id;
            }
        }

        private static async Task PutStatusAsync(HttpClient client, string missionId, string status)
        {
            using (HttpResponseMessage response = await client.PutAsync("/api/v1/missions/" + missionId + "/status", JsonHelper.ToJsonContent(new { status = status })).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertTrue(response.IsSuccessStatusCode, body);
            }
        }

        private static async Task<Mission> GetMissionAsync(HttpClient client, string missionId)
        {
            using (HttpResponseMessage response = await client.GetAsync("/api/v1/missions/" + missionId).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertTrue(response.IsSuccessStatusCode, body);
                return JsonHelper.Deserialize<Mission>(body);
            }
        }

        private static async Task SubscribeAsync(ClientWebSocket socket)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonHelper.Serialize(new { Route = "subscribe" }));
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            bool saw = await WaitForTypeAsync(socket, "status.snapshot", null, 5000).ConfigureAwait(false);
            AssertTrue(saw, "status.snapshot");
        }

        private static async Task SendReviewCommandAsync(int restPort, string missionId)
        {
            using (ClientWebSocket socket = await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(restPort).ConfigureAwait(false))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonHelper.Serialize(new
                {
                    Route = "command",
                    action = "transition_mission_status",
                    id = missionId,
                    status = "Review"
                }));
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                bool saw = await WaitForTypeAsync(socket, "command.result", null, 5000).ConfigureAwait(false);
                AssertTrue(saw, "command.result");
            }
        }

        private static async Task<bool> WaitForApprovalAsync(ClientWebSocket socket, string missionId, int timeoutMs)
        {
            return await WaitForTypeAsync(socket, "approval-needed", missionId, timeoutMs).ConfigureAwait(false);
        }

        private static async Task<bool> WaitForTypeAsync(ClientWebSocket socket, string type, string? missionId, int timeoutMs)
        {
            byte[] buffer = new byte[1048576];
            using (CancellationTokenSource cts = new CancellationTokenSource(timeoutMs))
            {
                while (true)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    SocketEnvelope? envelope = JsonSerializer.Deserialize<SocketEnvelope>(json, _ReplyOptions);
                    if (envelope == null || envelope.Type != type) continue;
                    if (missionId == null) return true;
                    if (envelope.Data != null && (envelope.Data.MissionId == missionId || envelope.Data.EntityId == missionId))
                        return true;
                }
            }
        }

        private static ToolPayload ReadToolPayload(string rpcBody)
        {
            RpcBody rpc = JsonHelper.Deserialize<RpcBody>(rpcBody);
            AssertTrue(rpc.Result != null && rpc.Result.Content != null && rpc.Result.Content.Count > 0, rpcBody);
            string text = rpc.Result!.Content![0].Text ?? "";
            return JsonHelper.Deserialize<ToolPayload>(text);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Nested-Types

        private sealed class DependentPair
        {
            public Mission Parent = null!;
            public Mission Child = null!;
        }

        private sealed class SocketEnvelope
        {
            public string? Type { get; set; }
            public SocketPayload? Data { get; set; }
        }

        private sealed class SocketPayload
        {
            public string? MissionId { get; set; }
            public string? EntityId { get; set; }
        }

        private sealed class RpcBody
        {
            public RpcResultBody? Result { get; set; }
        }

        private sealed class RpcResultBody
        {
            public List<RpcContentBody>? Content { get; set; }
        }

        private sealed class RpcContentBody
        {
            public string? Text { get; set; }
        }

        private sealed class ToolPayload
        {
            public MissionStatusEnum Status { get; set; }
            public McpToolErrorCodeEnum? ErrorCode { get; set; }
            public string? Error { get; set; }
        }

        private sealed class CommandReply
        {
            public string? Type { get; set; }
            public Mission? Data { get; set; }
            public string? Error { get; set; }
        }

        private sealed class LandingProbe
        {
            private readonly DatabaseDriver _Database;

            public LandingProbe(DatabaseDriver database)
            {
                _Database = database ?? throw new ArgumentNullException(nameof(database));
            }

            public int Calls { get; private set; }

            public MissionStatusEnum StatusSeen { get; private set; }

            public Func<Mission, Dock, Task> Callback
            {
                get
                {
                    return async (Mission mission, Dock dock) =>
                    {
                        Calls = Calls + 1;
                        Mission? row = await _Database.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                        StatusSeen = row != null ? row.Status : mission.Status;
                        if (row != null)
                        {
                            row.Status = MissionStatusEnum.PullRequestOpen;
                            row.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Missions.UpdateAsync(row).ConfigureAwait(false);
                        }
                    };
                }
            }
        }

        private sealed class ChannelFixture
        {
            private readonly DatabaseDriver _Database;
            private readonly ArmadaSettings _Settings;
            private readonly LoggingModule _Logging;
            private readonly IGitService _Git;
            private readonly IMissionService _Missions;
            private readonly Dictionary<string, Func<JsonElement?, Task<object>>> _McpHandlers;
            private readonly WebSocketCommandHandler _WebSocket;
            private readonly ILandingService _Landing;
            private readonly LandingPreviewService _LandingPreview;
            private readonly GitHubIntegrationService _GitHub;
            private readonly string _Token;

            private ChannelFixture(
                DatabaseDriver database,
                ArmadaSettings settings,
                LoggingModule logging,
                IGitService git,
                IMissionService missions,
                AdmiralService admiral,
                Dictionary<string, Func<JsonElement?, Task<object>>> mcpHandlers,
                WebSocketCommandHandler webSocket,
                ILandingService landing,
                LandingPreviewService landingPreview,
                GitHubIntegrationService gitHub,
                string token)
            {
                _Database = database;
                _Settings = settings;
                _Logging = logging;
                _Git = git;
                _Missions = missions;
                Admiral = admiral;
                _McpHandlers = mcpHandlers;
                _WebSocket = webSocket;
                _Landing = landing;
                _LandingPreview = landingPreview;
                _GitHub = gitHub;
                _Token = token;
            }

            public AdmiralService Admiral { get; }

            public StubGitService StubGit
            {
                get { return (StubGitService)_Git; }
            }

            public static Task<ChannelFixture> OpenAsync(DatabaseDriver database)
            {
                string root = TestTemp.NewDirectory("manual_status");
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                logging.Settings.FileLogging = FileLoggingMode.Disabled;

                ArmadaSettings settings = new ArmadaSettings();
                settings.DataDirectory = root;
                settings.SettingsFilePath = System.IO.Path.Combine(root, "settings.json");
                settings.DatabasePath = System.IO.Path.Combine(root, "armada.db");
                settings.LogDirectory = System.IO.Path.Combine(root, "logs");
                settings.DocksDirectory = System.IO.Path.Combine(root, "docks");
                settings.ReposDirectory = System.IO.Path.Combine(root, "repos");

                IGitService git = new StubGitService();
                IDockService docks = new DockService(logging, database, settings, git);
                ICaptainService captains = new CaptainService(logging, database, settings, git, docks);
                IMissionService missions = new MissionService(logging, database, settings, docks, captains);
                IVoyageService voyages = new VoyageService(logging, database);
                AdmiralService admiral = new AdmiralService(logging, database, settings, captains, missions, voyages, docks);
                admiral.OnCaptureDiff = (Mission mission, Dock dock) =>
                {
                    mission.CommitHash = "0123456789abcdef0123456789abcdef01234567";
                    return Task.CompletedTask;
                };
                ILandingService landing = new LandingService(logging, database, settings, git);
                LandingPreviewService landingPreview = new LandingPreviewService(database, logging, settings);
                WorkflowProfileService profiles = new WorkflowProfileService(database, logging);
                VesselReadinessService readiness = new VesselReadinessService(database, profiles, logging);
                CheckRunService checkRuns = new CheckRunService(database, profiles, readiness, logging);
                DeploymentEnvironmentService environments = new DeploymentEnvironmentService(database, profiles, logging);
                DeploymentService deployments = new DeploymentService(database, profiles, environments, checkRuns, logging);
                GitHubIntegrationService gitHub = new GitHubIntegrationService(database, new ObjectiveService(database), checkRuns, deployments, settings, logging);
                IMergeQueueService mergeQueue = new MergeQueueService(logging, database, settings, git);

                Dictionary<string, Func<JsonElement?, Task<object>>> handlers = new Dictionary<string, Func<JsonElement?, Task<object>>>();
                McpMissionTools.Register(
                    (name, description, schema, handler) => handlers[name] = handler,
                    database,
                    admiral,
                    settings,
                    git,
                    landing,
                    missions,
                    statusMissionService: missions);

                JsonSerializerOptions jsonOptions = new JsonSerializerOptions();
                jsonOptions.PropertyNameCaseInsensitive = true;
                WebSocketCommandHandler webSocket = new WebSocketCommandHandler(
                    admiral,
                    database,
                    mergeQueue,
                    settings,
                    git,
                    null,
                    jsonOptions,
                    (Mission mission, string? status) => { },
                    (Voyage voyage, string? status) => { });
                webSocket.Missions = missions;

                string token = Guid.NewGuid().ToString("N").Substring(0, 12);
                return Task.FromResult(new ChannelFixture(database, settings, logging, git, missions, admiral, handlers, webSocket, landing, landingPreview, gitHub, token));
            }

            public LandingProbe AttachLandingProbe(DatabaseDriver database)
            {
                LandingProbe probe = new LandingProbe(database);
                Admiral.OnMissionComplete = probe.Callback;
                return probe;
            }

            public async Task<Mission> CreateMissionAsync(MissionStatusEnum status, bool activeDock, MissionModeEnum mode = MissionModeEnum.Implementation)
            {
                Vessel vessel = await CreateVesselAsync().ConfigureAwait(false);
                Captain? captain = null;
                Dock? dock = null;
                if (activeDock)
                {
                    captain = await CreateCaptainAsync().ConfigureAwait(false);
                    dock = await CreateDockAsync(vessel.Id, captain.Id, System.IO.Path.Combine(_Settings.DocksDirectory, "manual-" + _Token)).ConfigureAwait(false);
                }

                Mission mission = new Mission("manual-status-" + _Token);
                mission.Mode = mode;
                mission.TenantId = Constants.DefaultTenantId;
                mission.UserId = Constants.DefaultUserId;
                mission.Status = status;
                mission.VesselId = vessel.Id;
                mission.Persona = "Worker";
                if (captain != null) mission.CaptainId = captain.Id;
                if (dock != null) mission.DockId = dock.Id;
                await _Database.Missions.CreateAsync(mission).ConfigureAwait(false);
                if (captain != null && dock != null)
                {
                    captain.CurrentMissionId = mission.Id;
                    captain.CurrentDockId = dock.Id;
                    await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                }
                return mission;
            }

            public async Task<DependentPair> CreateDependentPairAsync(MissionStatusEnum parentStatus, bool requiresReview = false, bool landable = false)
            {
                Voyage voyage = new Voyage("stage-voyage-" + _Token);
                voyage.TenantId = Constants.DefaultTenantId;
                voyage.UserId = Constants.DefaultUserId;
                await _Database.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                Vessel vessel = await CreateVesselAsync().ConfigureAwait(false);
                Captain captain = await CreateCaptainAsync().ConfigureAwait(false);
                Dock dock = await CreateDockAsync(vessel.Id, captain.Id, landable ? System.IO.Path.Combine(_Settings.DocksDirectory, "stage-" + _Token) : "").ConfigureAwait(false);

                Mission parent = new Mission("parent-stage-" + _Token);
                parent.TenantId = Constants.DefaultTenantId;
                parent.UserId = Constants.DefaultUserId;
                parent.Status = parentStatus;
                parent.VesselId = vessel.Id;
                parent.VoyageId = voyage.Id;
                parent.CaptainId = captain.Id;
                parent.DockId = dock.Id;
                parent.Persona = "Worker";
                parent.BranchName = "armada/stage-" + _Token;
                parent.DiffSnapshot = SampleDiff;
                parent.RequiresReview = requiresReview;
                if (parentStatus == MissionStatusEnum.Review)
                {
                    parent.ReviewRequestedUtc = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                    parent.ReviewComment = "awaiting approval";
                }
                await _Database.Missions.CreateAsync(parent).ConfigureAwait(false);

                captain.CurrentMissionId = parent.Id;
                captain.CurrentDockId = dock.Id;
                await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);

                Mission child = new Mission("child-stage-" + _Token, "next stage");
                child.TenantId = Constants.DefaultTenantId;
                child.UserId = Constants.DefaultUserId;
                child.Status = MissionStatusEnum.Pending;
                child.VoyageId = voyage.Id;
                child.DependsOnMissionId = parent.Id;
                child.Persona = "Worker";
                await _Database.Missions.CreateAsync(child).ConfigureAwait(false);

                DependentPair pair = new DependentPair();
                pair.Parent = parent;
                pair.Child = child;
                return pair;
            }

            public async Task<Mission> TransitionMcpAsync(string missionId, string status)
            {
                object result = await InvokeMcpAsync(missionId, status).ConfigureAwait(false);
                Mission? mission = result as Mission;
                AssertTrue(mission != null, result.GetType().Name);
                return mission!;
            }

            public async Task<McpToolError> TransitionMcpErrorAsync(string missionId, string status)
            {
                object result = await InvokeMcpAsync(missionId, status).ConfigureAwait(false);
                McpToolError? error = result as McpToolError;
                AssertTrue(error != null, result.GetType().Name);
                return error!;
            }

            public async Task<Mission> TransitionWebSocketAsync(string missionId, string status)
            {
                object result = await InvokeWebSocketAsync(missionId, status).ConfigureAwait(false);
                string json = JsonSerializer.Serialize(result, _ReplyOptions);
                CommandReply? reply = JsonSerializer.Deserialize<CommandReply>(json, _ReplyOptions);
                AssertTrue(reply != null && reply.Type == "command.result" && reply.Data != null, json);
                return reply!.Data!;
            }

            public async Task<WebSocketCommandError> TransitionWebSocketErrorAsync(string missionId, string status)
            {
                object result = await InvokeWebSocketAsync(missionId, status).ConfigureAwait(false);
                WebSocketCommandError? error = result as WebSocketCommandError;
                AssertTrue(error != null, result.GetType().Name);
                return error!;
            }

            public async Task<Mission> TransitionRestAsync(string missionId, string status, LandingProbe probe)
            {
                (int code, string body) = await SendRestAsync(missionId, status, probe).ConfigureAwait(false);
                AssertEqual(200, code, body);
                return JsonHelper.Deserialize<Mission>(body);
            }

            public async Task<string> TransitionRestErrorAsync(string missionId, string status, LandingProbe probe, int expectedCode)
            {
                (int code, string body) = await SendRestAsync(missionId, status, probe).ConfigureAwait(false);
                AssertEqual(expectedCode, code, body);
                return body;
            }

            private async Task<(int Code, string Body)> SendRestAsync(string missionId, string status, LandingProbe probe)
            {
                Admiral.OnMissionComplete = probe.Callback;
                int port;
                using (TcpListener listener = new TcpListener(IPAddress.Loopback, 0))
                {
                    listener.Start();
                    port = ((IPEndPoint)listener.LocalEndpoint).Port;
                }

                WebserverSettings webSettings = new WebserverSettings();
                webSettings.Hostname = "127.0.0.1";
                webSettings.Port = port;
                webSettings.Ssl.Enable = false;
                Webserver app = new Webserver(webSettings, RejectAsync);
                try
                {
                    app.UseOpenApi(openApi =>
                    {
                        openApi.Info.Title = "manual-completion";
                    });
                    AuthContext admin = AuthContext.Authenticated(Constants.DefaultTenantId, Constants.DefaultUserId, true, true, "api-key");
                    MissionRoutes routes = new MissionRoutes(
                        _Database,
                        Admiral,
                        _Missions,
                        _Settings,
                        _Git,
                        _Landing,
                        _LandingPreview,
                        _GitHub,
                        (eventType, message, entityType, entityId, captainId, recordedMissionId, vesselId, voyageId) => WriteEventAsync(_Database, eventType, message, entityType, entityId, captainId, recordedMissionId, vesselId, voyageId),
                        (mission, previous, message) => WriteStatusEventAsync(_Database, mission, previous, message),
                        probe.Callback,
                        null,
                        _Logging,
                        _ReplyOptions);
                    routes.Register(app, (HttpContextBase ctx) => Task.FromResult(admin), new AuthorizationService());
                    app.Start(CancellationToken.None);

                    using (HttpClient client = new HttpClient())
                    {
                        client.BaseAddress = new Uri("http://127.0.0.1:" + port.ToString());
                        using (HttpResponseMessage response = await client.PutAsync("/api/v1/missions/" + missionId + "/status", JsonHelper.ToJsonContent(new { status = status })).ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return ((int)response.StatusCode, body);
                        }
                    }
                }
                finally
                {
                    try { app.Stop(); }
                    catch (Exception) { }
                }
            }

            private async Task<object> InvokeMcpAsync(string missionId, string status)
            {
                JsonElement args = JsonSerializer.SerializeToElement(new { missionId = missionId, status = status });
                return await _McpHandlers["transition_mission_status"](args).ConfigureAwait(false);
            }

            private async Task<object> InvokeWebSocketAsync(string missionId, string status)
            {
                WebSocketCommand command = new WebSocketCommand();
                command.Action = "transition_mission_status";
                command.Id = missionId;
                command.Status = status;
                return await _WebSocket.HandleCommandAsync("transition_mission_status", command, "{}").ConfigureAwait(false);
            }

            private async Task<Vessel> CreateVesselAsync()
            {
                Vessel vessel = new Vessel("manual-vessel-" + _Token, "https://example.invalid/" + _Token + ".git");
                vessel.TenantId = Constants.DefaultTenantId;
                vessel.UserId = Constants.DefaultUserId;
                vessel.LocalPath = System.IO.Path.Combine(_Settings.ReposDirectory, "repo");
                System.IO.Directory.CreateDirectory(vessel.LocalPath);
                vessel.SecretScanEnabled = false;
                vessel.AutoLandEnabled = false;
                vessel.DefinitionOfDoneEnabled = false;
                vessel.ProtectedPathPatterns = new List<string>();
                vessel.PrivateIdentifierDenylist = new List<string>();
                await _Database.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                return vessel;
            }

            private async Task<Captain> CreateCaptainAsync()
            {
                Captain captain = new Captain("manual-captain-" + _Token);
                captain.TenantId = Constants.DefaultTenantId;
                captain.UserId = Constants.DefaultUserId;
                captain.State = CaptainStateEnum.Working;
                await _Database.Captains.CreateAsync(captain).ConfigureAwait(false);
                return captain;
            }

            private async Task<Dock> CreateDockAsync(string vesselId, string captainId, string worktreePath)
            {
                Dock dock = new Dock(vesselId);
                dock.TenantId = Constants.DefaultTenantId;
                dock.UserId = Constants.DefaultUserId;
                dock.CaptainId = captainId;
                dock.Active = true;
                dock.BranchName = "armada/stage-" + _Token;
                dock.WorktreePath = worktreePath;
                await _Database.Docks.CreateAsync(dock).ConfigureAwait(false);
                return dock;
            }
        }

        private static Task RejectAsync(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 404;
            return ctx.Response.Send();
        }

        private static async Task WriteEventAsync(
            DatabaseDriver database,
            string eventType,
            string message,
            string? entityType,
            string? entityId,
            string? captainId,
            string? missionId,
            string? vesselId,
            string? voyageId)
        {
            ArmadaEvent evt = new ArmadaEvent(eventType, message);
            evt.EntityType = entityType;
            evt.EntityId = entityId;
            evt.CaptainId = captainId;
            evt.MissionId = missionId;
            evt.VesselId = vesselId;
            evt.VoyageId = voyageId;
            await database.Events.CreateAsync(evt).ConfigureAwait(false);
        }

        private static async Task WriteStatusEventAsync(DatabaseDriver database, Mission mission, MissionStatusEnum? previous, string message)
        {
            await WriteEventAsync(database, "mission.status_changed", message, "mission", mission.Id, mission.CaptainId, mission.Id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);
        }

        #endregion
    }
}
