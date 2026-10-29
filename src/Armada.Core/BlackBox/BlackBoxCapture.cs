namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Captures black box entries at failure sites (docs/BLACKBOX.md, "Capture"). Builds Subject, Status, Site,
    /// Mechanism, Classification and Configuration from the context and the subject rows, Workspace, Conflict and Scope
    /// from the site's facts, writes the entry as the subject's current attempt, then record.json, the three briefs and
    /// in-memory evidence (agent output, diff), sets the subject's LastBlackBoxEntryId, emits blackbox.captured and queues
    /// enrichment. Everything beyond the index insert runs under BlackBox.CaptureTimeoutMs. Never throws. Thread-safe.
    /// </summary>
    public class BlackBoxCapture : IBlackBoxCapture
    {
        #region Public-Members

        /// <summary>
        /// Enrichment queue new entries are added to, or null (the sweeper picks them up).
        /// </summary>
        public IBlackBoxEnrichmentQueue? Enrichment { get; set; } = null;

        /// <summary>
        /// Harbor links, or null. When set, a Harbor-hosted dock's entry records the link state and when the Harbor was
        /// last heard from at the moment of failure (Workspace.HarborLinkState, Workspace.HarborLastSeenUtc).
        /// </summary>
        public Armada.Core.Services.HarborConnectionManager? Harbors { get; set; } = null;

        /// <summary>
        /// Records and broadcasts mission.status_changed (mission after the change, previous status, message), or null
        /// to record the event row only. Every mission capture whose status changed emits it with the previous status, so
        /// the status timeline is complete; it runs whether or not the black box is enabled and never throws.
        /// </summary>
        public Func<Mission, MissionStatusEnum?, string, Task>? MissionStatusChanged { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxCapture] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly BlackBoxEntryWriter _Writer;
        private readonly IBlackBoxEventSink _Events;
        private readonly IBlackBoxVersionProvider _Version;
        private readonly BlackBoxRedactor _Redactor;
        private readonly LoggingModule _Logging;
        private static readonly System.Text.Json.JsonSerializerOptions _StatusPayloadOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Armada settings (BlackBox and the global values recorded as configuration).</param>
        /// <param name="writer">Entry writer.</param>
        /// <param name="events">Event sink.</param>
        /// <param name="version">Build identity.</param>
        /// <param name="redactor">Redactor (redacts error messages in events).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxCapture(DatabaseDriver database, ArmadaSettings settings, BlackBoxEntryWriter writer, IBlackBoxEventSink events, IBlackBoxVersionProvider version, BlackBoxRedactor redactor, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _Events = events ?? throw new ArgumentNullException(nameof(events));
            _Version = version ?? throw new ArgumentNullException(nameof(version));
            _Redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<string?> CaptureMissionFailureAsync(BlackBoxCaptureContext context, CancellationToken token = default)
        {
            if (context == null || context.Mission == null) return null;
            Mission mission = context.Mission;
            await EmitStatusChangedAsync(mission, context.StatusBefore, context.Step).ConfigureAwait(false);
            if (!_Settings.BlackBox.Enabled) return null;
            try
            {
                using (CancellationTokenSource budgetSource = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    budgetSource.CancelAfter(_Settings.BlackBox.CaptureTimeoutMs);
                    CancellationToken budget = budgetSource.Token;
                    BlackBoxRecord record = new BlackBoxRecord { CaptureMode = context.CaptureMode, CreatedUtc = DateTime.UtcNow };

                    Vessel? vessel = context.Vessel;
                    Captain? captain = context.Captain;
                    Voyage? voyage = context.Voyage;
                    Dock? dock = context.Dock;
                    Harbor? harbor = null;
                    try
                    {
                        if (vessel == null && !String.IsNullOrEmpty(mission.VesselId)) vessel = await _Database.Vessels.ReadAsync(mission.VesselId, budget).ConfigureAwait(false);
                        string? captainId = mission.CaptainId ?? context.CaptainId;
                        if (captain == null && !String.IsNullOrEmpty(captainId)) captain = await _Database.Captains.ReadAsync(captainId, budget).ConfigureAwait(false);
                        if (voyage == null && !String.IsNullOrEmpty(mission.VoyageId)) voyage = await _Database.Voyages.ReadAsync(mission.VoyageId, budget).ConfigureAwait(false);
                        if (dock == null && !String.IsNullOrEmpty(mission.DockId)) dock = await _Database.Docks.ReadAsync(mission.DockId, budget).ConfigureAwait(false);
                        string? harborId = dock?.HarborId ?? mission.AssignedHarborId;
                        if (!String.IsNullOrEmpty(harborId)) harbor = await _Database.Harbors.ReadAsync(harborId, budget).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        record.Truncation.TimedOut = true;
                        BlackBoxRecordBounder.AddNote(record.Truncation, "Capture ran out of its time budget while reading the mission's vessel, captain, voyage, dock or Harbor.");
                    }

                    await RecoverExitCodeAsync(record, mission, context, budget, token).ConfigureAwait(false);
                    BlackBoxEntry entry = BuildMissionEntry(mission, context, vessel, record);
                    BuildMissionRecord(record, entry, mission, context, vessel, captain, voyage, dock, harbor);
                    entry.ReasonExcerpt = BlackBoxLimits.Clip(_Redactor.Redact(record.Classification?.RawReason), BlackBoxLimits.MaxEntryReasonExcerptChars);
                    entry.ExpiresUtc = BlackBoxRetentionPolicy.ComputeExpiry(entry, _Settings.BlackBox.Retention, vessel?.BlackBoxRetentionDays);

                    entry = await _Database.BlackBoxEntries.CreateCurrentAsync(entry, token).ConfigureAwait(false);
                    record.EntryId = entry.Id;
                    if (record.Subject != null) record.Subject.Attempt = entry.Attempt;
                    await AddPreviousAttemptAsync(record, entry, budget, token).ConfigureAwait(false);

                    List<BlackBoxPendingObject> evidence = MissionEvidence(mission);
                    await _Writer.WriteAsync(entry, record, evidence, budget, token).ConfigureAwait(false);
                    await WriteFileRowsAsync(entry, record, budget, token).ConfigureAwait(false);

                    await _Database.BlackBoxEntries.SetMissionLastEntryAsync(mission.Id, entry.Id, token).ConfigureAwait(false);
                    mission.LastBlackBoxEntryId = entry.Id;

                    await EmitCapturedAsync(entry, token).ConfigureAwait(false);
                    Enrichment?.Enqueue(entry.Id);
                    _Logging.Info(_Header + "captured " + entry.Id + " for mission " + mission.Id + " (" + entry.Phase + "/" + entry.Step + ", attempt " + entry.Attempt + ")");
                    return entry.Id;
                }
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "capture failed for mission " + mission.Id + ": " + e.GetType().Name + ": " + e.Message);
                await EmitFailedAsync(mission.TenantId, mission.Id, mission.VoyageId, mission.VesselId, context.Step, e).ConfigureAwait(false);
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<string?> CaptureVoyageFailureAsync(BlackBoxVoyageCaptureContext context, CancellationToken token = default)
        {
            if (context == null || context.Voyage == null) return null;
            if (!_Settings.BlackBox.Enabled) return null;
            Voyage voyage = context.Voyage;
            try
            {
                using (CancellationTokenSource budgetSource = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    budgetSource.CancelAfter(_Settings.BlackBox.CaptureTimeoutMs);
                    CancellationToken budget = budgetSource.Token;
                    BlackBoxRecord record = new BlackBoxRecord { CaptureMode = context.CaptureMode, CreatedUtc = DateTime.UtcNow };
                    BlackBoxVoyageRollup rollup = new BlackBoxVoyageRollup { SettlementRule = context.SettlementRule, HaltReason = context.HaltReason };
                    Mission? root = null;
                    Vessel? vessel = null;
                    try
                    {
                        root = await BuildRollupAsync(voyage, rollup, context.RootMissionId, budget).ConfigureAwait(false);
                        if (root != null && !String.IsNullOrEmpty(root.VesselId)) vessel = await _Database.Vessels.ReadAsync(root.VesselId, budget).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        record.Truncation.TimedOut = true;
                        BlackBoxRecordBounder.AddNote(record.Truncation, "Capture ran out of its time budget while reading the voyage's missions.");
                    }

                    BlackBoxPhaseEnum phase = BlackBoxTaxonomy.PhaseOf(context.Step);
                    string? reason = context.Reason ?? (root != null ? "Mission " + root.Id + " failed: " + root.FailureReason : null);
                    BlackBoxEntry entry = new BlackBoxEntry
                    {
                        TenantId = voyage.TenantId,
                        UserId = voyage.UserId,
                        SubjectType = BlackBoxSubjectTypeEnum.Voyage,
                        VoyageId = voyage.Id,
                        VesselId = root?.VesselId,
                        FleetId = vessel?.FleetId,
                        MissionTitle = BlackBoxLimits.Clip(voyage.Title, BlackBoxLimits.MaxTitleChars),
                        VesselName = vessel?.Name,
                        Phase = phase,
                        Step = context.Step,
                        FailureKind = root?.FailureKind,
                        StatusBefore = context.StatusBefore?.ToString(),
                        StatusAfter = voyage.Status.ToString(),
                        Triage = context.Step == BlackBoxStepEnum.Cancelled ? BlackBoxTriageEnum.External : BlackBoxTriageEnum.Unknown,
                        CaptureMode = context.CaptureMode,
                        StorageTarget = _Settings.BlackBox.Storage.Target,
                        ArmadaVersion = _Version.Version,
                        ArmadaCommit = _Version.Commit,
                        CreatedUtc = record.CreatedUtc
                    };
                    entry.StoragePrefix = PrefixFor(entry);
                    entry.Fingerprint = BlackBoxFingerprint.Compute(phase, context.Step, entry.FailureKind, context.CallerMember, reason);
                    entry.ReasonExcerpt = BlackBoxLimits.Clip(_Redactor.Redact(reason), BlackBoxLimits.MaxEntryReasonExcerptChars);
                    entry.ExpiresUtc = BlackBoxRetentionPolicy.ComputeExpiry(entry, _Settings.BlackBox.Retention, vessel?.BlackBoxRetentionDays);

                    record.Subject = new BlackBoxSubject
                    {
                        SubjectType = BlackBoxSubjectTypeEnum.Voyage,
                        TenantId = voyage.TenantId,
                        UserId = voyage.UserId,
                        VoyageId = voyage.Id,
                        Title = voyage.Title,
                        VoyageTitle = voyage.Title,
                        VesselId = vessel?.Id,
                        VesselName = vessel?.Name,
                        VesselRepoUrl = vessel?.RepoUrl,
                        VesselDefaultBranch = vessel?.DefaultBranch,
                        FleetId = vessel?.FleetId
                    };
                    record.Status = StatusOf(context.StatusBefore?.ToString(), voyage.Status.ToString(), voyage.LastUpdateUtc);
                    record.Site = SiteOf(context.Step, null, context.Component, context.CallerMember, context.CallerFile, context.CallerLine);
                    record.Mechanism = MechanismOf(context.Exception, null, null, null, null, null, null, null);
                    record.Classification = new BlackBoxClassification { Kind = root?.FailureKind, RawReason = reason };
                    record.Voyage = rollup;
                    record.Related = new BlackBoxRelated { Fingerprint = entry.Fingerprint, CascadeRootEntryId = rollup.CascadeRootEntryId };
                    record.Configuration = BlackBoxConfigurationResolver.Resolve(new BlackBoxConfigurationInputs { Settings = _Settings, Voyage = voyage, Vessel = vessel });
                    if (context.CaptureMode != BlackBoxCaptureModeEnum.Inline) BlackBoxRecordBounder.AddNote(record.Truncation, "Captured by the " + context.CaptureMode.ToString().ToLowerInvariant() + "; the failure site's volatile facts are missing.");
                    if (!String.IsNullOrWhiteSpace(context.Note)) BlackBoxRecordBounder.AddNote(record.Truncation, context.Note!);

                    entry = await _Database.BlackBoxEntries.CreateCurrentAsync(entry, token).ConfigureAwait(false);
                    record.EntryId = entry.Id;
                    record.Subject.Attempt = entry.Attempt;
                    await _Writer.WriteAsync(entry, record, null, budget, token).ConfigureAwait(false);
                    await _Database.BlackBoxEntries.SetVoyageLastEntryAsync(voyage.Id, entry.Id, token).ConfigureAwait(false);
                    voyage.LastBlackBoxEntryId = entry.Id;
                    await EmitCapturedAsync(entry, token).ConfigureAwait(false);
                    Enrichment?.Enqueue(entry.Id);
                    _Logging.Info(_Header + "captured " + entry.Id + " for voyage " + voyage.Id);
                    return entry.Id;
                }
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "capture failed for voyage " + voyage.Id + ": " + e.GetType().Name + ": " + e.Message);
                await EmitFailedAsync(voyage.TenantId, null, voyage.Id, null, context.Step, e).ConfigureAwait(false);
                return null;
            }
        }

        /// <summary>
        /// Trim a [CallerFilePath] to a repository-relative path (from the last "src/" segment), with forward slashes.
        /// </summary>
        /// <param name="path">Path, or null.</param>
        /// <returns>The trimmed path, or null.</returns>
        public static string? TrimSourcePath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string normalized = path.Replace('\\', '/');
            int index = normalized.LastIndexOf("/src/", StringComparison.Ordinal);
            if (index >= 0) return normalized.Substring(index + 1);
            if (normalized.StartsWith("src/", StringComparison.Ordinal)) return normalized;
            return Path.GetFileName(normalized);
        }

        #endregion

        #region Private-Methods

        private BlackBoxEntry BuildMissionEntry(Mission mission, BlackBoxCaptureContext context, Vessel? vessel, BlackBoxRecord record)
        {
            BlackBoxPhaseEnum phase = BlackBoxTaxonomy.PhaseOf(context.Step);
            BlackBoxEntry entry = new BlackBoxEntry
            {
                TenantId = mission.TenantId,
                UserId = mission.UserId,
                SubjectType = BlackBoxSubjectTypeEnum.Mission,
                MissionId = mission.Id,
                VoyageId = mission.VoyageId,
                VesselId = mission.VesselId,
                FleetId = vessel?.FleetId,
                CaptainId = mission.CaptainId ?? context.CaptainId ?? context.Captain?.Id,
                HarborId = context.Dock?.HarborId ?? mission.AssignedHarborId,
                DockId = mission.DockId ?? context.Dock?.Id,
                MergeEntryId = context.MergeEntryId,
                IncidentId = context.IncidentId,
                StageOutcomeId = context.StageOutcomeId,
                ParentMissionId = mission.ParentMissionId,
                MissionTitle = BlackBoxLimits.Clip(mission.Title, BlackBoxLimits.MaxTitleChars),
                VesselName = vessel?.Name,
                Phase = phase,
                Step = context.Step,
                FailureKind = EffectiveKind(mission, context.Step),
                StatusBefore = context.StatusBefore?.ToString(),
                StatusAfter = mission.Status.ToString(),
                Triage = IsOperatorOrExternal(mission, context.Step) ? BlackBoxTriageEnum.External : BlackBoxTriageEnum.Unknown,
                CaptureMode = context.CaptureMode,
                StorageTarget = _Settings.BlackBox.Storage.Target,
                ArmadaVersion = _Version.Version,
                ArmadaCommit = _Version.Commit,
                CreatedUtc = record.CreatedUtc
            };
            entry.StoragePrefix = PrefixFor(entry);
            entry.Fingerprint = BlackBoxFingerprint.Compute(phase, context.Step, entry.FailureKind, context.CallerMember, mission.FailureReason ?? context.Reason);
            return entry;
        }

        /// <summary>
        /// The failure kind recorded on the entry: the mission's, or OperatorAction for an operator stop or an
        /// External-phase step (a manual transition, a deleted vessel) when the mission row has none, so every surface
        /// shows a kind instead of a blank.
        /// </summary>
        private static MissionFailureKindEnum? EffectiveKind(Mission mission, BlackBoxStepEnum step)
        {
            if (mission.FailureKind.HasValue) return mission.FailureKind;
            if (step == BlackBoxStepEnum.OperatorStop || BlackBoxTaxonomy.PhaseOf(step) == BlackBoxPhaseEnum.External) return MissionFailureKindEnum.OperatorAction;
            return null;
        }

        /// <summary>
        /// Operator stops and cancellations, and failures caused outside Armada (a deleted vessel, a manual transition),
        /// are triaged External (plan open question 1) so they stay out of defect bundles.
        /// </summary>
        private static bool IsOperatorOrExternal(Mission mission, BlackBoxStepEnum step)
        {
            if (mission.FailureKind == MissionFailureKindEnum.OperatorAction) return true;
            if (step == BlackBoxStepEnum.OperatorStop) return true;
            return BlackBoxTaxonomy.PhaseOf(step) == BlackBoxPhaseEnum.External;
        }

        /// <summary>
        /// A capture with no exit code in its context recovers it from the mission's FailureReason and, when the capture
        /// is not inline (sweeper or backfill), from the mission's latest <c>captain.process_exited</c> event, and notes
        /// where it came from.
        /// </summary>
        private async Task RecoverExitCodeAsync(BlackBoxRecord record, Mission mission, BlackBoxCaptureContext context, CancellationToken budget, CancellationToken token)
        {
            if (context.ExitCode.HasValue) return;
            int? fromReason = BlackBoxRecoveredFacts.ParseExitCode(mission.FailureReason ?? context.Reason);
            if (fromReason.HasValue)
            {
                context.ExitCode = fromReason;
                if (context.CaptureMode != BlackBoxCaptureModeEnum.Inline)
                    BlackBoxRecordBounder.AddNote(record.Truncation, "Exit code " + fromReason.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " recovered from the mission's failure reason.");
                return;
            }

            if (context.CaptureMode == BlackBoxCaptureModeEnum.Inline) return;
            try
            {
                EnumerationResult<ArmadaEvent> exits = await _Database.Events.EnumerateAsync(new EnumerationQuery
                {
                    MissionId = mission.Id,
                    EventType = "captain.process_exited",
                    Order = EnumerationOrderEnum.CreatedDescending,
                    PageSize = 1
                }, budget).ConfigureAwait(false);
                ArmadaEvent? last = exits.Objects.FirstOrDefault();
                int? fromEvent = BlackBoxRecoveredFacts.ParseExitCode(last?.Message);
                if (!fromEvent.HasValue) return;
                context.ExitCode = fromEvent;
                BlackBoxRecordBounder.AddNote(record.Truncation, "Exit code " + fromEvent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " recovered from the captain.process_exited event of " + last!.CreatedUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                record.Truncation.TimedOut = true;
                BlackBoxRecordBounder.AddNote(record.Truncation, "Capture ran out of its time budget while reading the mission's captain.process_exited event.");
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not read captain.process_exited for " + mission.Id + ": " + e.Message);
            }
        }

        private async Task EmitStatusChangedAsync(Mission mission, MissionStatusEnum? previous, BlackBoxStepEnum step)
        {
            if (previous.HasValue && previous.Value == mission.Status) return;
            string message = "Mission " + mission.Status + " (" + step + "): " + mission.Title;
            try
            {
                if (MissionStatusChanged != null)
                {
                    await MissionStatusChanged(mission, previous, message).ConfigureAwait(false);
                    return;
                }

                ArmadaEvent evt = new ArmadaEvent("mission.status_changed", message);
                evt.TenantId = mission.TenantId;
                evt.UserId = mission.UserId;
                evt.EntityType = "mission";
                evt.EntityId = mission.Id;
                evt.CaptainId = mission.CaptainId;
                evt.MissionId = mission.Id;
                evt.VesselId = mission.VesselId;
                evt.VoyageId = mission.VoyageId;
                evt.Payload = System.Text.Json.JsonSerializer.Serialize(new MissionStatusChangedPayload { Status = mission.Status, PreviousStatus = previous }, _StatusPayloadOptions);
                await _Database.Events.CreateAsync(evt, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "mission.status_changed for " + mission.Id + " could not be emitted: " + e.Message);
            }
        }

        private string? PrefixFor(BlackBoxEntry entry)
        {
            if (entry.StorageTarget == BlackBoxStorageTargetEnum.Database) return null;
            return BlackBoxKeyLayout.BuildEntryPrefix(_Settings.BlackBox.Storage.KeyPrefix, entry.TenantId, entry.VesselId, entry.CreatedUtc, entry.Id);
        }

        private void BuildMissionRecord(BlackBoxRecord record, BlackBoxEntry entry, Mission mission, BlackBoxCaptureContext context, Vessel? vessel, Captain? captain, Voyage? voyage, Dock? dock, Harbor? harbor)
        {
            record.Subject = new BlackBoxSubject
            {
                SubjectType = BlackBoxSubjectTypeEnum.Mission,
                TenantId = mission.TenantId,
                UserId = mission.UserId,
                MissionId = mission.Id,
                VoyageId = mission.VoyageId,
                Title = mission.Title,
                VoyageTitle = voyage?.Title,
                VesselId = mission.VesselId,
                VesselName = vessel?.Name,
                VesselRepoUrl = vessel?.RepoUrl,
                VesselDefaultBranch = vessel?.DefaultBranch,
                FleetId = vessel?.FleetId,
                CaptainId = mission.CaptainId ?? captain?.Id ?? context.CaptainId,
                CaptainName = captain?.Name,
                CaptainRuntime = captain?.Runtime.ToString(),
                CaptainModel = captain?.Model,
                ModelEndpointId = captain?.ModelEndpointId,
                Persona = mission.Persona,
                HarborId = harbor?.Id ?? entry.HarborId,
                HarborName = harbor?.Name,
                DockId = mission.DockId ?? dock?.Id,
                MergeEntryId = context.MergeEntryId,
                IncidentId = context.IncidentId,
                StageOutcomeId = context.StageOutcomeId,
                IsRescue = mission.IsRescue,
                ParentMissionId = mission.ParentMissionId,
                BranchName = mission.BranchName
            };
            record.Status = StatusOf(context.StatusBefore?.ToString(), mission.Status.ToString(), mission.LastUpdateUtc);
            record.Site = SiteOf(context.Step, entry.HarborId, context.Component, context.CallerMember, context.CallerFile, context.CallerLine);
            if (harbor != null) record.Site.Host = "Harbor:" + harbor.Id;
            record.Mechanism = MechanismOf(context.Exception, context.ExitCode, context.RuntimeFailureKind, context.FailingCommand, context.CommandExitCode, context.CommandOutput, context.GitFacts?.GitOperation, context.GitFacts?.GitStderr);
            record.Classification = new BlackBoxClassification
            {
                Kind = EffectiveKind(mission, context.Step),
                RawReason = mission.FailureReason ?? context.Reason,
                SuspectedCause = context.SuspectedCause ?? FailureCauseEnum.None,
                CauseRuleId = context.CauseRuleId,
                IsTransient = context.RuntimeFailureKind.HasValue ? (context.RuntimeFailureKind.Value == RuntimeFailureKindEnum.Transient || context.RuntimeFailureKind.Value == RuntimeFailureKindEnum.UsageLimit || context.RuntimeFailureKind.Value == RuntimeFailureKindEnum.ModelUnavailable) : (bool?)null
            };
            record.Configuration = ConfigurationOf(mission, vessel, voyage, captain);
            record.Workspace = WorkspaceOf(mission, vessel, dock, context.GitFacts);
            AddHarborLink(record.Workspace, harbor?.Id ?? entry.HarborId, harbor);
            record.Conflict = ConflictOf(context.GitFacts);
            if (context.ScopedFiles.Count > 0 || context.OutOfScopeFiles.Count > 0 || context.BoundaryFindings.Count > 0)
            {
                record.Scope = new BlackBoxScope
                {
                    ScopedFiles = context.ScopedFiles.Take(BlackBoxLimits.MaxScopeFiles).ToList(),
                    OutOfScopeFiles = context.OutOfScopeFiles.Take(BlackBoxLimits.MaxScopeFiles).ToList(),
                    OutOfScopeFileTotal = context.OutOfScopeFiles.Count,
                    BoundaryFindings = context.BoundaryFindings.Take(BlackBoxLimits.MaxScopeFiles).ToList()
                };
                if (context.OutOfScopeFiles.Count > BlackBoxLimits.MaxScopeFiles) BlackBoxRecordBounder.AddField(record.Truncation, "Scope.OutOfScopeFiles", context.OutOfScopeFiles.Count, BlackBoxLimits.MaxScopeFiles, "items");
            }
            BlackBoxEvidenceIndex evidence = new BlackBoxEvidenceIndex();
            foreach (StageCheckResult check in context.CheckResults.Take(BlackBoxLimits.MaxCheckResults)) evidence.CheckResults.Add(check);
            record.Evidence = evidence;
            record.Related = new BlackBoxRelated { Fingerprint = entry.Fingerprint };
            if (context.CaptureMode != BlackBoxCaptureModeEnum.Inline) BlackBoxRecordBounder.AddNote(record.Truncation, "Captured by the " + context.CaptureMode.ToString().ToLowerInvariant() + " after the fact; the failure site's volatile facts (git state, exception, command output) are missing.");
            if (context.Step == BlackBoxStepEnum.Unknown) BlackBoxRecordBounder.AddNote(record.Truncation, "The failure step is unknown.");
            if (!String.IsNullOrWhiteSpace(context.Note)) BlackBoxRecordBounder.AddNote(record.Truncation, context.Note!);
        }

        private static BlackBoxStatusChange StatusOf(string? before, string after, DateTime changedUtc)
        {
            BlackBoxStatusChange status = new BlackBoxStatusChange { Before = before, After = after, ChangedUtc = changedUtc };
            if (before != null) status.Timeline.Add(new BlackBoxStatusTimelineEntry { Status = before, Utc = changedUtc, Source = "before failure write" });
            status.Timeline.Add(new BlackBoxStatusTimelineEntry { Status = after, Utc = changedUtc, Source = "failure write" });
            return status;
        }

        private BlackBoxSite SiteOf(BlackBoxStepEnum step, string? harborId, string? component, string? member, string? file, int? line)
        {
            string? trimmed = TrimSourcePath(file);
            return new BlackBoxSite
            {
                Phase = BlackBoxTaxonomy.PhaseOf(step),
                Step = step,
                HostKind = String.IsNullOrEmpty(harborId) ? BlackBoxHostKindEnum.Admiral : BlackBoxHostKindEnum.Harbor,
                Host = String.IsNullOrEmpty(harborId) ? "Admiral" : "Harbor:" + harborId,
                Component = component ?? (trimmed != null ? Path.GetFileNameWithoutExtension(trimmed) : null),
                Method = String.IsNullOrEmpty(member) ? null : member,
                SourceFile = trimmed,
                SourceLine = line.HasValue && line.Value > 0 ? line : null,
                ArmadaVersion = _Version.Version,
                ArmadaCommit = _Version.Commit
            };
        }

        private static BlackBoxMechanism MechanismOf(Exception? exception, int? exitCode, RuntimeFailureKindEnum? runtimeKind, string? command, int? commandExit, string? commandOutput, string? gitOperation, string? gitStderr)
        {
            BlackBoxMechanism m = new BlackBoxMechanism
            {
                ExitCode = exitCode,
                RuntimeFailureKind = runtimeKind,
                FailingCommand = command,
                CommandExitCode = commandExit,
                CommandOutputTail = BlackBoxLimits.Tail(commandOutput, BlackBoxLimits.MaxCommandOutputChars),
                GitOperation = gitOperation,
                GitStderrTail = BlackBoxLimits.Tail(gitStderr, BlackBoxLimits.MaxGitStderrChars)
            };
            if (exception != null)
            {
                Exception inner = exception;
                while (inner is AggregateException agg && agg.InnerException != null) inner = agg.InnerException;
                m.ExceptionType = inner.GetType().FullName;
                m.ExceptionMessage = inner.Message;
                if (!String.IsNullOrEmpty(inner.StackTrace))
                {
                    foreach (string line in inner.StackTrace.Split('\n'))
                    {
                        string frame = line.Trim();
                        if (frame.Contains("Armada.", StringComparison.Ordinal) && m.StackFrames.Count < BlackBoxLimits.MaxStackFrames)
                            m.StackFrames.Add(frame.StartsWith("at ", StringComparison.Ordinal) ? frame.Substring(3) : frame);
                    }
                }
            }
            return m;
        }

        private BlackBoxConfiguration ConfigurationOf(Mission mission, Vessel? vessel, Voyage? voyage, Captain? captain)
        {
            // The persona and workflow profile need reads; BlackBoxConfigurationEnricher adds them during enrichment.
            return BlackBoxConfigurationResolver.Resolve(new BlackBoxConfigurationInputs { Settings = _Settings, Mission = mission, Vessel = vessel, Voyage = voyage, Captain = captain });
        }

        private static BlackBoxWorkspace WorkspaceOf(Mission mission, Vessel? vessel, Dock? dock, BlackBoxGitFacts? facts)
        {
            BlackBoxWorkspace w = new BlackBoxWorkspace
            {
                DockId = mission.DockId ?? dock?.Id,
                DockState = dock?.State.ToString(),
                WorktreePath = dock?.WorktreePath,
                WorktreeExists = facts?.WorktreeExisted,
                CheckoutPath = facts?.CheckoutPath,
                BranchName = mission.BranchName ?? dock?.BranchName,
                BranchTip = facts?.BranchTip,
                MissionCommit = mission.CommitHash,
                StartCommit = mission.StartCommit ?? Armada.Core.Services.DockStartCommit.FromDock(dock),
                StartCommitSource = mission.StartCommit != null ? "Mission" : Armada.Core.Services.DockStartCommit.FromDock(dock) != null ? "DockAnchors" : null,
                TargetBranch = vessel?.DefaultBranch,
                TargetTipAtStart = facts?.TargetTipBefore,
                TargetTipAtFailure = facts?.TargetTipAfter ?? facts?.TargetTipBefore
            };
            if (facts != null && facts.DirtyPaths.Count > 0)
            {
                w.CheckoutDirty = true;
                w.DirtyPathTotal = facts.DirtyPaths.Count;
                w.DirtyPaths = facts.DirtyPaths.Select(NormalizePath).Take(BlackBoxLimits.MaxDirtyPaths).ToList();
            }
            return w;
        }

        private void AddHarborLink(BlackBoxWorkspace workspace, string? harborId, Harbor? harbor)
        {
            if (String.IsNullOrEmpty(harborId)) return;
            try
            {
                if (Harbors != null)
                {
                    workspace.HarborLinkState = Harbors.GetLinkState(harborId!).ToString();
                    workspace.HarborLastSeenUtc = Harbors.GetLinkLastSeenUtc(harborId!) ?? harbor?.LastSeenUtc;
                }
                else
                {
                    workspace.HarborLastSeenUtc = harbor?.LastSeenUtc;
                }
            }
            catch (Exception e)
            {
                _Logging.Debug(_Header + "Harbor link state of " + harborId + " could not be read: " + e.Message);
            }
        }

        private static BlackBoxConflict? ConflictOf(BlackBoxGitFacts? facts)
        {
            if (facts == null || facts.ConflictedFiles.Count == 0) return null;
            List<string> files = facts.ConflictedFiles.Select(NormalizePath).Where(f => f.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            BlackBoxConflict c = new BlackBoxConflict
            {
                IsConflict = true,
                ConflictedFileTotal = files.Count,
                ConflictedFilesTruncated = files.Count > BlackBoxLimits.MaxConflictedFiles,
                ConflictedFiles = files.Take(BlackBoxLimits.MaxConflictedFiles).ToList()
            };
            c.ConflictedDirectories = RollUp(files).Take(BlackBoxLimits.MaxConflictedDirectories).ToList();
            return c;
        }

        private static List<BlackBoxDirectoryCount> RollUp(List<string> files)
        {
            Dictionary<string, BlackBoxDirectoryCount> dirs = new Dictionary<string, BlackBoxDirectoryCount>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string[] parts = file.Split('/');
                for (int depth = 1; depth <= 3 && depth < parts.Length; depth++)
                {
                    string dir = String.Join("/", parts.Take(depth));
                    if (!dirs.TryGetValue(dir, out BlackBoxDirectoryCount? count))
                    {
                        count = new BlackBoxDirectoryCount { Directory = dir, Depth = depth };
                        dirs[dir] = count;
                    }
                    count.FileCount++;
                }
            }
            return dirs.Values.OrderByDescending(d => d.FileCount).ThenByDescending(d => d.Depth).ThenBy(d => d.Directory, StringComparer.Ordinal).ToList();
        }

        private static string NormalizePath(string path)
        {
            return (path ?? String.Empty).Trim().Replace('\\', '/').TrimStart('/');
        }

        private static string DirectoryOf(string path)
        {
            int index = path.LastIndexOf('/');
            return index < 0 ? String.Empty : path.Substring(0, index);
        }

        private static List<BlackBoxPendingObject> MissionEvidence(Mission mission)
        {
            List<BlackBoxPendingObject> evidence = new List<BlackBoxPendingObject>();
            if (!String.IsNullOrEmpty(mission.AgentOutput))
                evidence.Add(new BlackBoxPendingObject { Name = BlackBoxObjectNames.AgentOutput, ContentType = BlackBoxObjectNames.TextContentType, Data = Encoding.UTF8.GetBytes(mission.AgentOutput!) });
            if (!String.IsNullOrEmpty(mission.DiffSnapshot))
                evidence.Add(new BlackBoxPendingObject { Name = BlackBoxObjectNames.Diff, ContentType = BlackBoxObjectNames.DiffContentType, Data = Encoding.UTF8.GetBytes(mission.DiffSnapshot!) });
            return evidence;
        }

        private async Task AddPreviousAttemptAsync(BlackBoxRecord record, BlackBoxEntry entry, CancellationToken budget, CancellationToken token)
        {
            if (entry.SupersedesId == null) return;
            try
            {
                BlackBoxEntry? previous = await _Database.BlackBoxEntries.ReadAsync(entry.SupersedesId, null, true, budget).ConfigureAwait(false);
                if (previous == null) return;
                record.Attempts.Add(new BlackBoxAttempt
                {
                    Attempt = previous.Attempt,
                    EntryId = previous.Id,
                    MissionId = previous.MissionId,
                    StageOutcomeId = previous.StageOutcomeId,
                    Kind = previous.FailureKind,
                    ReasonExcerpt = BlackBoxLimits.Clip(previous.ReasonExcerpt, BlackBoxLimits.MaxReasonExcerptChars),
                    CreatedUtc = previous.CreatedUtc
                });
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                record.Truncation.TimedOut = true;
            }
        }

        private async Task WriteFileRowsAsync(BlackBoxEntry entry, BlackBoxRecord record, CancellationToken budget, CancellationToken token)
        {
            List<BlackBoxFile> rows = new List<BlackBoxFile>();
            if (record.Conflict != null) foreach (string f in record.Conflict.ConflictedFiles) rows.Add(FileRow(entry, f, BlackBoxFileRoleEnum.Conflicted));
            if (record.Workspace != null) foreach (string f in record.Workspace.DirtyPaths) rows.Add(FileRow(entry, f, BlackBoxFileRoleEnum.DirtyInCheckout));
            if (record.Scope != null) foreach (string f in record.Scope.OutOfScopeFiles.Where(x => !String.IsNullOrWhiteSpace(x))) rows.Add(FileRow(entry, f, BlackBoxFileRoleEnum.OutOfScope));
            if (rows.Count == 0) return;
            try
            {
                await _Database.BlackBoxFiles.CreateManyAsync(rows.Take(Armada.Core.Services.CollectionLimits.MaxRequestItems).ToList(), budget).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                _Logging.Warn(_Header + "file rows of " + entry.Id + " were not written: time budget exhausted (enrichment adds them)");
            }
        }

        private static BlackBoxFile FileRow(BlackBoxEntry entry, string path, BlackBoxFileRoleEnum role)
        {
            string normalized = NormalizePath(path);
            return new BlackBoxFile { EntryId = entry.Id, TenantId = entry.TenantId, VesselId = entry.VesselId, Path = normalized, Directory = DirectoryOf(normalized), Role = role, CreatedUtc = entry.CreatedUtc };
        }

        private async Task<Mission?> BuildRollupAsync(Voyage voyage, BlackBoxVoyageRollup rollup, string? rootMissionId, CancellationToken budget)
        {
            int total = 0;
            foreach (MissionStatusEnum status in Enum.GetValues(typeof(MissionStatusEnum)))
            {
                EnumerationQuery query = new EnumerationQuery { VoyageId = voyage.Id, Status = status.ToString(), PageSize = 1 };
                EnumerationResult<Mission> page = await _Database.Missions.EnumerateAsync(query, budget).ConfigureAwait(false);
                if (page.TotalRecords > 0)
                {
                    rollup.MissionsByStatus.Add(new BlackBoxStatusCount { Status = status.ToString(), Count = (int)page.TotalRecords });
                    total += (int)page.TotalRecords;
                }
            }
            rollup.MissionTotal = total;
            Mission? root = null;
            foreach (MissionStatusEnum failed in new[] { MissionStatusEnum.Failed, MissionStatusEnum.LandingFailed, MissionStatusEnum.Blocked })
            {
                EnumerationQuery query = new EnumerationQuery { VoyageId = voyage.Id, Status = failed.ToString(), PageSize = BlackBoxLimits.MaxVoyageFailedMissions, Order = EnumerationOrderEnum.CreatedAscending };
                EnumerationResult<Mission> page = await _Database.Missions.EnumerateAsync(query, budget).ConfigureAwait(false);
                foreach (Mission m in page.Objects)
                {
                    if (rollup.FailedMissionIds.Count >= BlackBoxLimits.MaxVoyageFailedMissions) break;
                    rollup.FailedMissionIds.Add(m.Id);
                    if (m.LastBlackBoxEntryId != null) rollup.FailedMissionEntryIds.Add(m.LastBlackBoxEntryId);
                    if (root == null && (rootMissionId == null || rootMissionId == m.Id)) root = m;
                }
            }
            if (root == null && rootMissionId != null) root = await _Database.Missions.ReadAsync(rootMissionId, budget).ConfigureAwait(false);
            if (root != null)
            {
                rollup.CascadeRootMissionId = root.Id;
                rollup.CascadeRootEntryId = root.LastBlackBoxEntryId;
            }
            return root;
        }

        private async Task EmitCapturedAsync(BlackBoxEntry entry, CancellationToken token)
        {
            try
            {
                await EmitCapturedCoreAsync(entry, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "blackbox.captured for " + entry.Id + " could not be emitted: " + e.Message);
            }
        }

        private async Task EmitCapturedCoreAsync(BlackBoxEntry entry, CancellationToken token)
        {
            await _Events.EmitAsync(BlackBoxEventTypes.Captured, "Black box captured for " + (entry.MissionId ?? entry.VoyageId) + ": " + entry.Phase + "/" + entry.Step,
                entry.TenantId, entry.Id, entry.MissionId, entry.VoyageId, entry.VesselId,
                new BlackBoxCapturedPayload { EntryId = entry.Id, MissionId = entry.MissionId, VoyageId = entry.VoyageId, VesselId = entry.VesselId, Phase = entry.Phase, Step = entry.Step, Kind = entry.FailureKind, Attempt = entry.Attempt, CaptureMode = entry.CaptureMode },
                token).ConfigureAwait(false);
        }

        private async Task EmitFailedAsync(string? tenantId, string? missionId, string? voyageId, string? vesselId, BlackBoxStepEnum step, Exception e)
        {
            try
            {
                await _Events.EmitAsync(BlackBoxEventTypes.CaptureFailed, "Black box capture failed for " + (missionId ?? voyageId) + ": " + _Redactor.Redact(e.Message),
                    tenantId, null, missionId, voyageId, vesselId,
                    new BlackBoxCaptureFailedPayload { MissionId = missionId, VoyageId = voyageId, Step = step, Error = BlackBoxLimits.Clip(_Redactor.Redact(e.GetType().Name + ": " + e.Message), BlackBoxLimits.MaxShortTextChars) },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception inner)
            {
                _Logging.Warn(_Header + "capture_failed event could not be emitted: " + inner.Message);
            }
        }

        #endregion
    }
}
