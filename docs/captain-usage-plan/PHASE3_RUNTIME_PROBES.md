# Phase 3: runtime probes and routing (AC3)

This document records the phase 3 behavior of `CAPTAIN_USAGE_PLAN.md` ("Runtime probes and routing"),
which adapters are enabled and why, and the named checks behind AC3, D4 and D6. It builds on the
phase 0 evidence in `PROBE_INTERFACES.md` and the phase 2 host execution and scheduler. Phases 4-7
(REST/MCP routes, dashboard limits tabs, baseline freeze, retention and telemetry) are not part of it.

## Audit before implementation

At start commit `b524da942` (origin/main, which already contains the completed recovery voyage), phases
0-2 were landed: the probe contract (`IUsageProbe`, `UsageProbeHost`, `UsageProbeResult`), the host
runner with the Custom contract, the scheduler and capture service, storage and settings. No phase 3
adapter, parser, header handler or quota routing existed: `src/Armada.Runtimes/Usage/` was absent, the
production runner was composed without a runtime probe registry (Auto answered `RuntimeProbeUnavailable`),
`ModelEndpointAuth.CreateHttpClient` returned null on the provider-default path, and Claude
`rate_limit_event` lines were discarded. Nothing from phases 3-7 was already satisfied.

## Source policy (D4)

| Runtime | Probe | Result | Why |
|---|---|---|---|
| Codex | `CodexUsageProbe` | Active: `codex app-server` stdio, `initialize`, `initialized`, `account/rateLimits/read` | Official interface verified from the installed binary's generated schema. No thread or turn request exists in the exchange, so no model is invoked. |
| ClaudeCode | `ClaudeUsageProbe` | Unsupported `ClaudeUsageEndpointUnverified` | The OAuth usage endpoint is undocumented and phase 0 could not prove the CLI credential may read it. The probe never reads the credential store or the network. Passive `rate_limit_event` lines are harvested instead. |
| Gemini | `GeminiUsageProbe` | Unsupported `GeminiQuotaUnavailable` | No read-only quota interface; a Derived estimate needs an operator budget that this build does not configure. |
| OpenCode | `OpenCodeUsageProbe` | Unsupported `UnsupportedOpenCode` | `opencode stats` is accounting and its format was not verified. |
| ApiEndpoint | `UnsupportedUsageProbe` | Unsupported `PassiveOnly` | Headers are harvested from existing inference calls; nothing is actively requested. |
| Custom runtime, Auto mode | `UnsupportedUsageProbe` | Unsupported `CustomCommandRequired` | Captures only through a Custom usage command. |
| Cursor, Mux, Pi | `UnsupportedUsageProbe` | Unsupported `Unsupported<Runtime>` with the capability reason | No verified source. |
| Any runtime, Custom mode | `CustomUsageProbe` | The administrator's executable contract | Moved out of the host runner unchanged; additionally refuses unnamed windows and a percentage that disagrees with its used and limit counts by more than one point. |

PTY is never an automatic fallback. With `AllowPty` on, Claude still answers Unsupported and says the
opted-in PTY source has no verified adapter; `/usage` is never sent as a prompt. `UsageProbeRegistry` is
composed once in `ArmadaServer` (Local mode) and in `HarborLinkHost` (Split mode), next to the existing
host runner, so probes run on the credential host.

`CodexUsageProbe` runs through `UsageProcessSession`: executable plus argument list (no shell), 64 KiB per
line and 256 KiB per session, stderr drained and discarded, a partial final line is `TruncatedOutput`,
an early exit is `TruncatedOutput`, an oversized line is `MalformedOutput`, a JSON-RPC error mentioning
authentication is `AuthMissing`, any other error is `ProviderError` with a redacted message, and a missing
CLI is Unsupported `RuntimeNotInstalled`. `Dispose` closes stdin, kills the whole process tree and waits
for the root to be reaped; it runs in `finally`, so the host deadline (`ProbeTimeout`) and cancellation
both leave nothing running.

## Parsers

- `CodexRateLimitUsage` (version 1): `rateLimitsByLimitId` first, then the backward-compatible `rateLimits`
  fills slots it did not report. Windows are named `<limitId>:primary|secondary`, durations come from
  `windowDurationMins`, resets from absolute `resetsAt`. Null windows and null percentages are skipped;
  no window at all is Unsupported `NoRateLimitWindows`; a percentage outside 0..100, a non-positive
  duration or an out-of-bounds reset rejects the observation (`MalformedOutput`).
- `ClaudeRateLimitUsage` (version 1): status in allowed, allowed_warning, rejected; utilization a 0..1
  fraction. An unknown status or a percent-form utilization is `UnknownEventVersion`; a null utilization
  stays null (a rejected status is never turned into 100 percent).
- `HeaderUsageCapture.Parse`: Anthropic `anthropic-ratelimit-{requests,tokens,input-tokens,output-tokens}-*`
  with RFC 3339 resets and OpenAI-style `x-ratelimit-*-{requests,tokens}` with duration resets, plus
  `retry-after`. Absent headers are Unsupported `HeadersAbsent` (never AuthMissing), 401/403 AuthMissing
  `CredentialRejected`, 5xx Failed `ProviderUnavailable` with only Retry-After kept, 429 keeps windows when
  present (else Failed `ProviderRateLimited`) and its Retry-After, malformed numbers `MalformedHeaders`.
- `UsageResetConverter`: absolute values as given, relative values from the observation time, all bounded to
  observation - 24 h .. observation + 8 days. `ProviderResetParser`'s 24-hour error policy is unchanged.

## Passive capture

- `ClaudeMissionStreamDecoder` adds each `rate_limit_event` to `MissionStreamDecodeResult.UsageObservations`
  with the time the line was read; it is still never output or transcript text. `CodexMissionStreamDecoder`
  is unchanged: `codex exec --json` limit events are unverified (phase 0), so nothing is harvested from it.
- `UsageRateLimitHandler` is the outermost handler of the client built by
  `ModelEndpointAuth.CreateHttpClient(endpoint, sink)`. It reports allowlisted headers (32 at most, values
  of 128 characters at most, host name only) right after the inner handler returns the response headers,
  before any body is read, and a failing sink never fails the call. With a sink the client is built for
  every auth type, closing the provider-default gap recorded in phase 0. `ApiAgentRuntime.UsageHeaders`
  is attached by the production client factory in both constructors; it keeps the latest observation and
  returns it with its real observation time.
- Persisting passive observations as snapshots is not part of phase 3; it follows with the phase 4 routes.

## Quota routing (D6)

`UsageQuotaPolicy.Evaluate(captain, observation, captainAccountKey, interval, now)`:

- NoChange for any status other than Ok, for a Derived source, for an observation older than
  max(2 x interval, 120 s) or more than 5 minutes in the future, and for another account's observation.
- Per-model windows (`UsageWindow.Model`) apply only to a captain whose `Model` matches; account-wide windows
  apply to every member. A window whose reset passed is expired.
- Hold when an applicable window is at or above 100 percent (until the latest exhausted reset, or the next
  freshness horizon when no reset was reported); Release when every applicable reported window is below.

`CaptainService.ApplyUsageQuotaAsync(captainId, decision)` holds only an idle captain (atomic reserve out of
Idle), never a working one, extends or clears only a quota-owned hold (reason prefix `usage quota: ` with the
observation time recorded), never touches manual, health, crash-loop, configuration or provider-error holds,
and skips any decision whose observation is not newer than the one that set the current hold. The existing
health tick lifts an expired quota hold like any other. Wiring the policy into the commit path is left to the
phase that persists passive observations; phase 3 delivers and proves the decision and the hold semantics.

## Named checks

| Criterion | Check | Observed |
|---|---|---|
| Codex success, partial, null, missing, malformed | `Services.UsageParsers` codex_fixture_success_keeps_every_window_and_duration, codex_partial_null_and_missing_windows, codex_malformed_values_reject_the_observation | PASS |
| No model invoked, account HMAC, request shape | codex_app_server_session_reads_limits_without_a_turn (real stdio session) | PASS |
| Auth, provider error, truncated EOF, oversize, CLI absent | codex_app_server_errors_truncation_and_oversize | PASS |
| Forced cleanup on timeout and cancellation | codex_hung_session_is_force_cleaned_on_timeout_and_cancel (root and child pids gone) | PASS |
| Claude passive event, unknown version, null window, truncated line | claude_rate_limit_event_is_harvested_not_transcribed, claude_rate_limit_event_versions_and_nulls | PASS |
| Custom unknown version, duplicates, units, `{}`, null windows, oversize | custom_contract_versions_duplicates_units_and_oversize | PASS |
| Relative and absolute resets, absent headers, 401/403/429/5xx | headers_absolute_and_relative_resets, headers_status_mapping_and_malformed_values | PASS |
| Capture attached before a known request; streamed EOF keeps headers | api_transport_capture_sees_known_requests_on_every_path (local HTTP fixture: URL, credential, payload, response) | PASS |
| Built-in probes never prompt; Unsupported stays Unsupported; PTY opt-in | builtin_probes_never_prompt_and_unsupported_stays_unsupported | PASS |
| D6 authority, freshness, session age, future skew | `Services.UsageQuarantine` only_fresh_authoritative_exhausted_windows_hold | PASS |
| Per-model, wrong account, null windows, passed resets | per_model_windows_wrong_account_null_windows_and_passed_resets | PASS |
| Quota-owned holds only, never interrupts work | captain_service_only_touches_quota_owned_holds | PASS |
| Compare-and-set | captain_service_compare_and_set_refuses_older_samples | PASS |
| Rejections preserve last-good | rejected_observation_preserves_last_good (real capture service commit) | PASS |
| Secrets in logs, excerpts, fixtures | `Services.UsageSecrets` (5 cases) | PASS |
| Headers reported before any body byte is read; count/length caps; failed and cancelled requests report nothing (Test Engineer) | `Services.UsageParsers` rate_limit_handler_reports_headers_before_the_body_is_read | PASS |
| Reset bounds inclusive at -24h/+8d, offset conversion, incomplete/over-limit/zero/signed headers reject (Test Engineer) | reset_converter_bounds_and_header_failures | PASS |
| 100 vs 99.99, skew and freshness edges, NaN/infinite windows, latest reset bounds the hold (Test Engineer) | `Services.UsageQuarantine` quota_policy_boundaries_and_invalid_windows | PASS |
| Assigned-idle and stopping captains never held, default hold length, legacy quota reason, argument guards (Test Engineer) | captain_service_quota_edge_states | PASS |

Guard-removal proofs: making the provider-default path skip the capture handler fails the transport case
("expected <1> but got <0>"); removing the compare-and-set fails the CAS case ("expected <SkippedStale> but
got <Released>"); removing the tree kill fails the cleanup case ("the app-server process is terminated was
false"); letting non-Ok statuses route fails the policy case; letting a release clear a foreign hold fails the
CaptainService case; dropping the decoder harvest fails both Claude cases.

Test Engineer proofs: buffering the response body before observing it in `UsageRateLimitHandler` fails only the
ordering case ("Error while copying content to a stream" after the 10 second gate; the transport case still
passes, so it does not prove ordering on its own); returning no failure for an incomplete header pair fails
both header cases ("expected <Failed> but got <Unsupported>"); treating 99 percent as exhausted fails the
boundary case ("expected <Release> but got <Hold>"); dropping the `CurrentMissionId` check in
`ApplyUsageQuotaAsync` fails the edge-state case ("expected <SkippedBusy> but got <Held>").

## Not verified

- No live provider was contacted: the Codex exchange runs against a fake app-server script that speaks the
  schema-verified wire shape, and the transport cases run against a local HTTP fixture. A live opt-in
  `account/rateLimits/read` on a credential host is still outstanding (`PROBE_INTERFACES.md` next step).
- The fake app-server and process-tree cases need a POSIX shell and are skipped on Windows.
- Claude's OAuth endpoint, PTY `/usage`, Codex session logs and OpenCode stats remain unimplemented by design.
