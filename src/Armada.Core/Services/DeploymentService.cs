namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Creates, updates, executes, verifies, and rolls back first-class deployments.
    /// </summary>
    public class DeploymentService
    {
        /// <summary>
        /// Optional callback invoked whenever a deployment changes.
        /// </summary>
        public Action<Deployment>? OnDeploymentChanged { get; set; }

        private readonly string _Header = "[DeploymentService] ";
        private readonly DatabaseDriver _Database;
        private readonly WorkflowProfileService _WorkflowProfiles;
        private readonly DeploymentEnvironmentService _Environments;
        private readonly CheckRunService _CheckRuns;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DeploymentService(
            DatabaseDriver database,
            WorkflowProfileService workflowProfiles,
            DeploymentEnvironmentService environments,
            CheckRunService checkRuns,
            LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _WorkflowProfiles = workflowProfiles ?? throw new ArgumentNullException(nameof(workflowProfiles));
            _Environments = environments ?? throw new ArgumentNullException(nameof(environments));
            _CheckRuns = checkRuns ?? throw new ArgumentNullException(nameof(checkRuns));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Enumerate deployments within the caller scope.
        /// </summary>
        public async Task<EnumerationResult<Deployment>> EnumerateAsync(
            AuthContext auth,
            DeploymentQuery query,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));

            DeploymentQuery scopedQuery = query ?? new DeploymentQuery();
            ApplyScope(auth, scopedQuery);
            return await _Database.Deployments.EnumerateAsync(scopedQuery, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one deployment within the caller scope.
        /// </summary>
        public async Task<Deployment?> ReadAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            return await _Database.Deployments.ReadAsync(id, BuildScopeQuery(auth), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a deployment and execute it immediately when approval is not required.
        /// </summary>
        public async Task<Deployment> CreateAsync(
            AuthContext auth,
            DeploymentUpsertRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));

            ResolvedDeploymentDraft draft = await ResolveDraftAsync(auth, null, request, token).ConfigureAwait(false);
            Deployment deployment = new Deployment
            {
                TenantId = draft.Vessel.TenantId,
                UserId = auth.UserId,
                VesselId = draft.Vessel.Id,
                WorkflowProfileId = draft.WorkflowProfile?.Id,
                EnvironmentId = draft.Environment.Id,
                EnvironmentName = draft.Environment.Name,
                ReleaseId = draft.Release?.Id,
                MissionId = draft.Mission?.Id,
                VoyageId = draft.Voyage?.Id,
                Title = draft.Title,
                SourceRef = draft.SourceRef,
                Summary = draft.Summary,
                Notes = draft.Notes,
                Status = draft.ApprovalRequired ? DeploymentStatusEnum.PendingApproval : DeploymentStatusEnum.Running,
                VerificationStatus = DeploymentVerificationStatusEnum.NotRun,
                ApprovalRequired = draft.ApprovalRequired,
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };

            deployment = await _Database.Deployments.CreateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);

            bool autoExecute = request.AutoExecute ?? true;
            if (!deployment.ApprovalRequired && autoExecute)
            {
                deployment = await ExecuteAsync(auth, deployment, draft.WorkflowProfile, draft.Environment, token).ConfigureAwait(false);
            }

            _Logging.Info(_Header + "created deployment " + deployment.Id + " for vessel " + deployment.VesselId);
            return deployment;
        }

        /// <summary>
        /// Update deployment metadata.
        /// </summary>
        public async Task<Deployment> UpdateAsync(
            AuthContext auth,
            string id,
            DeploymentUpsertRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));

            Deployment existing = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Deployment not found.");

            if (existing.Status == DeploymentStatusEnum.Running || existing.Status == DeploymentStatusEnum.RollingBack)
                throw new InvalidOperationException("Deployments cannot be edited while they are executing.");

            ResolvedDeploymentDraft draft = await ResolveDraftAsync(auth, existing, request, token).ConfigureAwait(false);
            existing.VesselId = draft.Vessel.Id;
            existing.WorkflowProfileId = draft.WorkflowProfile?.Id;
            existing.EnvironmentId = draft.Environment.Id;
            existing.EnvironmentName = draft.Environment.Name;
            existing.ReleaseId = draft.Release?.Id;
            existing.MissionId = draft.Mission?.Id;
            existing.VoyageId = draft.Voyage?.Id;
            existing.Title = draft.Title;
            existing.SourceRef = draft.SourceRef;
            existing.Summary = draft.Summary;
            existing.Notes = draft.Notes;
            existing.ApprovalRequired = draft.ApprovalRequired;
            existing.LastUpdateUtc = DateTime.UtcNow;

            existing = await _Database.Deployments.UpdateAsync(existing, token).ConfigureAwait(false);
            EmitChanged(existing);
            return existing;
        }

        /// <summary>
        /// Approve and execute a pending deployment.
        /// </summary>
        public async Task<Deployment> ApproveAsync(
            AuthContext auth,
            string id,
            string? comment = null,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Deployment deployment = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Deployment not found.");
            if (deployment.Status != DeploymentStatusEnum.PendingApproval)
                throw new InvalidOperationException("Only pending deployments can be approved.");

            if (String.IsNullOrWhiteSpace(deployment.VesselId) || String.IsNullOrWhiteSpace(deployment.EnvironmentId))
                throw new InvalidOperationException("Deployment is missing required vessel or environment links.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, deployment.VesselId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Vessel not found or not accessible.");
            DeploymentEnvironment environment = await _Environments.ReadAsync(auth, deployment.EnvironmentId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Environment not found or not accessible.");
            WorkflowProfile? profile = await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, deployment.WorkflowProfileId, token).ConfigureAwait(false);

            deployment.ApprovedByUserId = auth.UserId;
            deployment.ApprovedUtc = DateTime.UtcNow;
            deployment.ApprovalComment = Normalize(comment);
            deployment.Status = DeploymentStatusEnum.Running;
            deployment.LastUpdateUtc = DateTime.UtcNow;
            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);

            return await ExecuteAsync(auth, deployment, profile, environment, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Deny a pending deployment.
        /// </summary>
        public async Task<Deployment> DenyAsync(
            AuthContext auth,
            string id,
            string? comment = null,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Deployment deployment = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Deployment not found.");
            if (deployment.Status != DeploymentStatusEnum.PendingApproval)
                throw new InvalidOperationException("Only pending deployments can be denied.");

            deployment.ApprovedByUserId = auth.UserId;
            deployment.ApprovedUtc = DateTime.UtcNow;
            deployment.ApprovalComment = Normalize(comment);
            deployment.Status = DeploymentStatusEnum.Denied;
            deployment.CompletedUtc = DateTime.UtcNow;
            deployment.LastUpdateUtc = DateTime.UtcNow;
            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);
            return deployment;
        }

        /// <summary>
        /// Re-run post-deploy verification for an existing deployment.
        /// </summary>
        public async Task<Deployment> VerifyAsync(
            AuthContext auth,
            string id,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Deployment deployment = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Deployment not found.");
            if (String.IsNullOrWhiteSpace(deployment.VesselId) || String.IsNullOrWhiteSpace(deployment.EnvironmentId))
                throw new InvalidOperationException("Deployment is missing required vessel or environment links.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, deployment.VesselId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Vessel not found or not accessible.");
            DeploymentEnvironment environment = await _Environments.ReadAsync(auth, deployment.EnvironmentId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Environment not found or not accessible.");
            WorkflowProfile? profile = await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, deployment.WorkflowProfileId, token).ConfigureAwait(false);

            deployment.VerificationStatus = DeploymentVerificationStatusEnum.Running;
            deployment.LastUpdateUtc = DateTime.UtcNow;
            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);

            return await RunPostDeployVerificationAsync(auth, deployment, profile, environment, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Execute a rollback for an existing deployment.
        /// </summary>
        public async Task<Deployment> RollbackAsync(
            AuthContext auth,
            string id,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Deployment deployment = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Deployment not found.");
            if (String.IsNullOrWhiteSpace(deployment.VesselId) || String.IsNullOrWhiteSpace(deployment.EnvironmentName))
                throw new InvalidOperationException("Deployment is missing required vessel or environment links.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, deployment.VesselId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Vessel not found or not accessible.");
            WorkflowProfile? profile = await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, deployment.WorkflowProfileId, token).ConfigureAwait(false);
            if (profile == null)
                throw new InvalidOperationException("No active workflow profile could be resolved for rollback.");

            if (String.IsNullOrWhiteSpace(_WorkflowProfiles.ResolveCommand(profile, CheckRunTypeEnum.Rollback, deployment.EnvironmentName)))
                throw new InvalidOperationException("No rollback command is configured for environment " + deployment.EnvironmentName + ".");

            deployment.Status = DeploymentStatusEnum.RollingBack;
            deployment.LastUpdateUtc = DateTime.UtcNow;
            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);

            CheckRun rollbackRun = await _CheckRuns.RunAsync(auth, new CheckRunRequest
            {
                VesselId = vessel.Id,
                WorkflowProfileId = profile.Id,
                MissionId = deployment.MissionId,
                VoyageId = deployment.VoyageId,
                DeploymentId = deployment.Id,
                Type = CheckRunTypeEnum.Rollback,
                EnvironmentName = deployment.EnvironmentName,
                Label = "Rollback " + deployment.EnvironmentName
            }, token).ConfigureAwait(false);
            AppendCheckRun(deployment, rollbackRun.Id);
            deployment.RollbackCheckRunId = rollbackRun.Id;

            if (rollbackRun.Status != CheckRunStatusEnum.Passed)
            {
                deployment.Status = DeploymentStatusEnum.Failed;
                deployment.Summary = rollbackRun.Summary ?? "Rollback failed.";
                deployment.CompletedUtc = DateTime.UtcNow;
                deployment.LastUpdateUtc = DateTime.UtcNow;
                deployment.RequestHistorySummary = await BuildRequestHistorySummaryAsync(auth, deployment.CreatedUtc, deployment.CompletedUtc.Value, token).ConfigureAwait(false);
                deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
                EmitChanged(deployment);
                return deployment;
            }

            string? rollbackVerificationCommand = _WorkflowProfiles.ResolveCommand(profile, CheckRunTypeEnum.RollbackVerification, deployment.EnvironmentName);
            if (!String.IsNullOrWhiteSpace(rollbackVerificationCommand))
            {
                CheckRun verificationRun = await _CheckRuns.RunAsync(auth, new CheckRunRequest
                {
                    VesselId = vessel.Id,
                    WorkflowProfileId = profile.Id,
                    MissionId = deployment.MissionId,
                    VoyageId = deployment.VoyageId,
                    DeploymentId = deployment.Id,
                    Type = CheckRunTypeEnum.RollbackVerification,
                    EnvironmentName = deployment.EnvironmentName,
                    Label = "Rollback Verify " + deployment.EnvironmentName
                }, token).ConfigureAwait(false);
                AppendCheckRun(deployment, verificationRun.Id);
                deployment.RollbackVerificationCheckRunId = verificationRun.Id;
                if (verificationRun.Status != CheckRunStatusEnum.Passed)
                {
                    deployment.Status = DeploymentStatusEnum.Failed;
                    deployment.VerificationStatus = DeploymentVerificationStatusEnum.Failed;
                    deployment.Summary = verificationRun.Summary ?? "Rollback verification failed.";
                    deployment.CompletedUtc = DateTime.UtcNow;
                    deployment.LastUpdateUtc = DateTime.UtcNow;
                    deployment.RequestHistorySummary = await BuildRequestHistorySummaryAsync(auth, deployment.CreatedUtc, deployment.CompletedUtc.Value, token).ConfigureAwait(false);
                    deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
                    EmitChanged(deployment);
                    return deployment;
                }
            }

            deployment.Status = DeploymentStatusEnum.RolledBack;
            deployment.VerificationStatus = deployment.RollbackVerificationCheckRunId != null
                ? DeploymentVerificationStatusEnum.Passed
                : deployment.VerificationStatus;
            deployment.RolledBackUtc = DateTime.UtcNow;
            deployment.CompletedUtc = DateTime.UtcNow;
            deployment.LastUpdateUtc = DateTime.UtcNow;
            deployment.Summary = "Rollback completed successfully.";
            deployment.RequestHistorySummary = await BuildRequestHistorySummaryAsync(auth, deployment.CreatedUtc, deployment.CompletedUtc.Value, token).ConfigureAwait(false);
            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);
            return deployment;
        }

        /// <summary>
        /// Delete one deployment.
        /// </summary>
        public async Task DeleteAsync(AuthContext auth, string id, CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Deployment? existing = await ReadAsync(auth, id, token).ConfigureAwait(false);
            if (existing == null)
                throw new InvalidOperationException("Deployment not found.");

            await _Database.Deployments.DeleteAsync(id, BuildScopeQuery(auth), token).ConfigureAwait(false);
            _Logging.Info(_Header + "deleted deployment " + id);
        }

        /// <summary>
        /// Append existing check runs to a deployment when external systems provide additional evidence.
        /// </summary>
        public async Task<Deployment> LinkCheckRunsAsync(
            AuthContext auth,
            string deploymentId,
            IEnumerable<string> checkRunIds,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(deploymentId)) throw new ArgumentNullException(nameof(deploymentId));
            if (checkRunIds == null) throw new ArgumentNullException(nameof(checkRunIds));

            Deployment deployment = await ReadAsync(auth, deploymentId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Deployment not found.");

            bool changed = false;
            foreach (string checkRunId in checkRunIds)
            {
                string? normalized = Normalize(checkRunId);
                if (String.IsNullOrWhiteSpace(normalized))
                    continue;

                AppendCheckRun(deployment, normalized);
                changed = true;
            }

            if (!changed)
                return deployment;

            deployment.LastUpdateUtc = DateTime.UtcNow;
            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);
            return deployment;
        }

        private async Task<Deployment> ExecuteAsync(
            AuthContext auth,
            Deployment deployment,
            WorkflowProfile? profile,
            DeploymentEnvironment environment,
            CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(deployment.VesselId))
                throw new InvalidOperationException("Deployment is missing a vessel link.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, deployment.VesselId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Vessel not found or not accessible.");
            WorkflowProfile? resolvedProfile = profile ?? await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, deployment.WorkflowProfileId, token).ConfigureAwait(false);
            if (resolvedProfile == null)
                throw new InvalidOperationException("No active workflow profile could be resolved for this deployment.");
            if (String.IsNullOrWhiteSpace(_WorkflowProfiles.ResolveCommand(resolvedProfile, CheckRunTypeEnum.Deploy, environment.Name)))
                throw new InvalidOperationException("No deploy command is configured for environment " + environment.Name + ".");

            deployment.WorkflowProfileId = resolvedProfile.Id;
            deployment.EnvironmentId = environment.Id;
            deployment.EnvironmentName = environment.Name;
            deployment.Status = DeploymentStatusEnum.Running;
            deployment.VerificationStatus = DeploymentVerificationStatusEnum.NotRun;
            deployment.StartedUtc = deployment.StartedUtc ?? DateTime.UtcNow;
            deployment.CompletedUtc = null;
            deployment.LastUpdateUtc = DateTime.UtcNow;
            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);

            CheckRun deployRun = await _CheckRuns.RunAsync(auth, new CheckRunRequest
            {
                VesselId = vessel.Id,
                WorkflowProfileId = resolvedProfile.Id,
                MissionId = deployment.MissionId,
                VoyageId = deployment.VoyageId,
                DeploymentId = deployment.Id,
                Type = CheckRunTypeEnum.Deploy,
                EnvironmentName = environment.Name,
                Label = "Deploy " + environment.Name,
                BranchName = deployment.SourceRef
            }, token).ConfigureAwait(false);
            AppendCheckRun(deployment, deployRun.Id);
            deployment.DeployCheckRunId = deployRun.Id;

            if (deployRun.Status != CheckRunStatusEnum.Passed)
            {
                deployment.Status = DeploymentStatusEnum.Failed;
                deployment.Summary = deployRun.Summary ?? "Deployment failed.";
                deployment.CompletedUtc = DateTime.UtcNow;
                deployment.LastUpdateUtc = DateTime.UtcNow;
                deployment.RequestHistorySummary = await BuildRequestHistorySummaryAsync(auth, deployment.StartedUtc ?? deployment.CreatedUtc, deployment.CompletedUtc.Value, token).ConfigureAwait(false);
                deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
                EmitChanged(deployment);
                return deployment;
            }

            deployment.Summary = deployRun.Summary ?? "Deployment command completed.";
            return await RunPostDeployVerificationAsync(auth, deployment, resolvedProfile, environment, token).ConfigureAwait(false);
        }

        private async Task<Deployment> RunPostDeployVerificationAsync(
            AuthContext auth,
            Deployment deployment,
            WorkflowProfile? profile,
            DeploymentEnvironment environment,
            CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(deployment.VesselId))
                throw new InvalidOperationException("Deployment is missing a vessel link.");

            Vessel vessel = await ReadAccessibleVesselAsync(auth, deployment.VesselId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Vessel not found or not accessible.");
            WorkflowProfile? resolvedProfile = profile ?? await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, deployment.WorkflowProfileId, token).ConfigureAwait(false);

            List<CheckRun> verificationRuns = new List<CheckRun>();
            bool anyVerificationConfigured = false;

            string? smokeCommand = resolvedProfile != null
                ? _WorkflowProfiles.ResolveCommand(resolvedProfile, CheckRunTypeEnum.SmokeTest, environment.Name)
                : null;
            if (!String.IsNullOrWhiteSpace(smokeCommand))
            {
                anyVerificationConfigured = true;
                CheckRun smokeRun = await _CheckRuns.RunAsync(auth, new CheckRunRequest
                {
                    VesselId = vessel.Id,
                    WorkflowProfileId = resolvedProfile!.Id,
                    MissionId = deployment.MissionId,
                    VoyageId = deployment.VoyageId,
                    DeploymentId = deployment.Id,
                    Type = CheckRunTypeEnum.SmokeTest,
                    EnvironmentName = environment.Name,
                    Label = "Smoke " + environment.Name
                }, token).ConfigureAwait(false);
                verificationRuns.Add(smokeRun);
                AppendCheckRun(deployment, smokeRun.Id);
                deployment.SmokeTestCheckRunId = smokeRun.Id;
            }

            string? baseUrl = Normalize(environment.BaseUrl);
            string? healthEndpoint = Normalize(environment.HealthEndpoint);
            if (!String.IsNullOrWhiteSpace(baseUrl) && !String.IsNullOrWhiteSpace(healthEndpoint))
            {
                anyVerificationConfigured = true;
                CheckRun healthRun = await ExecuteHealthCheckAsync(deployment, baseUrl!, healthEndpoint!, token).ConfigureAwait(false);
                verificationRuns.Add(healthRun);
                AppendCheckRun(deployment, healthRun.Id);
                deployment.HealthCheckRunId = healthRun.Id;
            }

            List<DeploymentVerificationDefinition> definitions = environment.VerificationDefinitions
                .Where(definition => definition.Active)
                .ToList();
            if (definitions.Count > 0)
            {
                anyVerificationConfigured = true;
                List<CheckRun> definitionRuns = await ExecuteVerificationDefinitionsAsync(
                    deployment,
                    environment,
                    definitions,
                    CheckRunTypeEnum.DeploymentVerification,
                    "Verify ",
                    token).ConfigureAwait(false);

                if (definitionRuns.Count > 0)
                {
                    verificationRuns.AddRange(definitionRuns);
                    deployment.DeploymentVerificationCheckRunId = definitionRuns[definitionRuns.Count - 1].Id;
                }
            }

            string? verificationCommand = resolvedProfile != null
                ? _WorkflowProfiles.ResolveCommand(resolvedProfile, CheckRunTypeEnum.DeploymentVerification, environment.Name)
                : null;
            if (!String.IsNullOrWhiteSpace(verificationCommand))
            {
                anyVerificationConfigured = true;
                CheckRun verifyRun = await _CheckRuns.RunAsync(auth, new CheckRunRequest
                {
                    VesselId = vessel.Id,
                    WorkflowProfileId = resolvedProfile!.Id,
                    MissionId = deployment.MissionId,
                    VoyageId = deployment.VoyageId,
                    DeploymentId = deployment.Id,
                    Type = CheckRunTypeEnum.DeploymentVerification,
                    EnvironmentName = environment.Name,
                    Label = "Deploy Verify " + environment.Name
                }, token).ConfigureAwait(false);
                verificationRuns.Add(verifyRun);
                AppendCheckRun(deployment, verifyRun.Id);
                deployment.DeploymentVerificationCheckRunId = verifyRun.Id;
            }

            DeploymentVerificationStatusEnum verificationStatus = DetermineVerificationStatus(verificationRuns, anyVerificationConfigured);
            deployment.VerificationStatus = verificationStatus;
            deployment.VerifiedUtc = verificationStatus == DeploymentVerificationStatusEnum.Passed ? DateTime.UtcNow : deployment.VerifiedUtc;
            deployment.CompletedUtc = DateTime.UtcNow;
            deployment.LastUpdateUtc = DateTime.UtcNow;
            deployment.RequestHistorySummary = await BuildRequestHistorySummaryAsync(auth, deployment.StartedUtc ?? deployment.CreatedUtc, deployment.CompletedUtc.Value, token).ConfigureAwait(false);

            if (verificationStatus == DeploymentVerificationStatusEnum.Failed)
            {
                deployment.Status = DeploymentStatusEnum.VerificationFailed;
                deployment.Summary = "Deployment completed but post-deploy verification failed.";
                deployment.MonitoringWindowEndsUtc = null;
                deployment.LastMonitoredUtc = null;
                deployment.LastRegressionAlertUtc = null;
                deployment.LatestMonitoringSummary = "Post-deploy verification failed.";
                deployment.MonitoringFailureCount = Math.Max(deployment.MonitoringFailureCount, 1);
            }
            else
            {
                deployment.Status = DeploymentStatusEnum.Succeeded;
                deployment.Summary = verificationStatus == DeploymentVerificationStatusEnum.Skipped
                    ? "Deployment completed. No post-deploy verification steps were configured."
                    : "Deployment and post-deploy verification completed successfully.";
                if (verificationStatus == DeploymentVerificationStatusEnum.Passed
                    && HasRolloutMonitoringConfigured(environment)
                    && HasMonitoringChecksConfigured(environment))
                {
                    deployment.MonitoringWindowEndsUtc = DateTime.UtcNow.AddMinutes(environment.RolloutMonitoringWindowMinutes);
                    deployment.LastMonitoredUtc = DateTime.UtcNow;
                    deployment.LastRegressionAlertUtc = null;
                    deployment.LatestMonitoringSummary = "Monitoring window opened after successful verification.";
                    deployment.MonitoringFailureCount = 0;
                }
                else
                {
                    deployment.MonitoringWindowEndsUtc = null;
                    deployment.LastMonitoredUtc = null;
                    deployment.LastRegressionAlertUtc = null;
                    deployment.LatestMonitoringSummary = verificationStatus == DeploymentVerificationStatusEnum.Skipped
                        ? "No post-deploy verification or rollout monitoring steps were configured."
                        : "Deployment verification passed.";
                    deployment.MonitoringFailureCount = 0;
                }
            }

            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);
            return deployment;
        }

        private async Task<CheckRun> ExecuteHealthCheckAsync(
            Deployment deployment,
            string baseUrl,
            string healthEndpoint,
            CancellationToken token)
        {
            string url = BuildAbsoluteUrl(baseUrl, healthEndpoint);
            DateTime startedUtc = DateTime.UtcNow;

            using HttpClient client = new HttpClient();
            HttpResponseMessage response;
            string body;
            try
            {
                response = await client.GetAsync(url, token).ConfigureAwait(false);
                body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await _CheckRuns.RecordCompletedAsync(new CheckRun
                {
                    TenantId = deployment.TenantId,
                    UserId = deployment.UserId,
                    WorkflowProfileId = deployment.WorkflowProfileId,
                    VesselId = deployment.VesselId,
                    MissionId = deployment.MissionId,
                    VoyageId = deployment.VoyageId,
                    DeploymentId = deployment.Id,
                    Label = "Health " + deployment.EnvironmentName,
                    Type = CheckRunTypeEnum.HealthCheck,
                    Source = CheckRunSourceEnum.Armada,
                    Status = CheckRunStatusEnum.Failed,
                    EnvironmentName = deployment.EnvironmentName,
                    Command = "GET " + url,
                    Output = ex.ToString(),
                    Summary = "Health probe failed: " + ex.Message,
                    StartedUtc = startedUtc,
                    CompletedUtc = DateTime.UtcNow,
                    CreatedUtc = startedUtc,
                    LastUpdateUtc = DateTime.UtcNow,
                    DurationMs = Convert.ToInt64(Math.Round((DateTime.UtcNow - startedUtc).TotalMilliseconds)),
                    ExitCode = -1
                }, token).ConfigureAwait(false);
            }

            bool success = response.IsSuccessStatusCode;
            string output = "GET " + url + Environment.NewLine + Environment.NewLine
                + "HTTP " + ((int)response.StatusCode).ToString() + Environment.NewLine
                + body;

            return await _CheckRuns.RecordCompletedAsync(new CheckRun
            {
                TenantId = deployment.TenantId,
                UserId = deployment.UserId,
                WorkflowProfileId = deployment.WorkflowProfileId,
                VesselId = deployment.VesselId,
                MissionId = deployment.MissionId,
                VoyageId = deployment.VoyageId,
                DeploymentId = deployment.Id,
                Label = "Health " + deployment.EnvironmentName,
                Type = CheckRunTypeEnum.HealthCheck,
                Source = CheckRunSourceEnum.Armada,
                Status = success ? CheckRunStatusEnum.Passed : CheckRunStatusEnum.Failed,
                EnvironmentName = deployment.EnvironmentName,
                Command = "GET " + url,
                Output = output,
                Summary = success
                    ? "Health probe returned HTTP " + ((int)response.StatusCode).ToString() + "."
                    : "Health probe returned HTTP " + ((int)response.StatusCode).ToString() + ".",
                StartedUtc = startedUtc,
                CompletedUtc = DateTime.UtcNow,
                CreatedUtc = startedUtc,
                LastUpdateUtc = DateTime.UtcNow,
                DurationMs = Convert.ToInt64(Math.Round((DateTime.UtcNow - startedUtc).TotalMilliseconds)),
                ExitCode = success ? 0 : 1
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Run configured rollout-monitoring checks for deployments whose monitoring windows are still active.
        /// </summary>
        public async Task<int> MonitorRolloutWindowsAsync(CancellationToken token = default)
        {
            DateTime utcNow = DateTime.UtcNow;
            List<Deployment> deployments = await _Database.Deployments.EnumerateAllAsync(new DeploymentQuery
            {
                PageNumber = 1,
                PageSize = 1000
            }, token).ConfigureAwait(false);

            int monitoredCount = 0;
            foreach (Deployment deployment in deployments)
            {
                if (!ShouldMonitorDeployment(deployment, utcNow))
                    continue;
                if (String.IsNullOrWhiteSpace(deployment.EnvironmentId))
                    continue;

                AuthContext internalAuth = BuildInternalAuthContext(deployment.TenantId, deployment.UserId);
                DeploymentEnvironment? environment = await _Environments.ReadAsync(internalAuth, deployment.EnvironmentId, token).ConfigureAwait(false);
                if (environment == null || !environment.Active || !HasMonitoringChecksConfigured(environment))
                    continue;

                int monitoringIntervalSeconds = Math.Max(30, environment.RolloutMonitoringIntervalSeconds);
                if (deployment.LastMonitoredUtc.HasValue
                    && deployment.LastMonitoredUtc.Value.AddSeconds(monitoringIntervalSeconds) > utcNow)
                {
                    continue;
                }

                Deployment updated = await RunRolloutMonitoringPassAsync(deployment, environment, token).ConfigureAwait(false);
                monitoredCount += 1;

                if (updated.Status == DeploymentStatusEnum.VerificationFailed
                    && environment.AlertOnRegression
                    && (!updated.LastRegressionAlertUtc.HasValue
                        || updated.LastRegressionAlertUtc.Value < updated.LastMonitoredUtc))
                {
                    updated.LastRegressionAlertUtc = updated.LastMonitoredUtc;
                    updated.LastUpdateUtc = DateTime.UtcNow;
                    updated = await _Database.Deployments.UpdateAsync(updated, token).ConfigureAwait(false);
                    EmitChanged(updated);
                }
            }

            return monitoredCount;
        }

        private async Task<List<CheckRun>> ExecuteVerificationDefinitionsAsync(
            Deployment deployment,
            DeploymentEnvironment environment,
            List<DeploymentVerificationDefinition> definitions,
            CheckRunTypeEnum type,
            string labelPrefix,
            CancellationToken token)
        {
            List<CheckRun> runs = new List<CheckRun>();
            foreach (DeploymentVerificationDefinition definition in definitions)
            {
                CheckRun run = await ExecuteVerificationDefinitionAsync(
                    deployment,
                    environment,
                    definition,
                    type,
                    labelPrefix,
                    token).ConfigureAwait(false);
                runs.Add(run);
                AppendCheckRun(deployment, run.Id);
            }

            return runs;
        }

        private async Task<CheckRun> ExecuteVerificationDefinitionAsync(
            Deployment deployment,
            DeploymentEnvironment environment,
            DeploymentVerificationDefinition definition,
            CheckRunTypeEnum type,
            string labelPrefix,
            CancellationToken token)
        {
            string baseUrl = Normalize(environment.BaseUrl) ?? String.Empty;
            string method = String.IsNullOrWhiteSpace(definition.Method)
                ? "GET"
                : definition.Method.Trim().ToUpperInvariant();
            DateTime startedUtc = DateTime.UtcNow;

            string url;
            if (TryParseHttpUrl(definition.Path, out Uri? absolute))
            {
                url = absolute!.ToString();
            }
            else if (!String.IsNullOrWhiteSpace(baseUrl))
            {
                url = BuildAbsoluteUrl(baseUrl, definition.Path);
            }
            else
            {
                return await _CheckRuns.RecordCompletedAsync(new CheckRun
                {
                    TenantId = deployment.TenantId,
                    UserId = deployment.UserId,
                    WorkflowProfileId = deployment.WorkflowProfileId,
                    VesselId = deployment.VesselId,
                    MissionId = deployment.MissionId,
                    VoyageId = deployment.VoyageId,
                    DeploymentId = deployment.Id,
                    Label = labelPrefix + definition.Name,
                    Type = type,
                    Source = CheckRunSourceEnum.Armada,
                    Status = CheckRunStatusEnum.Failed,
                    EnvironmentName = deployment.EnvironmentName,
                    Command = method + " " + definition.Path,
                    Output = "Verification definition uses a relative path but the environment base URL is empty.",
                    Summary = definition.Name + " could not be executed because the environment base URL is not configured.",
                    StartedUtc = startedUtc,
                    CompletedUtc = DateTime.UtcNow,
                    CreatedUtc = startedUtc,
                    LastUpdateUtc = DateTime.UtcNow,
                    DurationMs = Convert.ToInt64(Math.Round((DateTime.UtcNow - startedUtc).TotalMilliseconds)),
                    ExitCode = -1
                }, token).ConfigureAwait(false);
            }

            using HttpClient client = new HttpClient();
            using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), url);
            Dictionary<string, string> requestHeaders = definition.Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> header in requestHeaders)
            {
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    if (request.Content == null)
                        request.Content = new StringContent(String.Empty, Encoding.UTF8, "application/json");
                    request.Content.Headers.Remove(header.Key);
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (!String.IsNullOrWhiteSpace(definition.RequestBody))
            {
                string contentType = requestHeaders.TryGetValue("Content-Type", out string? headerValue) && !String.IsNullOrWhiteSpace(headerValue)
                    ? headerValue
                    : "application/json";
                request.Content = new StringContent(definition.RequestBody, Encoding.UTF8, contentType);
            }

            HttpResponseMessage response;
            string responseBody;
            try
            {
                response = await client.SendAsync(request, token).ConfigureAwait(false);
                responseBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await _CheckRuns.RecordCompletedAsync(new CheckRun
                {
                    TenantId = deployment.TenantId,
                    UserId = deployment.UserId,
                    WorkflowProfileId = deployment.WorkflowProfileId,
                    VesselId = deployment.VesselId,
                    MissionId = deployment.MissionId,
                    VoyageId = deployment.VoyageId,
                    DeploymentId = deployment.Id,
                    Label = labelPrefix + definition.Name,
                    Type = type,
                    Source = CheckRunSourceEnum.Armada,
                    Status = CheckRunStatusEnum.Failed,
                    EnvironmentName = deployment.EnvironmentName,
                    Command = method + " " + url,
                    Output = ex.ToString(),
                    Summary = definition.Name + " failed: " + ex.Message,
                    StartedUtc = startedUtc,
                    CompletedUtc = DateTime.UtcNow,
                    CreatedUtc = startedUtc,
                    LastUpdateUtc = DateTime.UtcNow,
                    DurationMs = Convert.ToInt64(Math.Round((DateTime.UtcNow - startedUtc).TotalMilliseconds)),
                    ExitCode = -1
                }, token).ConfigureAwait(false);
            }

            bool statusMatched = !definition.ExpectedStatusCode.HasValue
                || (int)response.StatusCode == definition.ExpectedStatusCode.Value;
            bool bodyMatched = String.IsNullOrWhiteSpace(definition.MustContainText)
                || responseBody.IndexOf(definition.MustContainText, StringComparison.OrdinalIgnoreCase) >= 0;
            bool success = statusMatched && bodyMatched;

            StringBuilder outputBuilder = new StringBuilder();
            outputBuilder.AppendLine(method + " " + url);
            foreach (KeyValuePair<string, string> header in requestHeaders)
                outputBuilder.AppendLine(header.Key + ": " + header.Value);
            if (!String.IsNullOrWhiteSpace(definition.RequestBody))
            {
                outputBuilder.AppendLine();
                outputBuilder.AppendLine(definition.RequestBody);
            }
            outputBuilder.AppendLine();
            outputBuilder.AppendLine("HTTP " + ((int)response.StatusCode).ToString());
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
                outputBuilder.AppendLine(header.Key + ": " + String.Join(", ", header.Value));
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
                outputBuilder.AppendLine(header.Key + ": " + String.Join(", ", header.Value));
            outputBuilder.AppendLine();
            outputBuilder.Append(responseBody);

            string summary;
            if (!statusMatched && definition.ExpectedStatusCode.HasValue)
                summary = definition.Name + " expected HTTP " + definition.ExpectedStatusCode.Value + " but received HTTP " + ((int)response.StatusCode).ToString() + ".";
            else if (!bodyMatched)
                summary = definition.Name + " response did not contain required text.";
            else
                summary = definition.Name + " returned HTTP " + ((int)response.StatusCode).ToString() + ".";

            return await _CheckRuns.RecordCompletedAsync(new CheckRun
            {
                TenantId = deployment.TenantId,
                UserId = deployment.UserId,
                WorkflowProfileId = deployment.WorkflowProfileId,
                VesselId = deployment.VesselId,
                MissionId = deployment.MissionId,
                VoyageId = deployment.VoyageId,
                DeploymentId = deployment.Id,
                Label = labelPrefix + definition.Name,
                Type = type,
                Source = CheckRunSourceEnum.Armada,
                Status = success ? CheckRunStatusEnum.Passed : CheckRunStatusEnum.Failed,
                EnvironmentName = deployment.EnvironmentName,
                Command = method + " " + url,
                Output = outputBuilder.ToString(),
                Summary = summary,
                StartedUtc = startedUtc,
                CompletedUtc = DateTime.UtcNow,
                CreatedUtc = startedUtc,
                LastUpdateUtc = DateTime.UtcNow,
                DurationMs = Convert.ToInt64(Math.Round((DateTime.UtcNow - startedUtc).TotalMilliseconds)),
                ExitCode = success ? 0 : 1
            }, token).ConfigureAwait(false);
        }

        private async Task<Deployment> RunRolloutMonitoringPassAsync(
            Deployment deployment,
            DeploymentEnvironment environment,
            CancellationToken token)
        {
            List<CheckRun> verificationRuns = new List<CheckRun>();
            bool anyVerificationConfigured = false;

            List<DeploymentVerificationDefinition> definitions = environment.VerificationDefinitions
                .Where(definition => definition.Active)
                .ToList();
            if (definitions.Count > 0)
            {
                anyVerificationConfigured = true;
                List<CheckRun> definitionRuns = await ExecuteVerificationDefinitionsAsync(
                    deployment,
                    environment,
                    definitions,
                    CheckRunTypeEnum.DeploymentVerification,
                    "Monitor ",
                    token).ConfigureAwait(false);
                verificationRuns.AddRange(definitionRuns);
                if (definitionRuns.Count > 0)
                    deployment.DeploymentVerificationCheckRunId = definitionRuns[definitionRuns.Count - 1].Id;
            }

            string? baseUrl = Normalize(environment.BaseUrl);
            string? healthEndpoint = Normalize(environment.HealthEndpoint);
            if (!String.IsNullOrWhiteSpace(baseUrl) && !String.IsNullOrWhiteSpace(healthEndpoint))
            {
                anyVerificationConfigured = true;
                CheckRun healthRun = await ExecuteHealthCheckAsync(deployment, baseUrl!, healthEndpoint!, token).ConfigureAwait(false);
                verificationRuns.Add(healthRun);
                AppendCheckRun(deployment, healthRun.Id);
                deployment.HealthCheckRunId = healthRun.Id;
            }

            DateTime utcNow = DateTime.UtcNow;
            deployment.LastMonitoredUtc = utcNow;
            deployment.LastUpdateUtc = utcNow;

            DeploymentVerificationStatusEnum monitoringStatus = DetermineVerificationStatus(verificationRuns, anyVerificationConfigured);
            if (monitoringStatus == DeploymentVerificationStatusEnum.Failed)
            {
                deployment.Status = DeploymentStatusEnum.VerificationFailed;
                deployment.VerificationStatus = DeploymentVerificationStatusEnum.Failed;
                deployment.MonitoringFailureCount += 1;
                deployment.LatestMonitoringSummary = BuildMonitoringSummary(environment.Name, verificationRuns, false);
                deployment.Summary = "Rollout monitoring detected a regression.";
            }
            else
            {
                deployment.VerificationStatus = monitoringStatus == DeploymentVerificationStatusEnum.Skipped
                    ? deployment.VerificationStatus
                    : DeploymentVerificationStatusEnum.Passed;
                if (deployment.Status == DeploymentStatusEnum.VerificationFailed)
                    deployment.Status = DeploymentStatusEnum.Succeeded;
                deployment.MonitoringFailureCount = 0;
                deployment.LatestMonitoringSummary = BuildMonitoringSummary(environment.Name, verificationRuns, true);
                if (monitoringStatus == DeploymentVerificationStatusEnum.Passed)
                    deployment.VerifiedUtc = utcNow;
            }

            deployment = await _Database.Deployments.UpdateAsync(deployment, token).ConfigureAwait(false);
            EmitChanged(deployment);
            return deployment;
        }

        private async Task<RequestHistorySummaryResult> BuildRequestHistorySummaryAsync(
            AuthContext auth,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken token)
        {
            RequestHistoryQuery query = new RequestHistoryQuery
            {
                TenantId = auth.IsAdmin ? null : auth.TenantId,
                UserId = auth.IsAdmin || auth.IsTenantAdmin ? null : auth.UserId,
                FromUtc = fromUtc.ToUniversalTime(),
                ToUtc = toUtc.ToUniversalTime(),
                BucketMinutes = 5
            };

            List<RequestHistoryEntry> entries = await _Database.RequestHistory.EnumerateForSummaryAsync(query, token).ConfigureAwait(false);
            return RequestHistorySummaryBuilder.Build(entries, query);
        }

        private async Task<ResolvedDeploymentDraft> ResolveDraftAsync(
            AuthContext auth,
            Deployment? existing,
            DeploymentUpsertRequest request,
            CancellationToken token)
        {
            string? explicitReleaseId = Normalize(request.ReleaseId) ?? Normalize(existing?.ReleaseId);
            Release? release = null;
            if (!String.IsNullOrWhiteSpace(explicitReleaseId))
            {
                release = await ReadAccessibleReleaseAsync(auth, explicitReleaseId, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Release not found or not accessible.");
            }

            string? explicitMissionId = Normalize(request.MissionId) ?? Normalize(existing?.MissionId);
            Mission? mission = null;
            if (!String.IsNullOrWhiteSpace(explicitMissionId))
            {
                mission = await ReadAccessibleMissionAsync(auth, explicitMissionId, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Mission not found or not accessible.");
            }

            string? explicitVoyageId = Normalize(request.VoyageId) ?? Normalize(existing?.VoyageId);
            Voyage? voyage = null;
            if (!String.IsNullOrWhiteSpace(explicitVoyageId))
            {
                voyage = await ReadAccessibleVoyageAsync(auth, explicitVoyageId, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Voyage not found or not accessible.");
            }

            HashSet<string> candidateVesselIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidate(candidateVesselIds, Normalize(request.VesselId));
            AddCandidate(candidateVesselIds, Normalize(existing?.VesselId));
            AddCandidate(candidateVesselIds, release?.VesselId);
            AddCandidate(candidateVesselIds, mission?.VesselId);
            if (candidateVesselIds.Count == 0 && voyage != null)
            {
                List<Mission> voyageMissions = await ReadAccessibleMissionsForVoyageAsync(auth, voyage.Id, token).ConfigureAwait(false);
                foreach (Mission voyageMission in voyageMissions)
                    AddCandidate(candidateVesselIds, voyageMission.VesselId);
            }

            if (candidateVesselIds.Count == 0)
                throw new InvalidOperationException("A vessel must be supplied or inferable from the linked release, mission, or voyage.");
            if (candidateVesselIds.Count > 1)
                throw new InvalidOperationException("Linked records must all belong to a single vessel.");

            string resolvedVesselId = candidateVesselIds.First();
            Vessel vessel = await ReadAccessibleVesselAsync(auth, resolvedVesselId, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Vessel not found or not accessible.");

            string? explicitWorkflowProfileId = Normalize(request.WorkflowProfileId)
                ?? Normalize(existing?.WorkflowProfileId)
                ?? Normalize(release?.WorkflowProfileId);
            WorkflowProfile? workflowProfile = await _WorkflowProfiles.ResolveForVesselAsync(auth, vessel, explicitWorkflowProfileId, token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(explicitWorkflowProfileId) && workflowProfile == null)
                throw new InvalidOperationException("Workflow profile not found or not accessible.");

            DeploymentEnvironment environment = await ResolveEnvironmentAsync(
                auth,
                vessel,
                Normalize(request.EnvironmentId) ?? Normalize(existing?.EnvironmentId),
                Normalize(request.EnvironmentName) ?? Normalize(existing?.EnvironmentName),
                token).ConfigureAwait(false);

            string sourceRef = Normalize(request.SourceRef)
                ?? Normalize(existing?.SourceRef)
                ?? Normalize(release?.TagName)
                ?? Normalize(release?.Version)
                ?? Normalize(mission?.BranchName)
                ?? vessel.DefaultBranch;

            string title = Normalize(request.Title)
                ?? Normalize(existing?.Title)
                ?? BuildTitle(vessel, environment, release);
            string? notes = Normalize(request.Notes) ?? Normalize(existing?.Notes);
            string? summary = Normalize(request.Summary) ?? Normalize(existing?.Summary);

            return new ResolvedDeploymentDraft
            {
                Vessel = vessel,
                WorkflowProfile = workflowProfile,
                Environment = environment,
                Release = release,
                Mission = mission,
                Voyage = voyage,
                Title = title,
                SourceRef = sourceRef,
                Summary = summary,
                Notes = notes,
                ApprovalRequired = environment.RequiresApproval
            };
        }

        private async Task<DeploymentEnvironment> ResolveEnvironmentAsync(
            AuthContext auth,
            Vessel vessel,
            string? environmentId,
            string? environmentName,
            CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(environmentId))
            {
                DeploymentEnvironment? byId = await _Environments.ReadAsync(auth, environmentId, token).ConfigureAwait(false);
                if (byId == null)
                    throw new InvalidOperationException("Environment not found or not accessible.");
                if (!String.Equals(byId.VesselId, vessel.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("The selected environment does not belong to the resolved vessel.");
                return byId;
            }

            DeploymentEnvironmentQuery query = BuildEnvironmentScopeQuery(auth);
            query.VesselId = vessel.Id;
            query.PageNumber = 1;
            query.PageSize = 500;
            List<DeploymentEnvironment> matches = await _Database.Environments.EnumerateAllAsync(query, token).ConfigureAwait(false);

            if (!String.IsNullOrWhiteSpace(environmentName))
            {
                DeploymentEnvironment? named = matches.FirstOrDefault(item =>
                    String.Equals(item.Name, environmentName, StringComparison.OrdinalIgnoreCase));
                if (named == null)
                    throw new InvalidOperationException("Environment " + environmentName + " was not found for vessel " + vessel.Name + ".");
                return named;
            }

            DeploymentEnvironment? defaultEnvironment = matches.FirstOrDefault(item => item.IsDefault && item.Active);
            if (defaultEnvironment != null)
                return defaultEnvironment;

            DeploymentEnvironment? singleActive = matches.Where(item => item.Active).Take(2).Count() == 1
                ? matches.First(item => item.Active)
                : null;
            if (singleActive != null)
                return singleActive;

            throw new InvalidOperationException("No active default environment is available for vessel " + vessel.Name + ".");
        }

        private async Task<Vessel?> ReadAccessibleVesselAsync(AuthContext auth, string vesselId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Vessels.ReadAsync(auth.TenantId!, vesselId, token).ConfigureAwait(false);
            return await _Database.Vessels.ReadAsync(auth.TenantId!, auth.UserId!, vesselId, token).ConfigureAwait(false);
        }

        private async Task<Release?> ReadAccessibleReleaseAsync(AuthContext auth, string releaseId, CancellationToken token)
        {
            return await _Database.Releases.ReadAsync(releaseId, new ReleaseQuery
            {
                TenantId = auth.IsAdmin ? null : auth.TenantId,
                UserId = auth.IsAdmin || auth.IsTenantAdmin ? null : auth.UserId
            }, token).ConfigureAwait(false);
        }

        private async Task<Mission?> ReadAccessibleMissionAsync(AuthContext auth, string missionId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Missions.ReadAsync(auth.TenantId!, missionId, token).ConfigureAwait(false);
            return await _Database.Missions.ReadAsync(auth.TenantId!, auth.UserId!, missionId, token).ConfigureAwait(false);
        }

        private async Task<Voyage?> ReadAccessibleVoyageAsync(AuthContext auth, string voyageId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Voyages.ReadAsync(auth.TenantId!, voyageId, token).ConfigureAwait(false);
            return await _Database.Voyages.ReadAsync(auth.TenantId!, auth.UserId!, voyageId, token).ConfigureAwait(false);
        }

        private async Task<List<Mission>> ReadAccessibleMissionsForVoyageAsync(AuthContext auth, string voyageId, CancellationToken token)
        {
            if (auth.IsAdmin)
                return await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            if (auth.IsTenantAdmin)
                return await _Database.Missions.EnumerateByVoyageAsync(auth.TenantId!, voyageId, token).ConfigureAwait(false);

            List<Mission> tenantMissions = await _Database.Missions.EnumerateByVoyageAsync(auth.TenantId!, voyageId, token).ConfigureAwait(false);
            return tenantMissions
                .Where(mission => String.Equals(mission.UserId, auth.UserId, StringComparison.Ordinal))
                .ToList();
        }

        private static DeploymentQuery BuildScopeQuery(AuthContext auth)
        {
            DeploymentQuery query = new DeploymentQuery();
            ApplyScope(auth, query);
            return query;
        }

        private static DeploymentEnvironmentQuery BuildEnvironmentScopeQuery(AuthContext auth)
        {
            DeploymentEnvironmentQuery query = new DeploymentEnvironmentQuery();
            ApplyScope(auth, query);
            return query;
        }

        private static void ApplyScope(AuthContext auth, DeploymentQuery query)
        {
            if (auth.IsAdmin)
                return;

            query.TenantId = auth.TenantId;
            if (!auth.IsTenantAdmin)
                query.UserId = auth.UserId;
        }

        private static void ApplyScope(AuthContext auth, DeploymentEnvironmentQuery query)
        {
            if (auth.IsAdmin)
                return;

            query.TenantId = auth.TenantId;
            if (!auth.IsTenantAdmin)
                query.UserId = auth.UserId;
        }

        private void EmitChanged(Deployment deployment)
        {
            OnDeploymentChanged?.Invoke(deployment);
        }

        private static string BuildAbsoluteUrl(string baseUrl, string healthEndpoint)
        {
            if (TryParseHttpUrl(healthEndpoint, out Uri? absolute))
                return absolute!.ToString();
            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                baseUrl += "/";
            return new Uri(new Uri(baseUrl), healthEndpoint.TrimStart('/')).ToString();
        }

        /// <summary>
        /// Parse an absolute http(s) URL. A rooted path such as <c>/api/health</c> is not treated as absolute:
        /// on Linux and macOS <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> accepts it as a
        /// <c>file:///</c> URI, which would bypass the environment's base URL.
        /// </summary>
        private static bool TryParseHttpUrl(string? value, out Uri? uri)
        {
            uri = null;
            if (String.IsNullOrWhiteSpace(value)) return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)) return false;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
            uri = parsed;
            return true;
        }

        private static string BuildTitle(Vessel vessel, DeploymentEnvironment environment, Release? release)
        {
            if (release != null)
                return release.Title + " -> " + environment.Name;
            return vessel.Name + " -> " + environment.Name;
        }

        private static DeploymentVerificationStatusEnum DetermineVerificationStatus(List<CheckRun> runs, bool anyVerificationConfigured)
        {
            if (!anyVerificationConfigured || runs.Count == 0)
                return DeploymentVerificationStatusEnum.Skipped;

            bool anyFailed = runs.Any(run => run.Status != CheckRunStatusEnum.Passed);
            bool allPassed = runs.All(run => run.Status == CheckRunStatusEnum.Passed);
            if (allPassed)
                return DeploymentVerificationStatusEnum.Passed;
            if (anyFailed)
                return DeploymentVerificationStatusEnum.Failed;
            return DeploymentVerificationStatusEnum.Partial;
        }

        private static void AppendCheckRun(Deployment deployment, string? checkRunId)
        {
            if (deployment == null || String.IsNullOrWhiteSpace(checkRunId))
                return;
            if (!deployment.CheckRunIds.Contains(checkRunId, StringComparer.OrdinalIgnoreCase))
                deployment.CheckRunIds.Add(checkRunId);
        }

        private static void AddCandidate(HashSet<string> values, string? value)
        {
            if (!String.IsNullOrWhiteSpace(value))
                values.Add(value);
        }

        private static AuthContext BuildInternalAuthContext(string? tenantId, string? userId)
        {
            if (!String.IsNullOrWhiteSpace(tenantId))
            {
                return AuthContext.Authenticated(
                    tenantId,
                    !String.IsNullOrWhiteSpace(userId) ? userId : Constants.SystemUserId,
                    false,
                    true,
                    "Internal",
                    null,
                    "Armada Internal");
            }

            return AuthContext.Authenticated(
                Constants.SystemTenantId,
                !String.IsNullOrWhiteSpace(userId) ? userId : Constants.SystemUserId,
                true,
                true,
                "Internal",
                null,
                "Armada Internal");
        }

        private static bool HasRolloutMonitoringConfigured(DeploymentEnvironment environment)
        {
            return environment.RolloutMonitoringWindowMinutes > 0;
        }

        private static bool HasMonitoringChecksConfigured(DeploymentEnvironment environment)
        {
            bool hasDefinitions = environment.VerificationDefinitions.Any(definition => definition.Active);
            bool hasHealth = !String.IsNullOrWhiteSpace(environment.BaseUrl) && !String.IsNullOrWhiteSpace(environment.HealthEndpoint);
            return hasDefinitions || hasHealth;
        }

        private static bool ShouldMonitorDeployment(Deployment deployment, DateTime utcNow)
        {
            return deployment.Status == DeploymentStatusEnum.Succeeded
                && deployment.MonitoringWindowEndsUtc.HasValue
                && deployment.MonitoringWindowEndsUtc.Value > utcNow;
        }

        private static string BuildMonitoringSummary(string? environmentName, List<CheckRun> runs, bool success)
        {
            if (runs.Count == 0)
                return "No rollout monitoring checks were executed.";

            string target = String.IsNullOrWhiteSpace(environmentName) ? "environment" : environmentName;
            string joined = String.Join("; ", runs.Select(run => run.Label + ": " + (run.Summary ?? run.Status.ToString())));
            return success
                ? "Rollout monitoring passed for " + target + ". " + joined
                : "Rollout monitoring detected a regression for " + target + ". " + joined;
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class ResolvedDeploymentDraft
        {
            public Vessel Vessel { get; set; } = new Vessel();
            public WorkflowProfile? WorkflowProfile { get; set; } = null;
            public DeploymentEnvironment Environment { get; set; } = new DeploymentEnvironment();
            public Release? Release { get; set; } = null;
            public Mission? Mission { get; set; } = null;
            public Voyage? Voyage { get; set; } = null;
            public string Title { get; set; } = "Deployment";
            public string SourceRef { get; set; } = "main";
            public string? Summary { get; set; } = null;
            public string? Notes { get; set; } = null;
            public bool ApprovalRequired { get; set; } = false;
        }
    }
}
