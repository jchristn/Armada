namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="PlanningSessionCoordinator"/>: captain/dock reservation on create,
    /// runtime and double-booking guards, provisioning-failure rollback, voyage/mission dispatch
    /// lineage (including default latest-assistant selection and playbook snapshots), stop and delete
    /// resource release, inactivity/abandonment/retention maintenance, and server-recovery
    /// reconciliation. Each case runs against a fresh SQLite store with a stub git service and a
    /// disposable coordinator fixture.
    /// </summary>
    public sealed class PlanningSessionCoordinatorSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PlanningSessionCoordinator";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Planning Session Coordinator suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_async_reserves_captain_and_dock", "CreateAsync reserves captain and dock", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    Vessel vessel = await fixture.CreateVesselAsync().ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-1").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Plan API hardening"
                        }).ConfigureAwait(false);

                    AssertEqual(PlanningSessionStatusEnum.Active, session.Status);
                    AssertNotNull(session.DockId);
                    AssertNotNull(session.BranchName);
                    AssertStartsWith(Constants.BranchPrefix + "planning/", session.BranchName!);

                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(updatedCaptain);
                    AssertEqual(CaptainStateEnum.Planning, updatedCaptain!.State);
                    AssertEqual(session.DockId, updatedCaptain.CurrentDockId);

                    Dock? dock = await testDb.Driver.Docks.ReadAsync(session.DockId!).ConfigureAwait(false);
                    AssertNotNull(dock);
                    AssertEqual(vessel.Id, dock!.VesselId);
                    AssertEqual(session.BranchName, dock.BranchName);
                }
            }));

            cases.Add(CaseAsync("create_async_rejects_unsupported_custom_runtime", "CreateAsync rejects unsupported custom runtime", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    Vessel vessel = await fixture.CreateVesselAsync().ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("custom-planner", AgentRuntimeEnum.Custom).ConfigureAwait(false);

                    InvalidOperationException ex = await CaptureExceptionAsync<InvalidOperationException>(() =>
                        fixture.Coordinator.CreateAsync(
                            null,
                            null,
                            captain,
                            vessel,
                            new PlanningSessionCreateRequest())).ConfigureAwait(false);

                    AssertContains("built-in ClaudeCode, Codex, Gemini, Cursor, Mux, and OpenCode runtimes", ex.Message);
                }
            }));

            cases.Add(CaseAsync("create_async_failure_resets_captain_and_marks_session_failed", "CreateAsync failure resets captain and marks session failed", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    fixture.Git.ShouldThrowOnWorktree = true;
                    Vessel vessel = await fixture.CreateVesselAsync().ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-failure").ConfigureAwait(false);

                    DockProvisioningException ex = await CaptureExceptionAsync<DockProvisioningException>(() =>
                        fixture.Coordinator.CreateAsync(
                            null,
                            null,
                            captain,
                            vessel,
                            new PlanningSessionCreateRequest
                            {
                                Title = "Broken session"
                            })).ConfigureAwait(false);

                    AssertEqual(vessel.Id, ex.VesselId, "the provisioning failure names the vessel");

                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(updatedCaptain);
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State);
                    AssertNull(updatedCaptain.CurrentDockId);

                    List<PlanningSession> sessions = await testDb.Driver.PlanningSessions.EnumerateByCaptainAsync(captain.Id).ConfigureAwait(false);
                    AssertEqual(1, sessions.Count);
                    AssertEqual(PlanningSessionStatusEnum.Failed, sessions[0].Status);
                    AssertEqual(ex.Message, sessions[0].FailureReason, "the session records the provisioning failure");
                }
            }));

            cases.Add(CaseAsync("create_async_rejects_double_booking_a_captain_already_reserved_for_planning", "CreateAsync rejects double-booking a captain already reserved for planning", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    Vessel vesselOne = await fixture.CreateVesselAsync("planning-vessel-a").ConfigureAwait(false);
                    Vessel vesselTwo = await fixture.CreateVesselAsync("planning-vessel-b").ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-double-booked").ConfigureAwait(false);

                    await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vesselOne,
                        new PlanningSessionCreateRequest
                        {
                            Title = "First plan"
                        }).ConfigureAwait(false);

                    CaptainNotIdleException ex = await CaptureExceptionAsync<CaptainNotIdleException>(() =>
                        fixture.Coordinator.CreateAsync(
                            null,
                            null,
                            captain,
                            vesselTwo,
                            new PlanningSessionCreateRequest
                            {
                                Title = "Second plan"
                            })).ConfigureAwait(false);

                    AssertEqual(captain.Id, ex.CaptainId, "the refusal names the busy captain");
                    AssertTrue(ex.State.HasValue && ex.State.Value != CaptainStateEnum.Idle, "the refusal carries the captain's non-idle state");
                }
            }));

            cases.Add(CaseAsync("dispatch_async_creates_voyage_and_mission_lineage_from_assistant_message", "DispatchAsync creates voyage and mission lineage from assistant message", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    CoordinatorFixture.TenantUserResult tenantUser = await fixture.CreateTenantUserAsync().ConfigureAwait(false);
                    Playbook playbook = await fixture.CreatePlaybookAsync(tenantUser.TenantId, tenantUser.UserId, "planning-playbook.md").ConfigureAwait(false);
                    Pipeline pipeline = await fixture.CreatePipelineAsync(tenantUser.TenantId, "Full planning pipeline").ConfigureAwait(false);
                    Vessel vessel = await fixture.CreateVesselAsync("planning-vessel-dispatch", tenantUser.TenantId, tenantUser.UserId).ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-dispatch", AgentRuntimeEnum.ClaudeCode, tenantUser.TenantId, tenantUser.UserId).ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        tenantUser.TenantId,
                        tenantUser.UserId,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Repository plan",
                            PipelineId = pipeline.Id,
                            SelectedPlaybooks = new List<SelectedPlaybook>
                            {
                                new SelectedPlaybook
                                {
                                    PlaybookId = playbook.Id,
                                    DeliveryMode = PlaybookDeliveryModeEnum.InlineFullContent
                                }
                            }
                        }).ConfigureAwait(false);

                    PlanningSessionMessage assistantMessage = await testDb.Driver.PlanningSessionMessages.CreateAsync(new PlanningSessionMessage
                    {
                        PlanningSessionId = session.Id,
                        Role = "Assistant",
                        Sequence = 1,
                        Content = "Implement the API hardening changes and add regression tests."
                    }).ConfigureAwait(false);

                    Voyage voyage = await fixture.Coordinator.DispatchAsync(
                        session,
                        new PlanningSessionDispatchRequest
                        {
                            MessageId = assistantMessage.Id,
                            Title = "API hardening dispatch"
                        }).ConfigureAwait(false);

                    AssertEqual(session.Id, voyage.SourcePlanningSessionId);
                    AssertEqual(assistantMessage.Id, voyage.SourcePlanningMessageId);
                    AssertEqual("API hardening dispatch", voyage.Title);

                    Voyage? persistedVoyage = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(persistedVoyage);
                    AssertEqual(session.Id, persistedVoyage!.SourcePlanningSessionId);
                    AssertEqual(assistantMessage.Id, persistedVoyage.SourcePlanningMessageId);

                    List<SelectedPlaybook> voyageSelections = await testDb.Driver.Playbooks.GetVoyageSelectionsAsync(voyage.Id).ConfigureAwait(false);
                    AssertEqual(1, voyageSelections.Count);
                    AssertEqual(playbook.Id, voyageSelections[0].PlaybookId);

                    List<Mission> missions = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    AssertEqual(3, missions.Count);
                    AssertTrue(missions.Exists(m => m.Persona == "Architect"), "Pipeline dispatch should include the Architect stage");
                    AssertTrue(missions.Exists(m => m.Persona == "Worker"), "Pipeline dispatch should include the Worker stage");
                    AssertTrue(missions.Exists(m => m.Persona == "Judge"), "Pipeline dispatch should include the Judge stage");

                    Mission architectMission = missions.Find(m => m.Persona == "Architect")
                        ?? throw new Exception("Expected architect mission to exist");
                    AssertContains("Implement the API hardening changes and add regression tests.", architectMission.Description ?? String.Empty);

                    List<MissionPlaybookSnapshot> snapshots = await testDb.Driver.Playbooks.GetMissionSnapshotsAsync(architectMission.Id).ConfigureAwait(false);
                    AssertEqual(1, snapshots.Count);
                    AssertEqual(playbook.Id, snapshots[0].PlaybookId);
                    AssertContains("planning-playbook.md", snapshots[0].FileName ?? String.Empty);
                }
            }));

            cases.Add(CaseAsync("dispatch_async_defaults_to_the_latest_non_empty_assistant_response", "DispatchAsync defaults to the latest non-empty assistant response", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    Vessel vessel = await fixture.CreateVesselAsync("planning-vessel-default-dispatch").ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-default-dispatch").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Default source selection"
                        }).ConfigureAwait(false);

                    await testDb.Driver.PlanningSessionMessages.CreateAsync(new PlanningSessionMessage
                    {
                        PlanningSessionId = session.Id,
                        Role = "Assistant",
                        Sequence = 1,
                        Content = "Older planning response"
                    }).ConfigureAwait(false);

                    await testDb.Driver.PlanningSessionMessages.CreateAsync(new PlanningSessionMessage
                    {
                        PlanningSessionId = session.Id,
                        Role = "Assistant",
                        Sequence = 2,
                        Content = ""
                    }).ConfigureAwait(false);

                    PlanningSessionMessage latestAssistant = await testDb.Driver.PlanningSessionMessages.CreateAsync(new PlanningSessionMessage
                    {
                        PlanningSessionId = session.Id,
                        Role = "Assistant",
                        Sequence = 3,
                        Content = "Latest planning response should be dispatched."
                    }).ConfigureAwait(false);

                    Voyage voyage = await fixture.Coordinator.DispatchAsync(
                        session,
                        new PlanningSessionDispatchRequest()).ConfigureAwait(false);

                    AssertEqual(session.Id, voyage.SourcePlanningSessionId);
                    AssertEqual(latestAssistant.Id, voyage.SourcePlanningMessageId);

                    List<Mission> missions = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    AssertEqual(1, missions.Count);
                    AssertEqual("Latest planning response should be dispatched.", missions[0].Description);
                }
            }));

            cases.Add(CaseAsync("stop_async_releases_captain_and_dock", "StopAsync releases captain and dock", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    Vessel vessel = await fixture.CreateVesselAsync().ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-stop").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Stop me"
                        }).ConfigureAwait(false);

                    PlanningSession stopped = await fixture.Coordinator.StopAsync(session).ConfigureAwait(false);

                    AssertEqual(PlanningSessionStatusEnum.Stopped, stopped.Status);
                    AssertNull(stopped.ProcessId);
                    AssertNotNull(stopped.CompletedUtc);

                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(updatedCaptain);
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State);
                    AssertNull(updatedCaptain.CurrentDockId);
                    AssertNull(updatedCaptain.ProcessId);

                    Dock? dock = await testDb.Driver.Docks.ReadAsync(session.DockId!).ConfigureAwait(false);
                    AssertNotNull(dock);
                    AssertFalse(dock!.Active);
                    AssertNull(dock.CaptainId);
                }
            }));

            cases.Add(CaseAsync("delete_async_removes_session_transcript_and_releases_resources", "DeleteAsync removes session transcript and releases resources", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    Vessel vessel = await fixture.CreateVesselAsync("planning-vessel-delete").ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-delete").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Delete me"
                        }).ConfigureAwait(false);

                    await testDb.Driver.PlanningSessionMessages.CreateAsync(new PlanningSessionMessage
                    {
                        PlanningSessionId = session.Id,
                        Role = "Assistant",
                        Sequence = 1,
                        Content = "Planning transcript content"
                    }).ConfigureAwait(false);

                    await fixture.Coordinator.DeleteAsync(session).ConfigureAwait(false);

                    AssertNull(await testDb.Driver.PlanningSessions.ReadAsync(session.Id).ConfigureAwait(false));
                    AssertEqual(0, (await testDb.Driver.PlanningSessionMessages.EnumerateBySessionAsync(session.Id).ConfigureAwait(false)).Count);

                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(updatedCaptain);
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State);
                    AssertNull(updatedCaptain.CurrentDockId);

                    Dock? dock = await testDb.Driver.Docks.ReadAsync(session.DockId!).ConfigureAwait(false);
                    AssertNotNull(dock);
                    AssertFalse(dock!.Active);
                }
            }));

            cases.Add(CaseAsync("maintain_sessions_async_stops_inactive_planning_sessions", "MaintainSessionsAsync stops inactive planning sessions", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    fixture.Settings.PlanningSessionInactivityTimeoutMinutes = 1;
                    Vessel vessel = await fixture.CreateVesselAsync("planning-vessel-maintain-stop").ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-maintain-stop").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Inactive session"
                        }).ConfigureAwait(false);

                    using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
                    {
                        await conn.OpenAsync().ConfigureAwait(false);
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "UPDATE planning_sessions SET last_update_utc = @lastUpdateUtc WHERE id = @id;";
                            cmd.Parameters.AddWithValue("@id", session.Id);
                            cmd.Parameters.AddWithValue("@lastUpdateUtc", DateTime.UtcNow.AddMinutes(-10).ToString("O"));
                            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                    }

                    await fixture.Coordinator.MaintainSessionsAsync().ConfigureAwait(false);

                    PlanningSession? updatedSession = await testDb.Driver.PlanningSessions.ReadAsync(session.Id).ConfigureAwait(false);
                    AssertNotNull(updatedSession);
                    AssertEqual(PlanningSessionStatusEnum.Stopped, updatedSession!.Status);
                    AssertNotNull(updatedSession.CompletedUtc);

                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(updatedCaptain);
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State);
                }
            }));

            cases.Add(CaseAsync("maintain_sessions_async_stops_abandoned_planning_sessions_when_inactivity_timeout_is_disabled", "MaintainSessionsAsync stops abandoned planning sessions when inactivity timeout is disabled", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    fixture.Settings.PlanningSessionInactivityTimeoutMinutes = 0;
                    fixture.Settings.PlanningSessionAbandonmentTimeoutMinutes = 1;
                    Vessel vessel = await fixture.CreateVesselAsync("planning-vessel-maintain-abandonment").ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-maintain-abandonment").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Abandoned session"
                        }).ConfigureAwait(false);

                    using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
                    {
                        await conn.OpenAsync().ConfigureAwait(false);
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "UPDATE planning_sessions SET last_update_utc = @lastUpdateUtc WHERE id = @id;";
                            cmd.Parameters.AddWithValue("@id", session.Id);
                            cmd.Parameters.AddWithValue("@lastUpdateUtc", DateTime.UtcNow.AddMinutes(-10).ToString("O"));
                            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                    }

                    await fixture.Coordinator.MaintainSessionsAsync().ConfigureAwait(false);

                    PlanningSession? updatedSession = await testDb.Driver.PlanningSessions.ReadAsync(session.Id).ConfigureAwait(false);
                    AssertNotNull(updatedSession);
                    AssertEqual(PlanningSessionStatusEnum.Stopped, updatedSession!.Status);

                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(updatedCaptain);
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State);
                }
            }));

            cases.Add(CaseAsync("maintain_sessions_async_deletes_retained_stopped_sessions", "MaintainSessionsAsync deletes retained stopped sessions", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    fixture.Settings.PlanningSessionRetentionDays = 1;
                    Vessel vessel = await fixture.CreateVesselAsync("planning-vessel-retention").ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-retention").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Retention session"
                        }).ConfigureAwait(false);

                    await testDb.Driver.PlanningSessionMessages.CreateAsync(new PlanningSessionMessage
                    {
                        PlanningSessionId = session.Id,
                        Role = "Assistant",
                        Sequence = 1,
                        Content = "Retention transcript"
                    }).ConfigureAwait(false);

                    PlanningSession stopped = await fixture.Coordinator.StopAsync(session).ConfigureAwait(false);
                    stopped.CompletedUtc = DateTime.UtcNow.AddDays(-5);
                    stopped.LastUpdateUtc = DateTime.UtcNow.AddDays(-5);
                    await testDb.Driver.PlanningSessions.UpdateAsync(stopped).ConfigureAwait(false);

                    await fixture.Coordinator.MaintainSessionsAsync().ConfigureAwait(false);

                    AssertNull(await testDb.Driver.PlanningSessions.ReadAsync(session.Id).ConfigureAwait(false));
                    AssertEqual(0, (await testDb.Driver.PlanningSessionMessages.EnumerateBySessionAsync(session.Id).ConfigureAwait(false)).Count);
                }
            }));

            cases.Add(CaseAsync("recover_sessions_async_restores_responding_session_to_active_state", "RecoverSessionsAsync restores responding session to active state", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    Vessel vessel = await fixture.CreateVesselAsync().ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-recover").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Recover me"
                        }).ConfigureAwait(false);

                    session.Status = PlanningSessionStatusEnum.Responding;
                    session.ProcessId = 999999;
                    await testDb.Driver.PlanningSessions.UpdateAsync(session).ConfigureAwait(false);

                    captain.State = CaptainStateEnum.Idle;
                    captain.CurrentDockId = null;
                    captain.ProcessId = null;
                    await testDb.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);

                    await fixture.Coordinator.RecoverSessionsAsync().ConfigureAwait(false);

                    PlanningSession? recoveredSession = await testDb.Driver.PlanningSessions.ReadAsync(session.Id).ConfigureAwait(false);
                    AssertNotNull(recoveredSession);
                    AssertEqual(PlanningSessionStatusEnum.Active, recoveredSession!.Status);
                    AssertNull(recoveredSession.ProcessId);

                    Captain? recoveredCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(recoveredCaptain);
                    AssertEqual(CaptainStateEnum.Planning, recoveredCaptain!.State);
                    AssertEqual(session.DockId, recoveredCaptain.CurrentDockId);

                    List<PlanningSessionMessage> messages = await testDb.Driver.PlanningSessionMessages.EnumerateBySessionAsync(session.Id).ConfigureAwait(false);
                    AssertEqual(1, messages.Count);
                    AssertEqual("System", messages[0].Role);
                    AssertContains("interrupted during server recovery", messages[0].Content);
                }
            }));

            cases.Add(CaseAsync("recover_sessions_async_stops_abandoned_active_sessions_and_releases_captains", "RecoverSessionsAsync stops abandoned active sessions and releases captains", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    fixture.Settings.PlanningSessionInactivityTimeoutMinutes = 0;
                    fixture.Settings.PlanningSessionAbandonmentTimeoutMinutes = 1;
                    Vessel vessel = await fixture.CreateVesselAsync("planning-vessel-recover-abandonment").ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-recover-abandonment").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Recover abandoned session"
                        }).ConfigureAwait(false);

                    using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
                    {
                        await conn.OpenAsync().ConfigureAwait(false);
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "UPDATE planning_sessions SET last_update_utc = @lastUpdateUtc WHERE id = @id;";
                            cmd.Parameters.AddWithValue("@id", session.Id);
                            cmd.Parameters.AddWithValue("@lastUpdateUtc", DateTime.UtcNow.AddMinutes(-10).ToString("O"));
                            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                    }

                    await fixture.Coordinator.RecoverSessionsAsync().ConfigureAwait(false);

                    PlanningSession? recoveredSession = await testDb.Driver.PlanningSessions.ReadAsync(session.Id).ConfigureAwait(false);
                    AssertNotNull(recoveredSession);
                    AssertEqual(PlanningSessionStatusEnum.Stopped, recoveredSession!.Status);
                    AssertNotNull(recoveredSession.CompletedUtc);

                    Captain? recoveredCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(recoveredCaptain);
                    AssertEqual(CaptainStateEnum.Idle, recoveredCaptain!.State);
                    AssertNull(recoveredCaptain.CurrentDockId);
                }
            }));

            cases.Add(CaseAsync("recover_sessions_async_completes_stopping_session_cleanup", "RecoverSessionsAsync completes stopping session cleanup", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver))
                {
                    Vessel vessel = await fixture.CreateVesselAsync("planning-vessel-recover-stop").ConfigureAwait(false);
                    Captain captain = await fixture.CreateCaptainAsync("planner-recover-stop").ConfigureAwait(false);

                    PlanningSession session = await fixture.Coordinator.CreateAsync(
                        null,
                        null,
                        captain,
                        vessel,
                        new PlanningSessionCreateRequest
                        {
                            Title = "Recover stop"
                        }).ConfigureAwait(false);

                    session.Status = PlanningSessionStatusEnum.Stopping;
                    session.ProcessId = null;
                    await testDb.Driver.PlanningSessions.UpdateAsync(session).ConfigureAwait(false);

                    captain.State = CaptainStateEnum.Planning;
                    captain.CurrentDockId = session.DockId;
                    captain.ProcessId = null;
                    await testDb.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);

                    await fixture.Coordinator.RecoverSessionsAsync().ConfigureAwait(false);

                    PlanningSession? recoveredSession = await testDb.Driver.PlanningSessions.ReadAsync(session.Id).ConfigureAwait(false);
                    AssertNotNull(recoveredSession);
                    AssertEqual(PlanningSessionStatusEnum.Stopped, recoveredSession!.Status);
                    AssertNotNull(recoveredSession.CompletedUtc);

                    Captain? recoveredCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertNotNull(recoveredCaptain);
                    AssertEqual(CaptainStateEnum.Idle, recoveredCaptain!.State);
                    AssertNull(recoveredCaptain.CurrentDockId);

                    Dock? recoveredDock = await testDb.Driver.Docks.ReadAsync(session.DockId!).ConfigureAwait(false);
                    AssertNotNull(recoveredDock);
                    AssertFalse(recoveredDock!.Active);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Planning Session Coordinator",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private sealed class CoordinatorFixture : IDisposable
        {
            public sealed class TenantUserResult
            {
                public string TenantId { get; set; } = String.Empty;

                public string UserId { get; set; } = String.Empty;
            }

            public DatabaseDriver Database { get; }
            public ArmadaSettings Settings { get; }
            public StubGitService Git { get; }
            public PlanningSessionCoordinator Coordinator { get; }

            private readonly string _rootDirectory;
            private readonly LoggingModule _logging;

            public CoordinatorFixture(DatabaseDriver database)
            {
                Database = database;
                _logging = CreateLogging();
                _rootDirectory = Path.Combine(Path.GetTempPath(), "armada_planning_fixture_" + Guid.NewGuid().ToString("N"));

                Settings = new ArmadaSettings
                {
                    DataDirectory = _rootDirectory,
                    DatabasePath = Path.Combine(_rootDirectory, "armada.db"),
                    LogDirectory = Path.Combine(_rootDirectory, "logs"),
                    DocksDirectory = Path.Combine(_rootDirectory, "docks"),
                    ReposDirectory = Path.Combine(_rootDirectory, "repos")
                };
                Settings.InitializeDirectories();

                Git = new StubGitService();
                DockService docks = new DockService(_logging, Database, Settings, Git);
                AdmiralService admiral = CreateAdmiralService(_logging, Database, Settings, Git);
                AgentRuntimeFactory runtimeFactory = new AgentRuntimeFactory(_logging);

                Coordinator = new PlanningSessionCoordinator(
                    _logging,
                    Database,
                    Settings,
                    docks,
                    admiral,
                    runtimeFactory,
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
            }

            public async Task<TenantUserResult> CreateTenantUserAsync(string tenantName = "Planning Tenant")
            {
                TenantMetadata tenant = new TenantMetadata(tenantName);
                tenant = await Database.Tenants.CreateAsync(tenant).ConfigureAwait(false);

                UserMaster user = new UserMaster(tenant.Id, tenantName.Replace(" ", String.Empty).ToLowerInvariant() + "@example.com", "password");
                user = await Database.Users.CreateAsync(user).ConfigureAwait(false);

                return new TenantUserResult
                {
                    TenantId = tenant.Id,
                    UserId = user.Id
                };
            }

            public async Task<Vessel> CreateVesselAsync(string name = "planning-vessel", string? tenantId = null, string? userId = null)
            {
                Fleet fleet = new Fleet("planning-fleet-" + name);
                fleet.TenantId = tenantId;
                fleet.UserId = userId;
                fleet = await Database.Fleets.CreateAsync(fleet).ConfigureAwait(false);

                Vessel vessel = new Vessel(name, "https://github.com/test/" + name + ".git")
                {
                    TenantId = tenantId,
                    UserId = userId,
                    FleetId = fleet.Id,
                    LocalPath = Path.Combine(Settings.ReposDirectory, name + ".git"),
                    WorkingDirectory = Path.Combine(Settings.ReposDirectory, name + ".git"),
                    DefaultBranch = "main",
                    ProjectContext = "Test project context"
                };

                return await Database.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            }

            public async Task<Captain> CreateCaptainAsync(string name, AgentRuntimeEnum runtime = AgentRuntimeEnum.ClaudeCode, string? tenantId = null, string? userId = null)
            {
                Captain captain = new Captain(name, runtime)
                {
                    TenantId = tenantId,
                    UserId = userId
                };
                return await Database.Captains.CreateAsync(captain).ConfigureAwait(false);
            }

            public async Task<Playbook> CreatePlaybookAsync(string tenantId, string userId, string fileName)
            {
                Playbook playbook = new Playbook(fileName, "# " + fileName + "\n\nUse the repository planning conventions.")
                {
                    TenantId = tenantId,
                    UserId = userId,
                    Description = "Planning playbook for coordinator tests"
                };
                return await Database.Playbooks.CreateAsync(playbook).ConfigureAwait(false);
            }

            public async Task<Pipeline> CreatePipelineAsync(string tenantId, string name)
            {
                Pipeline pipeline = new Pipeline(name)
                {
                    TenantId = tenantId,
                    Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Architect"),
                        new PipelineStage(2, "Worker"),
                        new PipelineStage(3, "Judge")
                    }
                };
                return await Database.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(_rootDirectory))
                        Directory.Delete(_rootDirectory, true);
                }
                catch
                {
                }
            }

            private static LoggingModule CreateLogging()
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                return logging;
            }

            private static AdmiralService CreateAdmiralService(LoggingModule logging, DatabaseDriver db, ArmadaSettings settings, StubGitService git)
            {
                IDockService dockService = new DockService(logging, db, settings, git);
                ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
                IMissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
                IVoyageService voyageService = new VoyageService(logging, db);
                return new AdmiralService(logging, db, settings, captainService, missionService, voyageService, dockService);
            }
        }

        private static async Task<TException> CaptureExceptionAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException ex)
            {
                return ex;
            }

            throw new Exception("Assertion failed: expected " + typeof(TException).Name + " but no exception was thrown");
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
    }
}
