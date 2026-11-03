import type { CaptainSelectionCandidateView, CaptainSelectionStrategy } from '../types/models';

export const selectionStrategyKey: Record<CaptainSelectionStrategy, string> = {
  FirstAvailable: 'usageSelection.firstAvailable', MostRemainingAllowance: 'usageSelection.mostRemaining', MostUsedAllowance: 'usageSelection.mostUsed',
};
export const selectionReasons: Record<string, string> = {
  SelectedForHeadroom: 'usageSelection.reason.selected', SufficientHeadroom: 'usageSelection.reason.headroom',
  BelowReserve: 'usageSelection.reason.reserve', AccountQuotaExhausted: 'usageSelection.reason.exhausted', AccountCooldown: 'usageSelection.reason.cooldown',
  UsageIdentityUnknown: 'usageSelection.reason.identity', UsageConfigurationChanged: 'usageSelection.reason.configuration',
  UsageMissing: 'usageSelection.reason.missing', FutureObservation: 'usageSelection.reason.future', UsageStale: 'usageSelection.reason.stale',
  UsageNotAuthoritative: 'usageSelection.reason.authority', UsageUncertainDebt: 'usageSelection.reason.debt', NoApplicableWindows: 'usageSelection.reason.windows',
  UsageEvidenceInvalid: 'usageSelection.reason.invalid', UsageModelUnknown: 'usageSelection.reason.model', UsageScopeUnknown: 'usageSelection.reason.scope',
  AwaitingFreshUsage: 'usageSelection.reason.reset', NoEligibleCaptain: 'usageSelection.reason.eligible', CaptainConcurrencyLimit: 'usageSelection.reason.capacity',
  CandidateLimitExceeded: 'usageSelection.reason.limit', SelectionPersistenceUnavailable: 'usageSelection.reason.persistence',
  UsageHostMismatch: 'usageSelection.reason.host', UsageHostUnresolved: 'usageSelection.reason.hostUnknown', MissionScopeUnavailable: 'usageSelection.reason.missionScope',
  MissionNotPending: 'usageSelection.reason.notPending', VesselMissing: 'usageSelection.reason.vessel', VesselMisconfigured: 'usageSelection.reason.vesselConfiguration',
  RequestedCaptainInactive: 'usageSelection.reason.inactive', CaptainInactive: 'usageSelection.reason.inactive', CaptainQuarantined: 'usageSelection.reason.quarantine',
  ReviewAuthorExcluded: 'usageSelection.reason.review', CaptainScopeMismatch: 'usageSelection.reason.ownership', WorkerEnvelopeIncompatible: 'usageSelection.reason.envelope',
  PlacementWaiting: 'usageSelection.reason.placement', LaunchRetryPending: 'usageSelection.reason.retry', VoyageNotRunnable: 'usageSelection.reason.voyage',
  RoutingPreferenceExcluded: 'usageSelection.reason.routing', AuthorizedReserveException: 'usageSelection.reason.exception',
};
export function selectionReasonKey(code?: string | null): string {
  return code ? selectionReasons[code] ?? 'usageSelection.reason.routing' : 'usageSelection.reason.eligible';
}
export function selectionOutcomeKey(row: CaptainSelectionCandidateView, preview = false): string {
  if (preview && (row.actualSelection || row.hypotheticalSelection)) return 'usageSelection.proposed';
  return row.actualSelection ? 'usageSelection.selected' : row.hypotheticalSelection ? 'usageSelection.proposed'
    : row.band === 'Blocked' ? 'usageSelection.blocked' : row.band === 'Unknown' ? 'usageSelection.unknownState' : 'usageSelection.eligible';
}
