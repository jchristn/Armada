namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for workflow profiles and structured check runs, ported 1:1 from the
    /// retired automated WorkflowProfileCheckRunTests suite. Cases share the singleton e2e server
    /// fixture and carry created vessel, profile, and check-run identifiers across cases; the
    /// working directory is provisioned by the first case and torn down by a trailing cleanup case,
    /// mirroring the legacy try/finally teardown.
    /// </summary>
    public sealed class WorkflowProfileCheckRunSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.WorkflowProfileCheckRun";

        private string _WorkingDirectory = String.Empty;
        private string _VesselId = String.Empty;
        private string _GlobalProfileId = String.Empty;
        private string _VesselProfileId = String.Empty;
        private string _MissingInputProfileId = String.Empty;
        private string _FirstRunId = String.Empty;
        private string _RetryRunId = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Workflow Profiles and Checks suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("workflow_profiles_create_resolve_update_and_enumerate", "WorkflowProfiles_CreateResolveUpdateAndEnumerate", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                _WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada-workflow-checks-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(_WorkingDirectory, "artifacts"));
                await File.WriteAllTextAsync(Path.Combine(_WorkingDirectory, "artifacts", "existing.txt"), "artifact").ConfigureAwait(false);

                HttpResponseMessage vesselResponse = await authClient.PostAsync("/api/v1/vessels",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "Workflow Check Vessel",
                        RepoUrl = "file:///tmp/workflow-check-vessel.git",
                        LocalPath = _WorkingDirectory,
                        WorkingDirectory = _WorkingDirectory,
                        DefaultBranch = "main",
                        RequirePassingChecksToLand = true
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, vesselResponse.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResponse).ConfigureAwait(false);
                _VesselId = vessel.Id;
                AssertStartsWith("vsl_", _VesselId);

                HttpResponseMessage globalProfileResponse = await authClient.PostAsync("/api/v1/workflow-profiles",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "Global Workflow Profile",
                        Scope = WorkflowProfileScopeEnum.Global,
                        BuildCommand = "dotnet --version"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, globalProfileResponse.StatusCode);

                WorkflowProfile globalProfile = await JsonHelper.DeserializeAsync<WorkflowProfile>(globalProfileResponse).ConfigureAwait(false);
                _GlobalProfileId = globalProfile.Id;
                AssertStartsWith("wfp_", _GlobalProfileId);

                HttpResponseMessage vesselProfileResponse = await authClient.PostAsync("/api/v1/workflow-profiles",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "Vessel Workflow Profile",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = _VesselId,
                        IsDefault = true,
                        BuildCommand = "dotnet --version",
                        UnitTestCommand = "dotnet --version",
                        ExpectedArtifacts = new[] { "artifacts/existing.txt" },
                        Environments = new[]
                        {
                            new
                            {
                                EnvironmentName = "staging",
                                DeployCommand = "echo deploy-staging"
                            }
                        }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, vesselProfileResponse.StatusCode);

                WorkflowProfile vesselProfile = await JsonHelper.DeserializeAsync<WorkflowProfile>(vesselProfileResponse).ConfigureAwait(false);
                _VesselProfileId = vesselProfile.Id;

                HttpResponseMessage resolveResponse = await authClient.GetAsync("/api/v1/workflow-profiles/resolve/vessels/" + _VesselId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, resolveResponse.StatusCode);

                WorkflowProfile resolved = await JsonHelper.DeserializeAsync<WorkflowProfile>(resolveResponse).ConfigureAwait(false);
                AssertEqual(_VesselProfileId, resolved.Id);

                HttpResponseMessage previewResponse = await authClient.GetAsync("/api/v1/workflow-profiles/preview/vessels/" + _VesselId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, previewResponse.StatusCode);

                WorkflowProfileResolutionPreviewResult preview = await JsonHelper.DeserializeAsync<WorkflowProfileResolutionPreviewResult>(previewResponse).ConfigureAwait(false);
                AssertNotNull(preview.ResolvedProfile);
                AssertEqual(_VesselProfileId, preview.ResolvedProfile!.Id);
                AssertEqual(WorkflowProfileResolutionModeEnum.Vessel, preview.ResolutionMode);
                AssertTrue(preview.AvailableCheckTypes.Exists(type => type == CheckRunTypeEnum.Build.ToString()), "Expected build check type in preview.");
                AssertTrue(preview.CommandPreviews.Exists(command => command.CheckType == CheckRunTypeEnum.Build && command.Command == "dotnet --version"), "Expected build command preview.");
                AssertTrue(preview.CommandPreviews.Exists(command => command.CheckType == CheckRunTypeEnum.Deploy && command.EnvironmentName == "staging" && command.Command == "echo deploy-staging"), "Expected staging deploy preview.");

                HttpResponseMessage listResponse = await authClient.GetAsync("/api/v1/workflow-profiles?vesselId=" + Uri.EscapeDataString(_VesselId) + "&pageSize=100").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, listResponse.StatusCode);
                EnumerationResult<WorkflowProfile> profiles = await JsonHelper.DeserializeAsync<EnumerationResult<WorkflowProfile>>(listResponse).ConfigureAwait(false);
                AssertTrue(profiles.Objects.Count >= 1);

                HttpResponseMessage updateResponse = await authClient.PutAsync("/api/v1/workflow-profiles/" + _VesselProfileId,
                    JsonHelper.ToJsonContent(new
                    {
                        Id = _VesselProfileId,
                        Name = "Vessel Workflow Profile Updated",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = _VesselId,
                        IsDefault = true,
                        BuildCommand = "dotnet --version",
                        UnitTestCommand = "dotnet --version",
                        ExpectedArtifacts = new[] { "artifacts/existing.txt" },
                        Environments = new[]
                        {
                            new
                            {
                                EnvironmentName = "staging",
                                DeployCommand = "echo deploy-staging"
                            }
                        }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, updateResponse.StatusCode);

                WorkflowProfile updated = await JsonHelper.DeserializeAsync<WorkflowProfile>(updateResponse).ConfigureAwait(false);
                AssertEqual("Vessel Workflow Profile Updated", updated.Name);
            }));

            cases.Add(CaseAsync("workflow_profiles_validate_rejects_empty_profile", "WorkflowProfiles_ValidateRejectsEmptyProfile", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/workflow-profiles/validate",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "Invalid Profile",
                        Scope = WorkflowProfileScopeEnum.Global
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                WorkflowProfileValidationResult validation = await JsonHelper.DeserializeAsync<WorkflowProfileValidationResult>(response).ConfigureAwait(false);
                AssertFalse(validation.IsValid);
                AssertTrue(validation.Errors.Count >= 1);
            }));

            cases.Add(CaseAsync("vessel_readiness_and_check_run_block_when_required_input_is_missing", "VesselReadiness_And_CheckRun_Block_When_Required_Input_Is_Missing", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missingVariable = "ARMADA_AUTOMATED_INPUT_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                string stagingVariable = "ARMADA_AUTOMATED_STAGING_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

                HttpResponseMessage profileResponse = await authClient.PostAsync("/api/v1/workflow-profiles",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "Missing Input Workflow Profile",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = _VesselId,
                        BuildCommand = "dotnet --version",
                        Environments = new[]
                        {
                            new
                            {
                                EnvironmentName = "staging",
                                DeployCommand = "echo deploy-staging"
                            }
                        },
                        RequiredInputs = new[]
                        {
                            new
                            {
                                Provider = WorkflowInputReferenceProviderEnum.EnvironmentVariable,
                                Key = missingVariable,
                                EnvironmentName = (string?)null,
                                Description = (string?)null
                            },
                            new
                            {
                                Provider = WorkflowInputReferenceProviderEnum.EnvironmentVariable,
                                Key = stagingVariable,
                                EnvironmentName = (string?)"staging",
                                Description = (string?)"Staging deploy token"
                            },
                            new
                            {
                                Provider = WorkflowInputReferenceProviderEnum.OnePassword,
                                Key = "op://armada/staging/deploy-token",
                                EnvironmentName = (string?)"staging",
                                Description = (string?)"Provider-backed deploy token"
                            }
                        }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, profileResponse.StatusCode);

                WorkflowProfile missingInputProfile = await JsonHelper.DeserializeAsync<WorkflowProfile>(profileResponse).ConfigureAwait(false);
                _MissingInputProfileId = missingInputProfile.Id;
                AssertTrue(missingInputProfile.RequiredInputs.Count == 3, "Expected required inputs to round-trip.");
                AssertEqual(WorkflowInputReferenceProviderEnum.OnePassword, missingInputProfile.RequiredInputs[2].Provider);
                AssertEqual("staging", missingInputProfile.RequiredInputs[2].EnvironmentName);
                AssertEqual("Provider-backed deploy token", missingInputProfile.RequiredInputs[2].Description);

                HttpResponseMessage readinessResponse = await authClient.GetAsync(
                    "/api/v1/vessels/" + _VesselId
                    + "/readiness?workflowProfileId=" + Uri.EscapeDataString(_MissingInputProfileId)
                    + "&checkType=Build").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, readinessResponse.StatusCode);

                VesselReadinessResult readiness = await JsonHelper.DeserializeAsync<VesselReadinessResult>(readinessResponse).ConfigureAwait(false);
                AssertFalse(readiness.IsReady);
                AssertTrue(readiness.ErrorCount >= 1);
                AssertTrue(readiness.Issues.Exists(issue => issue.Code == "required_input_missing"), "Expected a required_input_missing readiness issue.");
                AssertContains(missingVariable, String.Join(" ", readiness.Issues.ConvertAll(issue => issue.Message)));
                AssertFalse(readiness.Issues.Exists(issue => issue.Message.Contains(stagingVariable, StringComparison.OrdinalIgnoreCase)), "Build readiness should ignore staging-scoped inputs.");

                HttpResponseMessage deployReadinessResponse = await authClient.GetAsync(
                    "/api/v1/vessels/" + _VesselId
                    + "/readiness?workflowProfileId=" + Uri.EscapeDataString(_MissingInputProfileId)
                    + "&checkType=Deploy&environmentName=staging").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, deployReadinessResponse.StatusCode);

                VesselReadinessResult deployReadiness = await JsonHelper.DeserializeAsync<VesselReadinessResult>(deployReadinessResponse).ConfigureAwait(false);
                AssertTrue(deployReadiness.Issues.Exists(issue => issue.Message.Contains(stagingVariable, StringComparison.OrdinalIgnoreCase)), "Deploy readiness should include staging-scoped inputs.");

                HttpResponseMessage blockedRunResponse = await authClient.PostAsync("/api/v1/check-runs",
                    JsonHelper.ToJsonContent(new
                    {
                        VesselId = _VesselId,
                        WorkflowProfileId = _MissingInputProfileId,
                        Type = CheckRunTypeEnum.Build
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.BadRequest, blockedRunResponse.StatusCode);

                string blockedBody = await blockedRunResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertContains(missingVariable, blockedBody);
                AssertFalse(blockedBody.Contains(stagingVariable, StringComparison.OrdinalIgnoreCase), "Build check failure should not mention staging-scoped inputs.");
            }));

            cases.Add(CaseAsync("workflow_profiles_validate_rejects_unknown_environment_scoped_inputs", "WorkflowProfiles_ValidateRejectsUnknownEnvironmentScopedInputs", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/workflow-profiles/validate",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "Scoped Validate Profile",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = _VesselId,
                        BuildCommand = "dotnet --version",
                        Environments = new[]
                        {
                            new
                            {
                                EnvironmentName = "dev",
                                DeployCommand = "echo deploy-dev"
                            }
                        },
                        RequiredInputs = new[]
                        {
                            new
                            {
                                Provider = WorkflowInputReferenceProviderEnum.EnvironmentVariable,
                                Key = "ARMADA_UNKNOWN_ENV",
                                EnvironmentName = "prod"
                            }
                        }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                WorkflowProfileValidationResult validation = await JsonHelper.DeserializeAsync<WorkflowProfileValidationResult>(response).ConfigureAwait(false);
                AssertFalse(validation.IsValid);
                AssertContains("unknown environments", String.Join(" ", validation.Errors));
                AssertContains("prod", String.Join(" ", validation.Errors));
            }));

            cases.Add(CaseAsync("vessel_readiness_and_landing_preview_surface_setup_metadata", "VesselReadiness_And_LandingPreview_Surface_Setup_Metadata", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage readinessResponse = await authClient.GetAsync(
                    "/api/v1/vessels/" + _VesselId
                    + "/readiness?workflowProfileId=" + Uri.EscapeDataString(_VesselProfileId)).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, readinessResponse.StatusCode);

                VesselReadinessResult readiness = await JsonHelper.DeserializeAsync<VesselReadinessResult>(readinessResponse).ConfigureAwait(false);
                AssertTrue(readiness.DeploymentEnvironments.Exists(name => name == "staging"), "Expected staging deployment environment.");
                AssertTrue(readiness.SetupChecklist.Count >= 5, "Expected setup checklist items.");
                AssertTrue(readiness.SetupChecklist.Exists(item => item.Code == "workflow_profile" && item.IsSatisfied), "Expected workflow profile checklist item.");

                HttpResponseMessage previewResponse = await authClient.GetAsync(
                    "/api/v1/vessels/" + _VesselId + "/landing-preview?sourceBranch=" + Uri.EscapeDataString("feature/workflow-check")).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, previewResponse.StatusCode);

                LandingPreviewResult preview = await JsonHelper.DeserializeAsync<LandingPreviewResult>(previewResponse).ConfigureAwait(false);
                AssertFalse(preview.IsReadyToLand);
                AssertTrue(preview.RequirePassingChecksToLand, "Expected landing preview to honor vessel setting.");
                AssertTrue(preview.Issues.Exists(issue => issue.Code == "passing_checks_required"), "Expected passing_checks_required issue.");
            }));

            cases.Add(CaseAsync("check_runs_run_read_retry_list_and_delete", "CheckRuns_RunReadRetryListAndDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage runResponse = await authClient.PostAsync("/api/v1/check-runs",
                    JsonHelper.ToJsonContent(new
                    {
                        VesselId = _VesselId,
                        WorkflowProfileId = _VesselProfileId,
                        Type = CheckRunTypeEnum.Build,
                        Label = "Build Check",
                        BranchName = "feature/workflow-check",
                        CommitHash = "abc123"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, runResponse.StatusCode);

                CheckRun firstRun = await JsonHelper.DeserializeAsync<CheckRun>(runResponse).ConfigureAwait(false);
                _FirstRunId = firstRun.Id;
                AssertStartsWith("chk_", _FirstRunId);
                AssertEqual(CheckRunStatusEnum.Passed, firstRun.Status);
                AssertEqual("feature/workflow-check", firstRun.BranchName);
                AssertEqual(0, firstRun.ExitCode ?? -1);
                AssertTrue(firstRun.Artifacts.Count == 1, "Expected one collected artifact");

                HttpResponseMessage detailResponse = await authClient.GetAsync("/api/v1/check-runs/" + _FirstRunId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, detailResponse.StatusCode);
                CheckRun detail = await JsonHelper.DeserializeAsync<CheckRun>(detailResponse).ConfigureAwait(false);
                AssertEqual(_FirstRunId, detail.Id);
                AssertEqual(_VesselProfileId, detail.WorkflowProfileId);

                HttpResponseMessage listResponse = await authClient.GetAsync(
                    "/api/v1/check-runs?vesselId=" + Uri.EscapeDataString(_VesselId)
                    + "&workflowProfileId=" + Uri.EscapeDataString(_VesselProfileId)
                    + "&type=" + Uri.EscapeDataString(CheckRunTypeEnum.Build.ToString())
                    + "&pageSize=100").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, listResponse.StatusCode);

                EnumerationResult<CheckRun> list = await JsonHelper.DeserializeAsync<EnumerationResult<CheckRun>>(listResponse).ConfigureAwait(false);
                AssertTrue(list.Objects.Exists(run => run.Id == _FirstRunId), "First run should be listed");

                HttpResponseMessage retryResponse = await authClient.PostAsync("/api/v1/check-runs/" + _FirstRunId + "/retry", null).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, retryResponse.StatusCode);

                CheckRun retryRun = await JsonHelper.DeserializeAsync<CheckRun>(retryResponse).ConfigureAwait(false);
                _RetryRunId = retryRun.Id;
                AssertNotEqual(_FirstRunId, _RetryRunId);
                AssertEqual(CheckRunStatusEnum.Passed, retryRun.Status);

                HttpResponseMessage previewResponse = await authClient.GetAsync(
                    "/api/v1/vessels/" + _VesselId + "/landing-preview").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, previewResponse.StatusCode);
                LandingPreviewResult preview = await JsonHelper.DeserializeAsync<LandingPreviewResult>(previewResponse).ConfigureAwait(false);
                AssertTrue(preview.HasPassingChecks, "Expected landing preview to detect passing checks.");
                AssertFalse(preview.Issues.Exists(issue => issue.Code == "passing_checks_required"), "Did not expect passing_checks_required after a successful check.");
                AssertTrue(preview.IsReadyToLand, "Expected landing preview to be ready after a passing check.");

                HttpResponseMessage deleteResponse = await authClient.DeleteAsync("/api/v1/check-runs/" + _FirstRunId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

                HttpResponseMessage deletedRead = await authClient.GetAsync("/api/v1/check-runs/" + _FirstRunId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, deletedRead.StatusCode);
            }));

            cases.Add(CaseAsync("check_runs_run_parses_structured_summaries", "CheckRuns_RunParsesStructuredSummaries", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await File.WriteAllTextAsync(
                    Path.Combine(_WorkingDirectory, "summary.txt"),
                    "Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 2 s").ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(_WorkingDirectory, "coverage.cobertura.xml"),
                    """
                    <coverage line-rate="0.8" branch-rate="0.5" lines-covered="8" lines-valid="10" branches-covered="2" branches-valid="4"></coverage>
                    """).ConfigureAwait(false);

                HttpResponseMessage profileResponse = await authClient.PostAsync("/api/v1/workflow-profiles",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "Structured Parse Profile",
                        Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = _VesselId,
                        UnitTestCommand = BuildEmitFileCommand("summary.txt"),
                        ExpectedArtifacts = new[] { "coverage.cobertura.xml" }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, profileResponse.StatusCode);

                WorkflowProfile parseProfile = await JsonHelper.DeserializeAsync<WorkflowProfile>(profileResponse).ConfigureAwait(false);

                try
                {
                    HttpResponseMessage runResponse = await authClient.PostAsync("/api/v1/check-runs",
                        JsonHelper.ToJsonContent(new
                        {
                            VesselId = _VesselId,
                            WorkflowProfileId = parseProfile.Id,
                            Type = CheckRunTypeEnum.UnitTest,
                            Label = "Structured Unit Tests"
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, runResponse.StatusCode);

                    CheckRun parsedRun = await JsonHelper.DeserializeAsync<CheckRun>(runResponse).ConfigureAwait(false);
                    AssertNotNull(parsedRun.TestSummary);
                    AssertEqual(5, parsedRun.TestSummary!.Passed ?? -1);
                    AssertEqual(5, parsedRun.TestSummary.Total ?? -1);
                    AssertNotNull(parsedRun.CoverageSummary);
                    AssertEqual(80d, parsedRun.CoverageSummary!.Lines?.Percentage ?? -1d);
                }
                finally
                {
                    try { await authClient.DeleteAsync("/api/v1/workflow-profiles/" + parseProfile.Id).ConfigureAwait(false); } catch { }
                }
            }));

            cases.Add(CaseAsync("check_runs_run_without_auth_returns_401", "CheckRuns_RunWithoutAuthReturns401", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.PostAsync("/api/v1/check-runs",
                    JsonHelper.ToJsonContent(new
                    {
                        VesselId = _VesselId,
                        Type = CheckRunTypeEnum.Build
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            }));

            cases.Add(CaseAsync("check_runs_import_rejects_too_many_artifacts", "CheckRuns_ImportRejectsTooManyArtifacts", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                List<CheckRunArtifact> artifacts = new List<CheckRunArtifact>();
                for (int i = 0; i < 257; i++) artifacts.Add(new CheckRunArtifact { Path = "artifacts/a" + i + ".txt", SizeBytes = 1, LastWriteUtc = DateTime.UtcNow });
                HttpResponseMessage rejected = await fx.AuthClient.PostAsync("/api/v1/check-runs/import", JsonHelper.ToJsonContent(new CheckRunImportRequest
                {
                    VesselId = _VesselId, Type = CheckRunTypeEnum.UnitTest, Status = CheckRunStatusEnum.Passed, ProviderName = "ci", ExternalId = "too-many", Artifacts = artifacts
                })).ConfigureAwait(false);
                string body = await rejected.Content.ReadAsStringAsync().ConfigureAwait(false);
                AssertEqual(HttpStatusCode.BadRequest, rejected.StatusCode, body);
                E2eLimitErrorBody error = JsonHelper.Deserialize<E2eLimitErrorBody>(body);
                AssertEqual("CollectionLimitExceeded", error.Data?.Code, body);
                AssertEqual("Artifacts", error.Data!.Field);
                AssertEqual(256, error.Data.Limit);
                AssertEqual(257L, error.Data.Actual);

                artifacts.RemoveAt(0);
                HttpResponseMessage accepted = await fx.AuthClient.PostAsync("/api/v1/check-runs/import", JsonHelper.ToJsonContent(new CheckRunImportRequest
                {
                    VesselId = _VesselId, Type = CheckRunTypeEnum.UnitTest, Status = CheckRunStatusEnum.Passed, ProviderName = "ci", ExternalId = "at-limit", Artifacts = artifacts
                })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, accepted.StatusCode, await accepted.Content.ReadAsStringAsync().ConfigureAwait(false));
                CheckRun run = await JsonHelper.DeserializeAsync<CheckRun>(accepted).ConfigureAwait(false);
                AssertEqual(256, run.Artifacts.Count, "exactly the limit is stored");
            }));

            cases.Add(CaseAsync("workflow_profile_check_run_cleanup_resources", "WorkflowProfileCheckRun_CleanupResources", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                if (!String.IsNullOrWhiteSpace(_RetryRunId))
                {
                    try { await authClient.DeleteAsync("/api/v1/check-runs/" + _RetryRunId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_FirstRunId))
                {
                    try { await authClient.DeleteAsync("/api/v1/check-runs/" + _FirstRunId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_VesselProfileId))
                {
                    try { await authClient.DeleteAsync("/api/v1/workflow-profiles/" + _VesselProfileId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_MissingInputProfileId))
                {
                    try { await authClient.DeleteAsync("/api/v1/workflow-profiles/" + _MissingInputProfileId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_GlobalProfileId))
                {
                    try { await authClient.DeleteAsync("/api/v1/workflow-profiles/" + _GlobalProfileId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_VesselId))
                {
                    try { await authClient.DeleteAsync("/api/v1/vessels/" + _VesselId).ConfigureAwait(false); } catch { }
                }

                try
                {
                    if (!String.IsNullOrWhiteSpace(_WorkingDirectory) && Directory.Exists(_WorkingDirectory))
                        Directory.Delete(_WorkingDirectory, true);
                }
                catch
                {
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Workflow Profiles and Checks",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string BuildEmitFileCommand(string relativePath)
        {
            return OperatingSystem.IsWindows()
                ? "type .\\" + relativePath.Replace('/', '\\')
                : "cat \"" + relativePath + "\"";
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
