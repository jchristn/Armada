# Usage selection: U0 baseline and contract review

Reviewed 2026-11-03 on feature/v1.3.0, starting at bf52253cc. The pure evaluator is the first executable contract. It is not wired to scheduling, settings, storage or HTTP. This record separates verified existing infrastructure from proposed integration work.

## Existing routing and admission

`MissionService.TryAssignAsync` obtains a vessel lease followed by `_CapacityLock`. It resolves preferences, selects a captain through `SelectCaptainAdmissionAsync`, checks launch policy and dock placement, then calls `Captains.TryClaimAsync`. That method uses `CaptainSlotStore.ClaimAsync` to commit a slot and mission assignment together. Branch metadata is updated afterward, before launch. Launch provisioning occurs outside the admission locks.

`FindRoutedCaptainAsync` honors requested captains and strict voyage overrides, persona defaults, review-author preference, persona priority definitions and tier floors before occupied-slot ordering. Usage ranking must operate within those same preference boundaries. `CaptainEligibility` and capacity exclusions are reused by diagnostics; a second eligibility implementation would break parity. Worker-envelope explanations currently use events rather than a standalone decision table. The new ledger must link existing evidence and not claim those events are already transactionally stored.

The schema head is 122. `CaptainSlotStore` currently owns its connection and transaction and cannot accept a decision callback. U1/U3 must refactor a transaction-scoped operation inside that store or introduce a domain admission store with the same conditional claim semantics. Wrapping the public TryClaim call in a new transaction on another connection is not atomic. Provider wrappers already supply real connections and SQLite's write gate, which can support the shared implementation. The shared method pattern in `MissionWaitIntervalMethods` demonstrates provider-aware dates and lease handling.

Capture persistence, memberships and settings do not currently share the admission capacity lock. A new revision protocol is required before enforcement; holding the selection lock alone is insufficient to reject a capture committed during selection. Observe is the deployment default and remains disconnected until history persistence is available. No partial implementation may turn enforcement on.

## Watson requirements discrepancy

Armada.Server references Watson 7.3.1. Its installed XML documentation at `~/.nuget/packages/watson/7.3.1/lib/net8.0/WatsonWebserver.xml` includes `WatsonWebserver.Core.ApiRequest` and typed routes. The requirements document's claim that those APIs do not exist describes a different version. There is no project-local replacement HTTP stack to remove.

For this feature, keep the installed Watson version and composition root; register new routes directly on Watson's authenticated route tables through a feature registrar, using typed DTO deserialization, server authorization, cancellation and explicit response status. Do not migrate unrelated existing registrars or downgrade Watson. U1 API work must compile-test the actual table overloads before relying on documentation examples. Existing session authentication, CORS, request history and OpenAPI registration remain required. The discrepancy is recorded, not silently treated as a reason to abandon the requirements.

## Evidence and identity fixtures

The executable fixtures live in `Services.UsageSelection`; capture parser and membership fixtures remain in `Services.UsageParsers`, `Services.UsageQuarantine`, `Services.UsageScheduler` and database usage suites. Source parsers continue to own normalization. The new evaluator consumes only sanitized typed windows and does not execute a harness or read raw payloads.

| Scenario | Normalized input | Expected hypothetical result |
|---|---|---|
| Shared Claude account A | Two eligible captains, 89% observed and 2 points reserved | Both BelowReserve; no duplicate headroom |
| Independent Codex account B | 40% observed and 2 reserved, same routing preference | Select B with 58 available points |
| Pinned A | Only A in eligible input | Wait; no invented fallback |
| Boundary | 88% used, no reservations, reserve 10 plus launch 2 | Constrained, permitted at equality |
| Headers count window | 40 of 100 requests with explicit unit | 60% observed headroom |
| Estimated capture | Derived source or non-Ok status | Unknown; cannot clear authoritative quota hold |
| Old successful capture | Older than min(existing horizon, configured cap) | UsageStale, irrespective of a newer failed retry |
| Model-scoped exhausted window | Opus window, known Sonnet captain | NoApplicableWindows, not account-wide exhaustion |
| Unknown Mux model/scope | Narrower window applicability not proven | Unknown; never fabricated available quota |
| Passed reset | Old exhausted capture whose reset passed | AwaitingFreshUsage, not zero utilization |
| Provider response | Proven future cooldown | AccountCooldown, even without usage identity |
| Corrupted estimate | NaN reservation plus proven 100% observed | Hard exhaustion remains blocked |

Host-local HMAC identity cannot prove that the same real account on two Harbors is one balance. A probe configuration group is not a usage budget identity. Live snapshots lack a reliable probe-start timestamp for debt settlement; U1 must add it and treat old missing timestamps as unsuitable for settlement.

## Pure evaluator contract

`UsageSelectionPolicy.Evaluate` takes a complete candidate set, validated `UsageSelectionSettings`, a supplied UTC time and an already-authorized soft-reserve exception. It returns a hypothetical enforced result for all rollout modes, leaving orchestration to store actual versus hypothetical decisions. Inputs are not mutated. Candidate IDs must be nonempty and distinct; exceeding MaximumCandidates returns CandidateLimitExceeded with no selected captain and no silently truncated comparison.

Machine reasons have no translated prose. New reasons currently include SelectedForHeadroom, SufficientHeadroom, AuthorizedReserveException, BelowReserve, AccountQuotaExhausted, AccountCooldown, UsageIdentityUnknown, UsageConfigurationChanged, UsageMissing, FutureObservation, UsageStale, UsageNotAuthoritative, UsageUncertainDebt, NoApplicableWindows, UsageEvidenceInvalid, UsageModelUnknown, UsageScopeUnknown, AwaitingFreshUsage and CandidateLimitExceeded. Existing eligibility/capacity reasons are passed through. Public route reason catalogs will be extended with translations when exposed, not by hard-coding new English labels in clients.

After the operator clarified strategy configurability, FirstAvailable preserves supplied routing rank. MostRemainingAllowance uses bucket, comfortable-first band, comparable headroom, occupied-slot ratio, known least-recent assignment and ordinal ID; MostUsedAllowance reverses eligible usage-band/headroom preference while preserving reserves. The common comparable-window signature must match across a whole equal-bucket/band group. Pairwise “compare headroom if comparable, otherwise compare age” can create cycles with three providers. A group containing incompatible signatures skips headroom ordering entirely, giving a deterministic total order independent of input enumeration.

## Proposed API packet

The resource paths and permission matrix are defined in sections 9 and 10 of the root plan. No route is claimed as implemented here. DTOs must distinguish actual versus hypothetical choice, intended versus actual model, observed usage versus estimates, and null retry/reset hints. Candidate detail is separately paginated. Existing mission authorization is the first gate; user-visible candidates are projected and counted only after authorization.

A representative preview response is:

```json
{
  "Mode": "Observe",
  "AlgorithmVersion": "usage-bands-v1",
  "ActualCaptainId": "cpt_existing_preference",
  "HypotheticalCaptainId": "cpt_healthy_account",
  "ReasonCode": "SelectedForHeadroom",
  "DecisionId": null,
  "IsPreview": true,
  "Candidates": {
    "PageNumber": 1,
    "PageSize": 25,
    "TotalRecords": 1,
    "Objects": [
      {
        "CaptainId": "cpt_healthy_account",
        "Band": "Comfortable",
        "ReasonCode": "SufficientHeadroom",
        "AvailablePercent": 58,
        "ObservedUsedPercent": 40,
        "ActiveReservationPercent": 2,
        "UnsettledPercent": 0
      }
    ]
  }
}
```

The example describes a future authorized projection, not the internal evaluator type or an exposed API today. Preview never persists a DecisionId, probes accounts or changes routing. Read-only callers cannot supply authorization flags. API errors and provider failures remain structured and sanitized.

## UX review and implementation handoff

No rendered UI changed in U0. The design review places Captain selection in mission detail, history in an expansion/drawer, policy in existing Settings, and reservation breakdowns in Captain Usage. It adds no sidebar page, Jobs page, duplicate breadcrumbs or floating report button. The comparison uses flexible details with compact identity/outcome columns and the shared table/refresh/modal controls. Waiting cards distinguish reported reset from guaranteed start. The test matrix requires all responsive/theme/permission/i18n states before exposing those controls.

The next milestone must implement relational history and live settings before dispatch integration. Validation of the pure evaluator is not evidence that admission, persistence, provider atomicity, routes or UI are complete. Full simulated-user validation remains U5; this review is a design/contract pass, not a fabricated screenshot or live session.

## Foundation validation

The core project built for net10.0 with zero warnings/errors. The console runner executed Services.UsageSelection, Services.UsageQuarantine and Services.UsageDefaults on net8.0 and net10.0: 50 passed, zero failed/skipped on each framework (26 new selection cases). The source-evidence immutability and permutation tests guard purity and total ordering. Existing quota/default cases protect current capture behavior. No product UI or route changed, so the UX evidence for this segment is the design review above; responsive runtime validation begins when controls are implemented.
