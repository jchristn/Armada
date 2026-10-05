namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="HistoricalTimelineService"/>: aggregation of current Armada
    /// entities into a single filterable timeline over a live SQLite store. Positive cases assert
    /// cross-entity aggregation, vessel/actor/source/text filtering, postmortem narrowing, and
    /// refinement-session routing; negative cases cover the audited null-argument guards.
    /// </summary>
    public sealed class HistoricalTimelineServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the HistoricalTimelineService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("enumerate_aggregates_current_entities_into_one_timeline", "EnumerateAsync aggregates current Armada entities into one timeline", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                string tenantId = "ten_history";
                string userId = "usr_history";
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-history-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                    Vessel vessel = CreateVessel(tenantId, userId, workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Voyage voyage = new Voyage
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Title = "History Voyage",
                        Description = "Voyage description",
                        Status = VoyageStatusEnum.Open
                    };
                    await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        VoyageId = voyage.Id,
                        Title = "History Mission",
                        Description = "Mission description",
                        Status = MissionStatusEnum.WorkProduced
                    };
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    CheckRun checkRun = new CheckRun
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        MissionId = mission.Id,
                        VoyageId = voyage.Id,
                        Type = CheckRunTypeEnum.UnitTest,
                        Status = CheckRunStatusEnum.Passed,
                        Command = "dotnet test",
                        Summary = "10 passed",
                        Output = "Passed! 10 tests run.",
                        DurationMs = 1234,
                        StartedUtc = DateTime.UtcNow.AddMinutes(-2),
                        CompletedUtc = DateTime.UtcNow.AddMinutes(-1)
                    };
                    await testDb.Driver.CheckRuns.CreateAsync(checkRun).ConfigureAwait(false);

                    Release release = new Release
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Title = "History Release",
                        Version = "1.0.0",
                        TagName = "v1.0.0",
                        Summary = "Release summary",
                        Status = ReleaseStatusEnum.Candidate,
                        VoyageIds = new List<string> { voyage.Id },
                        MissionIds = new List<string> { mission.Id },
                        CheckRunIds = new List<string> { checkRun.Id }
                    };
                    await testDb.Driver.Releases.CreateAsync(release).ConfigureAwait(false);

                    RequestHistoryEntry requestEntry = new RequestHistoryEntry
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        PrincipalDisplay = userId + "@armada.test",
                        AuthMethod = "Session",
                        Method = "GET",
                        Route = "/api/v1/status",
                        RouteTemplate = "/api/v1/status",
                        QueryString = "trace=history",
                        StatusCode = 200,
                        DurationMs = 12.5,
                        RequestSizeBytes = 10,
                        ResponseSizeBytes = 20,
                        RequestContentType = "application/json",
                        ResponseContentType = "application/json",
                        IsSuccess = true,
                        CreatedUtc = DateTime.UtcNow
                    };
                    RequestHistoryDetail requestDetail = new RequestHistoryDetail
                    {
                        RequestHistoryId = requestEntry.Id,
                        RequestHeadersJson = "{\"X-Test\":\"history\"}",
                        ResponseHeadersJson = "{\"Content-Type\":\"application/json\"}",
                        RequestBodyText = "{\"trace\":\"history\"}",
                        ResponseBodyText = "{\"ok\":true}"
                    };
                    await testDb.Driver.RequestHistory.CreateAsync(requestEntry, requestDetail).ConfigureAwait(false);

                    HistoricalTimelineService service = new HistoricalTimelineService(testDb.Driver);
                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");

                    EnumerationResult<HistoricalTimelineEntry> result = await service.EnumerateAsync(auth, new HistoricalTimelineQuery
                    {
                        PageNumber = 1,
                        PageSize = 50
                    }).ConfigureAwait(false);

                    AssertTrue(result.TotalRecords >= 5, "Expected voyage, mission, check run, release, and request history entries.");
                    AssertTrue(result.Objects.Exists(entry => entry.SourceType == "Voyage" && entry.SourceId == voyage.Id));
                    AssertTrue(result.Objects.Exists(entry => entry.SourceType == "Mission" && entry.SourceId == mission.Id));
                    AssertTrue(result.Objects.Exists(entry => entry.SourceType == "CheckRun" && entry.SourceId == checkRun.Id));
                    AssertTrue(result.Objects.Exists(entry => entry.SourceType == "Release" && entry.SourceId == release.Id));
                    AssertTrue(result.Objects.Exists(entry => entry.SourceType == "Request" && entry.SourceId == requestEntry.Id));
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("enumerate_exclude_read_requests_hides_get_polling", "EnumerateAsync with ExcludeReadRequests leaves out GET, HEAD, and OPTIONS request entries", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                string tenantId = "ten_history_reads";
                string userId = "usr_history_reads";
                await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                Dictionary<string, string> idsByMethod = new Dictionary<string, string>();
                foreach (string method in new[] { "GET", "head", "OPTIONS", "POST", "PUT", "DELETE" })
                {
                    RequestHistoryEntry entry = new RequestHistoryEntry
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Method = method,
                        Route = "/api/v1/missions",
                        RouteTemplate = "/api/v1/missions",
                        StatusCode = 200,
                        IsSuccess = true,
                        CreatedUtc = DateTime.UtcNow
                    };
                    await testDb.Driver.RequestHistory.CreateAsync(entry, new RequestHistoryDetail { RequestHistoryId = entry.Id }).ConfigureAwait(false);
                    idsByMethod[method] = entry.Id;
                }

                HistoricalTimelineService service = new HistoricalTimelineService(testDb.Driver);
                AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");

                EnumerationResult<HistoricalTimelineEntry> all = await service.EnumerateAsync(auth, new HistoricalTimelineQuery
                {
                    PageNumber = 1,
                    PageSize = 100
                }).ConfigureAwait(false);
                foreach (string id in idsByMethod.Values)
                    AssertTrue(all.Objects.Exists(entry => entry.SourceType == "Request" && entry.SourceId == id), "Default query should include every request entry.");

                EnumerationResult<HistoricalTimelineEntry> writesOnly = await service.EnumerateAsync(auth, new HistoricalTimelineQuery
                {
                    PageNumber = 1,
                    PageSize = 100,
                    ExcludeReadRequests = true
                }).ConfigureAwait(false);
                AssertFalse(writesOnly.Objects.Exists(entry => entry.SourceId == idsByMethod["GET"]), "GET entries should be excluded.");
                AssertFalse(writesOnly.Objects.Exists(entry => entry.SourceId == idsByMethod["head"]), "HEAD entries should be excluded regardless of case.");
                AssertFalse(writesOnly.Objects.Exists(entry => entry.SourceId == idsByMethod["OPTIONS"]), "OPTIONS entries should be excluded.");
                AssertTrue(writesOnly.Objects.Exists(entry => entry.SourceId == idsByMethod["POST"]), "POST entries should remain.");
                AssertTrue(writesOnly.Objects.Exists(entry => entry.SourceId == idsByMethod["PUT"]), "PUT entries should remain.");
                AssertTrue(writesOnly.Objects.Exists(entry => entry.SourceId == idsByMethod["DELETE"]), "DELETE entries should remain.");
                AssertEqual(all.TotalRecords - 3, writesOnly.TotalRecords, "TotalRecords should drop by the three read requests.");
            }));

            cases.Add(CaseAsync("enumerate_filters_by_vessel_actor_source_type_and_text", "EnumerateAsync filters by vessel, actor, source type, and text", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                string tenantId = "ten_history_filter";
                string userId = "usr_history_filter";
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-history-filter-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                    Vessel vessel = CreateVessel(tenantId, userId, workingDirectory);
                    vessel.Name = "Filter Vessel";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    CheckRun matchingRun = new CheckRun
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Type = CheckRunTypeEnum.Build,
                        Status = CheckRunStatusEnum.Passed,
                        Command = "dotnet build",
                        Summary = "Filter summary"
                    };
                    await testDb.Driver.CheckRuns.CreateAsync(matchingRun).ConfigureAwait(false);

                    CheckRun otherRun = new CheckRun
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = null,
                        Type = CheckRunTypeEnum.Build,
                        Status = CheckRunStatusEnum.Failed,
                        Command = "dotnet build",
                        Summary = "Other summary"
                    };
                    await testDb.Driver.CheckRuns.CreateAsync(otherRun).ConfigureAwait(false);

                    HistoricalTimelineService service = new HistoricalTimelineService(testDb.Driver);
                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, false, "UnitTest");

                    EnumerationResult<HistoricalTimelineEntry> result = await service.EnumerateAsync(auth, new HistoricalTimelineQuery
                    {
                        PageNumber = 1,
                        PageSize = 25,
                        VesselId = vessel.Id,
                        Actor = userId,
                        Text = "Filter summary",
                        SourceTypes = new List<string> { "CheckRun" }
                    }).ConfigureAwait(false);

                    AssertEqual(1, result.Objects.Count);
                    AssertEqual(matchingRun.Id, result.Objects[0].SourceId);
                    AssertEqual("CheckRun", result.Objects[0].SourceType);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("enumerate_narrows_to_postmortem_linked_incident_context", "EnumerateAsync can narrow to postmortem-linked incident context", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                string tenantId = "ten_history_postmortem";
                string userId = "usr_history_postmortem";
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-history-postmortem-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                    Vessel vessel = CreateVessel(tenantId, userId, workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Voyage voyage = new Voyage
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Title = "Postmortem Voyage",
                        Description = "Shared voyage",
                        Status = VoyageStatusEnum.Open
                    };
                    await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        VoyageId = voyage.Id,
                        Title = "Postmortem Mission",
                        Description = "Shared mission",
                        Status = MissionStatusEnum.WorkProduced
                    };
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    DeploymentEnvironment environment = new DeploymentEnvironment
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Name = "production",
                        Kind = EnvironmentKindEnum.Production,
                        Active = true,
                        IsDefault = true
                    };
                    await testDb.Driver.Environments.CreateAsync(environment).ConfigureAwait(false);

                    Release releaseWithPostmortem = new Release
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Title = "Release With Postmortem",
                        Version = "1.0.0",
                        TagName = "v1.0.0",
                        Status = ReleaseStatusEnum.Candidate,
                        MissionIds = new List<string> { mission.Id },
                        VoyageIds = new List<string> { voyage.Id }
                    };
                    await testDb.Driver.Releases.CreateAsync(releaseWithPostmortem).ConfigureAwait(false);

                    Release releaseWithoutPostmortem = new Release
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Title = "Release Without Postmortem",
                        Version = "1.0.1",
                        TagName = "v1.0.1",
                        Status = ReleaseStatusEnum.Candidate
                    };
                    await testDb.Driver.Releases.CreateAsync(releaseWithoutPostmortem).ConfigureAwait(false);

                    Deployment deploymentWithPostmortem = new Deployment
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        EnvironmentId = environment.Id,
                        EnvironmentName = environment.Name,
                        ReleaseId = releaseWithPostmortem.Id,
                        MissionId = mission.Id,
                        VoyageId = voyage.Id,
                        Title = "Deploy With Postmortem",
                        Status = DeploymentStatusEnum.RolledBack,
                        VerificationStatus = DeploymentVerificationStatusEnum.Passed
                    };
                    await testDb.Driver.Deployments.CreateAsync(deploymentWithPostmortem).ConfigureAwait(false);

                    Deployment deploymentWithoutPostmortem = new Deployment
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        EnvironmentId = environment.Id,
                        EnvironmentName = environment.Name,
                        ReleaseId = releaseWithoutPostmortem.Id,
                        Title = "Deploy Without Postmortem",
                        Status = DeploymentStatusEnum.Succeeded,
                        VerificationStatus = DeploymentVerificationStatusEnum.Passed
                    };
                    await testDb.Driver.Deployments.CreateAsync(deploymentWithoutPostmortem).ConfigureAwait(false);

                    IncidentService incidentService = new IncidentService(testDb.Driver);
                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");

                    Incident incidentWithPostmortem = await incidentService.CreateAsync(auth, new IncidentUpsertRequest
                    {
                        Title = "Incident With Postmortem",
                        Summary = "Rollback and review required",
                        Status = IncidentStatusEnum.Closed,
                        Severity = IncidentSeverityEnum.Critical,
                        EnvironmentId = environment.Id,
                        EnvironmentName = environment.Name,
                        DeploymentId = deploymentWithPostmortem.Id,
                        ReleaseId = releaseWithPostmortem.Id,
                        VesselId = vessel.Id,
                        MissionId = mission.Id,
                        VoyageId = voyage.Id,
                        RollbackDeploymentId = deploymentWithPostmortem.Id,
                        Postmortem = "Confirmed root cause and follow-up actions"
                    }).ConfigureAwait(false);

                    Incident incidentWithoutPostmortem = await incidentService.CreateAsync(auth, new IncidentUpsertRequest
                    {
                        Title = "Incident Without Postmortem",
                        Summary = "Still under investigation",
                        Status = IncidentStatusEnum.Open,
                        Severity = IncidentSeverityEnum.High,
                        EnvironmentId = environment.Id,
                        EnvironmentName = environment.Name,
                        DeploymentId = deploymentWithoutPostmortem.Id,
                        ReleaseId = releaseWithoutPostmortem.Id,
                        VesselId = vessel.Id
                    }).ConfigureAwait(false);

                    HistoricalTimelineService service = new HistoricalTimelineService(testDb.Driver);
                    EnumerationResult<HistoricalTimelineEntry> result = await service.EnumerateAsync(auth, new HistoricalTimelineQuery
                    {
                        PageNumber = 1,
                        PageSize = 100,
                        PostmortemOnly = true
                    }).ConfigureAwait(false);

                    AssertTrue(result.Objects.Exists(entry => entry.SourceType == "Incident" && entry.SourceId == incidentWithPostmortem.Id));
                    AssertTrue(result.Objects.Exists(entry => entry.SourceType == "Deployment" && entry.SourceId == deploymentWithPostmortem.Id));
                    AssertTrue(result.Objects.Exists(entry => entry.SourceType == "Release" && entry.SourceId == releaseWithPostmortem.Id));
                    AssertFalse(result.Objects.Exists(entry => entry.SourceType == "Incident" && entry.SourceId == incidentWithoutPostmortem.Id));
                    AssertFalse(result.Objects.Exists(entry => entry.SourceType == "Deployment" && entry.SourceId == deploymentWithoutPostmortem.Id));
                    AssertFalse(result.Objects.Exists(entry => entry.SourceType == "Release" && entry.SourceId == releaseWithoutPostmortem.Id));
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("enumerate_includes_backlog_refinement_sessions_routed_to_backlog_detail", "EnumerateAsync includes backlog refinement sessions and routes them to backlog detail", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);

                string tenantId = "ten_history_refinement";
                string userId = "usr_history_refinement";
                await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, true, "UnitTest");
                ObjectiveService objectives = new ObjectiveService(testDb.Driver);
                Objective objective = await objectives.CreateAsync(auth, new ObjectiveUpsertRequest
                {
                    Title = "History backlog item",
                    Status = ObjectiveStatusEnum.Scoped
                }).ConfigureAwait(false);
                Captain captain = new Captain("history-refinement-captain")
                {
                    TenantId = tenantId,
                    UserId = userId
                };
                await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                DateTime startedUtc = DateTime.UtcNow.AddMinutes(-5);
                ObjectiveRefinementSession session = new ObjectiveRefinementSession
                {
                    TenantId = tenantId,
                    UserId = userId,
                    ObjectiveId = objective.Id,
                    CaptainId = captain.Id,
                    Title = "Refine history coverage",
                    Status = ObjectiveRefinementSessionStatusEnum.Completed,
                    CreatedUtc = startedUtc.AddMinutes(-1),
                    StartedUtc = startedUtc,
                    CompletedUtc = startedUtc.AddMinutes(2),
                    LastUpdateUtc = startedUtc.AddMinutes(2)
                };
                await testDb.Driver.ObjectiveRefinementSessions.CreateAsync(session).ConfigureAwait(false);

                HistoricalTimelineService service = new HistoricalTimelineService(testDb.Driver);
                EnumerationResult<HistoricalTimelineEntry> result = await service.EnumerateAsync(auth, new HistoricalTimelineQuery
                {
                    PageNumber = 1,
                    PageSize = 25,
                    ObjectiveId = objective.Id,
                    SourceTypes = new List<string> { "ObjectiveRefinementSession" }
                }).ConfigureAwait(false);

                AssertEqual(1, result.Objects.Count);
                HistoricalTimelineEntry entry = result.Objects[0];
                AssertEqual("ObjectiveRefinementSession", entry.SourceType);
                AssertEqual(session.Id, entry.SourceId);
                AssertEqual(objective.Id, entry.ObjectiveId);
                AssertEqual("/backlog/" + objective.Id, entry.Route);
                AssertEqual(ObjectiveRefinementSessionStatusEnum.Completed.ToString(), entry.Status);
            }));

            // Audit addition: null auth is rejected (confirmed against source).

            cases.Add(CaseAsync("event_severity_from_action_segment", "Event severity comes from the event type's action segment, not a substring", TestTags.Positive, () =>
            {
                AssertEqual("error", HistoricalTimelineService.ClassifyEventSeverity("mission.failed"));
                AssertEqual("error", HistoricalTimelineService.ClassifyEventSeverity("mission.landing_failed"));
                AssertEqual("info", HistoricalTimelineService.ClassifyEventSeverity("mission.completed"));
                // The old "fail" substring check marked these as errors.
                AssertEqual("info", HistoricalTimelineService.ClassifyEventSeverity("captain.failover_completed"));
                AssertEqual("info", HistoricalTimelineService.ClassifyEventSeverity("failed.notification_sent"));
                AssertEqual("info", HistoricalTimelineService.ClassifyEventSeverity(null));
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("enumerate_null_auth_throws", "EnumerateAsync NullAuth Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HistoricalTimelineService service = new HistoricalTimelineService(testDb.Driver);
                await AssertThrowsAsync<ArgumentNullException>(() => service.EnumerateAsync(null!, new HistoricalTimelineQuery()));
            }));

            // Audit addition: null query is rejected (confirmed against source).

            cases.Add(CaseAsync("enumerate_null_query_throws", "EnumerateAsync NullQuery Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HistoricalTimelineService service = new HistoricalTimelineService(testDb.Driver);
                AuthContext auth = AuthContext.Authenticated("ten_hist_null", "usr_hist_null", false, true, "UnitTest");
                await AssertThrowsAsync<ArgumentNullException>(() => service.EnumerateAsync(auth, null!));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HistoricalTimelineService",
                displayName: "Historical Timeline Service",
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

        private static Vessel CreateVessel(string tenantId, string userId, string workingDirectory)
        {
            return new Vessel
            {
                TenantId = tenantId,
                UserId = userId,
                Name = "History Vessel",
                RepoUrl = "file:///tmp/armada-history.git",
                LocalPath = workingDirectory,
                WorkingDirectory = workingDirectory,
                DefaultBranch = "main"
            };
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
                suiteId: "Services.HistoricalTimelineService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
