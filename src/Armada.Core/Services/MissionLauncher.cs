namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Assigns a pending mission to a captain and launches it: claim, capacity, dock provisioning, launch, and rollback.
    /// </summary>
    internal sealed class MissionLauncher
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly MissionAdmissionResultStore _AdmissionResults;
        private readonly BackgroundLaunchSet _BackgroundLaunches;
        private readonly SemaphoreSlim _CapacityLock;
        private readonly ICaptainService _Captains;
        private readonly DatabaseDriver _Database;
        private readonly IDockService _Docks;
        private readonly EndpointCircuitBreakers _EndpointBreakers;
        private readonly string _Header;
        private readonly MissionLaunchBackoff _LaunchBackoff;
        private readonly LoggingModule _Logging;
        private readonly AsyncLocal<DispatchPassCache?> _Pass;
        private readonly ArmadaSettings _Settings;
        private readonly UsageSelectionBlockerStore _UsageSelection;
        private readonly VesselAdmissionLockRegistry _VesselAdmissionLocks;
        private readonly MissionWaitTracker _WaitTracker;

        private static readonly TimeSpan _UnhealthyEndpointWindow = TimeSpan.FromMinutes(15);

        private const int _MaxLaunchFailuresBeforeFail = 6;

        /// <summary>
        /// Tracks in-flight mission assignment operations by mission ID.
        /// Prevents duplicate provisioning/launch when multiple dispatch paths
        /// race on the same mission.
        /// </summary>
        private const int _TransientAssignmentAttempts = 3;

        private System.Collections.Concurrent.ConcurrentDictionary<string, byte> _InFlightAssignments =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal MissionLauncher(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _AdmissionResults = context.AdmissionResults;
            _BackgroundLaunches = context.BackgroundLaunches;
            _CapacityLock = context.CapacityLock;
            _Captains = context.Captains;
            _Database = context.Database;
            _Docks = context.Docks;
            _EndpointBreakers = context.EndpointBreakers;
            _Header = context.Header;
            _LaunchBackoff = context.LaunchBackoff;
            _Logging = context.Logging;
            _Pass = context.Pass;
            _Settings = context.Settings;
            _UsageSelection = context.UsageSelection;
            _VesselAdmissionLocks = context.VesselAdmissionLocks;
            _WaitTracker = context.WaitTracker;
        }

        #endregion

        #region Internal-Methods

        /// <inheritdoc />
        internal Task<bool> TryAssignAsync(Mission mission, Vessel vessel, CancellationToken token = default)
            => TryAssignAsync(mission, vessel, false, token);

        /// <inheritdoc />
        internal async Task<bool> TryAssignAsync(Mission mission, Vessel vessel, bool launchInBackground, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            // A server database can pick an admission read or write as a deadlock victim under concurrent dispatch. Every
            // admission write is its own atomic statement or transaction, so a retry re-reads current state; after that
            // the mission stays Pending for the next dispatch cycle instead of failing the caller.
            // A duplicate attempt records nothing: the attempt in flight owns this mission's admission result.
            if (_InFlightAssignments.ContainsKey(mission.Id)) return false;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    bool assigned = await TryAssignAsync(mission, vessel, launchInBackground, singleAttempt: true, token).ConfigureAwait(false);
                    await _Context.MissionWaitCoordinator.RecordAttemptWaitAsync(mission, assigned, token).ConfigureAwait(false);
                    return assigned;
                }
                catch (Exception ex) when (!TransientDatabaseError.IsTransient(ex) && !(ex is OperationCanceledException))
                {
                    _AdmissionResults.TryTakeAttempt(mission.Id, out MissionWaitObservation? _);
                    _Context.MissionDispatchPass.PassStateChanged();
                    _AdmissionResults.Record(new MissionWaitObservation { Mission = mission, Blocker = new MissionAssignmentBlocker
                    { Reason = MissionAssignmentBlockerReasonEnum.AwaitingDispatch, Summary = "Assignment failed with an error: " + ex.Message } });
                    throw;
                }
                catch (Exception ex) when (TransientDatabaseError.IsTransient(ex) && !token.IsCancellationRequested)
                {
                    if (attempt >= _TransientAssignmentAttempts)
                    {
                        _Logging.Warn(_Header + "assignment of mission " + mission.Id + " hit a transient database error " + attempt + " times; retrying next dispatch cycle: " + ex.Message);
                        return false;
                    }
                    _Context.MissionDispatchPass.PassStateChanged();
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt + Random.Shared.Next(0, 50)), token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        internal Task<bool> HasStrictCaptainAssignmentAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return _Context.MissionCaptainRouter.HasStrictCaptainOverrideAsync(mission, token);
        }

        /// <inheritdoc />
        internal async Task<bool> IsEndpointBreakerOpenAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(captain.ModelEndpointId)) return false;
            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId!, token).ConfigureAwait(false);
            return endpoint != null && _EndpointBreakers.IsOpen(EndpointCapacityKey(endpoint));
        }

        /// <inheritdoc />
        internal async Task RecordEndpointOutcomeAsync(Captain captain, bool healthy, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(captain.ModelEndpointId)) return;
            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId!, token).ConfigureAwait(false);
            if (endpoint == null) return;

            await _EndpointBreakers.RecordAsync(EndpointCapacityKey(endpoint), endpoint.BaseUrl, healthy, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Load the endpoint breakers and launch backoff a previous run left (RELIABILITY_SIMPLICITY_PLAN.md RS-3.7). Call
        /// once at startup, before dispatch.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        internal async Task LoadRecoveryStateAsync(CancellationToken token = default)
        {
            await _EndpointBreakers.LoadAsync(token).ConfigureAwait(false);
            await _LaunchBackoff.LoadAsync(token).ConfigureAwait(false);
        }

        internal async Task<bool> HasUntriedIdleCaptainAsync(Mission mission, MissionLaunchFailure failure, CancellationToken token)
        {
            // Backoff protects against relaunching the same broken captain; it should not hold a mission back when a
            // captain that has not failed it is idle and could start it now.
            Captain? candidate = (await _Context.MissionAdmissionPipeline.SelectCaptainAdmissionAsync(mission, token).ConfigureAwait(false)).Selected;
            lock (failure.FailedCaptainIds)
                return candidate != null && !failure.FailedCaptainIds.Contains(candidate.Id);
        }

        internal async Task<List<Captain>> ApplyCapacityLimitsAsync(List<Captain> idleCaptains, Mission mission, CancellationToken token,
            Dictionary<string, MissionAssignmentBlocker>? excluded = null)
        {
            if (idleCaptains.Count == 0) return idleCaptains;

            // Runtime and endpoint caps come from the one capacity model (RS-4.8), so several captains pointed at one
            // local model server share its budget instead of each starting a run against it.
            CapacityModel capacity = await _Context.MissionDispatchPass.PassCapacityAsync(token).ConfigureAwait(false);

            List<Captain> admitted = new List<Captain>();
            foreach (Captain candidate in idleCaptains)
            {
                if (!capacity.KnowsEndpointOf(candidate))
                    capacity.AddEndpoint(candidate.ModelEndpointId!, await _Context.MissionDispatchPass.PassEndpointAsync(candidate.ModelEndpointId!, token).ConfigureAwait(false));
                MissionAssignmentBlocker? runtimeBlocker = capacity.RuntimeBlocker(candidate);
                if (runtimeBlocker != null)
                {
                    if (excluded != null) excluded[candidate.Id] = runtimeBlocker;
                    _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": " + runtimeBlocker.Summary);
                    continue;
                }

                ModelEndpoint? endpoint = capacity.EndpointOf(candidate);
                if (endpoint != null)
                {
                    if (!endpoint.Enabled)
                    {
                        if (excluded != null) excluded[candidate.Id] = MissionAdmissionPipeline.EndpointUnavailable(endpoint, "disabled");
                        _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": its model endpoint " + endpoint.Id + " is disabled");
                        continue;
                    }

                    // A recent failed health probe means a launch would fail the same way. An old result says nothing about
                    // now (probes may be off), so only a check within the window counts.
                    if (endpoint.HealthStatus == EndpointHealthStatusEnum.Unhealthy && endpoint.LastHealthCheckUtc.HasValue
                        && DateTime.UtcNow - endpoint.LastHealthCheckUtc.Value <= _UnhealthyEndpointWindow)
                    {
                        if (excluded != null) excluded[candidate.Id] = MissionAdmissionPipeline.EndpointUnavailable(endpoint, "unhealthy");
                        _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": endpoint " + endpoint.BaseUrl + " failed its health check at "
                            + endpoint.LastHealthCheckUtc.Value.ToString("o") + (String.IsNullOrEmpty(endpoint.LastHealthError) ? String.Empty : " (" + endpoint.LastHealthError + ")"));
                        continue;
                    }

                    if (_EndpointBreakers.IsOpen(EndpointCapacityKey(endpoint)))
                    {
                        if (excluded != null) excluded[candidate.Id] = MissionAdmissionPipeline.EndpointUnavailable(endpoint, "temporarily unavailable after repeated failures");
                        _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": endpoint " + endpoint.BaseUrl + " circuit is open after repeated failures");
                        continue;
                    }

                    MissionAssignmentBlocker? endpointBlocker = capacity.EndpointBlocker(candidate);
                    if (endpointBlocker != null)
                    {
                        if (excluded != null) excluded[candidate.Id] = endpointBlocker;
                        _Logging.Debug(_Header + "captain " + candidate.Id + " skipped for mission " + mission.Id + ": " + endpointBlocker.Summary);
                        continue;
                    }
                }

                admitted.Add(candidate);
            }

            return admitted;
        }

        #endregion

        #region Private-Methods

        // One admission attempt. Named TryAssignAsync so failure records keep their original origin (CallerMemberName).
        private async Task<bool> TryAssignAsync(Mission mission, Vessel vessel, bool launchInBackground, bool singleAttempt, CancellationToken token)
        {

            if (!_InFlightAssignments.TryAdd(mission.Id, 0))
            {
                _Logging.Debug(_Header + "mission " + mission.Id + " assignment already in flight -- skipping duplicate");
                return false;
            }

            IDisposable? vesselAdmission = null;
            bool capacityHeld = false;
            bool launchDeferred = false;
            try
            {
                TimeSpan admissionTimeout = TimeSpan.FromSeconds(_Settings.Concurrency.AdmissionLockTimeoutSeconds);
                long vesselLockStart = System.Diagnostics.Stopwatch.GetTimestamp();
                vesselAdmission = await _VesselAdmissionLocks.TryEnterAsync(vessel.Id, admissionTimeout, token).ConfigureAwait(false);
                ArmadaMetrics.AdmissionLockWaitSeconds.Record(System.Diagnostics.Stopwatch.GetElapsedTime(vesselLockStart).TotalSeconds, new KeyValuePair<string, object?>("lock", "vessel"));
                if (vesselAdmission == null)
                {
                    ArmadaMetrics.AdmissionLockTimeouts.Add(1, new KeyValuePair<string, object?>("gate", "vessel"));
                    _Logging.Warn(_Header + "vessel admission lock timed out for mission " + mission.Id + "; retrying next dispatch cycle");
                    _Context.MissionWaitCoordinator.NoteUnadmitted(mission, "The vessel admission lock timed out; Armada retries on the next dispatch cycle.");
                    return false;
                }
                Mission? latestMission = null;
                if (!String.IsNullOrEmpty(mission.TenantId))
                {
                    latestMission = await _Database.Missions.ReadAsync(mission.TenantId, mission.Id, token).ConfigureAwait(false);
                }
                if (latestMission == null)
                {
                    latestMission = await _Database.Missions.ReadAsync(mission.Id, token).ConfigureAwait(false);
                }
                if (latestMission != null)
                {
                    mission = latestMission;
                }

                if (!String.IsNullOrEmpty(mission.VesselId) && mission.VesselId != vessel.Id) return false;
                if (mission.Status != MissionStatusEnum.Pending)
                {
                    _Logging.Debug(_Header + "mission " + mission.Id + " is " + mission.Status +
                        " in the database -- skipping assignment");
                    _Context.MissionWaitCoordinator.NoteLeftPending(mission);
                    return false;
                }

                if (_LaunchBackoff.TryGet(mission.Id, out MissionLaunchFailure? backoff) && DateTime.UtcNow < backoff.NextAttemptUtc
                    && !await HasUntriedIdleCaptainAsync(mission, backoff, token).ConfigureAwait(false))
                {
                    _Logging.Debug(_Header + "mission " + mission.Id + " backing off after " + backoff.Count + " failed launch(es) until " + backoff.NextAttemptUtc.ToString("o"));
                    _Context.MissionWaitCoordinator.NoteWait(mission, MissionWaitCoordinator.LaunchFailingBlocker(backoff), MissionWaitGateEnum.LaunchBackoff, backoff.Count + " failed launch(es)");
                    return false;
                }

                if (!String.IsNullOrEmpty(mission.VoyageId))
                {
                    Voyage? voyage = await _Context.MissionDispatchPass.PassVoyageAsync(mission.VoyageId, token).ConfigureAwait(false);
                    // RS-4.3: nothing of a voyage starts until every one of its missions exists.
                    if (voyage != null && voyage.Status == VoyageStatusEnum.Building)
                    {
                        _Logging.Debug(_Header + "mission " + mission.Id + " waits: voyage " + voyage.Id + " is still being created");
                        _Context.MissionWaitCoordinator.NoteUnadmitted(mission, "Voyage " + voyage.Id + " is still being created; none of its missions start until all exist.");
                        return false;
                    }

                    if (voyage != null &&
                        (voyage.Status == VoyageStatusEnum.Cancelled ||
                         voyage.Status == VoyageStatusEnum.Failed ||
                         voyage.Status == VoyageStatusEnum.Complete))
                    {
                        mission.ProcessId = null;
                        mission.CompletedUtc = DateTime.UtcNow;
                        if (String.IsNullOrWhiteSpace(mission.FailureReason))
                        {
                            mission.FailureReason = "Parent voyage " + voyage.Id + " is " + voyage.Status + ".";
                        }

                        if (!mission.FailureKind.HasValue)
                        {
                            mission.FailureKind = MissionFailureKindEnum.DependencyFailed;
                        }

                        if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.Pending }, MissionStatusEnum.Cancelled, null, token).ConfigureAwait(false)) return false;
                        _Logging.Info(_Header + "mission " + mission.Id + " belongs to terminal voyage " + voyage.Id +
                            " (" + voyage.Status + ") -- cancelling instead of assigning");
                        await _Context.MissionFailureCapture.RecordFailureAsync(mission, MissionStatusEnum.Pending, BlackBoxStepEnum.VoyageParentNotRunnable, null, context =>
                        {
                            context.Voyage = voyage;
                            context.Vessel = vessel;
                        }, token).ConfigureAwait(false);
                        _Context.MissionWaitCoordinator.NoteLeftPending(mission);
                        return false;
                    }
                }

                if (MissionCaptainRouter.UsesSharedLocalAndWorkingDirectory(vessel))
                {
                    _Logging.Warn(_Header + "vessel " + vessel.Id +
                        " uses the same path for LocalPath and WorkingDirectory (" +
                        (vessel.WorkingDirectory ?? vessel.LocalPath ?? "unknown") +
                        ") -- skipping mission assignment because dock provisioning requires a separate bare repository path");
                    _Context.MissionWaitCoordinator.NoteWait(mission, new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.VesselMisconfigured,
                        Summary = "Vessel " + vessel.Name + " uses the same directory for its local repository and working directory, so a dock cannot be provisioned. Change the vessel's working directory." },
                        MissionWaitGateEnum.Other, "VesselMisconfigured");
                    return false;
                }

            ReadinessAdmissionOutcome readiness = await _Context.MissionAdmissionPipeline.EvaluateReadinessAdmissionAsync(mission, token).ConfigureAwait(false);
            if (readiness.Blocker != null)
            {
                if (readiness.FailedSlice != null)
                {
                    await _Context.IntegrationBarrierService.CancelIntegrationForFailedSliceAsync(mission, readiness.FailedSlice, token).ConfigureAwait(false);
                    _Context.MissionWaitCoordinator.NoteLeftPending(mission);
                }
                else if (readiness.Blocker.Reason == MissionAssignmentBlockerReasonEnum.ParentNotFinished
                    && (await _Context.MissionHierarchyService.EvaluateParentReadinessAsync(mission, token).ConfigureAwait(false)).FailedParent is Mission failedParent)
                {
                    await _Context.MissionHierarchyService.CancelDescendantsForFailedParentAsync(mission, failedParent, token).ConfigureAwait(false);
                    _Context.MissionWaitCoordinator.NoteLeftPending(mission);
                }
                else
                {
                    _Logging.Debug(_Header + "mission " + mission.Id + " waits: " + readiness.Blocker.Summary);
                    _Context.MissionWaitCoordinator.NoteWait(mission, readiness.Blocker);
                }
                return false;
            }

            MissionAssignmentBlocker? admission = await _Context.MissionAdmissionPipeline.EvaluateSafetyAdmissionAsync(mission, vessel, token).ConfigureAwait(false);
            if (admission != null)
            {
                _Logging.Debug(_Header + "mission " + mission.Id + " waits: " + admission.Summary);
                _Context.MissionWaitCoordinator.NoteWait(mission, admission);
                return false;
            }

            // RS-4.8: skip early, without the shared lock, when the deployment-wide limit is already full.
            MissionAssignmentBlocker? fullBeforeLock = await _Context.MissionCaptainRouter.EvaluateGlobalAdmissionAsync(token).ConfigureAwait(false);
            if (fullBeforeLock != null) { _Context.MissionWaitCoordinator.NoteWait(mission, fullBeforeLock); return false; }

            // RS-4.9: captain selection, usage evidence, the Harbor policy gate and dock placement run without the shared
            // capacity lock, so a slow placement on one vessel no longer stalls admission on every other vessel. The
            // lock covers only the cap re-check and the claim below.

            // Resolve the mission's preferred captain + fallback tier (per-persona default / voyage override)
            // before assignment, so per-step captain selection applies to every mission of a step, including
            // fan-out missions that were created without an explicit dictated captain.
            await _Context.MissionCaptainRouter.ResolvePreferredCaptainAsync(mission, token).ConfigureAwait(false);

            // Find an idle captain: the preferred captain when idle, otherwise fall back by persona/tier.
            if (_Settings.EffectiveUsageSelection.Mode != UsageSelectionModeEnum.Disabled && _Context.MissionUsageSelector.UsageRetryPending(mission))
            { _Context.MissionWaitCoordinator.NoteWait(mission, MissionUsageSelector.UsageBlocker("UsageRetryPending", "Usage selection is waiting to retry this mission."), MissionWaitGateEnum.CaptainSlot, "UsageRetryPending"); return false; }
            CaptainAdmissionSelection captainSelection = await _Context.MissionAdmissionPipeline.SelectCaptainAdmissionAsync(mission, token, recordEnvelope: true).ConfigureAwait(false);
            Captain? captain = captainSelection.Selected;
            UsageSelectionSnapshot? usageSelection = null;
            if (_Settings.EffectiveUsageSelection.Mode == UsageSelectionModeEnum.Enforce && String.IsNullOrWhiteSpace(mission.TenantId))
            { _Context.MissionUsageSelector.UsageWait(mission, "MissionScopeUnavailable", "Usage selection requires a tenant-scoped mission."); return false; }
            if (_Settings.EffectiveUsageSelection.Mode != UsageSelectionModeEnum.Disabled && !String.IsNullOrEmpty(mission.TenantId))
            {
                // One detached policy revision governs this attempt. Observe never changes legacy routing.
                try { usageSelection = await _Context.MissionUsageSelector.BuildUsageSelectionAsync(captainSelection, _Settings.EffectiveUsageSelection.Copy(), token).ConfigureAwait(false); }
                catch (System.Data.Common.DbException)
                {
                    CaptainSelectionTelemetry.PersistenceFailure("Evidence");
                    _Context.MissionUsageSelector.UsageWait(mission, "SelectionPersistenceUnavailable", "Selection evidence is temporarily unavailable. Armada will retry without starting a process.");
                    return false;
                }
                if (usageSelection.Settings.Mode == UsageSelectionModeEnum.Enforce)
                {
                    captain = usageSelection.Evaluation.SelectedCaptainId == null ? null : usageSelection.Captains[usageSelection.Evaluation.SelectedCaptainId];
                    usageSelection.ActualCaptainId = captain?.Id;
                }
                if (usageSelection.Evaluation.CandidateLimitExceeded)
                {
                    _Context.MissionUsageSelector.UsageWait(mission, "CandidateLimitExceeded", "The complete captain comparison exceeds the configured candidate bound. An administrator can increase MaximumCandidates.");
                    return false;
                }
                _UsageSelection.ClearBlocker(mission.Id);
                if (captain == null && !await _Context.MissionUsageSelector.RecordUsageSelectionWaitAsync(usageSelection, mission, token).ConfigureAwait(false))
                { _Context.MissionWaitCoordinator.NoteWait(mission, MissionUsageSelector.UsageBlocker(usageSelection.Evaluation.ReasonCode, "No captain currently satisfies the configured usage policy."), MissionWaitGateEnum.CaptainSlot, usageSelection.Evaluation.ReasonCode); return false; }
            }
            if (captain == null)
            {
                if (usageSelection?.Settings.Mode == UsageSelectionModeEnum.Enforce)
                {
                    UsageSelectionCandidateResult? blocked = usageSelection.Evaluation.Candidates.FirstOrDefault();
                    _UsageSelection.SetBlocker(mission.Id, new UsageSelectionTransientBlocker { PolicyKey = _Context.MissionUsageSelector.UsagePolicyKey(), RetryUtc = DateTime.MinValue,
                        Blocker = MissionUsageSelector.AddRecoveryAdvice(new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.NoEligibleCaptain,
                            SelectionReasonCode = usageSelection.Evaluation.ReasonCode, Summary = "No captain currently satisfies the configured usage policy.",
                            SelectionNextReconsiderUtc = blocked?.NextReconsiderUtc }, mission, usageSelection) });
                }
                _Logging.Warn(_Header + "no idle captains available for mission " + mission.Id +
                    (mission.Persona != null ? " (persona: " + mission.Persona + ")" : ""));
                if (captainSelection.RoutingEligible.Count > 0 && captainSelection.CapacityExclusions.TryGetValue(captainSelection.RoutingEligible[0].Id, out MissionAssignmentBlocker? capacityBlocker))
                    _Context.MissionWaitCoordinator.NoteWait(mission, capacityBlocker);
                else _Context.MissionWaitCoordinator.NoteWait(mission, _Context.MissionAdmissionPipeline.CaptainGateBlocker(mission, captainSelection, out _), MissionWaitGateEnum.CaptainSlot, usageSelection?.Evaluation.ReasonCode);
                return false;
            }

            // Policy gate: when the deployment requires a connected Harbor, defer assignment until one is
            // available for this mission rather than running the captain in-process. The mission stays Pending
            // and is retried on the next dispatch cycle.
            if (_Service.CanAssignMissionAsync != null)
            {
                bool allowed = await _Service.CanAssignMissionAsync(mission, captain).ConfigureAwait(false);
                if (!allowed)
                {
                    if (usageSelection != null) await _Context.MissionUsageSelector.RecordUsageSelectionWaitAsync(usageSelection, mission, token, "PlacementWaiting").ConfigureAwait(false);
                    _Logging.Info(_Header + "deferring mission " + mission.Id + ": launch policy requires the requesting user's Harbor to be connected and eligible, and none is yet");
                    _Context.MissionWaitCoordinator.NoteWait(mission, new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.PlacementWaiting,
                        Summary = "A captain is free, but launch policy requires the requesting user's Harbor to be connected and eligible, and none is yet." },
                        MissionWaitGateEnum.Placement, "HarborRequired");
                    return false;
                }
            }

            // Choose where the dock lives before creating it: a Harbor that can serve the vessel, the Admiral's host, or
            // nowhere yet (the mission stays Pending and is retried on the next dispatch cycle).
            DockPlacement placement = DockPlacement.OnAdmiral("no Harbor placement is configured");
            if (_Service.ResolveDockPlacementAsync != null || _Service.ResolveUsageDockPlacementAsync != null)
            {
                UsageSelectionCandidate? chosenUsage = usageSelection?.Evaluation.Candidates.SingleOrDefault(c => c.Candidate.CaptainId == captain.Id)?.Candidate;
                if (usageSelection?.Settings.Mode == UsageSelectionModeEnum.Enforce && chosenUsage?.IdentityVerified == true && chosenUsage.ConfigurationMatches
                    && usageSelection.Evidence.TryGetValue(captain.Id, out UsageSelectionAccountEvidence? credentialSource) && _Service.ResolveUsageDockPlacementAsync != null)
                    placement = await _Service.ResolveUsageDockPlacementAsync(mission, captain, vessel, credentialSource.HostId).ConfigureAwait(false);
                else if (_Service.ResolveDockPlacementAsync != null) placement = await _Service.ResolveDockPlacementAsync(mission, captain, vessel).ConfigureAwait(false);
                if (placement.Wait)
                {
                    if (usageSelection != null) await _Context.MissionUsageSelector.RecordUsageSelectionWaitAsync(usageSelection, mission, token, "PlacementWaiting").ConfigureAwait(false);
                    // Log a reason once per mission until it changes, not on every dispatch cycle.
                    string summary = "A captain is free, but the mission's dock cannot be placed yet: " + placement.Reason;
                    if (!String.Equals(_Context.MissionWaitCoordinator.GetAdmissionResult(mission.Id)?.Blocker.Summary, summary, StringComparison.Ordinal))
                        _Logging.Warn(_Header + "mission " + mission.Id + " waits: " + placement.Reason);
                    _Context.MissionWaitCoordinator.NoteWait(mission, new MissionAssignmentBlocker { Reason = MissionAssignmentBlockerReasonEnum.PlacementWaiting, Summary = summary },
                        MissionWaitGateEnum.Placement, placement.Reason);
                    return false;
                }
            }

            string? usageExecutionHost = placement.HarborId ?? _Service.UsageSelectionLocalHostId;
            if (usageSelection?.Settings.Mode == UsageSelectionModeEnum.Enforce)
            {
                if (String.IsNullOrWhiteSpace(usageExecutionHost))
                { _Context.MissionUsageSelector.UsageWait(mission, "UsageHostUnresolved", "The execution credential host is not known. Configure the host before enforcing usage selection."); return false; }
                UsageSelectionCandidate? selectedUsage = usageSelection.Evaluation.Candidates.Single(c => c.Candidate.CaptainId == captain.Id).Candidate;
                if (selectedUsage.IdentityVerified && selectedUsage.ConfigurationMatches && usageSelection.Evidence.TryGetValue(captain.Id, out UsageSelectionAccountEvidence? observedHost) && observedHost.HostId != usageExecutionHost)
                { _Context.MissionUsageSelector.UsageWait(mission, "UsageHostMismatch", "Captured usage belongs to a different credential host than this execution. Capture usage on the selected execution host."); return false; }
            }


            // A hierarchy child continues its parent's branch or forks from its head (D16), decided when it starts.
            if (!String.IsNullOrEmpty(mission.ParentMissionId) && String.IsNullOrEmpty(mission.DependsOnMissionId)
                && String.IsNullOrEmpty(mission.BranchName) && String.IsNullOrEmpty(mission.BaseBranchName))
            {
                HierarchyReadiness hierarchy = await _Context.MissionHierarchyService.EvaluateParentReadinessAsync(mission, token).ConfigureAwait(false);
                if (hierarchy.ParentTerminal != null) await _Context.MissionHierarchyService.ApplyHierarchyBranchAsync(mission, hierarchy.ParentTerminal, token).ConfigureAwait(false);
            }

            // Missions with an existing branch continue work on that branch. This covers
            // downstream pipeline stages, review rework loops, and resumed missions.
            bool preserveExistingBranch = !String.IsNullOrEmpty(mission.BranchName);
            string branchName = preserveExistingBranch
                ? mission.BranchName!
                : BuildMissionBranchName(captain, mission);

            // Claim the captain before any slow work. The claim is atomic (only an Idle captain is taken), so a
            // concurrent dispatcher can never take it in the gap, and from here on the captain names this mission,
            // which keeps orphan recovery from reverting the assignment while its dock is being provisioned.
            // Only the cap re-check and the claim serialize across vessels. The captain was chosen from counts read
            // before the lock; a claim that landed since bumps the generation, so the re-check reads current counts.
            long capacityLockStart = System.Diagnostics.Stopwatch.GetTimestamp();
            bool capacityEntered = await _CapacityLock.WaitAsync(admissionTimeout, token).ConfigureAwait(false);
            ArmadaMetrics.AdmissionLockWaitSeconds.Record(System.Diagnostics.Stopwatch.GetElapsedTime(capacityLockStart).TotalSeconds, new KeyValuePair<string, object?>("lock", "capacity"));
            if (!capacityEntered)
            {
                ArmadaMetrics.AdmissionLockTimeouts.Add(1, new KeyValuePair<string, object?>("gate", "capacity"));
                _Logging.Warn(_Header + "capacity admission lock timed out for mission " + mission.Id + "; retrying next dispatch cycle");
                _Context.MissionWaitCoordinator.NoteUnadmitted(mission, "The capacity admission lock timed out; Armada retries on the next dispatch cycle.");
                return false;
            }
            capacityHeld = true;
            _Context.MissionDispatchPass.PassRevalidateUnderLock();
            MissionAssignmentBlocker? capBlocker = await _Context.MissionDispatchPass.CapacityBlockerUnderLockAsync(captain, token, placement.HarborId).ConfigureAwait(false);
            if (capBlocker != null)
            {
                _Logging.Debug(_Header + "mission " + mission.Id + " waits after selection: " + capBlocker.Summary);
                _Context.MissionWaitCoordinator.NoteWait(mission, capBlocker);
                return false;
            }

            bool claimed;
            _Context.MissionDispatchPass.NoteClaimAttempt();
            if (usageSelection != null)
            {
                // Evidence and the actual compatibility choice commit with the slot, before any process/dock work.
                // An attempt after an earlier one ended without work or was interrupted is Armada's own retry.
                string trigger = mission.NoOpRedispatchAttempts > 0 || mission.RedispatchAttempts > 0 ? CaptainSelectionTriggers.Redispatch : CaptainSelectionTriggers.Dispatch;
                CaptainSelectionRecordSet decision = UsageSelectionRecordBuilder.Build(usageSelection, mission, trigger, admitted: true);
                decision.AdmissionHostId = usageExecutionHost;
                CaptainSelectionAdmissionReceipt receipt;
                // A policy update that completed before this check makes this snapshot obsolete. An
                // overlapping later update may linearize after this attempt; history retains its exact policy.
                if (_Context.MissionUsageSelector.UsagePolicyKey() != System.Text.Json.JsonSerializer.Serialize(usageSelection.Settings))
                { _Context.MissionWaitCoordinator.NoteUnadmitted(mission, "The usage policy changed during admission; Armada retries with the new policy."); return false; }
                using CaptainSelectionStage admissionStage = CaptainSelectionTelemetry.Stage("transaction", mission.Id);
                try { receipt = usageSelection.Settings.Mode == UsageSelectionModeEnum.Enforce
                    ? await _Database.CaptainSelections.TryAdmitEnforcedAsync(decision, ceiling: _Settings.Concurrency.MaxCaptainConcurrency, token: token).ConfigureAwait(false)
                    : await _Database.CaptainSelections.TryAdmitObservedAsync(decision, ceiling: _Settings.Concurrency.MaxCaptainConcurrency, token: token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is System.Data.Common.DbException || TransientDatabaseError.IsTransient(ex))
                {
                    CaptainSelectionTelemetry.PersistenceFailure("Admission");
                    _Context.MissionUsageSelector.UsageWait(mission, UsageSelectionReasonCodes.SelectionPersistenceUnavailable, "Selection admission could not be confirmed. Armada will reconcile the recorded attempt before starting any process.");
                    return false;
                }
                claimed = receipt.Claimed;
                MissionUsageSelector.RecordAdmissionTelemetry(admissionStage, usageSelection, decision, receipt);
                if (claimed)
                {
                    _UsageSelection.ClearCarry(mission.Id);
                    mission.UsageAdmissionExecutionKey = decision.Decision.ExecutionAttemptId;
                    _Context.MissionUsageSelector.NotifySelectionChanged(mission, decision.Decision.Id, CaptainSelectionOutcomes.Admitted);
                }
                // A replay is recovered by the normal mission-owned lifecycle, never launched a second time here.
                if (receipt.AlreadyCommitted) return false;
            }
            else claimed = await _Database.CaptainSelections.TryAdmitLegacyAsync(captain.Id, mission.Id, usageExecutionHost, _Settings.Concurrency.MaxCaptainConcurrency, token).ConfigureAwait(false);
            // RS-4.7: a claim attempt changes captain and slot state, so the next mission in this pass reads it again.
            _Context.MissionDispatchPass.PassStateChanged();
            _Context.MissionDispatchPass.PassRevalidateUnderLock();
            if (!claimed)
            {
                _Logging.Info(_Header + "captain " + captain.Id + " was claimed by another mission before " + mission.Id + " could take it; retrying next cycle");
                _Context.MissionWaitCoordinator.NoteUnadmitted(mission, "Captain " + captain.Id + " was claimed by another mission first; Armada retries on the next dispatch cycle.");
                return false;
            }

            if (usageSelection == null && !String.IsNullOrEmpty(mission.TenantId))
                mission.UsageAdmissionExecutionKey = await _Database.CaptainSelections.ReadUsageExecutionKeyAsync(mission.TenantId, mission.Id, token).ConfigureAwait(false);
            _UsageSelection.ClearBlocker(mission.Id);
            mission.BranchName = branchName;
            mission.CaptainId = captain.Id;
            DateTime? previousReadyUtc = mission.ReadyUtc;
            DateTime? previousAssignedUtc = mission.AssignedUtc;
            AssignmentWaitTiming waitTiming = await _WaitTracker.PrepareAssignmentAsync(mission, token).ConfigureAwait(false);
            // Captain admission already moved the stored row from Pending to Assigned; this records the assignment fields.
            MissionTransitionRequest assignment = new MissionTransitionRequest { To = MissionStatusEnum.Assigned, Message = "Mission Assigned to " + captain.Id + ": " + mission.Title };
            assignment.AllowedFrom.Add(MissionStatusEnum.Assigned);
            bool assignmentRecorded = await _Service.Transitions.TryTransitionAsync(mission, assignment, token).ConfigureAwait(false);
            if (!assignmentRecorded)
            {
                // Someone else moved the mission (cancelled, or assigned by another caller) after it was read.
                _Logging.Info(_Header + "mission " + mission.Id + " left Pending before its assignment was recorded; releasing captain " + captain.Id);
                await _Database.Captains.TryReleaseFromMissionAsync(captain.Id, mission.Id, CancellationToken.None).ConfigureAwait(false);
                mission.ReadyUtc = previousReadyUtc;
                mission.AssignedUtc = previousAssignedUtc;
                return false;
            }

            _CapacityLock.Release();
            capacityHeld = false;
            vesselAdmission.Dispose();
            vesselAdmission = null;
            try { await _WaitTracker.CompleteAssignmentAsync(mission, waitTiming, token).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException)) { _Logging.Warn(_Header + "could not close the wait of mission " + mission.Id + ": " + ex.Message); }
            await _Context.MissionCaptainRouter.RecordRoutingNotesAsync(mission, captain, captainSelection, token).ConfigureAwait(false);

            if (launchInBackground)
            {
                // The mission is saved and admitted: it is Assigned and its captain slot is claimed. Dock provisioning and
                // launch are serialized per repository, so a create request returns now instead of queueing behind other
                // missions' git work. A failure still rolls back to Pending and the next dispatch cycle retries.
                launchDeferred = true;
                string deferredMissionId = mission.Id;
                Task background = Task.Run(async () =>
                {
                    _Pass.Value = null; // the launch outlives the pass and must never read its cache
                    try { await ProvisionAndLaunchAsync(_Service.ShutdownToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (_Service.ShutdownToken.IsCancellationRequested) { _Logging.Info(_Header + "background launch of mission " + deferredMissionId + " stopped by shutdown"); }
                    catch (Exception ex) { _Logging.Warn(_Header + "background launch of mission " + deferredMissionId + " failed: " + ex.Message); }
                    finally { _InFlightAssignments.TryRemove(deferredMissionId, out _); }
                });
                _BackgroundLaunches.Track(deferredMissionId, background);
                return true;
            }
            return await ProvisionAndLaunchAsync(token).ConfigureAwait(false);

            async Task<bool> ProvisionAndLaunchAsync(CancellationToken token)
            {
            captain.State = CaptainStateEnum.Working;
            captain = captain.ForMission(mission);
            captain.LastHeartbeatUtc = DateTime.UtcNow;
            captain.LastUpdateUtc = DateTime.UtcNow;

            // Everything from here to launch runs inside one rollback boundary: any failure puts the mission back to
            // Pending, releases the captain only while it still holds this mission, and reclaims the dock.
            Dock? dock = null;
            try
            {
                try
                {
                    _Logging.Debug(_Header + "provisioning dock for mission " + mission.Id + " on vessel " + vessel.Id + " with captain " + captain.Id);
                    if (placement.HarborId != null)
                        dock = await _Docks.ProvisionOnHarborAsync(vessel, captain, branchName, mission.Id, placement.HarborId, mission.BaseBranchName, token).ConfigureAwait(false);
                    else
                        dock = await _Docks.ProvisionAsync(vessel, captain, branchName, mission.Id, mission.BaseBranchName, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "dock provisioning threw for mission " + mission.Id + " vessel " + vessel.Id + " captain " + captain.Id + ": "
                        + (ex is DockProvisioningException ? ex.Message : ex.ToString()));
                    await RollbackAssignmentAsync(mission, captain, null, preserveExistingBranch).ConfigureAwait(false);
                    await _Context.MissionFailureCapture.RecordProvisionFailureAsync(mission, captain, placement.HarborId, ex, CancellationToken.None).ConfigureAwait(false);
                    return false;
                }

                if (dock == null)
                {
                    _Logging.Warn(_Header + "dock provisioning failed for captain " + captain.Id + " vessel " + vessel.Id + " mission " + mission.Id + " -- reverting to Pending");
                    await RollbackAssignmentAsync(mission, captain, null, preserveExistingBranch).ConfigureAwait(false);
                    await _Context.MissionFailureCapture.RecordProvisionFailureAsync(mission, captain, placement.HarborId, null, CancellationToken.None).ConfigureAwait(false);
                    return false;
                }

                // Track the dock on the mission and the captain, each only while they still describe this assignment. The
                // mission keeps the commit its dock started from (durable evidence for scope checks and the black box).
                _Context.MissionFailureCapture.ForgetProvisionFailureCapture(mission.Id);
                mission.DockId = dock.Id;
                mission.StartCommit = DockStartCommit.FromDock(dock) ?? mission.StartCommit;
                mission.LastUpdateUtc = DateTime.UtcNow;
                bool dockRecorded = await _Database.Missions.TryUpdateIfStatusAsync(mission, new MissionStatusEnum[] { MissionStatusEnum.Assigned }, token).ConfigureAwait(false);
                bool captainHasDock = dockRecorded && await _Database.Captains.TrySetDockAsync(captain.Id, mission.Id, dock.Id, token).ConfigureAwait(false);
                if (!captainHasDock)
                {
                    _Logging.Warn(_Header + "mission " + mission.Id + " or captain " + captain.Id + " changed while dock " + dock.Id + " was provisioned; abandoning this assignment");
                    await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                    return false;
                }

                captain = captain.ForMission(mission);

                // Commit continuity: a later stage must start from exactly the commit the stage before it produced. A dock
                // that lacks it (the branch lived on another host and this one created it from the default branch) would
                // silently discard upstream work, so the stage is refused with the reason instead.
                string? continuityFailure = preserveExistingBranch ? await _Context.StageCompletionHandler.CheckStageCommitContinuityAsync(mission, dock, token).ConfigureAwait(false) : null;
                if (continuityFailure != null)
                {
                    _Logging.Warn(_Header + "mission " + mission.Id + " refused: " + continuityFailure);
                    await FailAssignmentAsync(mission, captain, dock, continuityFailure, BlackBoxStepEnum.BranchCreate).ConfigureAwait(false);
                    return false;
                }

                // A final integration slice starts from every other slice's settled work, not only its own lineage.
                string? integrationFailure = await _Context.IntegrationBarrierService.IntegrateSiblingSlicesAsync(mission, dock, token).ConfigureAwait(false);
                if (integrationFailure != null)
                {
                    _Logging.Warn(_Header + "mission " + mission.Id + " refused: " + integrationFailure);
                    await FailAssignmentAsync(mission, captain, dock, integrationFailure, BlackBoxStepEnum.IntegrationBarrier).ConfigureAwait(false);
                    return false;
                }

                // Create assignment signal
                Signal signal = new Signal(SignalTypeEnum.Assignment, mission.Title);
                signal.TenantId = mission.TenantId;
                signal.UserId = mission.UserId;
                signal.ToCaptainId = captain.Id;
                await _Database.Signals.CreateAsync(signal, token).ConfigureAwait(false);

                // Generate mission CLAUDE.md into worktree (on the dock's Harbor for a Harbor-hosted dock)
                DockHost? harborHost = _Context.StageCheckRunner.HarborHostFor(dock);
                await _Context.MissionInstructionWriter.GenerateClaudeMdOnHostAsync(harborHost, dock.WorktreePath!, mission, vessel, captain, token).ConfigureAwait(false);
                await EnsureMissionInstructionsPresentAsync(harborHost, dock.WorktreePath!, mission, captain, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not prepare mission " + mission.Id + " for captain " + captain.Id + ": " + ex.Message);
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                return false;
            }
            catch (OperationCanceledException)
            {
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                throw;
            }

            // Launch agent process via captain service
            if (_Captains.OnLaunchAgent == null)
            {
                _Logging.Warn(_Header + "no OnLaunchAgent handler configured -- cannot launch agent for captain " + captain.Id);
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                return false;
            }

            // RS-4.10: record the intent to launch before the process exists, so a restart that finds no process id
            // knows an agent may have started and worked in the dock (recovery stops it and resumes there).
            mission.LaunchIntentUtc = DateTime.UtcNow;
            bool intentRecorded;
            try
            {
                intentRecorded = await _Database.Missions.TryUpdateIfStatusAsync(mission, new[] { MissionStatusEnum.Assigned }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not record the launch intent of mission " + mission.Id + ": " + ex.Message);
                intentRecorded = false;
            }

            if (!intentRecorded)
            {
                _Logging.Info(_Header + "mission " + mission.Id + " launch intent not recorded; not launching");
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                return false;
            }

            int processId;
            try
            {
                if (!String.IsNullOrEmpty(mission.TenantId) && !await _Database.CaptainSelections.MarkLaunchAttemptedAsync(mission.TenantId, mission.Id, captain.Id, mission.UsageAdmissionExecutionKey ?? String.Empty, CancellationToken.None).ConfigureAwait(false))
                    throw new InvalidOperationException("The execution no longer owns an un-released usage admission receipt.");
                processId = await _Captains.OnLaunchAgent.Invoke(captain, mission, dock).ConfigureAwait(false);
                await _Context.MissionUsageSelector.RecordSelectionExecutionStartedAsync(mission, CancellationToken.None).ConfigureAwait(false);
            }
            catch (MissionLaunchConfigurationException configuration)
            {
                // A deterministic policy/configuration problem cannot be repaired by repeatedly launching.
                // Keep it visible as a terminal operational failure, without a provider call or rescue loop.
                await ForgetLaunchFailureAsync(mission.Id).ConfigureAwait(false);
                await FailAssignmentAsync(mission, captain, dock, configuration.Message, BlackBoxStepEnum.AssignmentFailed, configuration).ConfigureAwait(false);

                // The same configuration fails every mission this captain is given; take it out of the pool for the
                // quarantine window instead of letting it be picked first again.
                await QuarantineMisconfiguredCaptainAsync(captain, configuration.Message).ConfigureAwait(false);
                return false;
            }
            catch (HarborDockNotFoundException dockMissing)
            {
                // The Harbor chosen for the mission does not have its dock, and the mission cannot run on the Admiral
                // host either. Returning it to Pending would provision a new dock and launch into the same missing path
                // on every dispatch cycle, so the mission fails with the actionable reason instead, and its dependents
                // and voyage settle like any other terminal failure.
                _Logging.Warn(_Header + "mission " + mission.Id + " failed at launch: " + dockMissing.Message);
                await FailAssignmentAsync(mission, captain, dock, dockMissing.Message, BlackBoxStepEnum.HarborUnavailable, dockMissing).ConfigureAwait(false);
                return false;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "failed to launch agent for captain " + captain.Id + ": " + ex.ToString());

                // Back off instead of re-provisioning and re-launching on every dispatch cycle; after repeated
                // failures with the same cause (a CLI that is not installed where the mission lands) fail the
                // mission with the reason rather than loop forever.
                MissionLaunchFailure failure = RecordLaunchFailure(mission, captain, ex);
                await SaveLaunchFailureAsync(mission.Id, failure).ConfigureAwait(false);
                if (failure.Count >= _MaxLaunchFailuresBeforeFail && IsRuntimeMissing(ex))
                {
                    await ForgetLaunchFailureAsync(mission.Id).ConfigureAwait(false);
                    await FailAssignmentAsync(mission, captain, dock,
                        "Captain " + captain.Name + " could not be started " + failure.Count + " times: " + failure.Reason
                        + " If this mission should run on your Harbor, connect it and turn on Require Harbor for launch (Server settings), then restart the mission.",
                        BlackBoxStepEnum.AssignmentFailed, ex).ConfigureAwait(false);
                    return false;
                }

                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);

                try
                {
                    Signal errorSignal = new Signal(SignalTypeEnum.Error, "Failed to launch agent: " + ex.Message);
                    errorSignal.TenantId = mission.TenantId;
                    errorSignal.UserId = mission.UserId;
                    errorSignal.FromCaptainId = captain.Id;
                    await _Database.Signals.CreateAsync(errorSignal, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception signalEx)
                {
                    _Logging.Warn(_Header + "could not record launch failure signal for mission " + mission.Id + ": " + signalEx.Message);
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                await RollbackAssignmentAsync(mission, captain, dock, preserveExistingBranch).ConfigureAwait(false);
                throw;
            }

            // A fast in-process captain (an ApiEndpoint loop against a quick or failing endpoint) can finish before the
            // launch call returns, and its exit handler may already have moved the mission on (WorkProduced, Failed) and
            // released the captain. Both writes below are therefore compare-and-set: the captain's process handle is
            // recorded only while it still holds this mission, and the launch only while the mission is still Assigned.
            // A failure past this point must not roll back: the agent is running and its exit handler owns the outcome.
            // These writes run outside the admission retry loop, so a deadlock victim or lock timeout (rolled back, so safe
            // to repeat) is retried here; otherwise a server provider under concurrent admissions leaves the mission
            // Assigned with a running agent until the health check notices.
            CrashPoints.Hit(CrashPointNames.LaunchBeforeProcessRecorded);
            try
            {
                if (await RetryTransientAsync(() => _Database.Captains.TrySetProcessAsync(captain.Id, mission.Id, processId, CancellationToken.None)).ConfigureAwait(false))
                {
                    captain.ProcessId = processId;
                }

                mission.ProcessId = processId;
                mission.StartedUtc = DateTime.UtcNow;
                mission.LaunchIntentUtc = null;
                MissionStatusEnum statusBeforeLaunch = mission.Status;
                bool launchRecorded = await RetryTransientAsync(() =>
                {
                    // A failed attempt leaves the in-memory status at InProgress; restore it so the retry sees the real edge.
                    mission.Status = statusBeforeLaunch;
                    return _Service.Transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.Assigned }, MissionStatusEnum.InProgress, null, CancellationToken.None);
                }).ConfigureAwait(false);
                Mission? currentMission = launchRecorded ? null : await _Database.Missions.ReadAsync(mission.Id, CancellationToken.None).ConfigureAwait(false);
                if (currentMission != null)
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " already moved to " + currentMission.Status + " before its launch was recorded (agent process " + processId + " finished first); keeping that state");
                    mission.ProcessId = currentMission.ProcessId;
                    mission.StartedUtc = currentMission.StartedUtc;
                    mission.LastUpdateUtc = currentMission.LastUpdateUtc;
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "agent process " + processId + " for mission " + mission.Id + " launched but its launch could not be recorded: " + ex.Message
                    + " (the process exit handler and health checks own the outcome)");
            }

            await ForgetLaunchFailureAsync(mission.Id).ConfigureAwait(false);
            _Logging.Info(_Header + "launched agent process " + processId + " for captain " + captain.Id);
            _Logging.Info(_Header + "assigned mission " + mission.Id + " to captain " + captain.Id + " at " + dock.WorktreePath);
            return true;
            }
            }
            finally
            {
                if (capacityHeld) _CapacityLock.Release();
                vesselAdmission?.Dispose();
                if (!launchDeferred) _InFlightAssignments.TryRemove(mission.Id, out _);
            }
        }

        private async Task QuarantineMisconfiguredCaptainAsync(Captain captain, string reason)
        {
            try
            {
                // Take the captain out of Idle atomically where the tenant-scoped reserve applies; a tenantless captain
                // (single-user setups and tests) is checked and written directly.
                if (!String.IsNullOrEmpty(captain.TenantId)
                    && !await _Database.Captains.TryReserveAsync(captain.TenantId!, captain.Id, CaptainStateEnum.Quarantined, CancellationToken.None).ConfigureAwait(false))
                {
                    _Logging.Info(_Header + "captain " + captain.Id + " was taken before it could be quarantined for its configuration; leaving it");
                    return;
                }

                Captain? current = await _Database.Captains.ReadAsync(captain.Id, CancellationToken.None).ConfigureAwait(false);
                if (current == null) return;
                if (String.IsNullOrEmpty(captain.TenantId))
                {
                    if (current.State != CaptainStateEnum.Idle || current.ActiveMissionCount > 0 || current.CurrentSessionId != null) return;
                    current.State = CaptainStateEnum.Quarantined;
                }
                else if (current.State != CaptainStateEnum.Quarantined)
                {
                    return;
                }
                current.QuarantineUntilUtc = DateTime.UtcNow.AddMinutes(_Settings.CaptainQuarantineMinutes);
                current.QuarantineReason = "configuration: " + reason;
                current.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Captains.UpdateAsync(current, CancellationToken.None).ConfigureAwait(false);
                _Logging.Warn(_Header + "quarantined captain " + captain.Id + " for " + _Settings.CaptainQuarantineMinutes + " minute(s): " + reason);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not quarantine misconfigured captain " + captain.Id + ": " + ex.Message);
            }
        }

        private static string EndpointCapacityKey(ModelEndpoint endpoint) => ConcurrencyCapacityPolicy.EndpointKey(endpoint);

        private static async Task<bool> RetryTransientAsync(Func<Task<bool>> write)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { return await write().ConfigureAwait(false); }
                catch (Exception ex) when (attempt < _TransientAssignmentAttempts && TransientDatabaseError.IsTransient(ex))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt + Random.Shared.Next(0, 50))).ConfigureAwait(false);
                }
            }
        }

        private async Task RollbackAssignmentAsync(Mission mission, Captain captain, Dock? dock, bool preserveExistingBranch)
        {
            if (!await _Context.MissionUsageSelector.OwnsSelectionCleanupAsync(mission).ConfigureAwait(false)) return;
            // Only durable no-launch proof permits releasing estimated consumption. Ambiguous launcher failures stay unsettled.
            if (!String.IsNullOrEmpty(mission.TenantId))
                await _Context.MissionUsageSelector.ReleaseSelectionIfUnstartedAsync(mission, captain).ConfigureAwait(false);
            // Cleanup runs with no cancellation: a shutdown mid-assignment must not strand a claimed captain.
            try
            {
                mission.CaptainId = null;
                if (!preserveExistingBranch)
                    mission.BranchName = null;
                mission.DockId = null;
                mission.ProcessId = null;
                mission.StartedUtc = null;
                mission.LaunchIntentUtc = null;
                bool reverted = await _Service.Transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.Assigned }, MissionStatusEnum.Pending, null, CancellationToken.None).ConfigureAwait(false);
                if (!reverted)
                    _Logging.Info(_Header + "mission " + mission.Id + " already left Assigned; leaving its current state during rollback");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not revert mission " + mission.Id + " to Pending during rollback: " + ex.Message);
            }

            await ReleaseClaimedCaptainAsync(mission, captain).ConfigureAwait(false);
            await ReclaimAssignmentDockAsync(mission, dock).ConfigureAwait(false);
        }

        private async Task FailAssignmentAsync(Mission mission, Captain captain, Dock dock, string reason, BlackBoxStepEnum step, Exception? exception = null)
        {
            if (!await _Context.MissionUsageSelector.OwnsSelectionCleanupAsync(mission).ConfigureAwait(false)) return;
            bool failed = false;
            if (!String.IsNullOrEmpty(mission.TenantId))
                await _Context.MissionUsageSelector.ReleaseSelectionIfUnstartedAsync(mission, captain).ConfigureAwait(false);
            try
            {
                mission.FailureKind = MissionFailureKindEnum.Infra;
                mission.FailureReason = reason;
                mission.CaptainId = null;
                mission.DockId = null;
                mission.ProcessId = null;
                mission.LaunchIntentUtc = null;
                mission.CompletedUtc = DateTime.UtcNow;
                failed = await _Service.Transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.Assigned }, MissionStatusEnum.Failed, null, CancellationToken.None).ConfigureAwait(false);
                if (failed)
                    ArmadaMetrics.MissionsFailed.Add(1);
                else
                    _Logging.Info(_Header + "mission " + mission.Id + " already left Assigned; not marking it Failed");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not mark mission " + mission.Id + " failed: " + ex.Message);
            }

            if (failed)
            {
                await _Context.MissionFailureCapture.RecordFailureAsync(mission, MissionStatusEnum.Assigned, step, dock, context =>
                {
                    context.Captain = captain;
                    BlackBoxSiteFacts.ApplyException(context, exception);
                }, CancellationToken.None).ConfigureAwait(false);
            }

            await ReleaseClaimedCaptainAsync(mission, captain).ConfigureAwait(false);
            await ReclaimAssignmentDockAsync(mission, dock).ConfigureAwait(false);

            try
            {
                Signal failedSignal = new Signal(SignalTypeEnum.Error, "Mission " + mission.Id + " failed: " + reason);
                failedSignal.TenantId = mission.TenantId;
                failedSignal.UserId = mission.UserId;
                failedSignal.FromCaptainId = captain.Id;
                await _Database.Signals.CreateAsync(failedSignal, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not record failure signal for mission " + mission.Id + ": " + ex.Message);
            }

            try
            {
                await _Context.MissionHandoffService.CancelDependentPipelineStagesAsync(mission, CancellationToken.None).ConfigureAwait(false);
                await _Context.MissionHandoffService.UpdateVoyageTerminalStatusAsync(mission.VoyageId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not settle dependents or voyage after mission " + mission.Id + " failed: " + ex.Message);
            }
        }

        private async Task ReleaseClaimedCaptainAsync(Mission mission, Captain captain)
        {
            try
            {
                bool released = await _Database.Captains.TryReleaseFromMissionAsync(captain.Id, mission.Id, CancellationToken.None).ConfigureAwait(false);
                if (released)
                {
                    captain.State = CaptainStateEnum.Idle;
                    captain.Assignment = null;
                    captain.ProcessId = null;
                    captain.RecoveryAttempts = 0;
                    _Logging.Info(_Header + "released captain " + captain.Id + " from mission " + mission.Id);
                }
                else
                {
                    _Logging.Info(_Header + "captain " + captain.Id + " no longer holds mission " + mission.Id + "; leaving it as is");
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not release captain " + captain.Id + " from mission " + mission.Id + ": " + ex.Message);
            }
        }

        private async Task ReclaimAssignmentDockAsync(Mission mission, Dock? dock)
        {
            if (dock == null) return;
            try
            {
                await _Docks.ReclaimAsync(dock.Id, token: CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to reclaim dock " + dock.Id + " for mission " + mission.Id + ": " + ex.Message);
            }
        }

        private static string BuildMissionBranchName(Captain captain, Mission mission)
        {
            return Constants.BranchPrefix + SanitizeBranchPathSegment(captain.Name) + "/" + mission.Id;
        }

        private static string SanitizeBranchPathSegment(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "captain";

            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
            bool previousDash = false;

            foreach (char current in value.Trim().ToLowerInvariant())
            {
                bool isAsciiLetter = current >= 'a' && current <= 'z';
                bool isAsciiDigit = current >= '0' && current <= '9';
                bool isSafeSeparator = current == '-' || current == '_';

                if (isAsciiLetter || isAsciiDigit || isSafeSeparator)
                {
                    builder.Append(current);
                    previousDash = false;
                }
                else if (!previousDash)
                {
                    builder.Append('-');
                    previousDash = true;
                }
            }

            string sanitized = builder.ToString().Trim('-', '_');
            if (String.IsNullOrEmpty(sanitized)) return "captain";

            const int maxSegmentLength = 64;
            if (sanitized.Length > maxSegmentLength)
            {
                sanitized = sanitized.Substring(0, maxSegmentLength).Trim('-', '_');
                if (String.IsNullOrEmpty(sanitized)) return "captain";
            }

            return sanitized;
        }

        private async Task EnsureMissionInstructionsPresentAsync(
            DockHost? harborHost,
            string worktreePath,
            Mission mission,
            Captain captain,
            CancellationToken token)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            string instructionsFileName = MissionPromptBuilder.GetInstructionsFileName(captain.Runtime.ToString());
            string instructionsPath = Path.Combine(worktreePath, instructionsFileName);
            IDockFileSystem files = harborHost != null ? harborHost.Files : _Context.LocalFiles;
            if (await files.ReadTextAsync(instructionsPath, token).ConfigureAwait(false) != null) return;

            string snapshotPath = Path.Combine(_Settings.LogDirectory, "instructions", mission.Id + "." + instructionsFileName);
            if (!File.Exists(snapshotPath))
            {
                _Logging.Warn(_Header + "mission instructions missing at " + instructionsPath +
                    " and no snapshot exists at " + snapshotPath);
                return;
            }

            await files.WriteTextAsync(instructionsPath, await File.ReadAllTextAsync(snapshotPath, token).ConfigureAwait(false), token).ConfigureAwait(false);
            _Logging.Warn(_Header + "restored missing mission instructions from snapshot to " + instructionsPath);
        }

        /// <summary>
        /// The Harbor host of a Harbor-hosted dock, or null for a dock on this machine (which keeps using the local git
        /// service and disk exactly as before).
        /// </summary>
        private Task SaveLaunchFailureAsync(string missionId, MissionLaunchFailure failure) => _LaunchBackoff.SaveAsync(missionId, failure);

        private Task ForgetLaunchFailureAsync(string missionId) => _LaunchBackoff.ForgetAsync(missionId);

        private MissionLaunchFailure RecordLaunchFailure(Mission mission, Captain captain, Exception ex)
        {
            HarborJobFailedException? harborFailure = FindInnerException<HarborJobFailedException>(ex);
            AgentRuntimeNotInstalledException? notInstalled = FindInnerException<AgentRuntimeNotInstalledException>(ex);
            string reason = notInstalled != null ? notInstalled.Message : harborFailure != null ? harborFailure.HarborMessage : ex.Message;
            MissionLaunchFailure failure = _LaunchBackoff.Record(mission.Id, captain.Id, reason, out double seconds);
            _Logging.Warn(_Header + "mission " + mission.Id + ": launch " + failure.Count + " of captain " + captain.Id + " failed (" + failure.Reason + "); next attempt in " + seconds.ToString("0") + "s");
            return failure;
        }

        private static bool IsRuntimeMissing(Exception ex)
        {
            if (FindInnerException<AgentRuntimeNotInstalledException>(ex) != null) return true;
            HarborJobFailedException? harborFailure = FindInnerException<HarborJobFailedException>(ex);
            return harborFailure != null && harborFailure.HarborMessage.Contains("not installed", StringComparison.OrdinalIgnoreCase);
        }

        private static T? FindInnerException<T>(Exception? ex) where T : Exception
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
                if (current is T match) return match;
            return null;
        }

        #endregion
    }
}
