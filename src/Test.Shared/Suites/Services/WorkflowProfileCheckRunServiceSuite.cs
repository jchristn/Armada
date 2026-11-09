namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for workflow-profile resolution/validation and structured check execution:
    /// validation rejections (no commands, cross-tenant vessel scope, inputs scoped to unknown
    /// environments), command previews, vessel/fleet/global resolution and preview, check-run
    /// execution with artifact collection, retry, structured test/coverage parsing, readiness
    /// evaluation (missing inputs, toolchains/environments/checklist, environment-scoped inputs),
    /// landing preview gating, and fail-fast on missing required inputs.
    /// </summary>
    public sealed class WorkflowProfileCheckRunServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.WorkflowProfileCheckRunService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Workflow Profile and Check Run Services suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("validate_async_rejects_workflow_profile_with_no_commands", "ValidateAsync rejects workflow profile with no commands", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService service = new WorkflowProfileService(testDb.Driver, logging);

                WorkflowProfileValidationResult result = await service.ValidateAsync(new WorkflowProfile
                {
                    TenantId = "ten_profile",
                    UserId = "usr_profile",
                    Name = "Empty Workflow",
                    Scope = WorkflowProfileScopeEnum.Global
                }).ConfigureAwait(false);

                AssertFalse(result.IsValid);
                AssertContains("At least one build, test, release, deploy, verification, migration, security, or performance command is required.", String.Join(" ", result.Errors));
            }));

            cases.Add(CaseAsync("validate_async_returns_resolved_command_previews", "ValidateAsync returns resolved command previews", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService service = new WorkflowProfileService(testDb.Driver, logging);

                WorkflowProfileValidationResult result = await service.ValidateAsync(new WorkflowProfile
                {
                    TenantId = "ten_profile",
                    UserId = "usr_profile",
                    Name = "Preview Workflow",
                    Scope = WorkflowProfileScopeEnum.Global,
                    BuildCommand = "dotnet build",
                    UnitTestCommand = "dotnet test",
                    Environments = new List<WorkflowEnvironmentProfile>
                    {
                        new WorkflowEnvironmentProfile
                        {
                            EnvironmentName = "dev",
                            DeployCommand = "./deploy-dev.sh",
                            HealthCheckCommand = "./health-dev.sh"
                        }
                    }
                }).ConfigureAwait(false);

                AssertTrue(result.IsValid);
                AssertTrue(result.CommandPreviews.Count == 4, "Expected build, unit test, deploy, and health previews.");
                AssertTrue(result.CommandPreviews.Exists(command => command.CheckType == CheckRunTypeEnum.Build && command.Command == "dotnet build"));
                AssertTrue(result.CommandPreviews.Exists(command => command.CheckType == CheckRunTypeEnum.UnitTest && command.Command == "dotnet test"));
                AssertTrue(result.CommandPreviews.Exists(command => command.CheckType == CheckRunTypeEnum.Deploy && command.EnvironmentName == "dev" && command.Command == "./deploy-dev.sh"));
                AssertTrue(result.CommandPreviews.Exists(command => command.CheckType == CheckRunTypeEnum.HealthCheck && command.EnvironmentName == "dev" && command.Command == "./health-dev.sh"));
            }));

            cases.Add(CaseAsync("validate_async_rejects_cross_tenant_vessel_scope", "ValidateAsync rejects cross-tenant vessel scope", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService service = new WorkflowProfileService(testDb.Driver, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_source", "usr_source").ConfigureAwait(false);
                await EnsureTenantAndUserAsync(testDb, "ten_other", "usr_other").ConfigureAwait(false);

                Vessel vessel = CreateVessel("ten_source", "usr_source", Path.Combine(Path.GetTempPath(), "armada-profile-cross-" + Guid.NewGuid().ToString("N")));
                await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                WorkflowProfileValidationResult result = await service.ValidateAsync(new WorkflowProfile
                {
                    TenantId = "ten_other",
                    UserId = "usr_other",
                    Name = "Cross Tenant Vessel Workflow",
                    Scope = WorkflowProfileScopeEnum.Vessel,
                    VesselId = vessel.Id,
                    BuildCommand = "dotnet --version"
                }).ConfigureAwait(false);

                AssertFalse(result.IsValid);
                AssertContains("Vessel does not belong to the workflow profile tenant.", String.Join(" ", result.Errors));
            }));

            cases.Add(CaseAsync("validate_async_rejects_required_inputs_scoped_to_unknown_environments", "ValidateAsync rejects required inputs scoped to unknown environments", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService service = new WorkflowProfileService(testDb.Driver, logging);

                WorkflowProfileValidationResult result = await service.ValidateAsync(new WorkflowProfile
                {
                    TenantId = "ten_profile_scope",
                    UserId = "usr_profile_scope",
                    Name = "Scoped Input Workflow",
                    Scope = WorkflowProfileScopeEnum.Global,
                    BuildCommand = "dotnet --version",
                    Environments = new List<WorkflowEnvironmentProfile>
                    {
                        new WorkflowEnvironmentProfile
                        {
                            EnvironmentName = "staging",
                            DeployCommand = "echo deploy"
                        }
                    },
                    RequiredInputs = new List<WorkflowInputReference>
                    {
                        new WorkflowInputReference
                        {
                            Provider = WorkflowInputReferenceProviderEnum.EnvironmentVariable,
                            Key = "MISSING_SCOPE_INPUT",
                            EnvironmentName = "production"
                        }
                    }
                }).ConfigureAwait(false);

                AssertFalse(result.IsValid);
                AssertContains("unknown environments", String.Join(" ", result.Errors));
                AssertContains("production", String.Join(" ", result.Errors));
            }));

            cases.Add(CaseAsync("resolve_for_vessel_async_prefers_vessel_over_fleet_over_global", "ResolveForVesselAsync prefers vessel over fleet over global", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService service = new WorkflowProfileService(testDb.Driver, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_profile", "usr_profile").ConfigureAwait(false);

                Fleet fleet = new Fleet
                {
                    TenantId = "ten_profile",
                    UserId = "usr_profile",
                    Name = "Resolution Fleet"
                };
                await testDb.Driver.Fleets.CreateAsync(fleet).ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-profile-resolve-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);
                try
                {
                    Vessel vessel = CreateVessel("ten_profile", "usr_profile", workingDirectory);
                    vessel.FleetId = fleet.Id;
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    WorkflowProfile global = new WorkflowProfile
                    {
                        TenantId = "ten_profile",
                        UserId = "usr_profile",
                        Name = "Global Workflow",
                        Scope = WorkflowProfileScopeEnum.Global,
                        BuildCommand = "global-build"
                    };
                    WorkflowProfile fleetProfile = new WorkflowProfile
                    {
                        TenantId = "ten_profile",
                        UserId = "usr_profile",
                        Name = "Fleet Workflow",
                        Scope = WorkflowProfileScopeEnum.Fleet,
                        FleetId = fleet.Id,
                        BuildCommand = "fleet-build"
                    };
                    WorkflowProfile vesselProfile = new WorkflowProfile
                    {
                        TenantId = "ten_profile",
                        UserId = "usr_profile",
                        Name = "Vessel Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        BuildCommand = "vessel-build"
                    };

                    await testDb.Driver.WorkflowProfiles.CreateAsync(global).ConfigureAwait(false);
                    await testDb.Driver.WorkflowProfiles.CreateAsync(fleetProfile).ConfigureAwait(false);
                    await testDb.Driver.WorkflowProfiles.CreateAsync(vesselProfile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_profile", "usr_profile", false, false, "UnitTest");
                    WorkflowProfile? resolved = await service.ResolveForVesselAsync(auth, vessel).ConfigureAwait(false);

                    AssertNotNull(resolved);
                    AssertEqual(vesselProfile.Id, resolved!.Id);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("preview_for_vessel_async_returns_resolution_mode_and_command_previews", "PreviewForVesselAsync returns resolution mode and command previews", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService service = new WorkflowProfileService(testDb.Driver, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_preview", "usr_preview").ConfigureAwait(false);

                Fleet fleet = new Fleet
                {
                    TenantId = "ten_preview",
                    UserId = "usr_preview",
                    Name = "Preview Fleet"
                };
                await testDb.Driver.Fleets.CreateAsync(fleet).ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-profile-preview-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);
                try
                {
                    Vessel vessel = CreateVessel("ten_preview", "usr_preview", workingDirectory);
                    vessel.FleetId = fleet.Id;
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    WorkflowProfile global = new WorkflowProfile
                    {
                        TenantId = "ten_preview",
                        UserId = "usr_preview",
                        Name = "Global Preview Workflow",
                        Scope = WorkflowProfileScopeEnum.Global,
                        BuildCommand = "global-build"
                    };
                    WorkflowProfile fleetProfile = new WorkflowProfile
                    {
                        TenantId = "ten_preview",
                        UserId = "usr_preview",
                        Name = "Fleet Preview Workflow",
                        Scope = WorkflowProfileScopeEnum.Fleet,
                        FleetId = fleet.Id,
                        BuildCommand = "fleet-build",
                        Environments = new List<WorkflowEnvironmentProfile>
                        {
                            new WorkflowEnvironmentProfile
                            {
                                EnvironmentName = "staging",
                                DeployCommand = "fleet-deploy"
                            }
                        }
                    };

                    await testDb.Driver.WorkflowProfiles.CreateAsync(global).ConfigureAwait(false);
                    await testDb.Driver.WorkflowProfiles.CreateAsync(fleetProfile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_preview", "usr_preview", false, false, "UnitTest");

                    WorkflowProfileResolutionPreviewResult? preview = await service.PreviewForVesselAsync(auth, vessel).ConfigureAwait(false);
                    AssertNotNull(preview);
                    AssertEqual(fleetProfile.Id, preview!.ResolvedProfile?.Id);
                    AssertEqual(WorkflowProfileResolutionModeEnum.Fleet, preview.ResolutionMode);
                    AssertTrue(preview.CommandPreviews.Exists(command => command.CheckType == CheckRunTypeEnum.Build && command.Command == "fleet-build"));
                    AssertTrue(preview.CommandPreviews.Exists(command => command.CheckType == CheckRunTypeEnum.Deploy && command.EnvironmentName == "staging" && command.Command == "fleet-deploy"));

                    WorkflowProfileResolutionPreviewResult? explicitPreview = await service.PreviewForVesselAsync(auth, vessel, global.Id).ConfigureAwait(false);
                    AssertNotNull(explicitPreview);
                    AssertEqual(global.Id, explicitPreview!.ResolvedProfile?.Id);
                    AssertEqual(WorkflowProfileResolutionModeEnum.Explicit, explicitPreview.ResolutionMode);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("run_async_executes_workflow_profile_command_and_collects_artifacts", "RunAsync executes workflow profile command and collects artifacts", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging);
                CheckRunService checkRuns = new CheckRunService(testDb.Driver, workflowProfiles, readiness, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_checks", "usr_checks").ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-check-run-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(workingDirectory, "artifacts"));

                try
                {
                    await File.WriteAllTextAsync(Path.Combine(workingDirectory, "artifacts", "existing.txt"), "artifact").ConfigureAwait(false);

                    Vessel vessel = CreateVessel("ten_checks", "usr_checks", workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    WorkflowProfile profile = new WorkflowProfile
                    {
                        TenantId = "ten_checks",
                        UserId = "usr_checks",
                        Name = "Build Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        BuildCommand = "dotnet --version",
                        ExpectedArtifacts = new List<string> { "artifacts/existing.txt" }
                    };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_checks", "usr_checks", false, false, "UnitTest");
                    CheckRun run = await checkRuns.RunAsync(auth, new CheckRunRequest
                    {
                        VesselId = vessel.Id,
                        Type = CheckRunTypeEnum.Build,
                        Label = "Build"
                    }).ConfigureAwait(false);

                    AssertEqual(CheckRunStatusEnum.Passed, run.Status);
                    AssertEqual(0, run.ExitCode ?? -1);
                    AssertTrue(run.DurationMs.HasValue && run.DurationMs.Value >= 0);
                    AssertTrue(run.Artifacts.Count == 1, "Expected one collected artifact");
                    AssertEqual("artifacts/existing.txt", run.Artifacts[0].Path);
                    AssertContains("passed", run.Summary ?? String.Empty);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("retry_async_re_executes_prior_check_run", "RetryAsync re-executes prior check run", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging);
                CheckRunService checkRuns = new CheckRunService(testDb.Driver, workflowProfiles, readiness, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_retry", "usr_retry").ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-check-retry-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    Vessel vessel = CreateVessel("ten_retry", "usr_retry", workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    WorkflowProfile profile = new WorkflowProfile
                    {
                        TenantId = "ten_retry",
                        UserId = "usr_retry",
                        Name = "Retry Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        UnitTestCommand = "dotnet --version"
                    };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_retry", "usr_retry", false, false, "UnitTest");
                    CheckRun first = await checkRuns.RunAsync(auth, new CheckRunRequest
                    {
                        VesselId = vessel.Id,
                        Type = CheckRunTypeEnum.UnitTest,
                        Label = "Unit Tests"
                    }).ConfigureAwait(false);
                    CheckRun retry = await checkRuns.RetryAsync(auth, first.Id).ConfigureAwait(false);

                    AssertNotEqual(first.Id, retry.Id);
                    AssertEqual(CheckRunStatusEnum.Passed, retry.Status);

                    EnumerationResult<CheckRun> allRuns = await testDb.Driver.CheckRuns.EnumerateAsync(new CheckRunQuery
                    {
                        TenantId = "ten_retry",
                        UserId = "usr_retry",
                        VesselId = vessel.Id,
                        PageNumber = 1,
                        PageSize = 10
                    }).ConfigureAwait(false);
                    AssertEqual(2L, allRuns.TotalRecords);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("run_async_parses_structured_test_and_coverage_summaries", "RunAsync parses structured test and coverage summaries", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging);
                CheckRunService checkRuns = new CheckRunService(testDb.Driver, workflowProfiles, readiness, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_parse", "usr_parse").ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-check-parse-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(workingDirectory, "summary.txt"),
                        "Passed!  - Failed: 0, Passed: 3, Skipped: 1, Total: 4, Duration: 1.5 s").ConfigureAwait(false);
                    await File.WriteAllTextAsync(
                        Path.Combine(workingDirectory, "coverage.cobertura.xml"),
                        """
                        <coverage line-rate="0.75" branch-rate="0.5" lines-covered="15" lines-valid="20" branches-covered="4" branches-valid="8"></coverage>
                        """).ConfigureAwait(false);

                    Vessel vessel = CreateVessel("ten_parse", "usr_parse", workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    WorkflowProfile profile = new WorkflowProfile
                    {
                        TenantId = "ten_parse",
                        UserId = "usr_parse",
                        Name = "Parse Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        UnitTestCommand = BuildEmitFileCommand("summary.txt"),
                        ExpectedArtifacts = new List<string> { "coverage.cobertura.xml" }
                    };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_parse", "usr_parse", false, false, "UnitTest");
                    CheckRun run = await checkRuns.RunAsync(auth, new CheckRunRequest
                    {
                        VesselId = vessel.Id,
                        Type = CheckRunTypeEnum.UnitTest,
                        Label = "Unit Tests"
                    }).ConfigureAwait(false);

                    AssertNotNull(run.TestSummary);
                    AssertEqual(3, run.TestSummary!.Passed ?? -1);
                    AssertEqual(0, run.TestSummary.Failed ?? -1);
                    AssertEqual(1, run.TestSummary.Skipped ?? -1);
                    AssertEqual(4, run.TestSummary.Total ?? -1);
                    AssertTrue(run.TestSummary.DurationMs.HasValue && run.TestSummary.DurationMs.Value >= 1500, "Expected parsed test duration");

                    AssertNotNull(run.CoverageSummary);
                    AssertEqual("cobertura", run.CoverageSummary!.Format);
                    AssertEqual("coverage.cobertura.xml", run.CoverageSummary.SourcePath);
                    AssertEqual(75d, run.CoverageSummary.Lines?.Percentage ?? -1d);
                    AssertEqual(15, run.CoverageSummary.Lines?.Covered ?? -1);
                    AssertEqual(20, run.CoverageSummary.Lines?.Total ?? -1);
                    AssertContains("3 passed", run.Summary ?? String.Empty);
                    AssertContains("75", run.Summary ?? String.Empty);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("readiness_blocks_specific_check_when_a_required_environment_variable_is_missing", "Readiness blocks specific check when a required environment variable is missing", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_ready", "usr_ready").ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-readiness-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    Vessel vessel = CreateVessel("ten_ready", "usr_ready", workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    string missingVariable = "ARMADA_TEST_INPUT_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                    WorkflowProfile profile = new WorkflowProfile
                    {
                        TenantId = "ten_ready",
                        UserId = "usr_ready",
                        Name = "Readiness Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        BuildCommand = "dotnet --version",
                        RequiredInputs = new List<WorkflowInputReference>
                        {
                            new WorkflowInputReference
                            {
                                Provider = WorkflowInputReferenceProviderEnum.EnvironmentVariable,
                                Key = missingVariable
                            }
                        }
                    };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_ready", "usr_ready", false, false, "UnitTest");
                    VesselReadinessResult result = await readiness.EvaluateAsync(
                        auth,
                        vessel,
                        requestedCheckType: CheckRunTypeEnum.Build).ConfigureAwait(false);

                    AssertFalse(result.IsReady);
                    AssertTrue(result.ErrorCount >= 1, "Expected at least one blocking readiness error");
                    AssertContains(missingVariable, String.Join(" ", result.Issues.Select(issue => issue.Message)));
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("readiness_exposes_toolchains_environments_and_setup_checklist", "Readiness exposes toolchains, environments, and setup checklist", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_setup", "usr_setup").ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-readiness-setup-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);
                Directory.CreateDirectory(Path.Combine(workingDirectory, ".git"));

                try
                {
                    await File.WriteAllTextAsync(Path.Combine(workingDirectory, "Service.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>").ConfigureAwait(false);

                    Vessel vessel = CreateVessel("ten_setup", "usr_setup", workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    WorkflowProfile profile = new WorkflowProfile
                    {
                        TenantId = "ten_setup",
                        UserId = "usr_setup",
                        Name = "Setup Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        BuildCommand = "dotnet --version",
                        Environments = new List<WorkflowEnvironmentProfile>
                        {
                            new WorkflowEnvironmentProfile
                            {
                                EnvironmentName = "staging",
                                DeployCommand = "echo deploy"
                            }
                        }
                    };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_setup", "usr_setup", false, false, "UnitTest");
                    VesselReadinessResult result = await readiness.EvaluateAsync(auth, vessel).ConfigureAwait(false);

                    AssertTrue(result.HasWorkingDirectory, "Expected working directory to be available.");
                    AssertTrue(result.DetectedToolchains.Contains("dotnet"), "Expected dotnet toolchain to be detected.");
                    AssertTrue(result.DeploymentEnvironments.Contains("staging"), "Expected staging environment to be surfaced.");
                    AssertTrue(result.SetupChecklist.Count >= 5, "Expected readiness setup checklist items.");
                    AssertTrue(result.SetupChecklist.Exists(item => item.Code == "workflow_profile" && item.IsSatisfied), "Expected workflow profile checklist item to be satisfied.");
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("readiness_scopes_required_inputs_by_environment_and_recognizes_provider_backed_references", "Readiness scopes required inputs by environment and recognizes provider-backed references", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_scoped", "usr_scoped").ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-readiness-scoped-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);
                Directory.CreateDirectory(Path.Combine(workingDirectory, ".git"));

                string buildVariable = "ARMADA_TEST_BUILD_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                string deployVariable = "ARMADA_TEST_DEPLOY_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                string? originalBuildVariable = Environment.GetEnvironmentVariable(buildVariable);
                string? originalDeployVariable = Environment.GetEnvironmentVariable(deployVariable);
                string? originalAwsRegion = Environment.GetEnvironmentVariable("AWS_REGION");
                string? originalAwsProfile = Environment.GetEnvironmentVariable("AWS_PROFILE");

                try
                {
                    Environment.SetEnvironmentVariable(buildVariable, "ready");
                    Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
                    Environment.SetEnvironmentVariable("AWS_PROFILE", "armada-test");

                    Vessel vessel = CreateVessel("ten_scoped", "usr_scoped", workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    WorkflowProfile profile = new WorkflowProfile
                    {
                        TenantId = "ten_scoped",
                        UserId = "usr_scoped",
                        Name = "Scoped Inputs Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        BuildCommand = "dotnet --version",
                        Environments = new List<WorkflowEnvironmentProfile>
                        {
                            new WorkflowEnvironmentProfile
                            {
                                EnvironmentName = "prod",
                                DeployCommand = "echo deploy"
                            }
                        },
                        RequiredInputs = new List<WorkflowInputReference>
                        {
                            new WorkflowInputReference
                            {
                                Provider = WorkflowInputReferenceProviderEnum.EnvironmentVariable,
                                Key = buildVariable
                            },
                            new WorkflowInputReference
                            {
                                Provider = WorkflowInputReferenceProviderEnum.EnvironmentVariable,
                                Key = deployVariable,
                                EnvironmentName = "prod"
                            },
                            new WorkflowInputReference
                            {
                                Provider = WorkflowInputReferenceProviderEnum.AwsSecretsManager,
                                Key = "armada/prod/deploy-token",
                                EnvironmentName = "prod",
                                Description = "Deploy token"
                            }
                        }
                    };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_scoped", "usr_scoped", false, false, "UnitTest");

                    VesselReadinessResult buildResult = await readiness.EvaluateAsync(
                        auth,
                        vessel,
                        requestedCheckType: CheckRunTypeEnum.Build).ConfigureAwait(false);
                    AssertTrue(buildResult.IsReady, "Build readiness should ignore environment-scoped deploy inputs.");
                    AssertFalse(buildResult.Issues.Any(issue => issue.Message.Contains(deployVariable, StringComparison.OrdinalIgnoreCase)));
                    AssertTrue(buildResult.SetupChecklistTotalCount >= 8, "Expected expanded onboarding checklist.");
                    AssertTrue(buildResult.SetupChecklistSatisfiedCount > 0, "Expected some onboarding checklist items to be satisfied.");

                    VesselReadinessResult deployBlockedResult = await readiness.EvaluateAsync(
                        auth,
                        vessel,
                        requestedCheckType: CheckRunTypeEnum.Deploy,
                        requestedEnvironmentName: "prod").ConfigureAwait(false);
                    AssertFalse(deployBlockedResult.IsReady, "Deploy readiness should block when the environment-scoped variable is missing.");
                    AssertTrue(deployBlockedResult.Issues.Any(issue => issue.Message.Contains(deployVariable, StringComparison.OrdinalIgnoreCase)));
                    AssertFalse(deployBlockedResult.Issues.Any(issue =>
                        issue.Code == "required_input_missing"
                        && issue.Message.Contains("armada/prod/deploy-token", StringComparison.OrdinalIgnoreCase)),
                        "AWS provider-backed reference should be satisfied by host credentials.");

                    Environment.SetEnvironmentVariable(deployVariable, "ready");

                    VesselReadinessResult deployReadyResult = await readiness.EvaluateAsync(
                        auth,
                        vessel,
                        requestedCheckType: CheckRunTypeEnum.Deploy,
                        requestedEnvironmentName: "prod").ConfigureAwait(false);
                    AssertTrue(deployReadyResult.IsReady, "Deploy readiness should pass once the prod-scoped input exists.");
                    AssertFalse(deployReadyResult.Issues.Any(issue => issue.Code == "required_input_missing"));
                }
                finally
                {
                    Environment.SetEnvironmentVariable(buildVariable, originalBuildVariable);
                    Environment.SetEnvironmentVariable(deployVariable, originalDeployVariable);
                    Environment.SetEnvironmentVariable("AWS_REGION", originalAwsRegion);
                    Environment.SetEnvironmentVariable("AWS_PROFILE", originalAwsProfile);
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("landing_preview_blocks_landing_when_passing_checks_are_required_and_none_exist", "Landing preview blocks landing when passing checks are required and none exist", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                LandingPreviewService landingPreview = new LandingPreviewService(testDb.Driver, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_land", "usr_land").ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-landing-preview-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    Vessel vessel = CreateVessel("ten_land", "usr_land", workingDirectory);
                    vessel.RequirePassingChecksToLand = true;
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_land", "usr_land", false, false, "UnitTest");
                    LandingPreviewResult preview = await landingPreview.PreviewForVesselAsync(auth, vessel, "feature/work").ConfigureAwait(false);

                    AssertFalse(preview.IsReadyToLand, "Expected landing preview to block landing.");
                    AssertFalse(preview.HasPassingChecks, "Expected preview to report no passing checks.");
                    AssertTrue(preview.Issues.Exists(issue => issue.Code == "passing_checks_required"), "Expected passing_checks_required issue.");
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("run_async_fails_fast_when_required_workflow_input_is_missing", "RunAsync fails fast when required workflow input is missing", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging);
                CheckRunService checkRuns = new CheckRunService(testDb.Driver, workflowProfiles, readiness, logging);

                await EnsureTenantAndUserAsync(testDb, "ten_blocked", "usr_blocked").ConfigureAwait(false);

                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-check-blocked-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    Vessel vessel = CreateVessel("ten_blocked", "usr_blocked", workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    string missingVariable = "ARMADA_TEST_BLOCK_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                    WorkflowProfile profile = new WorkflowProfile
                    {
                        TenantId = "ten_blocked",
                        UserId = "usr_blocked",
                        Name = "Blocked Workflow",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = vessel.Id,
                        BuildCommand = "dotnet --version",
                        RequiredInputs = new List<WorkflowInputReference>
                        {
                            new WorkflowInputReference
                            {
                                Provider = WorkflowInputReferenceProviderEnum.EnvironmentVariable,
                                Key = missingVariable
                            }
                        }
                    };
                    await testDb.Driver.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);

                    AuthContext auth = AuthContext.Authenticated("ten_blocked", "usr_blocked", false, false, "UnitTest");
                    await AssertThrowsAsync<InvalidOperationException>(async () =>
                    {
                        await checkRuns.RunAsync(auth, new CheckRunRequest
                        {
                            VesselId = vessel.Id,
                            Type = CheckRunTypeEnum.Build
                        }).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }
                finally
                {
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("run_async_timeout_keeps_host_output", "A timed out host check keeps captured output", TestTags.Negative, async () =>
            {
                using (HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false))
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = new ArmadaSettings
                    {
                        LogDirectory = Path.Combine(git.AdmiralData, "logs"),
                        DocksDirectory = Path.Combine(git.AdmiralData, "docks"),
                        ReposDirectory = Path.Combine(git.AdmiralData, "repos")
                    };
                    HarborConnectionManager manager = new HarborConnectionManager(new HarborService(testDb.Driver, logging), logging, null);
                    HarborDockSettings harborSettings = git.Settings.Clone();
                    harborSettings.Repositories.Add(new HarborRepositoryMapping("Workflow Vessel", git.Checkout));
                    HarborDockManager docks = new HarborDockManager(() => harborSettings, logging);
                    await EnsureTenantAndUserAsync(testDb, "ten_check_timeout", "usr_check_timeout").ConfigureAwait(false);
                    using (InProcessHarbor harbor = await InProcessHarbor.ConnectAsync(
                        manager,
                        "hbr_check_timeout",
                        "ten_check_timeout",
                        "usr_check_timeout",
                        new AgentRuntimeFactory(logging),
                        new List<string> { "ClaudeCode", "git" },
                        logging,
                        new CheckTimeoutExecutor(),
                        null,
                        docks).ConfigureAwait(false))
                    {
                        Vessel vessel = CreateVessel("ten_check_timeout", "usr_check_timeout", null);
                        vessel.RepoUrl = git.Origin;
                        await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                        WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                        VesselHostResolver hosts = new VesselHostResolver(testDb.Driver, settings, logging, manager);
                        VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging) { Hosts = hosts };
                        CheckRunService checkRuns = new CheckRunService(testDb.Driver, workflowProfiles, readiness, logging) { Hosts = hosts };

                        CheckRun result = await checkRuns.RunAsync(
                            AuthContext.Authenticated("ten_check_timeout", "usr_check_timeout", false, false, "UnitTest"),
                            new CheckRunRequest { VesselId = vessel.Id, CommandOverride = "check timeout" }).ConfigureAwait(false);

                        AssertEqual(CheckRunStatusEnum.Failed, result.Status);
                        AssertTrue(result.ExitCode.HasValue && result.ExitCode.Value != 0, "A timeout must fail the check.");
                        AssertContains("check stdout marker", result.Output);
                        AssertContains("check stderr marker", result.Output);
                        AssertEqual(1, result.Output.Split("timed out", StringSplitOptions.None).Length - 1, "one timeout message");

                        CheckRun cancelled = await checkRuns.RunAsync(
                            AuthContext.Authenticated("ten_check_timeout", "usr_check_timeout", false, false, "UnitTest"),
                            new CheckRunRequest { VesselId = vessel.Id, CommandOverride = "check cancel" }).ConfigureAwait(false);

                        AssertEqual(CheckRunStatusEnum.Failed, cancelled.Status);
                        AssertContains("caller cancelled", cancelled.Output);
                        AssertFalse(cancelled.Output.Contains("timed out", StringComparison.OrdinalIgnoreCase), "Cancellation must not become a timeout.");
                    }
                }
            }));

            cases.Add(CaseAsync("missing_profile_is_actionable", "With no workflow profile, readiness says dispatch still works and links to creating one for the vessel", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                LoggingModule logging = CreateLogging();
                WorkflowProfileService workflowProfiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, workflowProfiles, logging);
                await EnsureTenantAndUserAsync(testDb, "ten_noprof", "usr_noprof").ConfigureAwait(false);
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-noprofile-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);
                try
                {
                    Vessel vessel = CreateVessel("ten_noprof", "usr_noprof", workingDirectory);
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    AuthContext auth = AuthContext.Authenticated("ten_noprof", "usr_noprof", false, false, "UnitTest");

                    VesselReadinessResult result = await readiness.EvaluateAsync(auth, vessel).ConfigureAwait(false);
                    VesselReadinessIssue issue = result.Issues.Single(i => i.Code == "workflow_profile_missing");
                    AssertEqual(ReadinessSeverityEnum.Info, issue.Severity, "informational for dispatch, not a warning");
                    AssertTrue(issue.Message.Contains("Missions can be dispatched", StringComparison.Ordinal), "says dispatch is unaffected");
                    AssertEqual("Create a workflow profile", issue.ActionLabel);
                    AssertEqual("/configuration?tab=workflow-profiles&createProfile=vessel&vesselId=" + vessel.Id, issue.ActionHref);

                    VesselReadinessResult check = await readiness.EvaluateAsync(auth, vessel, null, CheckRunTypeEnum.Build).ConfigureAwait(false);
                    AssertEqual(ReadinessSeverityEnum.Error, check.Issues.Single(i => i.Code == "workflow_profile_missing").Severity, "a check still requires one");
                }
                finally
                {
                    try { Directory.Delete(workingDirectory, true); } catch { }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Workflow Profile and Check Run Services",
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
                Name = "Workflow Vessel",
                RepoUrl = "file:///tmp/armada-tests.git",
                LocalPath = workingDirectory,
                WorkingDirectory = workingDirectory,
                DefaultBranch = "main"
            };
        }

        private static string BuildEmitFileCommand(string relativePath)
        {
            return OperatingSystem.IsWindows()
                ? "type .\\" + relativePath.Replace('/', '\\')
                : "cat \"" + relativePath + "\"";
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
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private sealed class CheckTimeoutExecutor : IHostCommandExecutor
        {
            /// <inheritdoc />
            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                if (request == null) throw new ArgumentNullException(nameof(request));

                // The Harbor link rebuilds the host request from the executable and arguments. Kind and Label do not
                // arrive here, so the shell command text identifies the check.
                if (HasArgument(request, "check timeout"))
                {
                    return Task.FromResult(new HostCommandResult
                    {
                        ExitCode = 0,
                        StandardOutput = "check stdout marker",
                        StandardError = "check stderr marker",
                        TimedOut = true
                    });
                }

                if (HasArgument(request, "check cancel"))
                    return Task.FromException<HostCommandResult>(new InvalidOperationException("caller cancelled"));

                return Task.FromResult(new HostCommandResult { ExitCode = 0 });
            }

            private static bool HasArgument(HostCommandRequest request, string text)
            {
                if (request.Arguments == null) return false;
                foreach (string argument in request.Arguments)
                {
                    if (String.Equals(argument, text, StringComparison.Ordinal))
                        return true;
                }

                return false;
            }
        }

        #endregion
    }
}
