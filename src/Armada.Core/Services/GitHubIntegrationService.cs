namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Pull-based GitHub integration helpers for objectives, check runs, and PR evidence.
    /// </summary>
    public class GitHubIntegrationService
    {
        private const string GitHubProviderName = Objective.GitHubSourceProvider;
        private const string GitHubActionsProviderName = "GitHubActions";
        private const int MaxReviewStatusPages = 10;

        /// <summary>
        /// Largest page GitHub returns (per_page). Pull-request sub-enumerations normalize a larger page size to this.
        /// </summary>
        public const int GitHubMaxPageSize = 100;

        private readonly DatabaseDriver _Database;
        private readonly ObjectiveService _Objectives;
        private readonly CheckRunService _CheckRuns;
        private readonly DeploymentService _Deployments;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly HttpClient _Client;
        private readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GitHubIntegrationService(
            DatabaseDriver database,
            ObjectiveService objectives,
            CheckRunService checkRuns,
            DeploymentService deployments,
            ArmadaSettings settings,
            LoggingModule logging,
            HttpMessageHandler? handler = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
            _CheckRuns = checkRuns ?? throw new ArgumentNullException(nameof(checkRuns));
            _Deployments = deployments ?? throw new ArgumentNullException(nameof(deployments));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Client = handler == null ? new HttpClient() : new HttpClient(handler, true);
            _Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Armada", Constants.ProductVersion));
            _Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        /// <summary>
        /// Import or refresh an objective from GitHub.
        /// </summary>
        public async Task<Objective> ImportObjectiveAsync(
            AuthContext auth,
            GitHubObjectiveImportRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.VesselId)) throw new ArgumentNullException(nameof(request.VesselId));
            if (request.Number <= 0) throw new InvalidOperationException("GitHub number must be greater than zero.");

            GitHubRepositoryContext repository = await ResolveRepositoryContextAsync(auth, request.VesselId, token).ConfigureAwait(false);
            GitHubImportedObjectiveData imported = await ReadImportedObjectiveDataAsync(repository, request.SourceType, request.Number, token).ConfigureAwait(false);

            Objective objective;
            if (!String.IsNullOrWhiteSpace(request.ObjectiveId))
            {
                objective = await _Objectives.ReadAsync(auth, request.ObjectiveId, token).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("Objective not found.");
            }
            else
            {
                objective = new Objective
                {
                    TenantId = repository.Vessel.TenantId,
                    UserId = auth.UserId,
                    Title = imported.Title,
                    CreatedUtc = DateTime.UtcNow
                };
            }

            objective.Title = imported.Title;
            objective.Description = Normalize(imported.Description);
            objective.Owner = Normalize(imported.Owner);
            objective.SourceProvider = GitHubProviderName;
            objective.SourceType = imported.SourceType;
            objective.SourceId = imported.SourceId;
            objective.SourceUrl = imported.SourceUrl;
            objective.SourceUpdatedUtc = imported.SourceUpdatedUtc;
            objective.Tags = MergeDistinct(objective.Tags, imported.Tags);
            objective.EvidenceLinks = MergeDistinct(objective.EvidenceLinks, new[] { imported.SourceUrl });
            AddIfMissing(objective.VesselIds, repository.Vessel.Id);
            AddIfMissing(objective.FleetIds, repository.Vessel.FleetId);

            if (request.StatusOverride.HasValue)
            {
                objective.Status = request.StatusOverride.Value;
            }
            else
            {
                objective.Status = ResolveImportedObjectiveStatus(objective.Status, imported.Status);
            }

            return await _Objectives.PersistImportedAsync(auth, objective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Sync recent GitHub Actions workflow runs into Armada check history.
        /// </summary>
        public async Task<GitHubActionsSyncResult> SyncGitHubActionsAsync(
            AuthContext auth,
            GitHubActionsSyncRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.VesselId)) throw new ArgumentNullException(nameof(request.VesselId));

            GitHubRepositoryContext repository = await ResolveRepositoryContextAsync(auth, request.VesselId, token).ConfigureAwait(false);
            Deployment? deployment = null;
            if (!String.IsNullOrWhiteSpace(request.DeploymentId))
            {
                deployment = await _Deployments.ReadAsync(auth, request.DeploymentId, token).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("Deployment not found.");
                if (!String.Equals(deployment.VesselId, repository.Vessel.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected deployment does not belong to the supplied vessel.");
            }

            GitHubWorkflowRunPage page = await ReadWorkflowRunsAsync(repository, request, token).ConfigureAwait(false);
            List<GitHubWorkflowRunDto> runs = page.Runs;
            GitHubActionsSyncResult result = new GitHubActionsSyncResult
            {
                VesselId = repository.Vessel.Id,
                DeploymentId = deployment?.Id,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                SourceTotalCount = page.TotalCount,
                SourceTotalPages = page.TotalCount <= 0 ? 0 : (int)Math.Ceiling(page.TotalCount / (double)page.PageSize),
                FetchedCount = page.FetchedCount,
                FilteredOutCount = page.FetchedCount - runs.Count
            };
            List<string> linkedCheckRunIds = new List<string>();

            foreach (GitHubWorkflowRunDto workflowRun in runs)
            {
                CheckRun? existing = await ReadExistingImportedRunAsync(auth, repository.Vessel.Id, Convert.ToString(workflowRun.Id, CultureInfo.InvariantCulture), token).ConfigureAwait(false);
                CheckRunImportRequest importRequest = BuildImportRequest(request, deployment, repository.Vessel, workflowRun);
                CheckRun imported = await _CheckRuns.ImportOrUpdateAsync(auth, importRequest, token).ConfigureAwait(false);
                result.CheckRunIds.Add(imported.Id);
                linkedCheckRunIds.Add(imported.Id);
                if (existing == null)
                    result.CreatedCount++;
                else
                    result.UpdatedCount++;
            }

            if (deployment != null && linkedCheckRunIds.Count > 0)
                await _Deployments.LinkCheckRunsAsync(auth, deployment.Id, linkedCheckRunIds, token).ConfigureAwait(false);

            return result;
        }

        /// <summary>
        /// Retrieve normalized GitHub pull-request evidence for a mission.
        /// </summary>
        public async Task<GitHubPullRequestDetail> GetMissionPullRequestAsync(
            AuthContext auth,
            Mission mission,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (String.IsNullOrWhiteSpace(mission.VesselId))
                throw new InvalidOperationException("Mission is not linked to a vessel.");
            if (String.IsNullOrWhiteSpace(mission.PrUrl))
                throw new InvalidOperationException("Mission does not have a GitHub pull request URL.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, mission.VesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Mission vessel not found or not accessible.");
            string tokenValue = ResolveToken(vessel);
            GitHubPullRequestReference reference = ParsePullRequestReference(mission.PrUrl);
            return await ReadPullRequestDetailAsync(reference, tokenValue, mission.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one page of the reviews on a mission's pull request, paged by GitHub (page size at most
        /// <see cref="GitHubMaxPageSize"/>; a larger requested size is normalized to it). CreatedAscending is GitHub's
        /// order (oldest first); CreatedDescending (the default) reverses it.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="mission">Mission already read under the caller's scope.</param>
        /// <param name="query">Paging query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of reviews with the exact total.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the mission has no vessel or pull request URL, or GitHub fails.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the mission's vessel is not accessible.</exception>
        public async Task<EnumerationResult<GitHubPullRequestReview>> EnumerateMissionPullRequestReviewsAsync(
            AuthContext auth,
            Mission mission,
            EnumerationQuery query,
            CancellationToken token = default)
        {
            GitHubSourcePage source = await ResolveMissionSourceAsync(auth, mission, query, token).ConfigureAwait(false);
            string url = source.PullRequestApiUrl + "/reviews";
            long total = await CountViaLinkAsync<GitHubPullRequestReviewDto>(url, source.Token, token).ConfigureAwait(false);
            return await ReadSourcePageAsync(source.Query, total, async (pageNumber, pageSize, ct) =>
            {
                GitHubPage<List<GitHubPullRequestReviewDto>> page = await GetPageAsync<List<GitHubPullRequestReviewDto>>(PagedUrl(url, pageNumber, pageSize), source.Token, ct).ConfigureAwait(false);
                return EnumerationResult<GitHubPullRequestReview>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, page.Value.Select(MapReview).ToList(), total);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one page of the issue comments on a mission's pull request, paged by GitHub (page size at most
        /// <see cref="GitHubMaxPageSize"/>). CreatedAscending is GitHub's order (oldest first); CreatedDescending (the
        /// default) reverses it.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="mission">Mission already read under the caller's scope.</param>
        /// <param name="query">Paging query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of comments with the exact total.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the mission has no vessel or pull request URL, or GitHub fails.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the mission's vessel is not accessible.</exception>
        public async Task<EnumerationResult<GitHubPullRequestComment>> EnumerateMissionPullRequestCommentsAsync(
            AuthContext auth,
            Mission mission,
            EnumerationQuery query,
            CancellationToken token = default)
        {
            GitHubSourcePage source = await ResolveMissionSourceAsync(auth, mission, query, token).ConfigureAwait(false);
            string url = source.IssueApiUrl + "/comments";
            long total = await CountViaLinkAsync<GitHubIssueCommentDto>(url, source.Token, token).ConfigureAwait(false);
            return await ReadSourcePageAsync(source.Query, total, async (pageNumber, pageSize, ct) =>
            {
                GitHubPage<List<GitHubIssueCommentDto>> page = await GetPageAsync<List<GitHubIssueCommentDto>>(PagedUrl(url, pageNumber, pageSize), source.Token, ct).ConfigureAwait(false);
                return EnumerationResult<GitHubPullRequestComment>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, page.Value.Select(MapComment).ToList(), total);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one page of the provider checks on a mission's pull request head commit, paged by GitHub (page size at
        /// most <see cref="GitHubMaxPageSize"/>). CreatedAscending is GitHub's order; CreatedDescending (the default)
        /// reverses it. An empty page when the pull request has no head commit.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="mission">Mission already read under the caller's scope.</param>
        /// <param name="query">Paging query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of checks with the exact total.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the mission has no vessel or pull request URL, or GitHub fails.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the mission's vessel is not accessible.</exception>
        public async Task<EnumerationResult<GitHubPullRequestCheck>> EnumerateMissionPullRequestChecksAsync(
            AuthContext auth,
            Mission mission,
            EnumerationQuery query,
            CancellationToken token = default)
        {
            GitHubSourcePage source = await ResolveMissionSourceAsync(auth, mission, query, token).ConfigureAwait(false);
            GitHubPullRequestDto pullRequest = await GetAsync<GitHubPullRequestDto>(source.PullRequestApiUrl, source.Token, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(pullRequest.Head?.Sha))
                return EnumerationResult<GitHubPullRequestCheck>.Create(source.Query, new List<GitHubPullRequestCheck>(), 0);

            string url = source.RepositoryApiUrl + "/commits/" + Uri.EscapeDataString(pullRequest.Head!.Sha!) + "/check-runs";
            GitHubCheckRunsEnvelopeDto probe = await GetAsync<GitHubCheckRunsEnvelopeDto>(PagedUrl(url, 1, 1), source.Token, token).ConfigureAwait(false);
            long total = probe.TotalCount;
            return await ReadSourcePageAsync(source.Query, total, async (pageNumber, pageSize, ct) =>
            {
                GitHubCheckRunsEnvelopeDto page = await GetAsync<GitHubCheckRunsEnvelopeDto>(PagedUrl(url, pageNumber, pageSize), source.Token, ct).ConfigureAwait(false);
                return EnumerationResult<GitHubPullRequestCheck>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, (page.CheckRuns ?? new List<GitHubCheckRunDto>()).Select(MapCheck).ToList(), total);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// One page of the GitHub pull requests linked to a release's missions. The distinct pull requests are resolved
        /// from the release's mission links (at most <see cref="CollectionLimits.MaxLinkedIds"/>), ordered by pull
        /// request number (newest first) then repository, and only the requested page is read from GitHub, so the
        /// totals are exact and GitHub is asked for one page of details at a time.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="release">Release (already read within the caller's scope).</param>
        /// <param name="query">Paging (PageNumber, PageSize 1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of pull request details.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the release has no vessel.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the release vessel is not accessible.</exception>
        public async Task<EnumerationResult<GitHubPullRequestDetail>> EnumerateReleasePullRequestsAsync(
            AuthContext auth,
            Release release,
            EnumerationQuery query,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (String.IsNullOrWhiteSpace(release.VesselId))
                throw new InvalidOperationException("Release is not linked to a vessel.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, release.VesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Release vessel not found or not accessible.");
            string tokenValue = ResolveToken(vessel);

            // The distinct pull requests come from the release's links (bounded by MaxLinkedIds), not from GitHub.
            List<GitHubPullRequestReference> references = new List<GitHubPullRequestReference>();
            Dictionary<string, string> missionIdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string missionId in release.MissionIds.Take(CollectionLimits.MaxLinkedIds))
            {
                Mission? mission = await ReadAccessibleMissionAsync(auth, missionId, token).ConfigureAwait(false);
                if (mission == null || String.IsNullOrWhiteSpace(mission.PrUrl))
                    continue;

                GitHubPullRequestReference reference = ParsePullRequestReference(mission.PrUrl);
                string key = ReleasePullRequestKey(reference);
                if (missionIdByKey.ContainsKey(key))
                    continue;
                missionIdByKey[key] = mission.Id;
                references.Add(reference);
            }

            List<GitHubPullRequestReference> ordered = references
                .OrderByDescending(item => item.Number)
                .ThenBy(item => item.Owner + "/" + item.Repository, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<GitHubPullRequestDetail> details = new List<GitHubPullRequestDetail>();
            foreach (GitHubPullRequestReference reference in ordered.Skip(query.Offset).Take(query.PageSize))
            {
                token.ThrowIfCancellationRequested();
                details.Add(await ReadPullRequestDetailAsync(reference, tokenValue, missionIdByKey[ReleasePullRequestKey(reference)], token).ConfigureAwait(false));
            }

            return EnumerationResult<GitHubPullRequestDetail>.Create(query, details, ordered.Count);
        }

        private static string ReleasePullRequestKey(GitHubPullRequestReference reference)
        {
            return reference.Owner + "/" + reference.Repository + "#" + reference.Number.ToString(CultureInfo.InvariantCulture);
        }

        private async Task<GitHubImportedObjectiveData> ReadImportedObjectiveDataAsync(
            GitHubRepositoryContext repository,
            GitHubObjectiveSourceTypeEnum sourceType,
            int number,
            CancellationToken token)
        {
            if (sourceType == GitHubObjectiveSourceTypeEnum.Issue)
            {
                GitHubIssueDto issue = await GetAsync<GitHubIssueDto>(
                    repository.ApiBaseUrl + "/repos/" + Uri.EscapeDataString(repository.Owner) + "/" + Uri.EscapeDataString(repository.Repository) + "/issues/" + number,
                    repository.Token,
                    token).ConfigureAwait(false);
            return BuildImportedObjectiveData(repository, issue, null, GitHubObjectiveSourceTypeEnum.Issue, number);
            }

            GitHubPullRequestDto pullRequest = await GetAsync<GitHubPullRequestDto>(
                repository.ApiBaseUrl + "/repos/" + Uri.EscapeDataString(repository.Owner) + "/" + Uri.EscapeDataString(repository.Repository) + "/pulls/" + number,
                repository.Token,
                token).ConfigureAwait(false);
            GitHubIssueDto pullRequestIssue = await GetAsync<GitHubIssueDto>(
                repository.ApiBaseUrl + "/repos/" + Uri.EscapeDataString(repository.Owner) + "/" + Uri.EscapeDataString(repository.Repository) + "/issues/" + number,
                repository.Token,
                token).ConfigureAwait(false);
            return BuildImportedObjectiveData(repository, pullRequestIssue, pullRequest, GitHubObjectiveSourceTypeEnum.PullRequest, number);
        }

        private static GitHubImportedObjectiveData BuildImportedObjectiveData(
            GitHubRepositoryContext repository,
            GitHubIssueDto issue,
            GitHubPullRequestDto? pullRequest,
            GitHubObjectiveSourceTypeEnum sourceType,
            int number)
        {
            ObjectiveStatusEnum status;
            if (sourceType == GitHubObjectiveSourceTypeEnum.Issue)
            {
                status = String.Equals(issue.State, "closed", StringComparison.OrdinalIgnoreCase)
                    ? ObjectiveStatusEnum.Completed
                    : ObjectiveStatusEnum.Scoped;
            }
            else if (pullRequest != null && pullRequest.Merged.GetValueOrDefault())
            {
                status = ObjectiveStatusEnum.Released;
            }
            else if (String.Equals(issue.State, "closed", StringComparison.OrdinalIgnoreCase))
            {
                status = ObjectiveStatusEnum.Cancelled;
            }
            else
            {
                status = ObjectiveStatusEnum.InProgress;
            }

            return new GitHubImportedObjectiveData
            {
                Title = issue.Title?.Trim() ?? (sourceType == GitHubObjectiveSourceTypeEnum.PullRequest ? "GitHub Pull Request" : "GitHub Issue"),
                Description = Normalize(issue.Body),
                Owner = issue.Assignees.FirstOrDefault()?.Login,
                Tags = issue.Labels
                    .Select(label => Normalize(label.Name))
                    .Where(name => !String.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .ToList(),
                SourceType = sourceType.ToString(),
                SourceId = repository.Owner + "/" + repository.Repository + "#" + number.ToString(CultureInfo.InvariantCulture),
                SourceUrl = issue.HtmlUrl ?? pullRequest?.HtmlUrl ?? String.Empty,
                SourceUpdatedUtc = ToUtcDateTime(issue.UpdatedAt) ?? ToUtcDateTime(pullRequest?.UpdatedAt),
                Status = status
            };
        }

        private async Task<GitHubWorkflowRunPage> ReadWorkflowRunsAsync(
            GitHubRepositoryContext repository,
            GitHubActionsSyncRequest request,
            CancellationToken token)
        {
            // One bounded GitHub page (per_page at most 100); the caller reads further pages with PageNumber, and the
            // result reports GitHub's total_count so nothing is cut short silently.
            int runCount = Math.Clamp(request.RunCount, 1, 100);
            int pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
            List<string> queryParts = new List<string>
            {
                "per_page=" + runCount.ToString(CultureInfo.InvariantCulture),
                "page=" + pageNumber.ToString(CultureInfo.InvariantCulture)
            };
            if (!String.IsNullOrWhiteSpace(request.BranchName))
                queryParts.Add("branch=" + Uri.EscapeDataString(request.BranchName.Trim()));
            if (!String.IsNullOrWhiteSpace(request.RunStatus))
                queryParts.Add("status=" + Uri.EscapeDataString(request.RunStatus.Trim()));
            if (!String.IsNullOrWhiteSpace(request.CommitHash))
                queryParts.Add("head_sha=" + Uri.EscapeDataString(request.CommitHash.Trim()));

            string url = repository.ApiBaseUrl + "/repos/" + Uri.EscapeDataString(repository.Owner) + "/" + Uri.EscapeDataString(repository.Repository) + "/actions/runs?" + String.Join("&", queryParts);
            GitHubWorkflowRunsEnvelopeDto response = await GetAsync<GitHubWorkflowRunsEnvelopeDto>(url, repository.Token, token).ConfigureAwait(false);

            List<GitHubWorkflowRunDto> fetched = (response.WorkflowRuns ?? new List<GitHubWorkflowRunDto>()).Take(runCount).ToList();
            IEnumerable<GitHubWorkflowRunDto> filtered = fetched;
            if (!String.IsNullOrWhiteSpace(request.WorkflowName))
            {
                string workflowName = request.WorkflowName.Trim();
                filtered = filtered.Where(run => ContainsIgnoreCase(run.Name, workflowName) || ContainsIgnoreCase(run.DisplayTitle, workflowName));
            }

            if (!String.IsNullOrWhiteSpace(request.CommitHash))
            {
                string commitHash = request.CommitHash.Trim();
                filtered = filtered.Where(run => String.Equals(run.HeadSha, commitHash, StringComparison.OrdinalIgnoreCase));
            }

            return new GitHubWorkflowRunPage
            {
                Runs = filtered.ToList(),
                PageNumber = pageNumber,
                PageSize = runCount,
                TotalCount = response.TotalCount,
                FetchedCount = fetched.Count
            };
        }

        private CheckRunImportRequest BuildImportRequest(
            GitHubActionsSyncRequest request,
            Deployment? deployment,
            Vessel vessel,
            GitHubWorkflowRunDto workflowRun)
        {
            string? environmentName = Normalize(request.EnvironmentName) ?? deployment?.EnvironmentName;
            string label = Normalize(workflowRun.Name)
                ?? Normalize(workflowRun.DisplayTitle)
                ?? "GitHub Actions Run";

            StringBuilder output = new StringBuilder();
            output.AppendLine("GitHub Actions workflow run imported by Armada.");
            output.AppendLine("Workflow: " + label);
            output.AppendLine("Status: " + (Normalize(workflowRun.Status) ?? "unknown"));
            if (!String.IsNullOrWhiteSpace(workflowRun.Conclusion))
                output.AppendLine("Conclusion: " + workflowRun.Conclusion);
            if (!String.IsNullOrWhiteSpace(workflowRun.Event))
                output.AppendLine("Event: " + workflowRun.Event);
            if (!String.IsNullOrWhiteSpace(workflowRun.HeadBranch))
                output.AppendLine("Branch: " + workflowRun.HeadBranch);
            if (!String.IsNullOrWhiteSpace(workflowRun.HeadSha))
                output.AppendLine("Commit: " + workflowRun.HeadSha);
            if (!String.IsNullOrWhiteSpace(workflowRun.HtmlUrl))
                output.AppendLine("URL: " + workflowRun.HtmlUrl);

            CheckRunImportRequest importRequest = new CheckRunImportRequest
            {
                VesselId = vessel.Id,
                WorkflowProfileId = Normalize(request.WorkflowProfileId) ?? deployment?.WorkflowProfileId,
                MissionId = deployment?.MissionId,
                VoyageId = deployment?.VoyageId,
                DeploymentId = deployment?.Id,
                Label = label,
                Type = request.TypeOverride ?? InferCheckRunType(workflowRun.Name),
                Status = MapCheckRunStatus(workflowRun.Status, workflowRun.Conclusion),
                ProviderName = GitHubActionsProviderName,
                ExternalId = Convert.ToString(workflowRun.Id, CultureInfo.InvariantCulture),
                ExternalUrl = Normalize(workflowRun.HtmlUrl),
                EnvironmentName = environmentName,
                Command = label,
                BranchName = Normalize(request.BranchName) ?? Normalize(workflowRun.HeadBranch),
                CommitHash = Normalize(request.CommitHash) ?? Normalize(workflowRun.HeadSha),
                ExitCode = InferExitCode(workflowRun.Status, workflowRun.Conclusion),
                Output = output.ToString().Trim(),
                Summary = BuildWorkflowRunSummary(workflowRun),
                DurationMs = ComputeDurationMs(workflowRun.RunStartedAt, workflowRun.UpdatedAt),
                StartedUtc = ToUtcDateTime(workflowRun.RunStartedAt) ?? ToUtcDateTime(workflowRun.CreatedAt),
                CompletedUtc = String.Equals(workflowRun.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    ? ToUtcDateTime(workflowRun.UpdatedAt)
                    : null
            };

            return importRequest;
        }

        private async Task<GitHubPullRequestDetail> ReadPullRequestDetailAsync(
            GitHubPullRequestReference reference,
            string tokenValue,
            string? missionId,
            CancellationToken token)
        {
            string repoSegment = Uri.EscapeDataString(reference.Owner) + "/" + Uri.EscapeDataString(reference.Repository);
            string repositoryApiUrl = reference.ApiBaseUrl + "/repos/" + repoSegment;
            string number = reference.Number.ToString(CultureInfo.InvariantCulture);
            GitHubPullRequestDto pullRequest = await GetAsync<GitHubPullRequestDto>(
                repositoryApiUrl + "/pulls/" + number,
                tokenValue,
                token).ConfigureAwait(false);

            // Reviews are read in bounded pages of 100 to compute the review status, up to MaxReviewStatusPages pages;
            // beyond that the status reflects the first pages and ReviewStatusComplete says so.
            string reviewsUrl = repositoryApiUrl + "/pulls/" + number + "/reviews";
            List<GitHubPullRequestReview> reviews = new List<GitHubPullRequestReview>();
            bool reviewsComplete = true;
            for (int pageNumber = 1; ; pageNumber++)
            {
                GitHubPage<List<GitHubPullRequestReviewDto>> page = await GetPageAsync<List<GitHubPullRequestReviewDto>>(PagedUrl(reviewsUrl, pageNumber, GitHubMaxPageSize), tokenValue, token).ConfigureAwait(false);
                reviews.AddRange(page.Value.Select(MapReview));
                if (page.Value.Count < GitHubMaxPageSize) break;
                if (pageNumber >= MaxReviewStatusPages)
                {
                    reviewsComplete = false;
                    break;
                }
            }

            long reviewCount = reviewsComplete
                ? reviews.Count
                : await CountViaLinkAsync<GitHubPullRequestReviewDto>(reviewsUrl, tokenValue, token).ConfigureAwait(false);
            long commentCount = await CountViaLinkAsync<GitHubIssueCommentDto>(repositoryApiUrl + "/issues/" + number + "/comments", tokenValue, token).ConfigureAwait(false);

            long checkCount = 0;
            if (!String.IsNullOrWhiteSpace(pullRequest.Head?.Sha))
            {
                try
                {
                    GitHubCheckRunsEnvelopeDto checksEnvelope = await GetAsync<GitHubCheckRunsEnvelopeDto>(
                        PagedUrl(repositoryApiUrl + "/commits/" + Uri.EscapeDataString(pullRequest.Head.Sha) + "/check-runs", 1, 1),
                        tokenValue,
                        token).ConfigureAwait(false);
                    checkCount = checksEnvelope.TotalCount;
                }
                catch (InvalidOperationException)
                {
                    checkCount = 0;
                }
            }

            List<string> requestedReviewers = (pullRequest.RequestedReviewers ?? new List<GitHubUserDto>())
                .Select(reviewer => Normalize(reviewer.Login))
                .Where(login => !String.IsNullOrWhiteSpace(login))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> labels = (pullRequest.Labels ?? new List<GitHubLabelDto>())
                .Select(label => Normalize(label.Name))
                .Where(name => !String.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            GitHubPullRequestDetail detail = new GitHubPullRequestDetail
            {
                Repository = reference.Owner + "/" + reference.Repository,
                Number = reference.Number,
                MissionId = missionId,
                Url = Normalize(pullRequest.HtmlUrl) ?? reference.Url,
                Title = Normalize(pullRequest.Title) ?? "GitHub Pull Request",
                Body = Normalize(pullRequest.Body),
                State = Normalize(pullRequest.State) ?? "unknown",
                IsDraft = pullRequest.Draft.GetValueOrDefault(),
                IsMerged = pullRequest.Merged.GetValueOrDefault(),
                MergeableState = Normalize(pullRequest.MergeableState),
                BaseRefName = Normalize(pullRequest.Base?.Ref) ?? String.Empty,
                HeadRefName = Normalize(pullRequest.Head?.Ref) ?? String.Empty,
                HeadSha = Normalize(pullRequest.Head?.Sha),
                AuthorLogin = Normalize(pullRequest.User?.Login),
                MergedByLogin = Normalize(pullRequest.MergedBy?.Login),
                RequestedReviewers = requestedReviewers.Take(GitHubPullRequestDetail.MaxListed).ToList(),
                RequestedReviewerCount = requestedReviewers.Count,
                Labels = labels.Take(GitHubPullRequestDetail.MaxListed).ToList(),
                LabelCount = labels.Count,
                ChangedFiles = pullRequest.ChangedFiles ?? 0,
                Additions = pullRequest.Additions ?? 0,
                Deletions = pullRequest.Deletions ?? 0,
                CommitCount = pullRequest.Commits ?? 0,
                ReviewCount = (int)Math.Min(int.MaxValue, reviewCount),
                CommentCount = (int)Math.Min(int.MaxValue, commentCount),
                CheckCount = (int)Math.Min(int.MaxValue, checkCount),
                ReviewStatusComplete = reviewsComplete,
                CreatedUtc = ToUtcDateTime(pullRequest.CreatedAt),
                UpdatedUtc = ToUtcDateTime(pullRequest.UpdatedAt),
                MergedUtc = ToUtcDateTime(pullRequest.MergedAt)
            };
            detail.ReviewStatus = ComputeReviewStatus(reviews, requestedReviewers.Count);
            return detail;
        }

        private async Task<GitHubSourcePage> ResolveMissionSourceAsync(AuthContext auth, Mission mission, EnumerationQuery query, CancellationToken token)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (String.IsNullOrWhiteSpace(mission.VesselId))
                throw new InvalidOperationException("Mission is not linked to a vessel.");
            if (String.IsNullOrWhiteSpace(mission.PrUrl))
                throw new InvalidOperationException("Mission does not have a GitHub pull request URL.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, mission.VesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Mission vessel not found or not accessible.");
            GitHubPullRequestReference reference = ParsePullRequestReference(mission.PrUrl);
            string repositoryApiUrl = reference.ApiBaseUrl + "/repos/" + Uri.EscapeDataString(reference.Owner) + "/" + Uri.EscapeDataString(reference.Repository);
            string number = reference.Number.ToString(CultureInfo.InvariantCulture);
            return new GitHubSourcePage
            {
                Token = ResolveToken(vessel),
                RepositoryApiUrl = repositoryApiUrl,
                PullRequestApiUrl = repositoryApiUrl + "/pulls/" + number,
                IssueApiUrl = repositoryApiUrl + "/issues/" + number,
                Query = new EnumerationQuery
                {
                    PageNumber = query.PageNumber,
                    PageSize = Math.Min(query.PageSize, GitHubMaxPageSize),
                    Order = query.Order
                }
            };
        }

        private static async Task<EnumerationResult<T>> ReadSourcePageAsync<T>(EnumerationQuery query, long total, PageFetchDelegate<T> fetch, CancellationToken token)
        {
            // GitHub pages oldest first. CreatedAscending reads that order directly; CreatedDescending reads the mirrored
            // window and reverses it, so either order pages without gaps or overlaps.
            long offset = query.Offset;
            long count = Math.Max(0, Math.Min(query.PageSize, total - offset));
            List<T> items;
            if (count <= 0)
            {
                items = new List<T>();
            }
            else if (query.Order == EnumerationOrderEnum.CreatedAscending)
            {
                items = await SectionedPaging.ReadWindowAsync(fetch, offset, (int)count, query.PageSize, token).ConfigureAwait(false);
            }
            else
            {
                long start = Math.Max(0, total - offset - count);
                items = await SectionedPaging.ReadWindowAsync(fetch, start, (int)count, query.PageSize, token).ConfigureAwait(false);
                items.Reverse();
            }

            return EnumerationResult<T>.Create(query, items, total);
        }

        private async Task<long> CountViaLinkAsync<T>(string url, string tokenValue, CancellationToken token)
        {
            // With per_page=1 GitHub's Link rel="last" names the last page, which is the item count.
            GitHubPage<List<T>> page = await GetPageAsync<List<T>>(PagedUrl(url, 1, 1), tokenValue, token).ConfigureAwait(false);
            if (page.LastPage.HasValue) return page.LastPage.Value;
            return page.Value.Count;
        }

        private static string PagedUrl(string url, int pageNumber, int pageSize)
        {
            string separator = url.Contains('?') ? "&" : "?";
            return url + separator + "per_page=" + pageSize.ToString(CultureInfo.InvariantCulture) + "&page=" + pageNumber.ToString(CultureInfo.InvariantCulture);
        }

        private GitHubPullRequestReview MapReview(GitHubPullRequestReviewDto review)
        {
            return new GitHubPullRequestReview
            {
                ReviewerLogin = Normalize(review.User?.Login),
                State = Normalize(review.State) ?? String.Empty,
                Body = Normalize(review.Body),
                SubmittedUtc = ToUtcDateTime(review.SubmittedAt)
            };
        }

        private GitHubPullRequestComment MapComment(GitHubIssueCommentDto comment)
        {
            return new GitHubPullRequestComment
            {
                AuthorLogin = Normalize(comment.User?.Login),
                Body = Normalize(comment.Body),
                Url = Normalize(comment.HtmlUrl),
                CreatedUtc = ToUtcDateTime(comment.CreatedAt)
            };
        }

        private GitHubPullRequestCheck MapCheck(GitHubCheckRunDto check)
        {
            return new GitHubPullRequestCheck
            {
                Name = Normalize(check.Name) ?? String.Empty,
                Status = Normalize(check.Status) ?? String.Empty,
                Conclusion = Normalize(check.Conclusion),
                DetailsUrl = Normalize(check.DetailsUrl)
            };
        }

        private async Task<GitHubRepositoryContext> ResolveRepositoryContextAsync(
            AuthContext auth,
            string vesselId,
            CancellationToken token)
        {
            Vessel vessel = await ReadAccessibleVesselAsync(auth, vesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Vessel not found or not accessible.");

            string repoUrl = Normalize(vessel.RepoUrl)
                ?? throw new InvalidOperationException("The selected vessel does not have a GitHub repository URL.");
            GitHubRepositoryReference reference = ParseRepositoryReference(repoUrl);

            return new GitHubRepositoryContext
            {
                Vessel = vessel,
                Owner = reference.Owner,
                Repository = reference.Repository,
                ApiBaseUrl = reference.ApiBaseUrl,
                Token = ResolveToken(vessel)
            };
        }

        private string ResolveToken(Vessel vessel)
        {
            string? tokenValue = _Settings.ResolveGitHubToken(vessel);
            if (String.IsNullOrWhiteSpace(tokenValue))
                throw new InvalidOperationException("No GitHub token is configured for this vessel or server.");
            return tokenValue.Trim();
        }

        private async Task<Vessel?> ReadAccessibleVesselAsync(AuthContext auth, string vesselId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Vessels.ReadAsync(auth.TenantId!, vesselId, token).ConfigureAwait(false);
            return await _Database.Vessels.ReadAsync(auth.TenantId!, auth.UserId!, vesselId, token).ConfigureAwait(false);
        }

        private async Task<Mission?> ReadAccessibleMissionAsync(AuthContext auth, string missionId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Missions.ReadAsync(auth.TenantId!, missionId, token).ConfigureAwait(false);
            return await _Database.Missions.ReadAsync(auth.TenantId!, auth.UserId!, missionId).ConfigureAwait(false);
        }

        private async Task<CheckRun?> ReadExistingImportedRunAsync(
            AuthContext auth,
            string vesselId,
            string externalId,
            CancellationToken token)
        {
            CheckRunQuery query = new CheckRunQuery
            {
                VesselId = vesselId,
                Source = CheckRunSourceEnum.External,
                ProviderName = GitHubActionsProviderName,
                ExternalId = externalId,
                PageNumber = 1,
                PageSize = 5
            };
            ApplyCheckRunScope(auth, query);
            EnumerationResult<CheckRun> results = await _Database.CheckRuns.EnumerateAsync(query, token).ConfigureAwait(false);
            return results.Objects.FirstOrDefault();
        }

        private void ApplyCheckRunScope(AuthContext auth, CheckRunQuery query)
        {
            if (auth.IsAdmin)
                return;
            query.TenantId = auth.TenantId;
            if (!auth.IsTenantAdmin)
                query.UserId = auth.UserId;
        }

        private static GitHubRepositoryReference ParseRepositoryReference(string repoUrl)
        {
            if (Uri.TryCreate(repoUrl, UriKind.Absolute, out Uri? uri)
                && (String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 2)
                    throw new InvalidOperationException("The vessel repository URL does not contain an owner and repository name.");

                string owner = segments[0];
                string repository = TrimGitSuffix(segments[1]);
                return new GitHubRepositoryReference
                {
                    Owner = owner,
                    Repository = repository,
                    ApiBaseUrl = BuildApiBaseUrl(uri.Scheme, uri.Authority)
                };
            }

            if (repoUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            {
                int hostStart = repoUrl.IndexOf('@') + 1;
                int separator = repoUrl.IndexOf(':', hostStart);
                if (separator <= hostStart)
                    throw new InvalidOperationException("The vessel repository URL could not be parsed as a GitHub SSH URL.");

                string host = repoUrl.Substring(hostStart, separator - hostStart);
                string path = repoUrl.Substring(separator + 1).Trim('/');
                string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 2)
                    throw new InvalidOperationException("The vessel repository URL does not contain an owner and repository name.");

                return new GitHubRepositoryReference
                {
                    Owner = segments[0],
                    Repository = TrimGitSuffix(segments[1]),
                    ApiBaseUrl = BuildApiBaseUrl("https", host)
                };
            }

            throw new InvalidOperationException("The vessel repository URL is not a supported GitHub or GitHub Enterprise URL.");
        }

        private static GitHubPullRequestReference ParsePullRequestReference(string pullRequestUrl)
        {
            if (!Uri.TryCreate(pullRequestUrl, UriKind.Absolute, out Uri? uri)
                || (!String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("The mission pull-request URL is not a valid GitHub URL.");
            }

            string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 4 || !String.Equals(segments[2], "pull", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The mission pull-request URL is not a supported GitHub pull-request URL.");
            if (!Int32.TryParse(segments[3], out int number))
                throw new InvalidOperationException("The mission pull-request URL does not contain a valid pull-request number.");

            return new GitHubPullRequestReference
            {
                Owner = segments[0],
                Repository = TrimGitSuffix(segments[1]),
                Number = number,
                Url = pullRequestUrl,
                ApiBaseUrl = BuildApiBaseUrl(uri.Scheme, uri.Authority)
            };
        }

        private static string BuildApiBaseUrl(string scheme, string authority)
        {
            if (String.Equals(authority, "github.com", StringComparison.OrdinalIgnoreCase)
                || String.Equals(authority, "www.github.com", StringComparison.OrdinalIgnoreCase))
            {
                return "https://api.github.com";
            }

            if (String.Equals(authority, "api.github.com", StringComparison.OrdinalIgnoreCase))
                return "https://api.github.com";

            return scheme + "://" + authority + "/api/v3";
        }

        private async Task<T> GetAsync<T>(string url, string tokenValue, CancellationToken token)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);
            using HttpResponseMessage response = await _Client.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await BuildHttpErrorAsync(response).ConfigureAwait(false);

            T? value = await response.Content.ReadFromJsonAsync<T>(_JsonOptions, token).ConfigureAwait(false);
            if (value == null)
                throw new InvalidOperationException("GitHub returned an empty response for " + url + ".");
            return value;
        }

        private async Task<GitHubPage<T>> GetPageAsync<T>(string url, string tokenValue, CancellationToken token)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);
            using HttpResponseMessage response = await _Client.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await BuildHttpErrorAsync(response).ConfigureAwait(false);

            T? value = await response.Content.ReadFromJsonAsync<T>(_JsonOptions, token).ConfigureAwait(false);
            if (value == null)
                throw new InvalidOperationException("GitHub returned an empty response for " + url + ".");

            GitHubPage<T> page = new GitHubPage<T> { Value = value };
            if (response.Headers.TryGetValues("Link", out IEnumerable<string>? links))
                page.LastPage = ParseLastPage(String.Join(",", links));
            return page;
        }

        private static int? ParseLastPage(string linkHeader)
        {
            foreach (string part in linkHeader.Split(','))
            {
                if (part.IndexOf("rel=\"last\"", StringComparison.OrdinalIgnoreCase) < 0) continue;
                System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(part, "[?&]page=(\\d+)");
                if (match.Success && Int32.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int last)) return last;
            }

            return null;
        }

        private async Task<Exception> BuildHttpErrorAsync(HttpResponseMessage response)
        {
            string body = String.Empty;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                body = String.Empty;
            }

            string? message = null;
            if (!String.IsNullOrWhiteSpace(body))
            {
                try
                {
                    GitHubErrorDto? parsed = JsonSerializer.Deserialize<GitHubErrorDto>(body, _JsonOptions);
                    message = Normalize(parsed?.Message);
                }
                catch
                {
                    message = null;
                }
            }

            message ??= "GitHub API request failed with status " + (int)response.StatusCode + ".";
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new InvalidOperationException("GitHub resource not found. " + message);
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                return new InvalidOperationException("GitHub rejected the configured token. " + message);
            return new InvalidOperationException(message);
        }

        private static ObjectiveStatusEnum ResolveImportedObjectiveStatus(
            ObjectiveStatusEnum currentStatus,
            ObjectiveStatusEnum importedStatus)
        {
            if (importedStatus == ObjectiveStatusEnum.Completed
                || importedStatus == ObjectiveStatusEnum.Cancelled
                || importedStatus == ObjectiveStatusEnum.Released)
            {
                return importedStatus;
            }

            if ((int)currentStatus > (int)importedStatus)
                return currentStatus;
            return importedStatus;
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool ContainsIgnoreCase(string? value, string? fragment)
        {
            if (String.IsNullOrWhiteSpace(value) || String.IsNullOrWhiteSpace(fragment))
                return false;
            return value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime? ToUtcDateTime(DateTimeOffset? value)
        {
            if (!value.HasValue)
                return null;
            return value.Value.UtcDateTime;
        }

        private static List<string> MergeDistinct(IEnumerable<string>? existing, IEnumerable<string?>? incoming)
        {
            List<string> merged = new List<string>();
            if (existing != null)
            {
                foreach (string value in existing)
                {
                    string? normalized = Normalize(value);
                    if (!String.IsNullOrWhiteSpace(normalized) && !merged.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                        merged.Add(normalized);
                }
            }

            if (incoming != null)
            {
                foreach (string? value in incoming)
                {
                    string? normalized = Normalize(value);
                    if (!String.IsNullOrWhiteSpace(normalized) && !merged.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                        merged.Add(normalized);
                }
            }

            return merged;
        }

        private static void AddIfMissing(List<string> values, string? value)
        {
            string? normalized = Normalize(value);
            if (String.IsNullOrWhiteSpace(normalized))
                return;
            if (!values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                values.Add(normalized);
        }

        private static string TrimGitSuffix(string repository)
        {
            string normalized = repository.Trim();
            if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);
            return normalized;
        }

        /// <summary>
        /// Infer a check-run type from a GitHub workflow name when the caller did not supply a structured type
        /// (the import request's TypeOverride). GitHub workflow names are free text with no type field, so the name is
        /// split into lowercase words on any non-alphanumeric character and matched word by word; a substring of a
        /// longer word never counts ("upload" is not "load", "community" is not "unit", "latest" is not "test").
        /// Unmatched names are <see cref="CheckRunTypeEnum.Build"/>.
        /// </summary>
        /// <param name="workflowName">Workflow name.</param>
        /// <returns>The inferred check-run type.</returns>
        public static CheckRunTypeEnum InferCheckRunType(string? workflowName)
        {
            List<string> words = SplitWorkflowWords(workflowName);
            HashSet<string> set = new HashSet<string>(words, StringComparer.Ordinal);

            bool rollback = set.Contains("rollback") || set.Contains("rollbacks");
            bool verify = set.Contains("verify") || set.Contains("verification") || set.Contains("verifications");
            bool deploy = set.Contains("deploy") || set.Contains("deploys") || set.Contains("deployment") || set.Contains("deployments");

            if (rollback && verify) return CheckRunTypeEnum.RollbackVerification;
            if (rollback) return CheckRunTypeEnum.Rollback;
            if (verify) return CheckRunTypeEnum.DeploymentVerification;
            if (set.Contains("smoke")) return CheckRunTypeEnum.SmokeTest;
            if (set.Contains("health") || set.Contains("healthcheck") || set.Contains("healthchecks")) return CheckRunTypeEnum.HealthCheck;
            if (deploy) return CheckRunTypeEnum.Deploy;
            if (set.Contains("integration")) return CheckRunTypeEnum.IntegrationTest;
            if (set.Contains("e2e") || ContainsSequence(words, "end", "to", "end")) return CheckRunTypeEnum.E2ETest;
            if (set.Contains("unit") || set.Contains("test") || set.Contains("tests") || set.Contains("testing")) return CheckRunTypeEnum.UnitTest;
            if (set.Contains("release") || set.Contains("releases")) return CheckRunTypeEnum.ReleaseVersioning;
            if (set.Contains("security") || set.Contains("scan") || set.Contains("scanning") || set.Contains("codeql")) return CheckRunTypeEnum.SecurityScan;
            if (set.Contains("performance") || set.Contains("perf") || set.Contains("load") || set.Contains("benchmark") || set.Contains("benchmarks")) return CheckRunTypeEnum.Performance;
            if (set.Contains("migrate") || set.Contains("migration") || set.Contains("migrations")) return CheckRunTypeEnum.Migration;
            return CheckRunTypeEnum.Build;
        }

        private static List<string> SplitWorkflowWords(string? workflowName)
        {
            List<string> words = new List<string>();
            if (String.IsNullOrWhiteSpace(workflowName)) return words;

            StringBuilder current = new StringBuilder();
            foreach (char c in workflowName)
            {
                if (Char.IsLetterOrDigit(c))
                {
                    current.Append(Char.ToLowerInvariant(c));
                }
                else if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }

            if (current.Length > 0) words.Add(current.ToString());
            return words;
        }

        private static bool ContainsSequence(List<string> words, params string[] sequence)
        {
            for (int i = 0; i + sequence.Length <= words.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < sequence.Length; j++)
                {
                    if (!String.Equals(words[i + j], sequence[j], StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return true;
            }

            return false;
        }

        private static CheckRunStatusEnum MapCheckRunStatus(string? status, string? conclusion)
        {
            string normalizedStatus = Normalize(status)?.ToLowerInvariant() ?? String.Empty;
            string normalizedConclusion = Normalize(conclusion)?.ToLowerInvariant() ?? String.Empty;

            if (!String.Equals(normalizedStatus, "completed", StringComparison.Ordinal))
            {
                if (String.Equals(normalizedStatus, "queued", StringComparison.Ordinal)
                    || String.Equals(normalizedStatus, "in_progress", StringComparison.Ordinal)
                    || String.Equals(normalizedStatus, "requested", StringComparison.Ordinal)
                    || String.Equals(normalizedStatus, "waiting", StringComparison.Ordinal)
                    || String.Equals(normalizedStatus, "pending", StringComparison.Ordinal))
                {
                    return CheckRunStatusEnum.Running;
                }

                return CheckRunStatusEnum.Pending;
            }

            if (String.Equals(normalizedConclusion, "success", StringComparison.Ordinal)
                || String.Equals(normalizedConclusion, "neutral", StringComparison.Ordinal)
                || String.Equals(normalizedConclusion, "skipped", StringComparison.Ordinal))
            {
                return CheckRunStatusEnum.Passed;
            }

            if (String.Equals(normalizedConclusion, "cancelled", StringComparison.Ordinal))
                return CheckRunStatusEnum.Canceled;

            return CheckRunStatusEnum.Failed;
        }

        private static int? InferExitCode(string? status, string? conclusion)
        {
            CheckRunStatusEnum mapped = MapCheckRunStatus(status, conclusion);
            if (mapped == CheckRunStatusEnum.Passed)
                return 0;
            if (mapped == CheckRunStatusEnum.Failed)
                return 1;
            if (mapped == CheckRunStatusEnum.Canceled)
                return 130;
            return null;
        }

        private static long? ComputeDurationMs(DateTimeOffset? started, DateTimeOffset? ended)
        {
            if (!started.HasValue || !ended.HasValue)
                return null;
            if (ended.Value < started.Value)
                return null;
            return Convert.ToInt64(Math.Round((ended.Value - started.Value).TotalMilliseconds));
        }

        private static string BuildWorkflowRunSummary(GitHubWorkflowRunDto workflowRun)
        {
            string label = Normalize(workflowRun.Name)
                ?? Normalize(workflowRun.DisplayTitle)
                ?? "GitHub Actions Run";
            string status = Normalize(workflowRun.Status) ?? "unknown";
            string? conclusion = Normalize(workflowRun.Conclusion);
            if (!String.IsNullOrWhiteSpace(conclusion))
                return label + " completed with " + conclusion + ".";
            return label + " is currently " + status + ".";
        }

        private static string ComputeReviewStatus(List<GitHubPullRequestReview> reviews, int requestedReviewerCount)
        {
            if (reviews.Any(review => String.Equals(review.State, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)))
                return "ChangesRequested";
            if (reviews.Any(review => String.Equals(review.State, "APPROVED", StringComparison.OrdinalIgnoreCase)))
                return "Approved";
            if (requestedReviewerCount > 0)
                return "PendingReview";
            if (reviews.Any(review => String.Equals(review.State, "COMMENTED", StringComparison.OrdinalIgnoreCase)))
                return "Commented";
            return "NoReview";
        }

        private sealed class GitHubSourcePage
        {
            public string Token { get; set; } = String.Empty;
            public string RepositoryApiUrl { get; set; } = String.Empty;
            public string PullRequestApiUrl { get; set; } = String.Empty;
            public string IssueApiUrl { get; set; } = String.Empty;
            public EnumerationQuery Query { get; set; } = new EnumerationQuery();
        }

        private sealed class GitHubPage<T>
        {
            public T Value { get; set; } = default!;
            public int? LastPage { get; set; } = null;
        }

        private sealed class GitHubRepositoryContext
        {
            public Vessel Vessel { get; set; } = null!;
            public string Owner { get; set; } = String.Empty;
            public string Repository { get; set; } = String.Empty;
            public string ApiBaseUrl { get; set; } = String.Empty;
            public string Token { get; set; } = String.Empty;
        }

        private sealed class GitHubRepositoryReference
        {
            public string Owner { get; set; } = String.Empty;
            public string Repository { get; set; } = String.Empty;
            public string ApiBaseUrl { get; set; } = String.Empty;
        }

        private sealed class GitHubPullRequestReference
        {
            public string Owner { get; set; } = String.Empty;
            public string Repository { get; set; } = String.Empty;
            public int Number { get; set; } = 0;
            public string Url { get; set; } = String.Empty;
            public string ApiBaseUrl { get; set; } = String.Empty;
        }

        private sealed class GitHubImportedObjectiveData
        {
            public string Title { get; set; } = String.Empty;
            public string? Description { get; set; } = null;
            public string? Owner { get; set; } = null;
            public List<string> Tags { get; set; } = new List<string>();
            public string SourceType { get; set; } = String.Empty;
            public string SourceId { get; set; } = String.Empty;
            public string SourceUrl { get; set; } = String.Empty;
            public DateTime? SourceUpdatedUtc { get; set; } = null;
            public ObjectiveStatusEnum Status { get; set; } = ObjectiveStatusEnum.Draft;
        }

        private sealed class GitHubErrorDto
        {
            [JsonPropertyName("message")]
            public string? Message { get; set; } = null;
        }

        private sealed class GitHubIssueDto
        {
            [JsonPropertyName("number")]
            public int Number { get; set; } = 0;

            [JsonPropertyName("title")]
            public string? Title { get; set; } = null;

            [JsonPropertyName("body")]
            public string? Body { get; set; } = null;

            [JsonPropertyName("state")]
            public string? State { get; set; } = null;

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; } = null;

            [JsonPropertyName("updated_at")]
            public DateTimeOffset? UpdatedAt { get; set; } = null;

            [JsonPropertyName("labels")]
            public List<GitHubLabelDto> Labels { get; set; } = new List<GitHubLabelDto>();

            [JsonPropertyName("assignees")]
            public List<GitHubUserDto> Assignees { get; set; } = new List<GitHubUserDto>();
        }

        private sealed class GitHubPullRequestDto
        {
            [JsonPropertyName("number")]
            public int Number { get; set; } = 0;

            [JsonPropertyName("title")]
            public string? Title { get; set; } = null;

            [JsonPropertyName("body")]
            public string? Body { get; set; } = null;

            [JsonPropertyName("state")]
            public string? State { get; set; } = null;

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; } = null;

            [JsonPropertyName("draft")]
            public bool? Draft { get; set; } = null;

            [JsonPropertyName("merged")]
            public bool? Merged { get; set; } = null;

            [JsonPropertyName("mergeable_state")]
            public string? MergeableState { get; set; } = null;

            [JsonPropertyName("additions")]
            public int? Additions { get; set; } = null;

            [JsonPropertyName("deletions")]
            public int? Deletions { get; set; } = null;

            [JsonPropertyName("changed_files")]
            public int? ChangedFiles { get; set; } = null;

            [JsonPropertyName("commits")]
            public int? Commits { get; set; } = null;

            [JsonPropertyName("created_at")]
            public DateTimeOffset? CreatedAt { get; set; } = null;

            [JsonPropertyName("updated_at")]
            public DateTimeOffset? UpdatedAt { get; set; } = null;

            [JsonPropertyName("merged_at")]
            public DateTimeOffset? MergedAt { get; set; } = null;

            [JsonPropertyName("user")]
            public GitHubUserDto? User { get; set; } = null;

            [JsonPropertyName("merged_by")]
            public GitHubUserDto? MergedBy { get; set; } = null;

            [JsonPropertyName("base")]
            public GitHubPullRequestBranchDto? Base { get; set; } = null;

            [JsonPropertyName("head")]
            public GitHubPullRequestBranchDto? Head { get; set; } = null;

            [JsonPropertyName("requested_reviewers")]
            public List<GitHubUserDto> RequestedReviewers { get; set; } = new List<GitHubUserDto>();

            [JsonPropertyName("labels")]
            public List<GitHubLabelDto> Labels { get; set; } = new List<GitHubLabelDto>();
        }

        private sealed class GitHubPullRequestBranchDto
        {
            [JsonPropertyName("ref")]
            public string? Ref { get; set; } = null;

            [JsonPropertyName("sha")]
            public string? Sha { get; set; } = null;
        }

        private sealed class GitHubLabelDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; } = null;
        }

        private sealed class GitHubUserDto
        {
            [JsonPropertyName("login")]
            public string? Login { get; set; } = null;
        }

        private sealed class GitHubWorkflowRunPage
        {
            public List<GitHubWorkflowRunDto> Runs { get; set; } = new List<GitHubWorkflowRunDto>();

            public int PageNumber { get; set; } = 1;

            public int PageSize { get; set; } = 20;

            public long TotalCount { get; set; } = 0;

            public int FetchedCount { get; set; } = 0;
        }

        private sealed class GitHubWorkflowRunsEnvelopeDto
        {
            [JsonPropertyName("total_count")]
            public long TotalCount { get; set; } = 0;

            [JsonPropertyName("workflow_runs")]
            public List<GitHubWorkflowRunDto> WorkflowRuns { get; set; } = new List<GitHubWorkflowRunDto>();
        }

        private sealed class GitHubWorkflowRunDto
        {
            [JsonPropertyName("id")]
            public long Id { get; set; } = 0;

            [JsonPropertyName("name")]
            public string? Name { get; set; } = null;

            [JsonPropertyName("display_title")]
            public string? DisplayTitle { get; set; } = null;

            [JsonPropertyName("status")]
            public string? Status { get; set; } = null;

            [JsonPropertyName("conclusion")]
            public string? Conclusion { get; set; } = null;

            [JsonPropertyName("event")]
            public string? Event { get; set; } = null;

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; } = null;

            [JsonPropertyName("head_branch")]
            public string? HeadBranch { get; set; } = null;

            [JsonPropertyName("head_sha")]
            public string? HeadSha { get; set; } = null;

            [JsonPropertyName("created_at")]
            public DateTimeOffset? CreatedAt { get; set; } = null;

            [JsonPropertyName("run_started_at")]
            public DateTimeOffset? RunStartedAt { get; set; } = null;

            [JsonPropertyName("updated_at")]
            public DateTimeOffset? UpdatedAt { get; set; } = null;
        }

        private sealed class GitHubPullRequestReviewDto
        {
            [JsonPropertyName("state")]
            public string? State { get; set; } = null;

            [JsonPropertyName("body")]
            public string? Body { get; set; } = null;

            [JsonPropertyName("submitted_at")]
            public DateTimeOffset? SubmittedAt { get; set; } = null;

            [JsonPropertyName("user")]
            public GitHubUserDto? User { get; set; } = null;
        }

        private sealed class GitHubIssueCommentDto
        {
            [JsonPropertyName("body")]
            public string? Body { get; set; } = null;

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; } = null;

            [JsonPropertyName("created_at")]
            public DateTimeOffset? CreatedAt { get; set; } = null;

            [JsonPropertyName("user")]
            public GitHubUserDto? User { get; set; } = null;
        }

        private sealed class GitHubCheckRunsEnvelopeDto
        {
            [JsonPropertyName("total_count")]
            public long TotalCount { get; set; } = 0;

            [JsonPropertyName("check_runs")]
            public List<GitHubCheckRunDto> CheckRuns { get; set; } = new List<GitHubCheckRunDto>();
        }

        private sealed class GitHubCheckRunDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; } = null;

            [JsonPropertyName("status")]
            public string? Status { get; set; } = null;

            [JsonPropertyName("conclusion")]
            public string? Conclusion { get; set; } = null;

            [JsonPropertyName("details_url")]
            public string? DetailsUrl { get; set; } = null;
        }
    }
}
