namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for pull-based GitHub-backed objective import, Actions sync, and PR
    /// evidence, ported 1:1 from the retired automated GitHubIntegrationTests suite. Each case
    /// stands up an in-process fake GitHub API server and is fully self-contained against the
    /// shared e2e server fixture.
    /// </summary>
    public sealed class GitHubIntegrationSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.GitHubIntegration";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the GitHub Integration suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("github_objectives_import_and_refresh_from_issue", "GitHubObjectives_ImportAndRefreshFromIssue", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                using FakeGitHubServer fakeGitHub = new FakeGitHubServer("ghp_vessel_token");
                string vesselId = String.Empty;
                string objectiveId = String.Empty;
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-github-objectives-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    vesselId = await CreateVesselAsync(authClient, "GitHub Objective Vessel", fakeGitHub.RepositoryUrl, workingDirectory, fakeGitHub.ExpectedToken).ConfigureAwait(false);

                    HttpResponseMessage importResponse = await authClient.PostAsync("/api/v1/objectives/import/github",
                        JsonHelper.ToJsonContent(new
                        {
                            VesselId = vesselId,
                            SourceType = GitHubObjectiveSourceTypeEnum.Issue,
                            Number = 123
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Created, importResponse.StatusCode);

                    Objective imported = await JsonHelper.DeserializeAsync<Objective>(importResponse).ConfigureAwait(false);
                    objectiveId = imported.Id;
                    AssertStartsWith("obj_", objectiveId);
                    AssertEqual("GitHub Issue 123", imported.Title);
                    AssertEqual("GitHub", imported.SourceProvider);
                    AssertEqual("Issue", imported.SourceType);
                    AssertEqual("octo/armada-test#123", imported.SourceId);
                    AssertEqual(fakeGitHub.IssueUrl, imported.SourceUrl);
                    AssertTrue(imported.VesselIds.Contains(vesselId), "Expected imported objective to link back to the vessel.");

                    fakeGitHub.Issue123Title = "GitHub Issue 123 Updated";
                    fakeGitHub.Issue123State = "closed";

                    HttpResponseMessage refreshResponse = await authClient.PostAsync("/api/v1/objectives/import/github",
                        JsonHelper.ToJsonContent(new
                        {
                            VesselId = vesselId,
                            ObjectiveId = objectiveId,
                            SourceType = GitHubObjectiveSourceTypeEnum.Issue,
                            Number = 123
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, refreshResponse.StatusCode);

                    Objective refreshed = await JsonHelper.DeserializeAsync<Objective>(refreshResponse).ConfigureAwait(false);
                    AssertEqual(objectiveId, refreshed.Id);
                    AssertEqual("GitHub Issue 123 Updated", refreshed.Title);
                    AssertEqual(ObjectiveStatusEnum.Completed, refreshed.Status);
                }
                finally
                {
                    if (!String.IsNullOrWhiteSpace(objectiveId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/objectives/" + objectiveId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(vesselId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/vessels/" + vesselId).ConfigureAwait(false); } catch { }
                    }
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("github_actions_sync_creates_and_updates_deployment_linked_checks", "GitHubActions_SyncCreatesAndUpdatesDeploymentLinkedChecks", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string armadaBaseUrl = fx.BaseUrl;

                using FakeGitHubServer fakeGitHub = new FakeGitHubServer("ghp_vessel_token");
                string vesselId = String.Empty;
                string workflowProfileId = String.Empty;
                string environmentId = String.Empty;
                string deploymentId = String.Empty;
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-github-actions-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    vesselId = await CreateVesselAsync(authClient, "GitHub Actions Vessel", fakeGitHub.RepositoryUrl, workingDirectory, fakeGitHub.ExpectedToken).ConfigureAwait(false);
                    workflowProfileId = await CreateWorkflowProfileAsync(authClient, vesselId).ConfigureAwait(false);
                    environmentId = await CreateEnvironmentAsync(authClient, armadaBaseUrl, vesselId).ConfigureAwait(false);
                    deploymentId = await CreateDeploymentAsync(authClient, vesselId, workflowProfileId, environmentId).ConfigureAwait(false);

                    HttpResponseMessage firstSyncResponse = await authClient.PostAsync("/api/v1/check-runs/sync/github-actions",
                        JsonHelper.ToJsonContent(new
                        {
                            VesselId = vesselId,
                            WorkflowProfileId = workflowProfileId,
                            DeploymentId = deploymentId,
                            EnvironmentName = "staging",
                            BranchName = "main",
                            RunCount = 10
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, firstSyncResponse.StatusCode);

                    GitHubActionsSyncResult firstSync = await JsonHelper.DeserializeAsync<GitHubActionsSyncResult>(firstSyncResponse).ConfigureAwait(false);
                    AssertEqual(1, firstSync.CreatedCount);
                    AssertEqual(0, firstSync.UpdatedCount);
                    AssertEqual(1, firstSync.CheckRuns.Count);
                    AssertEqual("GitHubActions", firstSync.CheckRuns[0].ProviderName);

                    HttpResponseMessage secondSyncResponse = await authClient.PostAsync("/api/v1/check-runs/sync/github-actions",
                        JsonHelper.ToJsonContent(new
                        {
                            VesselId = vesselId,
                            WorkflowProfileId = workflowProfileId,
                            DeploymentId = deploymentId,
                            EnvironmentName = "staging",
                            BranchName = "main",
                            RunCount = 10
                        })).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, secondSyncResponse.StatusCode);

                    GitHubActionsSyncResult secondSync = await JsonHelper.DeserializeAsync<GitHubActionsSyncResult>(secondSyncResponse).ConfigureAwait(false);
                    AssertEqual(0, secondSync.CreatedCount);
                    AssertEqual(1, secondSync.UpdatedCount);

                    HttpResponseMessage deploymentResponse = await authClient.GetAsync("/api/v1/deployments/" + deploymentId).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, deploymentResponse.StatusCode);
                    Deployment deployment = await JsonHelper.DeserializeAsync<Deployment>(deploymentResponse).ConfigureAwait(false);
                    AssertTrue(deployment.CheckRunIds.Count > 0, "Expected the synced GitHub Actions run to be linked to the deployment.");

                    HttpResponseMessage checkRunListResponse = await authClient.GetAsync(
                        "/api/v1/check-runs?deploymentId=" + Uri.EscapeDataString(deploymentId)
                        + "&providerName=" + Uri.EscapeDataString("GitHubActions")
                        + "&pageSize=50").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, checkRunListResponse.StatusCode);
                    EnumerationResult<CheckRun> importedRuns = await JsonHelper.DeserializeAsync<EnumerationResult<CheckRun>>(checkRunListResponse).ConfigureAwait(false);
                    AssertEqual(1, importedRuns.Objects.Count);
                    AssertEqual("9001", importedRuns.Objects[0].ExternalId);
                }
                finally
                {
                    if (!String.IsNullOrWhiteSpace(deploymentId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/deployments/" + deploymentId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(environmentId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/environments/" + environmentId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(workflowProfileId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/workflow-profiles/" + workflowProfileId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(vesselId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/vessels/" + vesselId).ConfigureAwait(false); } catch { }
                    }
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("github_pull_request_routes_return_mission_and_release_evidence", "GitHubPullRequestRoutes_ReturnMissionAndReleaseEvidence", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                using FakeGitHubServer fakeGitHub = new FakeGitHubServer("ghp_vessel_token");
                string vesselId = String.Empty;
                string missionId = String.Empty;
                string releaseId = String.Empty;
                string workingDirectory = Path.Combine(Path.GetTempPath(), "armada-github-prs-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workingDirectory);

                try
                {
                    vesselId = await CreateVesselAsync(authClient, "GitHub PR Vessel", fakeGitHub.RepositoryUrl, workingDirectory, fakeGitHub.ExpectedToken).ConfigureAwait(false);
                    missionId = await CreateMissionAsync(authClient, vesselId, fakeGitHub.PullRequestUrl).ConfigureAwait(false);
                    releaseId = await CreateReleaseAsync(authClient, vesselId, missionId).ConfigureAwait(false);

                    HttpResponseMessage missionPullRequestResponse = await authClient.GetAsync("/api/v1/missions/" + missionId + "/github/pull-request").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, missionPullRequestResponse.StatusCode);
                    GitHubPullRequestDetail missionPullRequest = await JsonHelper.DeserializeAsync<GitHubPullRequestDetail>(missionPullRequestResponse).ConfigureAwait(false);
                    AssertEqual("octo/armada-test", missionPullRequest.Repository);
                    AssertEqual(45, missionPullRequest.Number);
                    AssertEqual("ChangesRequested", missionPullRequest.ReviewStatus);
                    AssertEqual(1, missionPullRequest.Checks.Count);

                    HttpResponseMessage releasePullRequestsResponse = await authClient.GetAsync("/api/v1/releases/" + releaseId + "/github/pull-requests").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, releasePullRequestsResponse.StatusCode);
                    List<GitHubPullRequestDetail> releasePullRequests = await JsonHelper.DeserializeAsync<List<GitHubPullRequestDetail>>(releasePullRequestsResponse).ConfigureAwait(false);
                    AssertEqual(1, releasePullRequests.Count);
                    AssertEqual("Armada Integration PR", releasePullRequests[0].Title);
                    AssertEqual("ChangesRequested", releasePullRequests[0].ReviewStatus);
                }
                finally
                {
                    if (!String.IsNullOrWhiteSpace(releaseId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/releases/" + releaseId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(missionId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/missions/" + missionId).ConfigureAwait(false); } catch { }
                    }
                    if (!String.IsNullOrWhiteSpace(vesselId))
                    {
                        try { await authClient.DeleteAsync("/api/v1/vessels/" + vesselId).ConfigureAwait(false); } catch { }
                    }
                    TryDeleteDirectory(workingDirectory);
                }
            }));

            cases.Add(CaseAsync("github_objective_import_without_auth_returns_401", "GitHubObjectiveImport_WithoutAuthReturns401", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                using FakeGitHubServer fakeGitHub = new FakeGitHubServer("ghp_vessel_token");
                HttpResponseMessage response = await unauthClient.PostAsync("/api/v1/objectives/import/github",
                    JsonHelper.ToJsonContent(new
                    {
                        VesselId = "vsl_missing",
                        SourceType = GitHubObjectiveSourceTypeEnum.Issue,
                        Number = 123
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "GitHub Integration",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<string> CreateVesselAsync(HttpClient authClient, string name, string repoUrl, string workingDirectory, string gitHubTokenOverride)
        {
            HttpResponseMessage vesselResponse = await authClient.PostAsync("/api/v1/vessels",
                JsonHelper.ToJsonContent(new
                {
                    Name = name,
                    RepoUrl = repoUrl,
                    LocalPath = workingDirectory,
                    WorkingDirectory = workingDirectory,
                    DefaultBranch = "main",
                    GitHubTokenOverride = gitHubTokenOverride
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, vesselResponse.StatusCode);
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResponse).ConfigureAwait(false);
            AssertTrue(vessel.HasGitHubTokenOverride, "Expected vessel GitHub token override to be flagged as configured.");
            return vessel.Id;
        }

        private static async Task<string> CreateWorkflowProfileAsync(HttpClient authClient, string vesselId)
        {
            HttpResponseMessage workflowProfileResponse = await authClient.PostAsync("/api/v1/workflow-profiles",
                JsonHelper.ToJsonContent(new
                {
                    Name = "GitHub Sync Workflow",
                    Scope = WorkflowProfileScopeEnum.Vessel,
                    VesselId = vesselId,
                    IsDefault = true,
                    Environments = new[]
                    {
                        new
                        {
                            EnvironmentName = "staging",
                            DeployCommand = "echo deploy-staging",
                            SmokeTestCommand = "echo smoke-staging",
                            DeploymentVerificationCommand = "echo verify-staging"
                        }
                    }
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, workflowProfileResponse.StatusCode);
            WorkflowProfile workflowProfile = await JsonHelper.DeserializeAsync<WorkflowProfile>(workflowProfileResponse).ConfigureAwait(false);
            return workflowProfile.Id;
        }

        private static async Task<string> CreateEnvironmentAsync(HttpClient authClient, string armadaBaseUrl, string vesselId)
        {
            HttpResponseMessage environmentResponse = await authClient.PostAsync("/api/v1/environments",
                JsonHelper.ToJsonContent(new
                {
                    VesselId = vesselId,
                    Name = "staging",
                    Kind = EnvironmentKindEnum.Staging,
                    BaseUrl = armadaBaseUrl,
                    HealthEndpoint = "/api/v1/status/health",
                    RequiresApproval = false,
                    IsDefault = true,
                    Active = true
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, environmentResponse.StatusCode);
            DeploymentEnvironment environment = await JsonHelper.DeserializeAsync<DeploymentEnvironment>(environmentResponse).ConfigureAwait(false);
            return environment.Id;
        }

        private static async Task<string> CreateDeploymentAsync(HttpClient authClient, string vesselId, string workflowProfileId, string environmentId)
        {
            HttpResponseMessage deploymentResponse = await authClient.PostAsync("/api/v1/deployments",
                JsonHelper.ToJsonContent(new
                {
                    VesselId = vesselId,
                    WorkflowProfileId = workflowProfileId,
                    EnvironmentId = environmentId,
                    Title = "GitHub Actions Deployment",
                    SourceRef = "main",
                    AutoExecute = true
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, deploymentResponse.StatusCode);
            Deployment deployment = await JsonHelper.DeserializeAsync<Deployment>(deploymentResponse).ConfigureAwait(false);
            return deployment.Id;
        }

        private static async Task<string> CreateMissionAsync(HttpClient authClient, string vesselId, string pullRequestUrl)
        {
            HttpResponseMessage missionResponse = await authClient.PostAsync("/api/v1/missions",
                JsonHelper.ToJsonContent(new
                {
                    Title = "GitHub PR Mission",
                    Description = "Mission linked to a GitHub pull request.",
                    VesselId = vesselId,
                    PrUrl = pullRequestUrl
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, missionResponse.StatusCode);

            string json = await missionResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement missionElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Mission", out JsonElement wrappedMission))
            {
                missionElement = wrappedMission;
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("mission", out JsonElement camelMission))
            {
                missionElement = camelMission;
            }
            else
            {
                missionElement = root;
            }

            Mission? mission = JsonSerializer.Deserialize<Mission>(missionElement.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (mission == null)
                throw new InvalidOperationException("Mission response could not be deserialized.");
            return mission.Id;
        }

        private static async Task<string> CreateReleaseAsync(HttpClient authClient, string vesselId, string missionId)
        {
            HttpResponseMessage releaseResponse = await authClient.PostAsync("/api/v1/releases",
                JsonHelper.ToJsonContent(new
                {
                    VesselId = vesselId,
                    Title = "GitHub Release",
                    MissionIds = new[] { missionId }
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, releaseResponse.StatusCode);
            Release release = await JsonHelper.DeserializeAsync<Release>(releaseResponse).ConfigureAwait(false);
            return release.Id;
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

        #endregion

        #region Nested-Types

        private sealed class FakeGitHubServer : IDisposable
        {
            private readonly HttpListener _Listener;
            private readonly CancellationTokenSource _TokenSource;
            private readonly Task _ListenerTask;

            public string ExpectedToken { get; }

            public string Issue123Title { get; set; } = "GitHub Issue 123";

            public string Issue123State { get; set; } = "open";

            public int Port { get; }

            public string RepositoryUrl => "http://localhost:" + Port + "/octo/armada-test.git";

            public string PullRequestUrl => "http://localhost:" + Port + "/octo/armada-test/pull/45";

            public string IssueUrl => "http://localhost:" + Port + "/octo/armada-test/issues/123";

            public FakeGitHubServer(string expectedToken)
            {
                ExpectedToken = expectedToken ?? throw new ArgumentNullException(nameof(expectedToken));
                Port = GetAvailablePort();
                _Listener = new HttpListener();
                _Listener.Prefixes.Add("http://localhost:" + Port + "/");
                _Listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
                _TokenSource = new CancellationTokenSource();
                _Listener.Start();
                _ListenerTask = Task.Run(ListenAsync);
            }

            public void Dispose()
            {
                _TokenSource.Cancel();
                try
                {
                    _Listener.Stop();
                }
                catch
                {
                }

                try
                {
                    _Listener.Close();
                }
                catch
                {
                }

                try
                {
                    _ListenerTask.GetAwaiter().GetResult();
                }
                catch
                {
                }
            }

            private async Task ListenAsync()
            {
                while (!_TokenSource.IsCancellationRequested)
                {
                    HttpListenerContext? context = null;
                    try
                    {
                        context = await _Listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (HttpListenerException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (context == null)
                        continue;

                    _ = Task.Run(() => HandleRequestAsync(context));
                }
            }

            private async Task HandleRequestAsync(HttpListenerContext context)
            {
                try
                {
                    string authorization = context.Request.Headers["Authorization"] ?? String.Empty;
                    if (!String.Equals(authorization, "Bearer " + ExpectedToken, StringComparison.Ordinal))
                    {
                        context.Response.StatusCode = 401;
                        await WriteJsonAsync(context.Response, "{\"message\":\"bad token\"}").ConfigureAwait(false);
                        return;
                    }

                    string path = context.Request.Url?.AbsolutePath ?? String.Empty;
                    if (String.Equals(path, "/api/v3/repos/octo/armada-test/issues/123", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = "{\"number\":123,\"title\":\"" + EscapeJson(Issue123Title) + "\",\"body\":\"Track intake from GitHub.\",\"state\":\"" + EscapeJson(Issue123State) + "\",\"html_url\":\"" + IssueUrl + "\",\"updated_at\":\"2026-05-06T12:00:00Z\",\"labels\":[{\"name\":\"intake\"},{\"name\":\"scope\"}],\"assignees\":[{\"login\":\"joel\"}]}";
                        await WriteJsonAsync(context.Response, body).ConfigureAwait(false);
                        return;
                    }

                    if (String.Equals(path, "/api/v3/repos/octo/armada-test/pulls/45", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = "{\"number\":45,\"title\":\"Armada Integration PR\",\"body\":\"Implements GitHub-backed delivery visibility.\",\"state\":\"open\",\"html_url\":\"" + PullRequestUrl + "\",\"draft\":false,\"merged\":false,\"mergeable_state\":\"clean\",\"additions\":12,\"deletions\":3,\"changed_files\":4,\"commits\":2,\"created_at\":\"2026-05-06T10:00:00Z\",\"updated_at\":\"2026-05-06T11:00:00Z\",\"merged_at\":null,\"user\":{\"login\":\"captain-armada\"},\"merged_by\":null,\"base\":{\"ref\":\"main\",\"sha\":\"base123\"},\"head\":{\"ref\":\"feature/github\",\"sha\":\"abc123\"},\"requested_reviewers\":[{\"login\":\"reviewer1\"}],\"labels\":[{\"name\":\"automation\"}]}";
                        await WriteJsonAsync(context.Response, body).ConfigureAwait(false);
                        return;
                    }

                    if (String.Equals(path, "/api/v3/repos/octo/armada-test/issues/45", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = "{\"number\":45,\"title\":\"Armada Integration PR\",\"body\":\"Implements GitHub-backed delivery visibility.\",\"state\":\"open\",\"html_url\":\"" + PullRequestUrl + "\",\"updated_at\":\"2026-05-06T11:00:00Z\",\"labels\":[{\"name\":\"automation\"}],\"assignees\":[{\"login\":\"joel\"}]}";
                        await WriteJsonAsync(context.Response, body).ConfigureAwait(false);
                        return;
                    }

                    if (String.Equals(path, "/api/v3/repos/octo/armada-test/actions/runs", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = "{\"workflow_runs\":[{\"id\":9001,\"name\":\"CI Build\",\"display_title\":\"CI Build\",\"status\":\"completed\",\"conclusion\":\"success\",\"event\":\"push\",\"html_url\":\"http://localhost:" + Port + "/octo/armada-test/actions/runs/9001\",\"head_branch\":\"main\",\"head_sha\":\"abc123\",\"created_at\":\"2026-05-06T09:00:00Z\",\"run_started_at\":\"2026-05-06T09:01:00Z\",\"updated_at\":\"2026-05-06T09:02:00Z\"}]}";
                        await WriteJsonAsync(context.Response, body).ConfigureAwait(false);
                        return;
                    }

                    if (String.Equals(path, "/api/v3/repos/octo/armada-test/pulls/45/reviews", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = "[{\"state\":\"APPROVED\",\"body\":\"Looks good\",\"submitted_at\":\"2026-05-06T11:05:00Z\",\"user\":{\"login\":\"reviewer1\"}},{\"state\":\"CHANGES_REQUESTED\",\"body\":\"Please rename one method\",\"submitted_at\":\"2026-05-06T10:30:00Z\",\"user\":{\"login\":\"reviewer2\"}}]";
                        await WriteJsonAsync(context.Response, body).ConfigureAwait(false);
                        return;
                    }

                    if (String.Equals(path, "/api/v3/repos/octo/armada-test/issues/45/comments", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = "[{\"body\":\"Need one more check\",\"html_url\":\"http://localhost:" + Port + "/octo/armada-test/issues/45#issuecomment-1\",\"created_at\":\"2026-05-06T10:10:00Z\",\"user\":{\"login\":\"reviewer1\"}}]";
                        await WriteJsonAsync(context.Response, body).ConfigureAwait(false);
                        return;
                    }

                    if (String.Equals(path, "/api/v3/repos/octo/armada-test/commits/abc123/check-runs", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = "{\"check_runs\":[{\"name\":\"build-and-test\",\"status\":\"completed\",\"conclusion\":\"success\",\"details_url\":\"http://localhost:" + Port + "/octo/armada-test/actions/runs/9001\"}]}";
                        await WriteJsonAsync(context.Response, body).ConfigureAwait(false);
                        return;
                    }

                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context.Response, "{\"message\":\"not found\"}").ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        context.Response.OutputStream.Close();
                    }
                    catch
                    {
                    }
                }
            }

            private static async Task WriteJsonAsync(HttpListenerResponse response, string body)
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                response.ContentType = "application/json";
                response.ContentEncoding = Encoding.UTF8;
                response.ContentLength64 = data.Length;
                await response.OutputStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            }

            private static string EscapeJson(string value)
            {
                return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            }

            private static int GetAvailablePort()
            {
                return TestPorts.Reserve(1)[0];
            }
        }

        #endregion
    }
}
