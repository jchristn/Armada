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
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ReleaseService"/>: first-class release drafting, version
    /// derivation and semantic bumping, and artifact refresh over a live SQLite store. Positive
    /// cases assert derivation from linked work, prior-version bumping, and artifact rebuilds;
    /// negative cases cover the audited not-found refresh and null-request guards.
    /// </summary>
    public sealed class ReleaseServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the ReleaseService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("create_derives_version_missions_and_artifacts_from_linked_work", "CreateAsync derives version, missions, and artifacts from linked work", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                ReleaseService releases = new ReleaseService(testDb.Driver, workflowProfiles, logging);

                string tenantId = "ten_release";
                string userId = "usr_release";
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-release-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                    Vessel vessel = CreateVessel(tenantId, userId, workingDirectory);
                    vessel.Name = "Release Vessel";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    WorkflowProfile profile = new WorkflowProfile
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Name = "Release Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        ReleaseVersioningCommand = "echo 2.3.4"
                    };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                    Voyage voyage = new Voyage
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        Title = "Release Voyage",
                        Description = "Voyage for release drafting",
                        Status = VoyageStatusEnum.Open
                    };
                    await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        VoyageId = voyage.Id,
                        Title = "Release Mission",
                        Description = "Mission in release",
                        Status = MissionStatusEnum.Complete,
                        PrUrl = "https://example.test/pr/123"
                    };
                    await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    CheckRun checkRun = new CheckRun
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        WorkflowProfileId = profile.Id,
                        VesselId = vessel.Id,
                        MissionId = mission.Id,
                        VoyageId = voyage.Id,
                        Type = CheckRunTypeEnum.ReleaseVersioning,
                        Status = CheckRunStatusEnum.Passed,
                        Command = "echo 2.3.4",
                        Summary = "Release candidate 2.3.4",
                        Artifacts = new List<CheckRunArtifact>
                        {
                            new CheckRunArtifact
                            {
                                Path = "artifacts/app.zip",
                                SizeBytes = 1024,
                                LastWriteUtc = DateTime.UtcNow
                            }
                        }
                    };
                    await testDb.Driver.CheckRuns.CreateAsync(checkRun).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, false, "UnitTest");
                    Release release = await releases.CreateAsync(auth, new ReleaseUpsertRequest
                    {
                        WorkflowProfileId = profile.Id,
                        VoyageIds = new List<string> { voyage.Id },
                        CheckRunIds = new List<string> { checkRun.Id },
                        Status = ReleaseStatusEnum.Candidate
                    }).ConfigureAwait(false);

                    AssertStartsWith("rel_", release.Id);
                    AssertEqual(vessel.Id, release.VesselId);
                    AssertEqual(profile.Id, release.WorkflowProfileId);
                    AssertEqual("2.3.4", release.Version);
                    AssertEqual("v2.3.4", release.TagName);
                    AssertEqual(ReleaseStatusEnum.Candidate, release.Status);
                    AssertTrue(release.MissionIds.Exists(id => id == mission.Id), "Expected mission linked through voyage.");
                    AssertTrue(release.Artifacts.Count == 1, "Expected one derived artifact.");
                    AssertEqual("artifacts/app.zip", release.Artifacts[0].Path);
                    AssertContains(vessel.Name, release.Summary ?? String.Empty);
                    AssertContains(mission.Id, release.Notes ?? String.Empty);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("create_bumps_prior_semantic_version_when_no_explicit_version_exists", "CreateAsync bumps prior semantic version when no explicit version exists", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                ReleaseService releases = new ReleaseService(testDb.Driver, workflowProfiles, logging);

                string tenantId = "ten_release_bump";
                string userId = "usr_release_bump";
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-release-bump-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                    Vessel vessel = CreateVessel(tenantId, userId, workingDirectory);
                    vessel.Name = "Version Vessel";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    await testDb.Driver.Releases.CreateAsync(new Release
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Title = "Previous Release",
                        Version = "1.4.2",
                        TagName = "v1.4.2",
                        Status = ReleaseStatusEnum.Shipped
                    }).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, false, "UnitTest");
                    Release release = await releases.CreateAsync(auth, new ReleaseUpsertRequest
                    {
                        VesselId = vessel.Id,
                        Title = "Next Release"
                    }).ConfigureAwait(false);

                    AssertEqual("1.4.3", release.Version);
                    AssertEqual("v1.4.3", release.TagName);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("refresh_rebuilds_derived_artifacts_from_linked_checks", "RefreshAsync rebuilds derived artifacts from linked checks", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                ReleaseService releases = new ReleaseService(testDb.Driver, workflowProfiles, logging);

                string tenantId = "ten_release_refresh";
                string userId = "usr_release_refresh";
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-release-refresh-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                    Vessel vessel = CreateVessel(tenantId, userId, workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    CheckRun checkRun = new CheckRun
                    {
                        TenantId = tenantId,
                        UserId = userId,
                        VesselId = vessel.Id,
                        Type = CheckRunTypeEnum.PublishArtifact,
                        Status = CheckRunStatusEnum.Passed,
                        Command = "echo publish",
                        Summary = "Published artifacts",
                        Artifacts = new List<CheckRunArtifact>
                        {
                            new CheckRunArtifact
                            {
                                Path = "artifacts/one.zip",
                                SizeBytes = 128,
                                LastWriteUtc = DateTime.UtcNow
                            }
                        }
                    };
                    await testDb.Driver.CheckRuns.CreateAsync(checkRun).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, false, "UnitTest");
                    Release release = await releases.CreateAsync(auth, new ReleaseUpsertRequest
                    {
                        VesselId = vessel.Id,
                        Title = "Refreshable Release",
                        CheckRunIds = new List<string> { checkRun.Id }
                    }).ConfigureAwait(false);
                    AssertEqual(1, release.Artifacts.Count);

                    checkRun.Artifacts.Add(new CheckRunArtifact
                    {
                        Path = "artifacts/two.zip",
                        SizeBytes = 256,
                        LastWriteUtc = DateTime.UtcNow
                    });
                    await testDb.Driver.CheckRuns.UpdateAsync(checkRun).ConfigureAwait(false);

                    Release refreshed = await releases.RefreshAsync(auth, release.Id).ConfigureAwait(false);
                    AssertEqual(2, refreshed.Artifacts.Count);
                    AssertEqual("Refreshable Release", refreshed.Title);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            // Audit addition: refreshing an unknown release throws not-found (confirmed against source).

            cases.Add(CaseAsync("refresh_unknown_id_throws", "RefreshAsync UnknownId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                ReleaseService releases = new ReleaseService(testDb.Driver, workflowProfiles, logging);

                string tenantId = "ten_release_refresh_missing";
                string userId = "usr_release_refresh_missing";
                await EnsureTenantAndUserAsync(testDb, tenantId, userId).ConfigureAwait(false);

                AuthContext auth = AuthContext.Authenticated(tenantId, userId, false, false, "UnitTest");
                await AssertThrowsAsync<KeyNotFoundException>(() => releases.RefreshAsync(auth, "rel_missing"));
            }));

            // Audit addition: create with a null request throws (confirmed against source).

            cases.Add(CaseAsync("create_null_request_throws", "CreateAsync NullRequest Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                ReleaseService releases = new ReleaseService(testDb.Driver, workflowProfiles, logging);

                AuthContext auth = AuthContext.Authenticated("ten_release_null", "usr_release_null", false, false, "UnitTest");
                await AssertThrowsAsync<ArgumentNullException>(() => releases.CreateAsync(auth, null!));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ReleaseService",
                displayName: "Release Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
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

        private static Vessel CreateVessel(string tenantId, string userId, string workingDirectory)
        {
            return new Vessel
            {
                TenantId = tenantId,
                UserId = userId,
                Name = "Release Workflow Vessel",
                RepoUrl = "file:///tmp/armada-release.git",
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
                suiteId: "Services.ReleaseService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
