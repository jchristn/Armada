namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ObjectiveService"/>: internal-first objective capture, linkage,
    /// filtering, snapshot backfill, and deletion over the configured database provider. Positive cases assert
    /// the full scoped-objective lifecycle and latest-snapshot normalization; negative cases cover
    /// the audited not-found read, not-found update, null-request guards, and stored fields that cannot be decoded.
    /// </summary>
    public sealed class ObjectiveServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the ObjectiveService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_update_enumerate_and_delete_manages_scoped_objectives", "CreateUpdateEnumerateAndDeleteAsync manages scoped objectives", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ObjectiveService objectives = new ObjectiveService(testDb.Driver);

                string tenantId = "ten_objective";
                string userId = "usr_objective";
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-objective-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                    Vessel vessel = new Vessel
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Name = "Objective Vessel",
                        RepoUrl = "file:///tmp/objective.git",
                        LocalPath = workingDirectory,
                        WorkingDirectory = workingDirectory,
                        DefaultBranch = "main"
                    };
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    PlanningSession planningSession = new PlanningSession
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        CaptainId = "cpt_objective",
                        VesselId = vessel.Id,
                        FleetId = vessel.FleetId,
                        Title = "Objective Planning Session",
                        Status = PlanningSessionStatusEnum.Active
                    };
                    await testDb.Driver.PlanningSessions.CreateAsync(planningSession).ConfigureAwait(false);

                    Voyage voyage = new Voyage("Objective Voyage", "Implementation voyage")
                    {
                        TenantId = tenantId,
                        UserId = userId
                    };
                    await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        VoyageId = voyage.Id,
                        Title = "Objective Mission",
                        Description = "Implement objective scope",
                        Status = MissionStatusEnum.Pending
                    };
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Release release = new Release
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Title = "Objective Release",
                        Status = ReleaseStatusEnum.Draft
                    };
                    await testDb.Driver.Releases.CreateAsync(release).ConfigureAwait(false);

                    DeploymentEnvironment environment = new DeploymentEnvironment
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Name = "objective-test",
                        Kind = EnvironmentKindEnum.Staging,
                        IsDefault = true,
                        Active = true
                    };
                    await testDb.Driver.Environments.CreateAsync(environment).ConfigureAwait(false);

                    Deployment deployment = new Deployment
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        EnvironmentId = environment.Id,
                        EnvironmentName = environment.Name,
                        ReleaseId = release.Id,
                        MissionId = mission.Id,
                        VoyageId = voyage.Id,
                        Title = "Objective Deployment",
                        Status = DeploymentStatusEnum.Succeeded,
                        VerificationStatus = DeploymentVerificationStatusEnum.Passed,
                        CheckRunIds = new List<string> { "chk_objective_deploy" }
                    };
                    await testDb.Driver.Deployments.CreateAsync(deployment).ConfigureAwait(false);

                    CheckRun deploymentCheckRun = new CheckRun
                    {
                        Id = "chk_objective_deploy",
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        MissionId = mission.Id,
                        VoyageId = voyage.Id,
                        DeploymentId = deployment.Id,
                        Label = "Objective deploy verification",
                        Type = CheckRunTypeEnum.DeploymentVerification,
                        Source = CheckRunSourceEnum.Armada,
                        Status = CheckRunStatusEnum.Passed,
                        Command = "echo objective-deploy"
                    };
                    await testDb.Driver.CheckRuns.CreateAsync(deploymentCheckRun).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");
                    Objective created = await objectives.CreateAsync(auth, new ObjectiveUpsertRequest
                    {
                        Title = "Ship Objective Support",
                        Description = "Capture internal-first scoping data.",
                        Status = ObjectiveStatusEnum.Scoped,
                        Owner = "joel",
                        Tags = new List<string> { "delivery", "scope" },
                        AcceptanceCriteria = new List<string> { "Objective exists", "History can filter by objective" },
                        VesselIds = new List<string> { vessel.Id }
                    }).ConfigureAwait(false);

                    AssertStartsWith("obj_", created.Id);
                    AssertEqual(ObjectiveStatusEnum.Scoped, created.Status);
                    AssertTrue(created.VesselIds.Contains(vessel.Id), "Expected vessel link.");

                    EnumerationResult<Objective> filtered = await objectives.EnumerateAsync(auth, new ObjectiveQuery
                    {
                        PageNumber = 1,
                        PageSize = 25,
                        VesselId = vessel.Id,
                        Search = "objective support"
                    }).ConfigureAwait(false);

                    AssertEqual(1, filtered.Objects.Count);
                    AssertEqual(created.Id, filtered.Objects[0].Id);

                    Objective planned = await objectives.LinkPlanningSessionAsync(auth, created.Id, planningSession.Id).ConfigureAwait(false);
                    AssertTrue(planned.PlanningSessionIds.Contains(planningSession.Id), "Expected planning session link.");
                    AssertEqual(ObjectiveStatusEnum.Planned, planned.Status);

                    Objective inProgress = await objectives.LinkVoyageAsync(auth, created.Id, voyage.Id).ConfigureAwait(false);
                    AssertTrue(inProgress.VoyageIds.Contains(voyage.Id), "Expected voyage link.");
                    AssertTrue(inProgress.MissionIds.Contains(mission.Id), "Expected mission linkage from voyage.");
                    AssertEqual(ObjectiveStatusEnum.InProgress, inProgress.Status);

                    Objective released = await objectives.LinkReleaseAsync(auth, created.Id, release.Id).ConfigureAwait(false);
                    AssertTrue(released.ReleaseIds.Contains(release.Id), "Expected release link.");
                    AssertEqual(ObjectiveStatusEnum.Released, released.Status);

                    Objective deployed = await objectives.LinkDeploymentAsync(auth, created.Id, deployment.Id).ConfigureAwait(false);
                    AssertTrue(deployed.DeploymentIds.Contains(deployment.Id), "Expected deployment link.");
                    AssertTrue(deployed.ReleaseIds.Contains(release.Id), "Expected deployment to preserve release lineage.");
                    AssertTrue(deployed.MissionIds.Contains(mission.Id), "Expected deployment to preserve mission lineage.");
                    AssertTrue(deployed.CheckRunIds.Contains("chk_objective_deploy"), "Expected deployment check-run linkage.");
                    AssertEqual(ObjectiveStatusEnum.Deployed, deployed.Status);
                    AssertEqual(ObjectiveBacklogStateEnum.Dispatched, deployed.BacklogState);

                    IncidentService incidents = new IncidentService(testDb.Driver);
                    Incident incident = await incidents.CreateAsync(auth, new IncidentUpsertRequest
                    {
                        Title = "Objective Incident",
                        Status = IncidentStatusEnum.Open,
                        Severity = IncidentSeverityEnum.High,
                        EnvironmentId = environment.Id,
                        EnvironmentName = environment.Name,
                        DeploymentId = deployment.Id,
                        ReleaseId = release.Id,
                        VesselId = vessel.Id,
                        MissionId = mission.Id,
                        VoyageId = voyage.Id,
                        RollbackDeploymentId = deployment.Id
                    }).ConfigureAwait(false);

                    Objective withIncident = await objectives.LinkIncidentAsync(auth, created.Id, incident.Id).ConfigureAwait(false);
                    AssertTrue(withIncident.IncidentIds.Contains(incident.Id), "Expected incident link.");
                    AssertTrue(withIncident.DeploymentIds.Contains(deployment.Id), "Expected incident to preserve deployment lineage.");
                    AssertTrue(withIncident.ReleaseIds.Contains(release.Id), "Expected incident to preserve release lineage.");

                    Objective updated = await objectives.UpdateAsync(auth, created.Id, new ObjectiveUpsertRequest
                    {
                        Status = ObjectiveStatusEnum.Completed,
                        EvidenceLinks = new List<string> { "https://example.test/releases/objective" }
                    }).ConfigureAwait(false);

                    AssertEqual(ObjectiveStatusEnum.Completed, updated.Status);
                    AssertTrue(updated.CompletedUtc.HasValue, "Expected completed timestamp.");
                    AssertEqual(1, updated.EvidenceLinks.Count);

                    Objective? loaded = await objectives.ReadAsync(auth, created.Id).ConfigureAwait(false);
                    loaded = NotNull(loaded);
                    AssertEqual(created.Id, loaded.Id);
                    AssertEqual(ObjectiveStatusEnum.Completed, loaded.Status);

                    await objectives.DeleteAsync(auth, created.Id).ConfigureAwait(false);
                    Objective? deleted = await objectives.ReadAsync(auth, created.Id).ConfigureAwait(false);
                    AssertNull(deleted);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("enumerate_backfills_latest_snapshot_into_normalized_storage", "EnumerateAsync backfills latest snapshot into normalized objective storage", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ObjectiveService objectives = new ObjectiveService(testDb.Driver);

                string tenantId = "ten_objective_backfill";
                string userId = "usr_objective_backfill";
                await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                DateTime createdUtc = DateTime.UtcNow.AddMinutes(-30);
                Objective original = new Objective
                {
                    Id = "obj_backfill_latest_snapshot",
                    TenantId = tenantId,
                    UserId = userId,
                    Title = "Original backlog title",
                    Description = "Original description",
                    Status = ObjectiveStatusEnum.Scoped,
                    Kind = ObjectiveKindEnum.Feature,
                    Priority = ObjectivePriorityEnum.P2,
                    Rank = 100,
                    BacklogState = ObjectiveBacklogStateEnum.Inbox,
                    Effort = ObjectiveEffortEnum.M,
                    CreatedUtc = createdUtc,
                    LastUpdateUtc = createdUtc.AddMinutes(1)
                };

                Objective latest = new Objective
                {
                    Id = original.Id,
                    TenantId = tenantId,
                    UserId = userId,
                    Title = "Latest backlog title",
                    Description = "Latest description",
                    Status = ObjectiveStatusEnum.Planned,
                    Kind = ObjectiveKindEnum.Bug,
                    Priority = ObjectivePriorityEnum.P0,
                    Rank = 5,
                    BacklogState = ObjectiveBacklogStateEnum.ReadyForPlanning,
                    Effort = ObjectiveEffortEnum.S,
                    RefinementSummary = "Use the safer release rollback path.",
                    CreatedUtc = createdUtc,
                    LastUpdateUtc = createdUtc.AddMinutes(10)
                };

                await testDb.Driver.Events.CreateAsync(new ArmadaEvent
                {
                    TenantId = tenantId,
                    UserId = userId,
                    EventType = "objective.snapshot",
                    EntityType = "objective",
                    EntityId = original.Id,
                    Message = original.Title,
                    Payload = JsonSerializer.Serialize(original),
                    CreatedUtc = original.LastUpdateUtc
                }).ConfigureAwait(false);

                await testDb.Driver.Events.CreateAsync(new ArmadaEvent
                {
                    TenantId = tenantId,
                    UserId = userId,
                    EventType = "objective.snapshot",
                    EntityType = "objective",
                    EntityId = latest.Id,
                    Message = latest.Title,
                    Payload = JsonSerializer.Serialize(latest),
                    CreatedUtc = latest.LastUpdateUtc
                }).ConfigureAwait(false);

                AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");
                EnumerationResult<Objective> result = await objectives.EnumerateAsync(auth, new ObjectiveQuery
                {
                    PageNumber = 1,
                    PageSize = 25,
                    Search = "Latest backlog title"
                }).ConfigureAwait(false);

                AssertEqual(1, result.Objects.Count);
                AssertEqual(latest.Id, result.Objects[0].Id);
                AssertEqual(latest.Title, result.Objects[0].Title);
                AssertEqual(latest.Description, result.Objects[0].Description);
                AssertEqual(latest.Status, result.Objects[0].Status);
                AssertEqual(latest.Kind, result.Objects[0].Kind);
                AssertEqual(latest.Priority, result.Objects[0].Priority);
                AssertEqual(latest.Rank, result.Objects[0].Rank);
                AssertEqual(latest.BacklogState, result.Objects[0].BacklogState);
                AssertEqual(latest.Effort, result.Objects[0].Effort);
                AssertEqual(latest.RefinementSummary, result.Objects[0].RefinementSummary);

                Objective? stored = await testDb.Driver.Objectives.ReadAsync(latest.Id).ConfigureAwait(false);
                stored = NotNull(stored);
                AssertEqual(latest.Title, stored.Title);
                AssertEqual(latest.Status, stored.Status);
                AssertEqual(latest.Kind, stored.Kind);
                AssertEqual(latest.Priority, stored.Priority);
                AssertEqual(latest.Rank, stored.Rank);
                AssertEqual(latest.BacklogState, stored.BacklogState);
                AssertEqual(latest.Effort, stored.Effort);
                AssertEqual(latest.RefinementSummary, stored.RefinementSummary);
            }));

            // Audit addition: reading an unknown objective returns null (confirmed against source).

            cases.Add(CaseAsync("read_unknown_id_returns_null", "ReadAsync UnknownId ReturnsNull", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ObjectiveService objectives = new ObjectiveService(testDb.Driver);

                AuthContext auth = AuthContext.Authenticated("ten_objective_missing", "usr_objective_missing", false, true, "UnitTest");
                Objective? loaded = await objectives.ReadAsync(auth, "obj_missing").ConfigureAwait(false);
                AssertNull(loaded);
            }));

            // Audit addition: updating an unknown objective throws not-found (confirmed against source).

            cases.Add(CaseAsync("update_unknown_id_throws", "UpdateAsync UnknownId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ObjectiveService objectives = new ObjectiveService(testDb.Driver);

                AuthContext auth = AuthContext.Authenticated("ten_objective_update_missing", "usr_objective_update_missing", false, true, "UnitTest");
                await AssertThrowsAsync<KeyNotFoundException>(() => objectives.UpdateAsync(auth, "obj_missing", new ObjectiveUpsertRequest
                {
                    Status = ObjectiveStatusEnum.Completed
                }));
            }));

            // Audit addition: create with a null request throws (confirmed against source).

            cases.Add(CaseAsync("create_null_request_throws", "CreateAsync NullRequest Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ObjectiveService objectives = new ObjectiveService(testDb.Driver);

                AuthContext auth = AuthContext.Authenticated("ten_objective_null", "usr_objective_null", false, true, "UnitTest");
                await AssertThrowsAsync<ArgumentNullException>(() => objectives.CreateAsync(auth, null!));
            }));

            cases.Add(CaseAsync("corrupt_blocker_json_update_keeps_stored_text", "A title update rejects malformed blocker JSON and leaves the stored text", TestTags.Negative, async () =>
            {
                await RejectStoredFieldOnOtherUpdateAsync(
                    "ten_obj_blocker",
                    "Blocker field",
                    "blocked_by_objective_ids_json",
                    "{not-json").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("corrupt_playbook_json_update_keeps_stored_text", "A title update rejects malformed playbook JSON and leaves the stored text", TestTags.Negative, async () =>
            {
                await RejectStoredFieldOnOtherUpdateAsync(
                    "ten_obj_playbook",
                    "Playbook field",
                    "suggested_playbooks_json",
                    "{not-json").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("unknown_status_update_keeps_stored_text", "A title update rejects an unknown status name and leaves the stored text", TestTags.Negative, async () =>
            {
                await RejectStoredFieldOnOtherUpdateAsync(
                    "ten_obj_status",
                    "Status field",
                    "status",
                    "NotAStatus").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("corrupt_blocker_json_enumerate_fails", "Enumeration rejects malformed blocker JSON and leaves the stored text", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ObjectiveService objectives = new ObjectiveService(testDb.Driver);
                    string tenantId = "ten_obj_enumerate";
                    string userId = "usr_obj_stored";
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);
                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");
                    Objective created = await objectives.CreateAsync(auth, new ObjectiveUpsertRequest
                    {
                        Title = "Enumerate field"
                    }).ConfigureAwait(false);
                    string column = "blocked_by_objective_ids_json";
                    string storedText = "{not-json";
                    await SetObjectiveColumnAsync(testDb, created.Id, column, storedText).ConfigureAwait(false);

                    Exception? thrown = null;
                    try
                    {
                        await objectives.EnumerateAsync(auth, new ObjectiveQuery
                        {
                            PageNumber = 1,
                            PageSize = 25
                        }).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        thrown = exception;
                    }

                    string? stored = await ReadObjectiveColumnAsync(testDb, created.Id, column).ConfigureAwait(false);
                    Assert(thrown != null, "enumerate accepted stored " + column + " and left '" + stored + "' for " + created.Id);
                    Assert(thrown.Message.IndexOf(created.Id, StringComparison.Ordinal) >= 0, "error must name objective " + created.Id + " but was: " + thrown.Message);
                    Assert(thrown.Message.IndexOf(column, StringComparison.Ordinal) >= 0, "error must name field " + column + " but was: " + thrown.Message);
                    AssertEqual(storedText, stored, column);
                }
            }));

            cases.Add(CaseAsync("empty_and_valid_blocker_json_reads", "Empty blocker text stays an empty list and valid JSON still reads", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ObjectiveService objectives = new ObjectiveService(testDb.Driver);
                    string tenantId = "ten_obj_empty";
                    string userId = "usr_obj_stored";
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);
                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");
                    Objective created = await objectives.CreateAsync(auth, new ObjectiveUpsertRequest
                    {
                        Title = "Empty blocker field"
                    }).ConfigureAwait(false);
                    string column = "blocked_by_objective_ids_json";

                    await SetObjectiveColumnAsync(testDb, created.Id, column, "").ConfigureAwait(false);
                    Objective emptyText = NotNull(await objectives.ReadAsync(auth, created.Id).ConfigureAwait(false));
                    AssertEqual(0, emptyText.BlockedByObjectiveIds.Count, "empty text");

                    await SetObjectiveColumnAsync(testDb, created.Id, column, "   ").ConfigureAwait(false);
                    Objective blankText = NotNull(await objectives.ReadAsync(auth, created.Id).ConfigureAwait(false));
                    AssertEqual(0, blankText.BlockedByObjectiveIds.Count, "blank text");

                    await SetObjectiveColumnAsync(testDb, created.Id, column, null).ConfigureAwait(false);
                    Objective nullText = NotNull(await objectives.ReadAsync(auth, created.Id).ConfigureAwait(false));
                    AssertEqual(0, nullText.BlockedByObjectiveIds.Count, "null text");

                    await SetObjectiveColumnAsync(testDb, created.Id, column, "[\"obj_blocker\"]").ConfigureAwait(false);
                    Objective valid = NotNull(await objectives.ReadAsync(auth, created.Id).ConfigureAwait(false));
                    AssertEqual(1, valid.BlockedByObjectiveIds.Count, "valid json count");
                    AssertEqual("obj_blocker", valid.BlockedByObjectiveIds[0], "valid json value");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ObjectiveService",
                displayName: "Objective Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task EnsureTenantAndUserAsync(TestDatabase testDb, string tenantId, string userId)
        {
            TenantMetadata? existingTenant = await testDb.Driver.Tenants.ReadAsync(tenantId).ConfigureAwait(false);
            if (existingTenant == null)
            {
                await testDb.Driver.Tenants.CreateAsync(new TenantMetadata
                {
                    Id = tenantId,
                    Name = tenantId
                }).ConfigureAwait(false);
            }

            UserMaster? existingUser = await testDb.Driver.Users.ReadByIdAsync(userId).ConfigureAwait(false);
            if (existingUser == null)
            {
                await testDb.Driver.Users.CreateAsync(new UserMaster
                {
                    Id = userId,
                    TenantId = tenantId,
                    Email = userId + "@armada.test",
                    PasswordSha256 = UserMaster.ComputePasswordHash("password"),
                    IsTenantAdmin = true
                }).ConfigureAwait(false);
            }
        }

        private static async Task RejectStoredFieldOnOtherUpdateAsync(string tenantId, string title, string column, string storedText)
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
            {
                ObjectiveService objectives = new ObjectiveService(testDb.Driver);
                string userId = "usr_obj_stored";
                await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);
                AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");
                Objective created = await objectives.CreateAsync(auth, new ObjectiveUpsertRequest
                {
                    Title = title
                }).ConfigureAwait(false);
                await SetObjectiveColumnAsync(testDb, created.Id, column, storedText).ConfigureAwait(false);

                Exception? thrown = null;
                try
                {
                    await objectives.UpdateAsync(auth, created.Id, new ObjectiveUpsertRequest
                    {
                        Title = title + " renamed"
                    }).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }

                string? stored = await ReadObjectiveColumnAsync(testDb, created.Id, column).ConfigureAwait(false);
                Assert(thrown != null, "update accepted stored " + column + " and left '" + stored + "' for " + created.Id);
                Assert(thrown.Message.IndexOf(created.Id, StringComparison.Ordinal) >= 0, "error must name objective " + created.Id + " but was: " + thrown.Message);
                Assert(thrown.Message.IndexOf(column, StringComparison.Ordinal) >= 0, "error must name field " + column + " but was: " + thrown.Message);
                AssertEqual(storedText, stored, column);
            }
        }

        private static async Task SetObjectiveColumnAsync(TestDatabase testDb, string objectiveId, string column, string? value)
        {
            RequireObjectiveColumn(column);
            using (DbConnection connection = OpenConnection(testDb.ConnectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE objectives SET " + column + " = @stored WHERE id = @id";
                    AddParameter(command, "@stored", (object?)value ?? DBNull.Value);
                    AddParameter(command, "@id", objectiveId);
                    int rows = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    AssertEqual(1, rows, column);
                }
            }
        }

        private static async Task<string?> ReadObjectiveColumnAsync(TestDatabase testDb, string objectiveId, string column)
        {
            RequireObjectiveColumn(column);
            using (DbConnection connection = OpenConnection(testDb.ConnectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT " + column + " FROM objectives WHERE id = @id";
                    AddParameter(command, "@id", objectiveId);
                    object? result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                    if (result == null || result == DBNull.Value)
                        return null;
                    return result.ToString();
                }
            }
        }

        private static void RequireObjectiveColumn(string column)
        {
            if (column == "blocked_by_objective_ids_json"
                || column == "suggested_playbooks_json"
                || column == "status")
                return;

            throw new InvalidOperationException("Column is not an objective stored field under test: " + column);
        }

        private static DbConnection OpenConnection(string connectionString)
        {
            switch (TestDatabaseConfig.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteConnection(connectionString);
                case DatabaseTypeEnum.Postgresql:
                    return new NpgsqlConnection(connectionString);
                case DatabaseTypeEnum.Mysql:
                    return new MySqlConnection(connectionString);
                case DatabaseTypeEnum.SqlServer:
                    return new SqlConnection(connectionString);
                default:
                    throw new InvalidOperationException("Objective stored-field cases do not support database provider " + TestDatabaseConfig.Type + ".");
            }
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private static Objective NotNull(Objective? objective)
        {
            if (objective == null) throw new InvalidOperationException("Expected objective to be present.");
            return objective;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ObjectiveService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
