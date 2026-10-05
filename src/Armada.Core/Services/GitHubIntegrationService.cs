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
        private const string GitHubProviderName = "GitHub";
        private const string GitHubActionsProviderName = "GitHubActions";

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

            List<GitHubWorkflowRunDto> runs = await ReadWorkflowRunsAsync(repository, request, token).ConfigureAwait(false);
            GitHubActionsSyncResult result = new GitHubActionsSyncResult
            {
                VesselId = repository.Vessel.Id,
                DeploymentId = deployment?.Id
            };
            List<string> linkedCheckRunIds = new List<string>();

            foreach (GitHubWorkflowRunDto workflowRun in runs)
            {
                CheckRun? existing = await ReadExistingImportedRunAsync(auth, repository.Vessel.Id, Convert.ToString(workflowRun.Id, CultureInfo.InvariantCulture), token).ConfigureAwait(false);
                CheckRunImportRequest importRequest = BuildImportRequest(request, deployment, repository.Vessel, workflowRun);
                CheckRun imported = await _CheckRuns.ImportOrUpdateAsync(auth, importRequest, token).ConfigureAwait(false);
                result.CheckRuns.Add(imported);
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
        /// Retrieve normalized GitHub pull-request evidence for the missions linked to a release.
        /// </summary>
        public async Task<List<GitHubPullRequestDetail>> GetReleasePullRequestsAsync(
            AuthContext auth,
            Release release,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (String.IsNullOrWhiteSpace(release.VesselId))
                throw new InvalidOperationException("Release is not linked to a vessel.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, release.VesselId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Release vessel not found or not accessible.");
            string tokenValue = ResolveToken(vessel);
            List<GitHubPullRequestDetail> results = new List<GitHubPullRequestDetail>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string missionId in release.MissionIds)
            {
                Mission? mission = await ReadAccessibleMissionAsync(auth, missionId, token).ConfigureAwait(false);
                if (mission == null || String.IsNullOrWhiteSpace(mission.PrUrl))
                    continue;

                GitHubPullRequestReference reference = ParsePullRequestReference(mission.PrUrl);
                string key = reference.Repository + "#" + reference.Number.ToString(CultureInfo.InvariantCulture);
                if (!seen.Add(key))
                    continue;

                GitHubPullRequestDetail detail = await ReadPullRequestDetailAsync(reference, tokenValue, mission.Id, token).ConfigureAwait(false);
                results.Add(detail);
            }

            return results
                .OrderByDescending(item => item.UpdatedUtc ?? DateTime.MinValue)
                .ThenByDescending(item => item.Number)
                .ToList();
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

        private async Task<List<GitHubWorkflowRunDto>> ReadWorkflowRunsAsync(
            GitHubRepositoryContext repository,
            GitHubActionsSyncRequest request,
            CancellationToken token)
        {
            int runCount = Math.Clamp(request.RunCount, 1, 100);
            List<string> queryParts = new List<string>
            {
                "per_page=" + runCount.ToString(CultureInfo.InvariantCulture)
            };
            if (!String.IsNullOrWhiteSpace(request.BranchName))
                queryParts.Add("branch=" + Uri.EscapeDataString(request.BranchName.Trim()));
            if (!String.IsNullOrWhiteSpace(request.RunStatus))
                queryParts.Add("status=" + Uri.EscapeDataString(request.RunStatus.Trim()));
            if (!String.IsNullOrWhiteSpace(request.CommitHash))
                queryParts.Add("head_sha=" + Uri.EscapeDataString(request.CommitHash.Trim()));

            string url = repository.ApiBaseUrl + "/repos/" + Uri.EscapeDataString(repository.Owner) + "/" + Uri.EscapeDataString(repository.Repository) + "/actions/runs?" + String.Join("&", queryParts);
            GitHubWorkflowRunsEnvelopeDto response = await GetAsync<GitHubWorkflowRunsEnvelopeDto>(url, repository.Token, token).ConfigureAwait(false);

            IEnumerable<GitHubWorkflowRunDto> filtered = response.WorkflowRuns ?? new List<GitHubWorkflowRunDto>();
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

            return filtered.ToList();
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
            GitHubPullRequestDto pullRequest = await GetAsync<GitHubPullRequestDto>(
                reference.ApiBaseUrl + "/repos/" + repoSegment + "/pulls/" + reference.Number.ToString(CultureInfo.InvariantCulture),
                tokenValue,
                token).ConfigureAwait(false);

            List<GitHubPullRequestReviewDto> reviews = await GetAsync<List<GitHubPullRequestReviewDto>>(
                reference.ApiBaseUrl + "/repos/" + repoSegment + "/pulls/" + reference.Number.ToString(CultureInfo.InvariantCulture) + "/reviews",
                tokenValue,
                token).ConfigureAwait(false);
            List<GitHubIssueCommentDto> comments = await GetAsync<List<GitHubIssueCommentDto>>(
                reference.ApiBaseUrl + "/repos/" + repoSegment + "/issues/" + reference.Number.ToString(CultureInfo.InvariantCulture) + "/comments",
                tokenValue,
                token).ConfigureAwait(false);

            List<GitHubCheckRunDto> checkRuns = new List<GitHubCheckRunDto>();
            if (!String.IsNullOrWhiteSpace(pullRequest.Head?.Sha))
            {
                try
                {
                    GitHubCheckRunsEnvelopeDto checksEnvelope = await GetAsync<GitHubCheckRunsEnvelopeDto>(
                        reference.ApiBaseUrl + "/repos/" + repoSegment + "/commits/" + Uri.EscapeDataString(pullRequest.Head.Sha) + "/check-runs",
                        tokenValue,
                        token).ConfigureAwait(false);
                    checkRuns = checksEnvelope.CheckRuns ?? new List<GitHubCheckRunDto>();
                }
                catch (InvalidOperationException)
                {
                    checkRuns = new List<GitHubCheckRunDto>();
                }
            }

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
                RequestedReviewers = pullRequest.RequestedReviewers
                    .Select(reviewer => Normalize(reviewer.Login))
                    .Where(login => !String.IsNullOrWhiteSpace(login))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Labels = pullRequest.Labels
                    .Select(label => Normalize(label.Name))
                    .Where(name => !String.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ChangedFiles = pullRequest.ChangedFiles ?? 0,
                Additions = pullRequest.Additions ?? 0,
                Deletions = pullRequest.Deletions ?? 0,
                CommitCount = pullRequest.Commits ?? 0,
                Reviews = reviews
                    .Select(review => new GitHubPullRequestReview
                    {
                        ReviewerLogin = Normalize(review.User?.Login),
                        State = Normalize(review.State) ?? String.Empty,
                        Body = Normalize(review.Body),
                        SubmittedUtc = ToUtcDateTime(review.SubmittedAt)
                    })
                    .OrderByDescending(review => review.SubmittedUtc ?? DateTime.MinValue)
                    .ToList(),
                Comments = comments
                    .Select(comment => new GitHubPullRequestComment
                    {
                        AuthorLogin = Normalize(comment.User?.Login),
                        Body = Normalize(comment.Body),
                        Url = Normalize(comment.HtmlUrl),
                        CreatedUtc = ToUtcDateTime(comment.CreatedAt)
                    })
                    .OrderByDescending(comment => comment.CreatedUtc ?? DateTime.MinValue)
                    .ToList(),
                Checks = checkRuns
                    .Select(check => new GitHubPullRequestCheck
                    {
                        Name = Normalize(check.Name) ?? String.Empty,
                        Status = Normalize(check.Status) ?? String.Empty,
                        Conclusion = Normalize(check.Conclusion),
                        DetailsUrl = Normalize(check.DetailsUrl)
                    })
                    .ToList(),
                CreatedUtc = ToUtcDateTime(pullRequest.CreatedAt),
                UpdatedUtc = ToUtcDateTime(pullRequest.UpdatedAt),
                MergedUtc = ToUtcDateTime(pullRequest.MergedAt)
            };
            detail.ReviewStatus = ComputeReviewStatus(detail.Reviews, detail.RequestedReviewers);
            return detail;
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

        private static CheckRunTypeEnum InferCheckRunType(string? workflowName)
        {
            string normalized = Normalize(workflowName)?.ToLowerInvariant() ?? String.Empty;
            if (normalized.Contains("rollback verify", StringComparison.Ordinal))
                return CheckRunTypeEnum.RollbackVerification;
            if (normalized.Contains("rollback", StringComparison.Ordinal))
                return CheckRunTypeEnum.Rollback;
            if (normalized.Contains("deploy verify", StringComparison.Ordinal) || normalized.Contains("verification", StringComparison.Ordinal))
                return CheckRunTypeEnum.DeploymentVerification;
            if (normalized.Contains("smoke", StringComparison.Ordinal))
                return CheckRunTypeEnum.SmokeTest;
            if (normalized.Contains("health", StringComparison.Ordinal))
                return CheckRunTypeEnum.HealthCheck;
            if (normalized.Contains("deploy", StringComparison.Ordinal))
                return CheckRunTypeEnum.Deploy;
            if (normalized.Contains("integration", StringComparison.Ordinal))
                return CheckRunTypeEnum.IntegrationTest;
            if (normalized.Contains("e2e", StringComparison.Ordinal) || normalized.Contains("end-to-end", StringComparison.Ordinal))
                return CheckRunTypeEnum.E2ETest;
            if (normalized.Contains("unit", StringComparison.Ordinal) || normalized.Contains("test", StringComparison.Ordinal))
                return CheckRunTypeEnum.UnitTest;
            if (normalized.Contains("release", StringComparison.Ordinal))
                return CheckRunTypeEnum.ReleaseVersioning;
            if (normalized.Contains("security", StringComparison.Ordinal) || normalized.Contains("scan", StringComparison.Ordinal))
                return CheckRunTypeEnum.SecurityScan;
            if (normalized.Contains("performance", StringComparison.Ordinal) || normalized.Contains("load", StringComparison.Ordinal) || normalized.Contains("benchmark", StringComparison.Ordinal))
                return CheckRunTypeEnum.Performance;
            if (normalized.Contains("migrate", StringComparison.Ordinal) || normalized.Contains("migration", StringComparison.Ordinal))
                return CheckRunTypeEnum.Migration;
            return CheckRunTypeEnum.Build;
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

        private static string ComputeReviewStatus(List<GitHubPullRequestReview> reviews, List<string> requestedReviewers)
        {
            if (reviews.Any(review => String.Equals(review.State, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)))
                return "ChangesRequested";
            if (reviews.Any(review => String.Equals(review.State, "APPROVED", StringComparison.OrdinalIgnoreCase)))
                return "Approved";
            if (requestedReviewers.Count > 0)
                return "PendingReview";
            if (reviews.Any(review => String.Equals(review.State, "COMMENTED", StringComparison.OrdinalIgnoreCase)))
                return "Commented";
            return "NoReview";
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

        private sealed class GitHubWorkflowRunsEnvelopeDto
        {
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
