namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>Shared scoped selection history and preview access for REST and MCP callers.</summary>
    public sealed class CaptainSelectionAccessService
    {
        // Filter values are fixed codes: accept any casing, reject anything else so a typo is not reported as "no history".
        private static string? Canonical(string? value, string name, params string[] allowed)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string? match = allowed.FirstOrDefault(a => String.Equals(a, value.Trim(), StringComparison.OrdinalIgnoreCase));
            return match ?? throw new ArgumentException(name + " must be one of " + String.Join(", ", allowed) + ".");
        }

        private readonly DatabaseDriver _Database;
        private readonly IMissionService _Missions;
        private readonly ArmadaSettings _Settings;

        /// <summary>Create access over the shared mission evaluator and structured ledger.</summary>
        public CaptainSelectionAccessService(DatabaseDriver database, IMissionService missions, ArmadaSettings settings)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Missions = missions ?? throw new ArgumentNullException(nameof(missions));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Read current attempt separately from the latest retained decision.</summary>
        public async Task<CaptainSelectionSummary> SummaryAsync(AuthContext caller, string missionId, CancellationToken token = default)
        {
            Mission mission = await MissionAsync(caller, missionId, token).ConfigureAwait(false);
            string? owner = Owner(caller);
            CaptainSelectionDecision? current = await _Database.CaptainSelections.ReadCurrentDecisionAsync(mission.TenantId!, mission.Id, owner, token).ConfigureAwait(false);
            EnumerationResult<CaptainSelectionDecision> history = await _Database.CaptainSelections.EnumerateByMissionAsync(mission.TenantId!, mission.Id, owner, pageSize: 1, token: token).ConfigureAwait(false);
            List<string> ids = new List<string>();
            if (current != null) ids.Add(current.Id);
            if (history.Objects.Count > 0) ids.Add(history.Objects[0].Id);
            Dictionary<string, CaptainSelectionDecisionView> views = (await _Database.CaptainSelections.ReadDecisionViewsAsync(mission.TenantId!, ids.Distinct(StringComparer.Ordinal).ToList(), owner, owner, token).ConfigureAwait(false)).ToDictionary(v => v.Id, StringComparer.Ordinal);
            UsageSelectionSettings settings = _Settings.EffectiveUsageSelection.Copy();
            return new CaptainSelectionSummary { MissionId = mission.Id, CurrentMode = settings.Mode, CurrentStrategy = settings.Strategy,
                HistoryAvailable = history.TotalRecords != 0,
                Recovery = await _Database.CaptainSelections.ReadRecoveryAsync(mission.TenantId!, mission.Id, token).ConfigureAwait(false),
                CanManageRecovery = caller.MissionId == null && (caller.IsAdmin || caller.IsTenantAdmin) && !String.IsNullOrWhiteSpace(caller.UserId) && mission.Status == Armada.Core.Enums.MissionStatusEnum.Pending,
                CurrentAttempt = current == null ? null : views.GetValueOrDefault(current.Id),
                LatestDecision = history.Objects.Count == 0 ? null : views.GetValueOrDefault(history.Objects[0].Id) };
        }

        /// <summary>Authorize mission-specific recovery while Pending. Administrators must explain each immutable revision.</summary>
        public async Task<UsageSelectionRecovery> AuthorizeRecoveryAsync(AuthContext caller, string missionId, UsageSelectionRecoveryRequest request, CancellationToken token = default)
        {
            Mission mission = await MissionAsync(caller, missionId, token).ConfigureAwait(false);
            if (caller.MissionId != null || !(caller.IsAdmin || caller.IsTenantAdmin) || String.IsNullOrWhiteSpace(caller.UserId))
                throw new UnauthorizedAccessException("Captain-selection recovery requires a tenant or system administrator.");
            UsageSelectionRecovery recovery = await _Database.CaptainSelections.TryAuthorizeRecoveryAsync(mission.TenantId!, mission.Id, caller.UserId, request, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Recovery authorization changed or the mission is no longer Pending. Refresh before retrying.");
            _Missions.ReconsiderCaptainSelection(mission);
            return recovery;
        }

        /// <summary>Filter and page decisions after mission visibility; never scan another tenant.</summary>
        public async Task<EnumerationResult<CaptainSelectionDecisionView>> HistoryAsync(AuthContext caller, string missionId, CaptainSelectionHistoryQuery query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            query.Outcome = Canonical(query.Outcome, "outcome", CaptainSelectionOutcomes.Admitted, CaptainSelectionOutcomes.Waiting);
            query.Trigger = Canonical(query.Trigger, "trigger", CaptainSelectionTriggers.Dispatch, CaptainSelectionTriggers.Redispatch, CaptainSelectionTriggers.Repair, CaptainSelectionTriggers.Waiting);
            if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc.Value >= query.ToUtc.Value) throw new ArgumentException("from must be earlier than to.");
            Mission mission = await MissionAsync(caller, missionId, token).ConfigureAwait(false);
            string? owner = Owner(caller);
            EnumerationResult<CaptainSelectionDecision> page = await _Database.CaptainSelections.EnumerateHistoryAsync(mission.TenantId!, mission.Id, owner, query, token).ConfigureAwait(false);
            Dictionary<string, CaptainSelectionDecisionView> views = (await _Database.CaptainSelections.ReadDecisionViewsAsync(mission.TenantId!, page.Objects.Select(row => row.Id).ToList(), owner, owner, token).ConfigureAwait(false)).ToDictionary(v => v.Id, StringComparer.Ordinal);
            List<CaptainSelectionDecisionView> rows = page.Objects.Where(row => views.ContainsKey(row.Id)).Select(row => views[row.Id]).ToList();
            return EnumerationResult<CaptainSelectionDecisionView>.Create(query, rows, page.TotalRecords);
        }

        /// <summary>Evaluate current state without capture, persistence, claim or launch side effects.</summary>
        public async Task<CaptainSelectionPreview> PreviewAsync(AuthContext caller, string missionId, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            Mission mission = await MissionAsync(caller, missionId, token).ConfigureAwait(false);
            return await _Missions.PreviewCaptainSelectionAsync(mission, pageNumber, pageSize, token).ConfigureAwait(false);
        }

        /// <summary>Read retained historical detail, including after a mission is deleted; tenant override is global-admin-only.</summary>
        public async Task<CaptainSelectionDecisionDetail> DetailAsync(AuthContext caller, string decisionId, string? tenantId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            RequireCaller(caller);
            string tenant = Tenant(caller, tenantId);
            CaptainSelectionDecision decision = await _Database.CaptainSelections.ReadDecisionAsync(tenant, decisionId, Owner(caller), token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Captain selection decision not found.");
            if (caller.MissionId != null && caller.MissionId != decision.MissionId) throw new KeyNotFoundException("Captain selection decision not found.");
            CaptainSelectionPolicyRevision policy = await _Database.CaptainSelections.ReadPolicyAsync(tenant, decision.PolicyRevisionId, token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Captain selection policy not found.");
            CaptainSelectionDecisionView view = await ViewAsync(decision, Owner(caller), token).ConfigureAwait(false);
            EnumerationResult<CaptainSelectionExecutionEvent> events = await _Database.CaptainSelections.EnumerateExecutionEventsAsync(tenant, decision.Id, Owner(caller), pageSize: 25, token: token).ConfigureAwait(false);
            if (view.SelectedCaptainId == null)
                foreach (CaptainSelectionExecutionEvent item in events.Objects) { item.ActualModel = null; item.ActualProvider = null; }
            return new CaptainSelectionDecisionDetail { Decision = view, Policy = policy.ToSettings(),
                Recovery = await _Database.CaptainSelections.ReadDecisionRecoveryAsync(tenant, decision.Id, token).ConfigureAwait(false),
                Candidates = await CandidatesCoreAsync(caller, decision, pageNumber, pageSize, token, policy).ConfigureAwait(false), ExecutionEvents = events,
                OmittedCandidates = await _Database.CaptainSelections.ReadOmittedCandidatesAsync(tenant, decision.Id, Owner(caller), Owner(caller), token).ConfigureAwait(false) };
        }

        /// <summary>Page only visible candidate comparisons; internal account and credential identifiers are omitted.</summary>
        public async Task<EnumerationResult<CaptainSelectionCandidateView>> CandidatesAsync(AuthContext caller, string decisionId, string? tenantId = null, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            RequireCaller(caller);
            CaptainSelectionDecision decision = await _Database.CaptainSelections.ReadDecisionAsync(Tenant(caller, tenantId), decisionId, Owner(caller), token).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Captain selection decision not found.");
            if (caller.MissionId != null && caller.MissionId != decision.MissionId) throw new KeyNotFoundException("Captain selection decision not found.");
            return await CandidatesCoreAsync(caller, decision, pageNumber, pageSize, token).ConfigureAwait(false);
        }

        private async Task<EnumerationResult<CaptainSelectionCandidateView>> CandidatesCoreAsync(AuthContext caller, CaptainSelectionDecision decision, int pageNumber, int pageSize, CancellationToken token, CaptainSelectionPolicyRevision? recordedPolicy = null)
        {
            EnumerationResult<CaptainSelectionCandidateRecord> page = await _Database.CaptainSelections.EnumerateCandidatesAsync(decision.TenantId, decision.Id, Owner(caller), Owner(caller), pageNumber, pageSize, token).ConfigureAwait(false);
            CaptainSelectionPolicyRevision? policy = recordedPolicy ?? await _Database.CaptainSelections.ReadPolicyAsync(decision.TenantId, decision.PolicyRevisionId, token).ConfigureAwait(false);
            return EnumerationResult<CaptainSelectionCandidateView>.Create(new EnumerationQuery { PageNumber = pageNumber, PageSize = pageSize }, page.Objects.Select((row, index) => new CaptainSelectionCandidateView
            {
                CaptainId = row.CaptainId, CaptainAvailable = row.CurrentCaptainExists, CaptainName = row.CaptainName ?? row.CaptainId, Model = row.ConfiguredModel,
                Rank = (pageNumber - 1) * pageSize + index + 1, Band = row.Band, ReasonCode = row.PrimaryReasonCode, AvailablePercent = row.AvailablePercent,
                CanSelect = row.Eligible && row.Band != Armada.Core.Enums.UsageSelectionBandEnum.Blocked
                    && (row.Band != Armada.Core.Enums.UsageSelectionBandEnum.Unknown || policy?.UnknownUsageBehavior == Armada.Core.Enums.UsageUnknownBehaviorEnum.AllowDeprioritized),
                NextReconsiderUtc = row.NextReconsiderUtc, CapturedUtc = row.EvidenceCapturedUtc, ActualSelection = row.CaptainId == decision.SelectedCaptainId,
                HypotheticalSelection = row.CaptainId == decision.HypotheticalCaptainId
            }).ToList(), page.TotalRecords);
        }

        private async Task<CaptainSelectionDecisionView> ViewAsync(CaptainSelectionDecision row, string? owner, CancellationToken token)
        {
            List<CaptainSelectionDecisionView> rows = await _Database.CaptainSelections.ReadDecisionViewsAsync(row.TenantId, new[] { row.Id }, owner, owner, token).ConfigureAwait(false);
            return rows.FirstOrDefault() ?? throw new KeyNotFoundException("Captain selection decision not found.");
        }

        /// <summary>Resolve primary resource visibility before parsing secondary paging/filter arguments.</summary>
        public async Task ValidateScopeAsync(AuthContext caller, string id, bool decision, string? tenantId = null, CancellationToken token = default)
        {
            if (!decision) { await MissionAsync(caller, id, token).ConfigureAwait(false); return; }
            RequireCaller(caller);
            CaptainSelectionDecision? row = await _Database.CaptainSelections.ReadDecisionAsync(Tenant(caller, tenantId), id, Owner(caller), token).ConfigureAwait(false);
            if (row == null || caller.MissionId != null && caller.MissionId != row.MissionId)
                throw new KeyNotFoundException("Captain selection decision not found.");
        }

        private async Task<Mission> MissionAsync(AuthContext caller, string id, CancellationToken token)
        {
            RequireCaller(caller);
            if (caller.MissionId != null && caller.MissionId != id) throw new KeyNotFoundException("Mission not found.");
            Mission? mission = await _Database.Missions.ReadAsync(id, token).ConfigureAwait(false);
            if (mission == null || String.IsNullOrWhiteSpace(mission.TenantId)
                || !caller.IsAdmin && (mission.TenantId != caller.TenantId || !caller.IsTenantAdmin && mission.UserId != caller.UserId))
                throw new KeyNotFoundException("Mission not found.");
            return mission;
        }

        private static void RequireCaller(AuthContext caller)
        {
            ArgumentNullException.ThrowIfNull(caller);
            if (!caller.IsAuthenticated || caller.AskThreadId != null || !caller.IsAdmin && (String.IsNullOrWhiteSpace(caller.TenantId) || String.IsNullOrWhiteSpace(caller.UserId)))
                throw new UnauthorizedAccessException("Selection history requires an authenticated mission-visible identity.");
        }

        private static string? Owner(AuthContext caller) => caller.IsAdmin || caller.IsTenantAdmin ? null : caller.UserId;

        private static string Tenant(AuthContext caller, string? requested)
        {
            if (!String.IsNullOrWhiteSpace(requested) && requested != caller.TenantId && !caller.IsAdmin)
                throw new KeyNotFoundException("Captain selection decision not found.");
            string? tenant = String.IsNullOrWhiteSpace(requested) ? caller.TenantId : requested;
            if (String.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("An explicit tenant is required.", nameof(requested));
            return tenant;
        }
    }
}
