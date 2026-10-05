namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ObjectiveRefinementCoordinator"/>: transcript persistence and
    /// active-state recovery when the captain runtime is unsupported, assistant-transcript fallback
    /// summarization, objective field application with source-message selection, and stop cleanup
    /// that releases the captain. Each case runs against a fresh SQLite store with a disposable
    /// refinement coordinator fixture.
    /// </summary>
    public sealed class ObjectiveRefinementCoordinatorSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ObjectiveRefinementCoordinator";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Objective Refinement Coordinator suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("send_message_async_creates_transcript_rows_and_recovers_to_active_state_when_runtime_is_unsupported", "SendMessageAsync creates transcript rows and recovers to active state when runtime is unsupported", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver);

                CoordinatorFixture.TenantUserResult tenantUser = await fixture.CreateTenantUserAsync().ConfigureAwait(false);
                Objective objective = await fixture.CreateObjectiveAsync("Refine backlog capture", tenantUser.TenantId, tenantUser.UserId).ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("refinement-custom", AgentRuntimeEnum.Custom, tenantUser.TenantId, tenantUser.UserId, CaptainStateEnum.Refining).ConfigureAwait(false);
                ObjectiveRefinementSession session = await fixture.CreateSessionAsync(objective, captain).ConfigureAwait(false);

                ObjectiveRefinementMessage userMessage = await fixture.Coordinator.SendMessageAsync(session, "Clarify API retry behavior.").ConfigureAwait(false);

                await WaitForAsync(async () =>
                {
                    List<ObjectiveRefinementMessage> messages = await testDb.Driver.ObjectiveRefinementMessages.EnumerateBySessionAsync(session.Id).ConfigureAwait(false);
                    ObjectiveRefinementSession? refreshed = await testDb.Driver.ObjectiveRefinementSessions.ReadAsync(session.Id).ConfigureAwait(false);
                    return messages.Count == 2
                        && messages.Exists(message => message.Role == "Assistant" && !String.IsNullOrWhiteSpace(message.Content))
                        && refreshed?.Status == ObjectiveRefinementSessionStatusEnum.Active;
                }).ConfigureAwait(false);

                List<ObjectiveRefinementMessage> persistedMessages = await testDb.Driver.ObjectiveRefinementMessages.EnumerateBySessionAsync(session.Id).ConfigureAwait(false);
                ObjectiveRefinementMessage assistantMessage = persistedMessages.Find(message => message.Role == "Assistant")
                    ?? throw new Exception("Expected assistant refinement message");
                ObjectiveRefinementSession persistedSession = await RequireSessionAsync(testDb.Driver, session.Id).ConfigureAwait(false);

                AssertEqual("User", userMessage.Role);
                AssertEqual(2, persistedMessages.Count);
                AssertContains("Refinement response failed", assistantMessage.Content);
                AssertContains("built-in ClaudeCode, Codex, Gemini, Cursor, and Mux runtimes", persistedSession.FailureReason ?? String.Empty);
                AssertEqual(ObjectiveRefinementSessionStatusEnum.Active, persistedSession.Status);
            }));

            cases.Add(CaseAsync("send_message_async_releases_the_turn_before_the_session_is_reported_active", "SendMessageAsync releases the turn and settles the captain before the session is reported Active again", TestTags.Negative, async () =>
            {
                // Regression: the turn used to write the session back to Active and only then update the captain
                // and drop its in-memory turn reservation. A client that saw Active and immediately summarized or
                // applied got "already generating a response" (HTTP 409). The hook runs at the instant the Active
                // write is durable, before the coordinator continues, which is exactly what a polling client sees.
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using RefinementSessionHookDatabaseDriver hooked = new RefinementSessionHookDatabaseDriver(testDb.Driver);
                using CoordinatorFixture fixture = new CoordinatorFixture(hooked);

                CoordinatorFixture.TenantUserResult tenantUser = await fixture.CreateTenantUserAsync().ConfigureAwait(false);
                Objective objective = await fixture.CreateObjectiveAsync("Active means ready", tenantUser.TenantId, tenantUser.UserId).ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("active-ready-custom", AgentRuntimeEnum.Custom, tenantUser.TenantId, tenantUser.UserId, CaptainStateEnum.Refining).ConfigureAwait(false);
                ObjectiveRefinementSession session = await fixture.CreateSessionAsync(objective, captain).ConfigureAwait(false);

                TaskCompletionSource<Exception?> summarizeAtActive = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                bool sawResponding = false;
                hooked.HookedSessions.AfterUpdateAsync = async (ObjectiveRefinementSession updated) =>
                {
                    if (updated.Id != session.Id) return;
                    if (updated.Status == ObjectiveRefinementSessionStatusEnum.Responding)
                    {
                        sawResponding = true;
                        return;
                    }

                    if (!sawResponding || updated.Status != ObjectiveRefinementSessionStatusEnum.Active || summarizeAtActive.Task.IsCompleted)
                        return;

                    try
                    {
                        await fixture.Coordinator.SummarizeAsync(updated, new ObjectiveRefinementSummaryRequest()).ConfigureAwait(false);
                        summarizeAtActive.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        summarizeAtActive.TrySetResult(ex);
                    }
                };

                await fixture.Coordinator.SendMessageAsync(session, "Clarify API retry behavior.").ConfigureAwait(false);

                Task finished = await Task.WhenAny(summarizeAtActive.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
                AssertTrue(finished == summarizeAtActive.Task, "Expected the turn to return the session to Active.");
                Exception? summarizeError = await summarizeAtActive.Task.ConfigureAwait(false);
                AssertNull(summarizeError, "summarize at the moment the session became Active");
            }));

            cases.Add(CaseAsync("summarize_async_falls_back_to_transcript_parsing_when_runtime_prompt_execution_is_unavailable", "SummarizeAsync falls back to transcript parsing when runtime prompt execution is unavailable", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver);

                CoordinatorFixture.TenantUserResult tenantUser = await fixture.CreateTenantUserAsync().ConfigureAwait(false);
                Objective objective = await fixture.CreateObjectiveAsync("Fallback summary", tenantUser.TenantId, tenantUser.UserId).ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("summary-custom", AgentRuntimeEnum.Custom, tenantUser.TenantId, tenantUser.UserId, CaptainStateEnum.Refining).ConfigureAwait(false);
                ObjectiveRefinementSession session = await fixture.CreateSessionAsync(objective, captain).ConfigureAwait(false);
                ObjectiveRefinementMessage assistant = await fixture.CreateMessageAsync(session, "Assistant", 2,
                    "Summary paragraph.\n\n### Acceptance Criteria\n- Keep replay data wired\n- Preserve captain selection\n\n### Non-Goals\n- No CLI rewrite\n\n### Rollout Constraints\n- Ship behind feature flag").ConfigureAwait(false);

                ObjectiveRefinementSummaryResponse summary = await fixture.Coordinator.SummarizeAsync(
                    session,
                    new ObjectiveRefinementSummaryRequest { MessageId = assistant.Id }).ConfigureAwait(false);

                AssertEqual(session.Id, summary.SessionId);
                AssertEqual(assistant.Id, summary.MessageId);
                AssertEqual("assistant-fallback", summary.Method);
                AssertContains("Summary paragraph.", summary.Summary);
                AssertEqual(2, summary.AcceptanceCriteria.Count);
                AssertEqual("Keep replay data wired", summary.AcceptanceCriteria[0]);
                AssertEqual("No CLI rewrite", summary.NonGoals[0]);
                AssertEqual("Ship behind feature flag", summary.RolloutConstraints[0]);
            }));

            cases.Add(CaseAsync("apply_async_updates_objective_fields_and_selects_the_source_refinement_message", "ApplyAsync updates objective fields and selects the source refinement message", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver);

                CoordinatorFixture.TenantUserResult tenantUser = await fixture.CreateTenantUserAsync().ConfigureAwait(false);
                Vessel vessel = await fixture.CreateVesselAsync("refinement-apply", tenantUser.TenantId, tenantUser.UserId).ConfigureAwait(false);
                Objective objective = await fixture.CreateObjectiveAsync("Apply refinement", tenantUser.TenantId, tenantUser.UserId).ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("apply-custom", AgentRuntimeEnum.Custom, tenantUser.TenantId, tenantUser.UserId, CaptainStateEnum.Refining).ConfigureAwait(false);
                ObjectiveRefinementSession session = await fixture.CreateSessionAsync(objective, captain, vesselId: vessel.Id).ConfigureAwait(false);
                ObjectiveRefinementMessage assistant = await fixture.CreateMessageAsync(session, "Assistant", 2,
                    "Refined backlog summary.\n\n### Acceptance Criteria\n- Persist normalized objective fields\n\n### Non-Goals\n- No schema rollback\n\n### Rollout Constraints\n- Validate with SQLite first").ConfigureAwait(false);

                (ObjectiveRefinementSummaryResponse Summary, Objective Objective) applied = await fixture.Coordinator.ApplyAsync(
                    AuthContext.Authenticated(tenantUser.TenantId, tenantUser.UserId, false, true, "UnitTest"),
                    objective,
                    session,
                    new ObjectiveRefinementApplyRequest
                    {
                        MessageId = assistant.Id,
                        MarkMessageSelected = true,
                        PromoteBacklogState = true
                    },
                    fixture.Objectives).ConfigureAwait(false);

                Objective persistedObjective = await RequireObjectiveAsync(testDb.Driver, objective.Id).ConfigureAwait(false);
                List<ObjectiveRefinementMessage> persistedMessages = await testDb.Driver.ObjectiveRefinementMessages.EnumerateBySessionAsync(session.Id).ConfigureAwait(false);
                ObjectiveRefinementMessage selected = persistedMessages.Find(message => message.Id == assistant.Id)
                    ?? throw new Exception("Expected selected assistant message");

                AssertEqual("assistant-fallback", applied.Summary.Method);
                AssertEqual(ObjectiveStatusEnum.Scoped, applied.Objective.Status);
                AssertEqual(ObjectiveBacklogStateEnum.ReadyForPlanning, applied.Objective.BacklogState);
                AssertContains("Refined backlog summary.", applied.Objective.RefinementSummary ?? String.Empty);
                AssertEqual("Persist normalized objective fields", applied.Objective.AcceptanceCriteria[0]);
                AssertEqual("No schema rollback", applied.Objective.NonGoals[0]);
                AssertEqual("Validate with SQLite first", applied.Objective.RolloutConstraints[0]);
                AssertTrue(applied.Objective.RefinementSessionIds.Contains(session.Id), "Expected session linkage on updated objective.");
                AssertEqual(ObjectiveStatusEnum.Scoped, persistedObjective.Status);
                AssertTrue(selected.IsSelected, "Expected source refinement message to be selected.");
            }));

            cases.Add(CaseAsync("stop_async_releases_the_captain_and_marks_the_session_stopped_when_no_runtime_process_can_be_created", "StopAsync releases the captain and marks the session stopped when no runtime process can be created", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using CoordinatorFixture fixture = new CoordinatorFixture(testDb.Driver);

                CoordinatorFixture.TenantUserResult tenantUser = await fixture.CreateTenantUserAsync().ConfigureAwait(false);
                Objective objective = await fixture.CreateObjectiveAsync("Stop refinement", tenantUser.TenantId, tenantUser.UserId).ConfigureAwait(false);
                Captain captain = await fixture.CreateCaptainAsync("stop-custom", AgentRuntimeEnum.Custom, tenantUser.TenantId, tenantUser.UserId, CaptainStateEnum.Refining).ConfigureAwait(false);
                ObjectiveRefinementSession session = await fixture.CreateSessionAsync(objective, captain, processId: 1234).ConfigureAwait(false);

                ObjectiveRefinementSession stopped = await fixture.Coordinator.StopAsync(session).ConfigureAwait(false);
                Captain persistedCaptain = await RequireCaptainAsync(testDb.Driver, captain.Id).ConfigureAwait(false);

                AssertEqual(ObjectiveRefinementSessionStatusEnum.Stopped, stopped.Status);
                AssertNull(stopped.ProcessId);
                AssertTrue(stopped.CompletedUtc.HasValue, "Expected stop to set completion timestamp.");
                AssertEqual(CaptainStateEnum.Idle, persistedCaptain.State);
                AssertNull(persistedCaptain.ProcessId);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Objective Refinement Coordinator",
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
            public ObjectiveRefinementCoordinator Coordinator { get; }
            public ObjectiveService Objectives { get; }

            private readonly string _rootDirectory;
            private readonly LoggingModule _logging;

            public CoordinatorFixture(DatabaseDriver database)
            {
                Database = database;
                Objectives = new ObjectiveService(database);
                _logging = CreateLogging();
                _rootDirectory = Path.Combine(Path.GetTempPath(), "armada_refinement_fixture_" + Guid.NewGuid().ToString("N"));

                Settings = new ArmadaSettings
                {
                    DataDirectory = _rootDirectory,
                    DatabasePath = Path.Combine(_rootDirectory, "armada.db"),
                    LogDirectory = Path.Combine(_rootDirectory, "logs"),
                    DocksDirectory = Path.Combine(_rootDirectory, "docks"),
                    ReposDirectory = Path.Combine(_rootDirectory, "repos")
                };
                Settings.InitializeDirectories();

                Coordinator = new ObjectiveRefinementCoordinator(
                    _logging,
                    Database,
                    Settings,
                    new AgentRuntimeFactory(_logging),
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
            }

            public async Task<TenantUserResult> CreateTenantUserAsync(string tenantName = "Refinement Tenant")
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

            public async Task<Objective> CreateObjectiveAsync(string title, string? tenantId = null, string? userId = null)
            {
                Objective objective = new Objective
                {
                    Title = title,
                    TenantId = tenantId,
                    UserId = userId,
                    Status = ObjectiveStatusEnum.Draft,
                    BacklogState = ObjectiveBacklogStateEnum.Inbox
                };
                return await Database.Objectives.CreateAsync(objective).ConfigureAwait(false);
            }

            public async Task<Vessel> CreateVesselAsync(string name, string? tenantId = null, string? userId = null)
            {
                Fleet fleet = new Fleet("fleet-" + name)
                {
                    TenantId = tenantId,
                    UserId = userId
                };
                fleet = await Database.Fleets.CreateAsync(fleet).ConfigureAwait(false);

                Vessel vessel = new Vessel(name, "https://github.com/test/" + name + ".git")
                {
                    TenantId = tenantId,
                    UserId = userId,
                    FleetId = fleet.Id,
                    LocalPath = Path.Combine(Settings.ReposDirectory, name + ".git"),
                    WorkingDirectory = Path.Combine(Settings.ReposDirectory, name + ".git"),
                    DefaultBranch = "main"
                };
                return await Database.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            }

            public async Task<Captain> CreateCaptainAsync(
                string name,
                AgentRuntimeEnum runtime,
                string? tenantId = null,
                string? userId = null,
                CaptainStateEnum state = CaptainStateEnum.Idle)
            {
                Captain captain = new Captain(name, runtime)
                {
                    TenantId = tenantId,
                    UserId = userId,
                    State = state
                };
                return await Database.Captains.CreateAsync(captain).ConfigureAwait(false);
            }

            public async Task<ObjectiveRefinementSession> CreateSessionAsync(
                Objective objective,
                Captain captain,
                string? title = null,
                string? vesselId = null,
                int? processId = null)
            {
                ObjectiveRefinementSession session = new ObjectiveRefinementSession
                {
                    ObjectiveId = objective.Id,
                    TenantId = objective.TenantId,
                    UserId = objective.UserId,
                    CaptainId = captain.Id,
                    Title = title ?? "Refine: " + objective.Title,
                    VesselId = vesselId,
                    Status = ObjectiveRefinementSessionStatusEnum.Active,
                    ProcessId = processId,
                    StartedUtc = DateTime.UtcNow,
                    CreatedUtc = DateTime.UtcNow,
                    LastUpdateUtc = DateTime.UtcNow
                };
                return await Database.ObjectiveRefinementSessions.CreateAsync(session).ConfigureAwait(false);
            }

            public async Task<ObjectiveRefinementMessage> CreateMessageAsync(
                ObjectiveRefinementSession session,
                string role,
                int sequence,
                string content)
            {
                ObjectiveRefinementMessage message = new ObjectiveRefinementMessage
                {
                    ObjectiveRefinementSessionId = session.Id,
                    ObjectiveId = session.ObjectiveId,
                    TenantId = session.TenantId,
                    UserId = session.UserId,
                    Role = role,
                    Sequence = sequence,
                    Content = content,
                    CreatedUtc = DateTime.UtcNow,
                    LastUpdateUtc = DateTime.UtcNow
                };
                return await Database.ObjectiveRefinementMessages.CreateAsync(message).ConfigureAwait(false);
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
        }

        private static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
            while (!deadline.Passed)
            {
                if (await condition().ConfigureAwait(false))
                    return;
                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new TimeoutException("Timed out waiting for refinement coordinator background work.");
        }

        private static async Task<ObjectiveRefinementSession> RequireSessionAsync(DatabaseDriver database, string sessionId)
        {
            ObjectiveRefinementSession? session = await database.ObjectiveRefinementSessions.ReadAsync(sessionId).ConfigureAwait(false);
            return session ?? throw new Exception("Expected refinement session " + sessionId);
        }

        private static async Task<Objective> RequireObjectiveAsync(DatabaseDriver database, string objectiveId)
        {
            Objective? objective = await database.Objectives.ReadAsync(objectiveId).ConfigureAwait(false);
            return objective ?? throw new Exception("Expected objective " + objectiveId);
        }

        private static async Task<Captain> RequireCaptainAsync(DatabaseDriver database, string captainId)
        {
            Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
            return captain ?? throw new Exception("Expected captain " + captainId);
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
