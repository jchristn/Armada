namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="IncidentService"/>: incident context preservation across create,
    /// update, read, and enumerate flows over a live SQLite store, including snapshot tie-breaking.
    /// Positive cases assert rollback/delivery linkage and newest-snapshot selection; negative cases
    /// cover the audited not-found read, not-found update, and null-request guards.
    /// </summary>
    public sealed class IncidentServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the IncidentService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_update_read_enumerate_preserve_rollback_and_delivery_context", "CreateUpdateReadAndEnumerateAsync preserve rollback and delivery context", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                IncidentService incidents = new IncidentService(testDb.Driver);

                string tenantId = "ten_incident";
                string userId = "usr_incident";
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-incident-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                    Vessel vessel = new Vessel
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Name = "Incident Vessel",
                        RepoUrl = "file:///tmp/armada-incident.git",
                        LocalPath = workingDirectory,
                        WorkingDirectory = workingDirectory,
                        DefaultBranch = "main"
                    };
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Voyage voyage = new Voyage
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Title = "Incident Voyage",
                        Description = "Voyage for incident context",
                        Status = VoyageStatusEnum.Open
                    };
                    await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        VoyageId = voyage.Id,
                        Title = "Incident Mission",
                        Description = "Mission for incident context",
                        Status = MissionStatusEnum.Complete
                    };
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Release release = new Release
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Title = "Incident Release",
                        Version = "1.0.0",
                        TagName = "v1.0.0",
                        Status = ReleaseStatusEnum.Candidate,
                        VoyageIds = new List<string> { voyage.Id },
                        MissionIds = new List<string> { mission.Id }
                    };
                    await testDb.Driver.Releases.CreateAsync(release).ConfigureAwait(false);

                    DeploymentEnvironment environment = new DeploymentEnvironment
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Name = "production",
                        Kind = EnvironmentKindEnum.Production,
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
                        Title = "Deploy Incident Release",
                        Status = DeploymentStatusEnum.RolledBack,
                        VerificationStatus = DeploymentVerificationStatusEnum.Passed,
                        CreatedUtc = DateTime.UtcNow.AddMinutes(-30),
                        LastUpdateUtc = DateTime.UtcNow.AddMinutes(-5),
                        CompletedUtc = DateTime.UtcNow.AddMinutes(-20),
                        VerifiedUtc = DateTime.UtcNow.AddMinutes(-18),
                        RolledBackUtc = DateTime.UtcNow.AddMinutes(-10)
                    };
                    await testDb.Driver.Deployments.CreateAsync(deployment).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, false, "UnitTest");
                    Incident created = await incidents.CreateAsync(auth, new IncidentUpsertRequest
                    {
                        Title = "Production rollback",
                        Summary = "Users reported errors after rollout",
                        Status = IncidentStatusEnum.Open,
                        Severity = IncidentSeverityEnum.Critical,
                        EnvironmentId = environment.Id,
                        EnvironmentName = environment.Name,
                        DeploymentId = deployment.Id,
                        ReleaseId = release.Id,
                        VesselId = vessel.Id,
                        MissionId = mission.Id,
                        VoyageId = voyage.Id,
                        RollbackDeploymentId = deployment.Id,
                        Impact = "Checkout unavailable",
                        RootCause = "Regression in release build",
                        RecoveryNotes = "Rolled back to previous stable build",
                        Postmortem = "Initial notes",
                        DetectedUtc = DateTime.UtcNow.AddMinutes(-15)
                    }).ConfigureAwait(false);

                    Incident? loaded = await incidents.ReadAsync(auth, created.Id).ConfigureAwait(false);
                    loaded = NotNull(loaded);
                    AssertEqual(deployment.Id, loaded.DeploymentId);
                    AssertEqual(deployment.Id, loaded.RollbackDeploymentId);
                    AssertEqual(release.Id, loaded.ReleaseId);
                    AssertEqual(environment.Id, loaded.EnvironmentId);
                    AssertEqual(vessel.Id, loaded.VesselId);
                    AssertEqual(mission.Id, loaded.MissionId);
                    AssertEqual(voyage.Id, loaded.VoyageId);
                    AssertEqual("Initial notes", loaded.Postmortem);

                    Incident updated = await incidents.UpdateAsync(auth, created.Id, new IncidentUpsertRequest
                    {
                        Status = IncidentStatusEnum.Closed,
                        RecoveryNotes = "Rollback completed successfully",
                        Postmortem = "Root cause confirmed and corrected",
                        MitigatedUtc = DateTime.UtcNow.AddMinutes(-8),
                        ClosedUtc = DateTime.UtcNow.AddMinutes(-2)
                    }).ConfigureAwait(false);

                    AssertEqual(IncidentStatusEnum.Closed, updated.Status);
                    AssertEqual(deployment.Id, updated.DeploymentId);
                    AssertEqual(deployment.Id, updated.RollbackDeploymentId);
                    AssertEqual(release.Id, updated.ReleaseId);
                    AssertEqual(vessel.Id, updated.VesselId);
                    AssertEqual("Rollback completed successfully", updated.RecoveryNotes);
                    AssertEqual("Root cause confirmed and corrected", updated.Postmortem);
                    AssertTrue(updated.MitigatedUtc.HasValue, "Expected mitigated timestamp.");
                    AssertTrue(updated.ClosedUtc.HasValue, "Expected closed timestamp.");

                    EnumerationResult<Incident> byDeployment = await incidents.EnumerateAsync(auth, new IncidentQuery
                    {
                        DeploymentId = deployment.Id,
                        PageNumber = 1,
                        PageSize = 25
                    }).ConfigureAwait(false);
                    AssertEqual(1, byDeployment.Objects.Count);
                    AssertEqual(created.Id, byDeployment.Objects[0].Id);

                    EnumerationResult<Incident> bySearch = await incidents.EnumerateAsync(auth, new IncidentQuery
                    {
                        Search = "confirmed and corrected",
                        PageNumber = 1,
                        PageSize = 25
                    }).ConfigureAwait(false);
                    AssertEqual(1, bySearch.Objects.Count);
                    AssertEqual(created.Id, bySearch.Objects[0].Id);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("enumerate_prefers_newer_incident_snapshot_when_timestamps_tie", "EnumerateAsync prefers newer incident snapshot when timestamps tie", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                IncidentService incidents = new IncidentService(testDb.Driver);

                string tenantId = "ten_incident_tie";
                string userId = "usr_incident_tie";
                await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, false, "UnitTest");
                DateTime snapshotTime = DateTime.UtcNow;

                Incident original = new Incident
                {
                    Id = "inc_tie_case",
                    TenantId = tenantId,
                    UserId = userId,
                    Title = "Tie case incident",
                    Summary = "Original snapshot",
                    Status = IncidentStatusEnum.Open,
                    Severity = IncidentSeverityEnum.High,
                    DeploymentId = "dep_original",
                    Postmortem = "Original postmortem",
                    LastUpdateUtc = snapshotTime
                };

                Incident updated = new Incident
                {
                    Id = original.Id,
                    TenantId = tenantId,
                    UserId = userId,
                    Title = original.Title,
                    Summary = "Updated snapshot",
                    Status = IncidentStatusEnum.Closed,
                    Severity = IncidentSeverityEnum.Critical,
                    DeploymentId = "dep_updated",
                    Postmortem = "Updated postmortem text",
                    LastUpdateUtc = snapshotTime
                };

                ArmadaEvent originalSnapshot = new ArmadaEvent("incident.snapshot", original.Title)
                {
                    Id = "evt_000000000000000000000001",
                    TenantId = tenantId,
                    UserId = userId,
                    EntityType = "incident",
                    EntityId = original.Id,
                    Payload = JsonSerializer.Serialize(original),
                    CreatedUtc = snapshotTime
                };

                ArmadaEvent updatedSnapshot = new ArmadaEvent("incident.snapshot", updated.Title)
                {
                    Id = "evt_000000000000000000000002",
                    TenantId = tenantId,
                    UserId = userId,
                    EntityType = "incident",
                    EntityId = updated.Id,
                    Payload = JsonSerializer.Serialize(updated),
                    CreatedUtc = snapshotTime
                };

                await testDb.Driver.Events.CreateAsync(originalSnapshot).ConfigureAwait(false);
                await testDb.Driver.Events.CreateAsync(updatedSnapshot).ConfigureAwait(false);

                Incident? loaded = await incidents.ReadAsync(auth, original.Id).ConfigureAwait(false);
                loaded = NotNull(loaded);
                AssertEqual(IncidentStatusEnum.Closed, loaded.Status);
                AssertEqual("dep_updated", loaded.DeploymentId);

                EnumerationResult<Incident> bySearch = await incidents.EnumerateAsync(auth, new IncidentQuery
                {
                    Search = "Updated postmortem text",
                    PageNumber = 1,
                    PageSize = 25
                }).ConfigureAwait(false);

                AssertEqual(1, bySearch.Objects.Count);
                AssertEqual(original.Id, bySearch.Objects[0].Id);
                AssertEqual(IncidentStatusEnum.Closed, bySearch.Objects[0].Status);
            }));

            // Audit addition: reading an unknown incident returns null (confirmed against source).

            cases.Add(CaseAsync("read_unknown_id_returns_null", "ReadAsync UnknownId ReturnsNull", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                IncidentService incidents = new IncidentService(testDb.Driver);

                AuthContext auth = AuthContext.Authenticated("ten_incident_missing", "usr_incident_missing", false, false, "UnitTest");
                Incident? loaded = await incidents.ReadAsync(auth, "inc_missing").ConfigureAwait(false);
                AssertNull(loaded);
            }));

            // Audit addition: updating an unknown incident throws not-found (confirmed against source).

            cases.Add(CaseAsync("update_unknown_id_throws", "UpdateAsync UnknownId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                IncidentService incidents = new IncidentService(testDb.Driver);

                AuthContext auth = AuthContext.Authenticated("ten_incident_update_missing", "usr_incident_update_missing", false, false, "UnitTest");
                await AssertThrowsAsync<KeyNotFoundException>(() => incidents.UpdateAsync(auth, "inc_missing", new IncidentUpsertRequest
                {
                    Status = IncidentStatusEnum.Closed
                }));
            }));

            // Audit addition: create with a null request throws (confirmed against source).

            cases.Add(CaseAsync("create_null_request_throws", "CreateAsync NullRequest Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                IncidentService incidents = new IncidentService(testDb.Driver);

                AuthContext auth = AuthContext.Authenticated("ten_incident_null", "usr_incident_null", false, false, "UnitTest");
                await AssertThrowsAsync<ArgumentNullException>(() => incidents.CreateAsync(auth, null!));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.IncidentService",
                displayName: "Incident Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Incident NotNull(Incident? incident)
        {
            if (incident == null) throw new InvalidOperationException("Expected incident to exist.");
            return incident;
        }

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
                suiteId: "Services.IncidentService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
