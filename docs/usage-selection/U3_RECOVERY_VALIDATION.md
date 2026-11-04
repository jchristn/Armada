# U3: enforced admission and recovery (v1.3.0)

U3 adds durable provider cooldowns, proven-unstarted cleanup and audited operator permissions to the existing atomic admission boundary. Observe/FirstAvailable remains the default; Enforce remains opt-in. This does not complete the remaining native operator workflows, telemetry, platform/benchmark matrix or release-upgrade drills in U4-U6.

## Operator validation

Use an isolated development deployment and disposable repository. Retain existing persona/model-fit, Harbor and concurrency settings.

1. Log into Dashboard as a tenant or system administrator with a user identity. In Server > Captain selection, opt into Enforce. Use fresh captures from the actual execution hosts and Unknown usage = Wait for a controlled test.
2. Create a Pending mission whose qualified preferred captain cannot currently run. Mission > Captain selection should explain the waiting candidate. Preview does not claim capacity or write history. An explicit preferred assignment requires eligible-fallback permission in Enforce; Observe preserves legacy non-strict routing behavior. A persona's ordinary default remains a preference rather than a pin.
3. Open **Recovery permissions**. Both permissions start off. Check **Allow eligible fallback**, enter an operator reason and save. The mission is reconsidered on the next normal dispatch tick. An alternative must still pass persona, tier, ownership, worker-envelope and capacity rules. A strict voyage assignment remains strict.
4. For a separate controlled account with headroom smaller than the protected reserve but larger than one attempt estimate, grant **Allow soft-reserve exception** with a reason. The next admission may use that reserve. A 100% exhausted account, authoritative quota hold or provider cooldown must still wait. Unknown evidence is not repaired by a reserve exception.
5. Open the admitted decision and its JSON. Check the exact applied recovery revision, author, reason and timestamp. New authorization revisions do not rewrite older decisions. Current permissions and historical applied permissions are displayed separately.
6. Edit permissions in two sessions. The second stale save must return a conflict and preserve the draft reason. Use the shared refresh control in the dialog to reload the current permission choices/revision, review them, and save again. Running work cannot be edited through this mutation. Viewers have no mutation control; cross-tenant resources are not exposed.
7. Run the classified-cooldown fixtures below. A structured provider quota/rate-limit failure creates a bounded cooldown for the proven tenant/host/runtime/account, shared by its other captains. The original execution key is captured atomically with the mission row before claiming the process exit. Repeated delivery does not extend the deadline; fresh captures do not remove it. After expiry, eligibility is reevaluated using actual usage; time alone does not invent a new balance.
8. Run the lifecycle fixtures. Fail provisioning before the launcher boundary or cancel before launch: only an execution with durable negative launch proof releases its estimate. Once a launcher might have run, failure/cancellation/verification leaves consumption unsettled until an authoritative active capture started after execution end plus lag proves settlement. A sibling's reservation stays intact.
9. Restart with pre-upgrade work. Adoption is conservative: it is not proof that a launcher was never entered, and present membership cannot prove which credentials an older process used. Such uncertainty follows the configured Unknown policy. In-place continuation of an existing running attempt retains its original active estimate; verification repair and re-dispatch create new admission decisions/reservations.

## Contracts and storage

- `POST /api/v1/missions/{id}/captain-selection/recovery` accepts `expectedRevision`, `allowFallback`, `reserveException`, and a required plain `reason` (1..512 characters). The server supplies identity, tenant and time. Tenant/system administrator and resource authorization both apply; mission-token and Ask identities cannot grant permissions.
- MCP `authorize_captain_selection_recovery` accepts `missionId` and a camelCase `recovery` object with the same fields. Both .NET clients provide `AuthorizeCaptainSelectionRecoveryAsync`.
- Selection summary returns current `recovery` and role-aware `canManageRecovery`; historical detail returns the exact applied `recovery`. Internal account keys are never returned.
- Mutation/admission serialize on the mission. Admission checks the current recovery revision and its immutable audit reference in the same transaction as assignment/reservations. Permission edits cannot alter running work.
- Migration 127 adds `usage_execution_sources`, `usage_selection_recovery` captured candidate reconsideration deadlines, and explicit execution/mission/decision cooldown receipt columns. It does not rewrite migrations 123-126. All four mission readers retain the private execution key with the same row snapshot, without serializing it. Sources bind actual host/account/configuration to the execution; cooldown application locks the same account state row as admission. Per-execution receipts make repeated cooldown delivery idempotent.
- History retention prunes bounded expired attribution/cooldown rows while protecting executing or unresolved debt. Recovery audits remain while the mission or retained decisions require them. Tenant purge includes the added records.

## Verification evidence

Implementation commits `a15a707f0` and `c1526497f` are pushed to `feature/v1.3.0`. U3 is complete; U4-U6 remain open.

| Check | Result |
| --- | --- |
| Full .NET 10 regression | 6,895 passed; zero failed; 14 skipped (6,909 total) |
| Focused .NET 10 selection/routing/API/contracts | 110/110 |
| .NET 8 selection/API/contracts/TUI parity | 121/121 |
| PostgreSQL / MySQL / SQL Server shared suites | 63/63 each |
| Dashboard tests / production build | 1,897/1,897; build succeeded |
| Native tests / static checks | 1,392/1,392 in 84 suites, clean exit; type checking and native/shared lint passed |
| Browser recovery UX | 24/24; screenshots inspected |
| Existing client parity inventories | TUI 703 and Mobile 710 entries; none planned |
| Repository Postman selection collection | Eight requests parsed; eight assertion scripts compiled |
| build-all v1.3.0 | Server, Dashboard and proxy v1.3.0/latest published and pulled; VSIX packaged |
| Container verification | Three local content probes; Linux amd64/arm64 manifests for all three images |

The provider checks include 50 simultaneous contenders for exactly one reservation budget, recovery revision races, cooldown/account scope, rollback, repair, stale snapshot ownership and migration hygiene. SQLite is included in the focused/full runs. The 14 full-suite skips cover toolchain-specific packs, provider-specific cases and unavailable baseline-upgrade fixtures. Broader OS/framework/CI, benchmark, native device and populated upgrade validation remain U5/U6 work.

The browser review renders real Dashboard React/CSS with private fixture responses. It covers light/dark themes at 1280/768/390 pixels in English/Spanish/Japanese, RTL/enlarged text, focus trapping/Escape/focus return, a 480-character unbroken audit reason, and conflict refresh without draft loss. See [geometry/focus report](recovery-layout-results.json) and the `screenshots/recovery-*` images. This is fixture-backed visual evidence, not a native device review or production deployment.

Authenticated REST, SDK, Core-client and MCP behavior is exercised against an isolated local server. The installed Postman CLI does not implement `status`; collection parsing and script compilation are separate from these real transport tests. No credentials or external workspace publication are involved.

See [container evidence](container-validation.json) for platform digests and probe scope. Local probes verify the assemblies and new recovery content without networking or starting a server; both platform manifests are present. The user deployment was not restarted or changed.
