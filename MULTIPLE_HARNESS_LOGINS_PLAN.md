# Multiple Harness Logins Plan (v1.6.0)

Branch: `feature/v1.6.0`, cut from `feature/v1.5.0` at `723fe44f8`. Target release: Armada 1.6.0 (pending ML-0.2).
Created 2026-11-27.

## Why this plan exists

Most people who run Armada seriously mix two kinds of captain. Frontier models run through their vendor's CLI harness
(Claude Code, Codex, Gemini CLI, Cursor) and are paid for by a subscription login. Open models run through an API
endpoint (Ollama, vLLM, a proxy such as Conductor) and are paid for, or not, per endpoint. The endpoint side already
handles more than one account: every `ModelEndpoint` carries its own base URL and key, so two Ollama hosts are two
endpoints. The harness side does not. An operator with three Claude subscriptions can create three Claude Code
captains, but all three run as whichever account the host user happens to be signed into, and Armada cannot tell them
apart.

That gap does not stay in one place. It runs downhill through the rest of the platform:

- **Launch.** `ClaudeCodeRuntime.ApplyEnvironment` (`src/Armada.Runtimes/ClaudeCodeRuntime.cs:288`) sets no
  `CLAUDE_CONFIG_DIR`. Codex, Gemini, Cursor, OpenCode and Pi set nothing either. Every captain of a runtime on a
  host uses the same login.
- **Usage.** `UsageCredentialReader` (`src/Armada.Runtimes/Usage/UsageCredentialReader.cs:13`) reads one
  process-wide Claude directory, and `UsageProbeHost` has no notion of whose login it is probing. Every same-runtime
  captain on a host resolves to one usage account key.
- **Quota.** When one captain hits its five-hour limit, the account cooldown (`usage_account_cooldowns`, keyed by
  host, runtime and account key) blocks every other captain of that runtime on the host, including the two whose
  subscriptions are untouched.
- **Selection.** `MostRemainingAllowance` exists (`CaptainSelectionStrategyEnum`) but it is one server-wide
  setting (`UsageSelectionSettings`), applies only in `Enforce` mode, ranks only inside a routing bucket, and has
  nothing to compare because every captain reports the same account.
- **Routing.** A dispatch can name a captain (`RequestedCaptainId`, `CaptainAssignmentOverride.CaptainId`) but
  cannot say "any Claude Code" or "whichever Claude Code login has the most headroom". Persona priorities match on
  runtime, model, endpoint and tier, never on a captain or a login. Pipeline stages (`PipelineStage.cs`) cannot
  target anything at all; they only name a persona.
- **Isolation.** `CaptainLaunchIsolationPlanner` redirects `CODEX_HOME`, and `HOME` for Gemini and Cursor, to an
  empty temp directory when `IsolateCaptainLaunch` is on, which hides the CLI's login entirely.

When this plan is complete, an operator can register three Claude Code logins (and any number of Codex, Gemini and
Cursor logins), bind each to one or more captains, see which account each captain runs as on every surface, and, when
dispatching a mission, building a voyage, setting a persona's priorities or configuring a pipeline stage, choose one of:

1. **This captain** (and therefore its login).
2. **Any captain on this login.**
3. **Any captain of this runtime** (optionally a model), first available.
4. **The captain of this runtime whose login has the most remaining allowance** ("lowest usage").

A limit on one login cools down only the captains on that login. The other logins keep working.

## How to use this document

Every actionable line is a checklist item with a stable ID (`ML-<phase>.<n>`). Update the box and append evidence as
you go; never delete an item. If an item turns out to be wrong, mark it `[~]` and say why.

| Mark | Meaning |
|---|---|
| `[ ]` | Not started |
| `[-]` | In progress (add your name or session and the date) |
| `[x]` | Complete. Evidence is required: commit hash, test suite ids, command and result, log path, or screenshot path |
| `[!]` | Blocked. Say on what, and who can unblock it |
| `[~]` | Dropped or superseded. Say why and what replaces it |

Tags on items:

- **[HUMAN]** only a person can do it (interactive vendor sign-ins, approvals, real subscriptions). An agent prepares
  everything around it and stops.
- **[DECISION]** needs Joel's answer before work that depends on it starts. Each one carries a recommendation. Record
  the answer and date inline and in the [Decisions log](#decisions-log).
- **[VERIFY]** a fact about a vendor CLI that the design depends on and that must be confirmed against the installed
  CLI version before code is written. Record the CLI version and the observed behavior as evidence.
- **Surfaces:** which product surfaces the item touches. Every surface named there has a matching item in Phases 6
  to 11; an item is not complete until those are too, or are explicitly marked not applicable with a reason.

Each item lists "Start at" locations. Line numbers are from commit `723fe44f8` and will drift; search for the quoted
symbol if the line has moved.

Record build and test results in the [Validation log](#validation-log) at the end, not inside items, except for the
one-line evidence pointer.

## Ground rules

These apply to every item. A change that breaks one of them is not complete, however well it works.

1. **Requirements.** Follow `~/Code/Agents/requirements`: `CODE_STYLE.md` (no `var`, no tuples, one class or enum per
   file, usings inside the namespace with System first, XML docs on public members only, `<exception>` tags,
   `ConfigureAwait(false)` and a `CancellationToken` on every async method, configurable public members with
   validated backing fields instead of constants, specific exception types, guard clauses, nullable enabled, no
   `Console.WriteLine` in library code), `BACKEND_ARCHITECTURE.md`, `BACKEND_TEST_ARCHITECTURE.md`,
   `FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, `I18N.md`, `TELEMETRY_REQUIREMENTS.md`,
   `AUTHENTICATION.md`, `SIMULATED_USER_TESTING.md`, `REPOSITORY_REQUIREMENTS.md`, `INSTALLERS.md`, `VERSIONING.md`
   and `WRITING_DOCUMENTS.md`. Project rules in `CLAUDE.md` and `AGENTS.md` also apply (one dispatcher, checked
   status transitions, no partial classes, no `JsonElement` access, no exception message matching in Core).
2. **No em-dashes anywhere:** code, comments, XML docs, docs, Postman, changelog, commit messages, UI copy, all nine
   locales. `grep -rnP '\x{2014}'` over the diff before every commit.
3. **Persistence.** Every schema change is a numbered migration for SQLite, PostgreSQL, MySQL and SQL Server, added as
   a cross-provider `*Schema.cs` class in `src/Armada.Core/Database/` and appended to all four provider lists,
   relational columns or child rows (no JSON blobs for fixed routing contracts, per `CLAUDE.md` "Persona Routing"),
   covered by the database parity run and the upgrade test from the v1.5.0 baseline. **The latest migration at plan
   time is 143** (`TimeBoundedScanSchema.Version`); new work starts at 144. Grep `const int Version` in
   `src/Armada.Core/Database/*Schema.cs` before taking a number, because parallel work can claim one.
4. **Tests.** New behavior gets Touchstone descriptors in `Test.Shared` so it runs under `Test.Automated`, xUnit and
   NUnit. A bug fix gets a regression test that fails before the fix. Front-end changes get Vitest (dashboard), Jest
   (mobile), TUI shape tests, and VS Code unit tests. `Services.SourceGuards` and `Services.MissionTransitions` stay
   green.
5. **API sync.** Any REST, MCP or WebSocket change updates `docs/REST_API.md`, `docs/MCP_API.md`,
   `docs/WEBSOCKET_API.md`, `Armada.postman_collection.json`, the API surface file
   (`scripts/common/generate-api-surface.sh` to `docs/api-surface-1.0.json`), and `Armada.Client` in the same commit.
6. **Compatibility.** No breaking REST or MCP changes in a minor release. Every new field is optional with a default
   that reproduces 1.5.0 behavior: a captain with no login runs exactly as it does today (host default login), a
   dispatch with no target routes exactly as today, and the server-wide usage strategy still applies when nothing
   overrides it. A 1.6.0 Admiral works with a 1.5.0 Harbor (logins unavailable on that Harbor, with a clear message,
   never a broken launch); a 1.5.0 Admiral talking to a 1.6.0 Harbor ignores the new capability.
7. **Armada never stores vendor credentials.** A login is a pointer to a CLI config directory on a specific host, plus
   what Armada observed about it (account email, plan, status). OAuth tokens and keychain items stay where the vendor
   CLI keeps them. No token, refresh token or API key is read into the Admiral, logged, persisted, sent over the Harbor
   link, or included in the black box. See ML-0.6 for API-key logins.
8. **Versions.** Per `VERSIONING.md`, do not change any version number until ML-0.2 records Joel's explicit approval.
   Do not tag, sign, or publish to NuGet, Open VSX, the stores or Docker Hub without a new instruction.
9. **Secrets and private data.** The repository is public. Never commit real account emails, organization names tied
   to an account, team ids, or config directory paths that contain a username. Examples, seeds, screenshots, Postman
   and docs use `personal@example.com`, `foundation@example.org` and `work@example.net` (three Claude logins),
   `codex-a@example.com` and `codex-b@example.com`, and paths under `~/.armada/logins/`. Live-run evidence that shows
   real accounts goes in the git-ignored `benchmarks/results/1.6.0/*.local.md`; check with `git check-ignore -v`
   before writing.
10. **Leave alone:** the untracked `assets/vscode/` folder. `AGENTS.md` changes only through ML-12.14.
11. **Commits.** Small, one item or a tight group per commit, message prefixed with the item id
    (`ML-2.3: launch Codex captains with their login's CODEX_HOME`). Commit only when asked; push only when asked.

## Definitions

| Term | Meaning |
|---|---|
| **Harness** | A vendor CLI runtime that authenticates with its own login: Claude Code, Codex, Gemini, Cursor, and (per ML-0.5) OpenCode, Pi and Mux. `ApiEndpoint` is not a harness; its accounts are model endpoints. |
| **Runtime login** (code: `RuntimeLogin`, prefix `rlg_`) | One vendor account on one host for one harness: a name, a host (the Admiral or a Harbor), a config directory on that host, and what Armada last observed (account email, plan, status, usage). Name pending ML-0.3. |
| **Host default login** | Whatever the host user is signed into in the CLI's default location (`~/.claude`, the macOS keychain, `~/.codex`, ...). A captain with no `LoginId` uses it, exactly as in 1.5.0. |
| **Bound captain** | A captain whose `LoginId` points at a runtime login. Several captains may share one login; they then share its quota and its cooldowns. |
| **Captain target** (code: `CaptainTarget`) | What a dispatch, voyage assignment, persona priority or pipeline stage asks for: a kind (`Captain`, `Login`, `Runtime`), the matching id or runtime and optional model, a selection strategy, and whether to wait or fall back. |
| **Lowest usage** | `CaptainSelectionStrategyEnum.MostRemainingAllowance`, measured per login (per provider account), not per captain. |

## Acceptance gate (definition of done for 1.6.0)

The feature is done when every line below passes on a release candidate, with evidence in the Validation log.

| Gate | Check |
|---|---|
| **A1** | Three Claude Code logins on one macOS host, each bound to one captain, each verified, show three different account emails on the dashboard Logins tab, the TUI, `armada runtime-login list`, MCP `enumerate` and the mobile app. [HUMAN] sign-in, agent verifies. |
| **A2** | A mission dispatched to each of the three captains runs under the bound account, proven by the account email recorded on the mission's launch record and by the vendor's own usage page moving for that account only. |
| **A3** | "Any Claude Code" dispatches ten missions with three captains of concurrency one; all three captains run work, none of the three logins is skipped while idle. |
| **A4** | "Lowest usage Claude Code" picks the login with the most remaining five-hour allowance in at least 9 of 10 dispatches, where usage was seeded so the logins differ by at least 20 points. Each selection decision names the login, its band and its remaining percent. |
| **A5** | Forcing a usage limit on one login (simulated provider error) cools down only the captains on that login; the next "any Claude Code" mission lands on another login within one dispatch pass. |
| **A6** | The same as A1 to A5 for two Codex logins (`CODEX_HOME`), with `IsolateCaptainLaunch` both on and off. |
| **A7** | Split mode: a Harbor on a second machine advertises its logins; a captain bound to a Harbor login runs there, and a captain cannot be bound to a login on a different host (validation error on every surface). |
| **A8** | A persona with priorities `[login A, login B, runtime ClaudeCode]` and a pipeline whose Judge stage targets "lowest usage Codex" route as configured; the routing explanation on the mission shows which matcher won. |
| **A9** | Upgrade from v1.5.0 on all four providers: every existing captain keeps working with no login, every existing persona and voyage routes the same, no data lost. |
| **A10** | Every product surface in Phases 6 to 11 is complete or marked not applicable with a reason; the full test matrix (ML-13.1) is green; UI/UX passes 1 and 2 (Phases 14 and 15) are complete with no open S1 or S2 findings. |

## Prior decisions and constraints

Read these before starting the item they name.

- **Persona routing contract** (`CLAUDE.md`, `archive/PERSONA_PRIORITIES.md`, `docs/CAPTAIN_ROUTING.md`): empty
  priority lists and explicit dispatch overrides keep legacy default-captain behavior; priorities are relational child
  rows across all four providers; the Touchstone, REST/MCP and client-surface tests change together. Applies to ML-5.4.
- **Usage accounts** (`archive/USAGE_BASED_CAPTAIN_SELECTION_PLAN.md`, `archive/CAPTAIN_USAGE_PLAN.md`): raw account
  identity is HMAC'd on the host and never leaves it (`UsageProbeResult.cs:11,41`). This plan relaxes that for one
  field only, the account email, and only after ML-0.4. Applies to ML-3.2 and ML-4.2.
- **Thread MCP planning keeps logins visible** (`CaptainThreadMcpPlanner.cs:16`, test
  `CaptainThreadMcpPlannerSuite` "cli_plans_never_hide_login"). The new login environment must preserve that
  invariant, not work around it. Applies to ML-2.4.
- **One dispatcher** (`AdmiralService`); selection code may raise a dispatch wake but never runs a pass. Applies to
  Phase 5.
- **Mux already has a per-captain config directory** (`MuxCaptainOptions.ConfigDirectory`). Fold it into logins
  rather than keep two mechanisms (ML-2.6).

## Phase 0: Setup and baseline

- [ ] **ML-0.1** Create `feature/v1.6.0` from `feature/v1.5.0` at `723fe44f8` (or from `main` if 1.5.0 has merged by
  then; record which). Move this file onto the branch as its first commit. Evidence:
- [ ] **ML-0.2** [DECISION] Release version. The request named the branch `feature/v1.6.0`; `VERSIONING.md` still
  requires explicit approval before any version number moves. Recommendation: 1.6.0 (new, backward-compatible
  functionality, so a MINOR bump). Version locations to change once approved: `src/Directory.Build.props:8`,
  `src/Armada.Helm/Armada.Helm.csproj:11`, `src/Armada.Core/Constants.cs:25`, dashboard, VS Code and mobile
  `package.json` (and lock files), `src/Armada.Mobile/app.config.ts:44`, Docker compose tags
  (`docker/armada/compose.yaml`, `compose.split.yaml`, `docker/demo/compose.yaml`, `docker/proxy/compose.yaml`),
  Postman description and examples, and the expectations in `Services.ReleaseVersion`. Answer:
- [ ] **ML-0.3** [DECISION] Name of the concept in code and UI. "Login" collides with signing into Armada itself, and
  "account" collides with usage account groups. Recommendation: `RuntimeLogin` in code (prefix `rlg_`), "Logins" as
  the Captains hub tab, "Runtime login" in forms and docs, `armada runtime-login` in Helm. Answer:
- [ ] **ML-0.4** [DECISION] Showing account identity. The whole point is to tell logins apart, so the account email
  the CLI reports (`claude auth status` prints `email`, `orgName` and `subscriptionType`) has to be visible. 1.5.0
  hashes it on the host. Recommendation: store the email and plan on the login row (tenant-scoped, visible to users
  who can read captains), add a server setting `RuntimeLogins.IdentityDisplay` (`Full` default, `Masked` shows
  `j***@example.com`), never send it to telemetry or the black box, and keep the HMAC account key as the join key for
  usage. Answer:
- [ ] **ML-0.5** [DECISION] Which runtimes get logins in 1.6.0. Recommendation: Claude Code, Codex, Gemini and Cursor
  (the subscription harnesses, gates A1 to A6 cover Claude and Codex); OpenCode and Pi with config directory only and
  no identity probe; Mux by migrating its existing `ConfigDirectory`. `ApiEndpoint` stays on model endpoints. Answer:
- [ ] **ML-0.6** [DECISION] API-key logins (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY` instead of a
  subscription). Supporting them means Armada stores a secret, which ground rule 7 forbids today. Recommendation: not
  in 1.6.0; a login can be `ConfigDirectory` or `HostDefault` only. If an operator needs a key, it goes in the login's
  config directory using the CLI's own mechanism. Revisit with `AUTHENTICATION.md` credential rules in a later
  release. Answer:
- [ ] **ML-0.7** [DECISION] "Antigravity". Armada has no Antigravity runtime (`AgentRuntimeEnum` lists ClaudeCode,
  Codex, Gemini, Cursor, Mux, OpenCode, ApiEndpoint, Custom, Pi). Google's agentic IDE has no headless CLI that
  Armada can drive the way it drives `claude --print`. Recommendation: treat Google logins as Gemini CLI logins in
  1.6.0, and track an Antigravity runtime as a separate backlog item once a scriptable CLI exists. Answer:
- [ ] **ML-0.8** [DECISION] Sign-in flow. Vendor sign-in is interactive OAuth in a browser. Recommendation: Armada does
  not drive OAuth. It creates the config directory, shows the exact command to run on that host (for example
  `CLAUDE_CONFIG_DIR=~/.armada/logins/rlg_x claude auth login`), offers a one-click "Open terminal here" in Harbor
  (ML-10.2), and a Verify action that probes the result. Also offer "Adopt host default login", which records the
  default location without moving anything. Answer:
- [ ] **ML-0.9** [DECISION] Lowest-usage semantics when the server's usage mode is `Observe` (the default). An
  explicit per-dispatch, per-persona or per-stage `MostRemainingAllowance` should rank by usage even when the global
  mode does not enforce it, otherwise the option silently does nothing. Recommendation: an explicit strategy ranks by
  the freshest usage observation in any mode except `Disabled`; hard gates (reserve, cooldown) apply only in
  `Enforce`, as today; logins with unknown usage rank after logins with known headroom and the decision says so.
  Answer:
- [ ] **ML-0.10** [DECISION] Voyage captain assignments are stored as JSON (`Voyage.CaptainOverridesJson`), which
  predates the relational rule. Recommendation: add a `voyage_captain_assignments` child table in migration 14x with a
  backfill from the JSON, read both during 1.6.0, write only the table, and drop the column in a later release
  (record the deprecation in `docs/UPGRADING.md` and the deprecation doc, wherever it lives at the time). Answer:
- [ ] **ML-0.11** Baseline: warning-free Release build; `Test.Automated` on net8.0 and net10.0; xUnit and NUnit
  adapters; dashboard `npm ci && npm run build && npm run test:run`; mobile `npm run test:ci`; VS Code unit tests;
  TUI parity check. Record counts so later regressions are attributable. Evidence:
- [ ] **ML-0.12** [VERIFY] For each harness in ML-0.5, on the installed CLI version, record in the Validation log:
  the environment variable or flag that relocates its config and credentials; where credentials live on macOS,
  Linux and Windows (file or OS keychain, and whether the keychain item name changes with the config directory); the
  non-interactive command that reports the signed-in identity and its output format; the sign-in command. Starting
  points, all to be confirmed:

  | Runtime | Relocation | Identity command | Notes to check |
  |---|---|---|---|
  | Claude Code | `CLAUDE_CONFIG_DIR` | `claude auth status` (JSON: `email`, `orgName`, `subscriptionType`, `loginExpiresAt`) | macOS keychain item name per config directory; `UsageCredentialReader.cs:28` already skips the default item when the variable is set |
  | Codex | `CODEX_HOME` | `codex login status` | `auth.json` inside `CODEX_HOME`; interaction with the isolation temp `CODEX_HOME` |
  | Gemini | `GEMINI_CLI_HOME` (or `HOME`) | to confirm | keychain item `gemini-cli-oauth` / `main-account` in `GeminiCredentialStore.cs:20` |
  | Cursor | `HOME` (no dedicated variable known) | `cursor-agent status` | whether redirecting `HOME` breaks git, ssh or toolchains in the dock |
  | OpenCode | `XDG_DATA_HOME` / `XDG_CONFIG_HOME` | none | `auth.json` location |
  | Pi | `PI_CODING_AGENT_DIR` | none | |

  Evidence:

## Phase 1: Domain model and persistence

- [ ] **ML-1.1** `RuntimeLogin` model in `src/Armada.Core/Models/RuntimeLogin.cs` with `Id` (`rlg_`, add
  `RuntimeLoginIdPrefix` to `Constants.cs`), `TenantId`, `UserId`, `Name` (1 to 64 characters, unique per tenant,
  host and runtime), `Runtime` (`AgentRuntimeEnum`, harness runtimes only), `HarborId` (null means the Admiral host),
  `Mode` (`RuntimeLoginModeEnum`: `HostDefault`, `ConfigDirectory`), `ConfigDirectory` (host-local path, `~`
  expanded on the host, required for `ConfigDirectory`), `AccountEmail`, `AccountOrganization`, `AccountPlan`,
  `Status` (`RuntimeLoginStatusEnum`: `Unverified`, `SignedIn`, `SignedOut`, `Expired`, `Unreachable`, `Error`),
  `StatusDetail`, `LoginExpiresUtc`, `LastVerifiedUtc`, `MaxConcurrentMissions` (null is unlimited, else 1 to 64;
  caps missions across every captain sharing the login), `Notes`, `Active`, `CreatedUtc`, `LastUpdateUtc`. Setters
  validate. One enum per file with `[JsonConverter(typeof(JsonStringEnumConverter))]`. Start at:
  `Models/Captain.cs`, `Models/ModelEndpoint.cs` (closest existing pattern). Evidence:
- [ ] **ML-1.2** `Captain.LoginId` (nullable). Validation on create and update: the login exists in the same tenant,
  its runtime equals the captain's runtime, and its host equals the captain's launch host (Admiral, or the
  captain's Harbor). Changing runtime or host clears or rejects a mismatched login with a specific exception type
  (`RuntimeLoginMismatchException`). Start at: `Models/Captain.cs:53`, `CaptainService`,
  `src/Armada.Server/CaptainUpdateFieldPresence.cs`, `EntityUpdateMerger.cs`. Evidence:
- [ ] **ML-1.3** Migration 144 (`RuntimeLoginSchema`): `runtime_logins` table with indexes on
  (`tenant_id`, `runtime`), (`tenant_id`, `harbor_id`), unique (`tenant_id`, `harbor_id`, `runtime`, `name`); column
  `captains.login_id` with an index. Additive, no backfill: existing captains stay on the host default login. All
  four providers. Evidence:
- [ ] **ML-1.4** `IRuntimeLoginMethods` and the four provider implementations (create, read, update, delete, enumerate
  with paging, sorting and filters on runtime, host, status, name, account email; count captains per login). Follow
  the interface-per-entity pattern and provider folder rules. Add to `DatabaseDriver` and the factory. Start at:
  `Database/Sqlite/Implementations/CaptainMethods.cs` and its three siblings. Evidence:
- [ ] **ML-1.5** Captain persistence for `login_id` in all four `CaptainMethods` (INSERT, UPDATE, reader, and the
  partial-update paths such as the `usage_capture_harbor_id` update at Sqlite `CaptainMethods.cs:259`). Evidence:
- [ ] **ML-1.6** Delete semantics: deleting a login with bound captains is refused (`409`) unless the request names a
  replacement login or `unbind=true`; deleting a Harbor cascades its logins to `Unreachable`, not deletion. Tenant
  scoping on every query, tested across tenants. Evidence:
- [ ] **ML-1.7** `RuntimeLoginService` in Core: CRUD with validation, binding and unbinding captains, status updates
  from probes (Phase 3), and `login.changed` events. Every mutation is audited through the existing audit path.
  Evidence:
- [ ] **ML-1.8** Tests: `Database.RuntimeLogin` (all four providers through the parity run), `Models.RuntimeLogin`
  (setter validation, defaults), `Services.RuntimeLoginService` (mismatch rules, delete semantics, tenant isolation).
  Evidence:

## Phase 2: Launch every captain with its login

- [ ] **ML-2.1** `RuntimeLoginEnvironmentPlanner` (Runtimes): given a captain, its resolved login and the launch
  host, returns the environment overrides that point the harness at the login's config directory, per the ML-0.12
  table. `HostDefault` returns nothing (1.5.0 behavior). The planner is the only place that knows per-runtime variable
  names. Start at: `BaseAgentRuntime.cs:327` (`ApplyEnvironment` hook), `CaptainLaunchIsolationPlanner.cs`.
  Evidence:
- [ ] **ML-2.2** Apply the plan in `BaseAgentRuntime.StartAsync` after the caller environment and per-runtime
  `ApplyEnvironment`, and reconcile with isolation overrides (ML-2.4) so the login always wins for credential
  locations. Directory must exist; a missing directory fails the launch with `RuntimeLoginUnavailable` (new
  `RuntimeFailureKindEnum` value, classified as captain-unavailable) instead of silently using the default login.
  Evidence:
- [ ] **ML-2.3** Per-runtime wiring and tests for Claude Code, Codex, Gemini, Cursor, OpenCode and Pi, each asserting
  the exact environment produced for `HostDefault` and `ConfigDirectory`, and that no other captain's variables leak
  in. Start at: each runtime's `ApplyEnvironment` (`ClaudeCodeRuntime.cs:288`, `CodexRuntime.cs`,
  `GeminiRuntime.cs`, `CursorRuntime.cs`, `OpenCodeRuntime.cs`, `PiRuntime.cs:250`). Evidence:
- [ ] **ML-2.4** Isolation that keeps the login. With `IsolateCaptainLaunch` on, `CaptainLaunchIsolationPlanner`
  points `CODEX_HOME` and Gemini/Cursor `HOME` at an empty temp directory, which hides credentials today (also a
  1.5.0 bug for host default logins; add a regression test that fails first). Rework it so the MCP config is
  isolated and the credential location is the login's (or the host default's): for Codex, pass MCP config by `-c`
  overrides or seed the scoped `CODEX_HOME` with a link to the login's `auth.json`; for HOME-based runtimes, prefer a
  dedicated variable from ML-0.12 over `HOME`. Keep "cli_plans_never_hide_login" green and add the same invariant
  for full isolation. Evidence:
- [ ] **ML-2.5** Every launch site resolves the captain's login, not only missions: `AgentLifecycleHandler.cs:1155`
  (missions) and `:800` (model validation), `CaptainChatService.cs:720`, `PlanningSessionCoordinator.cs:888` and
  `:1289`, `ObjectiveRefinementCoordinator.cs:700` and `:999`, `CaptainPromptRunner.cs:125`,
  `VesselContextService.cs:277`, `ModelProbeExecutor.cs:179`. Add a `Services.SourceGuards` rule that fails if a new
  `runtime.StartAsync` call site skips login resolution. Evidence:
- [ ] **ML-2.6** Mux: migrate `MuxCaptainOptions.ConfigDirectory` into a `RuntimeLogin` (migration 145 backfill
  creates one login per distinct Mux config directory and binds the captains), keep reading the old option for one
  release, and record the deprecation. Evidence:
- [ ] **ML-2.7** Split mode transport. `HarborLaunchRequest` gains optional `LoginId` and `LoginConfigDirectory`
  (additive, protocol stays 1.0, per `docs/HARBOR_PROTOCOL.md:146`). The Harbor resolves `~` and validates the
  directory locally, and applies the same planner in `LocalHarborJobRunner.cs:303`. A Harbor without the
  `runtime-logins` capability rejects a launch that carries a login with a typed error, and the Admiral refuses to
  route a bound captain to it (blocker reason `HarborLacksRuntimeLogins`). Evidence:
- [ ] **ML-2.8** Launch record: the mission's launch record and black box entry gain `LoginId` and the login's
  account email as observed at launch (masked per ML-0.4 setting), so gate A2 is provable after the fact. Evidence:
- [ ] **ML-2.9** Per-login concurrency: admission counts active missions across all captains bound to a login and
  holds new work when `MaxConcurrentMissions` is reached (typed blocker `LoginConcurrencyLimit`). Evidence:
- [ ] **ML-2.10** Tests: `Services.RuntimeLoginEnvironment`, `Services.CaptainLaunchIsolation` (extended),
  `Services.HarborRuntimeLogins`, `E2E.SplitModeRuntimeLogins` in `scripts/common/e2e-split-mode.sh`, and a
  regression for the isolation credential bug. Evidence:

## Phase 3: Discover, verify and sign in

- [ ] **ML-3.1** `IRuntimeLoginProbe` per harness (Claude, Codex, Gemini, Cursor; none for OpenCode and Pi per
  ML-0.5): runs the identity command from ML-0.12 with the login's environment, parses structured output into a
  typed result class (no `JsonElement`), and maps exit codes and output to `RuntimeLoginStatusEnum`. Probes never
  read token values. Timeouts are configurable (`RuntimeLogins.ProbeTimeoutSeconds`, default 20, 5 to 120). Start at:
  `src/Armada.Runtimes/Usage/` probes, `UsageProbeHostRunner.cs`. Evidence:
- [ ] **ML-3.2** Probe execution on the right host: on the Admiral through `LocalHostProcessExecutor`, on a Harbor
  through a new `runtime-login.probe` request (additive message, Harbor capability `runtime-logins`). Results update
  the login row and emit `login.changed`. Evidence:
- [ ] **ML-3.3** Scheduled verification: a background check re-probes each active login every
  `RuntimeLogins.VerifyIntervalMinutes` (default 60, 5 to 1440) and before first use after an Admiral restart.
  A login moving to `SignedOut` or `Expired` quarantines its bound captains with reason "runtime login signed out"
  until it verifies again, and raises an Armada notification. Warn when `LoginExpiresUtc` is within
  `RuntimeLogins.ExpiryWarningDays` (default 7). Evidence:
- [ ] **ML-3.4** Discovery: `GET /api/v1/runtime-logins/discover?harborId=` lists candidate login locations on the
  host that Armada does not yet track: the host default for each installed harness, and any directory under the
  managed root `RuntimeLogins.ManagedRoot` (default `~/.armada/logins`). Each candidate shows the probed identity.
  "Adopt" turns a candidate into a login. Harbor advertises its candidates in its capability report. Evidence:
- [ ] **ML-3.5** Sign-in helper per ML-0.8: `POST /api/v1/runtime-logins` with `Mode=ConfigDirectory` and no path
  creates `<ManagedRoot>/<id>` on the host; `GET /api/v1/runtime-logins/{id}/sign-in` returns the exact sign-in
  command for that host's shell (POSIX and PowerShell variants), the directory, and the verify follow-up. No command
  is executed by the Admiral. Evidence:
- [ ] **ML-3.6** Duplicate detection: if two logins on the same host probe to the same account email and runtime,
  mark both with a warning ("same account, quotas are shared") but do not block; usage account keys will merge them
  for quota purposes (ML-4.3). Evidence:
- [ ] **ML-3.7** `armada doctor` reports, per host and harness, the logins, their status, expiry, and bound captains,
  and flags captains on signed-out logins. Evidence:
- [ ] **ML-3.8** Tests: `Services.RuntimeLoginProbe` with recorded CLI outputs for each harness (signed in, signed out,
  expired, CLI missing, timeout, garbage output), `Services.RuntimeLoginVerification` (quarantine and release),
  `E2E.RuntimeLoginDiscovery`. Evidence:

## Phase 4: Usage and quota per login

- [ ] **ML-4.1** Thread the login through usage capture: `UsageProbeHost` gains `LoginId` and the resolved config
  directory; `UsageProbeHostRunner` sets the login environment; `UsageCredentialReader.ClaudeDirectory` and the Gemini
  and Codex readers take the directory as a parameter instead of reading the process environment. Start at:
  `Services/UsageProbeHost.cs`, `Runtimes/Usage/UsageCredentialReader.cs:13`, `ClaudeUsageProbe.cs:32`,
  `CodexUsageProbe.cs:80`, `GeminiUsageProbe.cs:32`. Evidence:
- [ ] **ML-4.2** Account keys per login: pass the login id as the `profile` argument of
  `UsageAccountResolver.DeriveAccountKey` (`Services/UsageAccountResolver.cs:258`), so three logins produce three
  account keys and three memberships. Host default captains keep today's key so 1.5.0 history stays joined. Close and
  reopen memberships when a captain's login changes (`UsageAccountMembership` is already effective-dated). Evidence:
- [ ] **ML-4.3** Capture once per login, not once per captain: when several captains share a login, schedule one
  capture for the login and attribute windows to all of them. Captain-level capture settings remain as the override
  for host default captains. Evidence:
- [ ] **ML-4.4** Cooldowns per login: a `UsageLimit` failure on a bound captain records the cooldown under that
  login's account key, so only captains on that login are gated (`CaptainSelectionLaunchBoundary.cs:202`,
  `UsageSelectionPolicy.cs:157`). Quarantine on `AdmiralService.cs:1112` extends to the login's other captains with
  the same reset time. Verify `CaptainService.ApplyUsageQuotaAsync` (`:503`) has a live caller; if not, wire it or
  record why it is dead and remove it. Evidence:
- [ ] **ML-4.5** Usage read model by login: `GET /api/v1/runtime-logins/{id}/usage` and a `usage` summary on login
  list rows (five-hour and weekly percent used, reset times, band, freshness), reusing `UsageWindow`. Evidence:
- [ ] **ML-4.6** Tests: `Services.UsagePerLogin` (three logins, distinct keys, distinct windows, cooldown isolation),
  extended `Database.UsageEnforcement`, and a regression showing that in 1.5.0 one captain's limit blocked its
  siblings. Evidence:

## Phase 5: Selection targets (specific, any, lowest usage)

- [ ] **ML-5.1** `CaptainTarget` model (Core): `Kind` (`CaptainTargetKindEnum`: `Captain`, `Login`, `Runtime`),
  `CaptainId`, `LoginId`, `Runtime`, `Model` (optional, with `Runtime`), `Strategy`
  (`CaptainSelectionStrategyEnum?`, null means the server setting), `Strict` (wait for a match instead of falling
  back). Validation: exactly the fields the kind needs. One resolver, `CaptainTargetResolver`, turns a target into the
  candidate set and is used by every caller below. Evidence:
- [ ] **ML-5.2** Missions: `Mission` and the REST `MissionRequest` gain an optional `CaptainTarget`. The existing
  `RequestedCaptainId` and `Tier` keep working and are equivalent to `Kind=Captain` and the tier fallback; when both
  are sent, the target wins and the response says so. Persisted as typed columns (migration 146), never in the
  description. Start at: `Models/Mission.cs:72`, `src/Armada.Server/MissionRequest.cs:49`. Evidence:
- [ ] **ML-5.3** Voyages: `CaptainAssignmentOverride` gains `LoginId`, `Runtime`, `Model` and `Strategy` (so a voyage
  can say "Worker: lowest usage Claude Code; Judge: any Codex"). Storage per ML-0.10. Start at:
  `Models/CaptainAssignmentOverride.cs`, `MissionCaptainRouter.ResolvePreferredCaptainAsync` (`:344`). Evidence:
- [ ] **ML-5.4** Personas: `PersonaCaptainPriority` gains `CaptainId` and `LoginId` matchers (AND with the existing
  runtime, model, endpoint and tier fields), and `Persona` gains `SelectionStrategy` (null keeps the server setting).
  Same additions for `EscalationLadder` entries. Migration 147 adds the columns to the priority and ladder child tables
  in all four providers. Start at: `Models/PersonaCaptainPriority.cs`,
  `Services/PersonaCaptainPrioritySelector.cs:50`, `Models/Persona.cs:96-127`. Evidence:
- [ ] **ML-5.5** Pipeline stages: `PipelineStage` gains an optional `CaptainTarget` (child table
  `pipeline_stage_captain_targets`, migration 148). Precedence, documented in `docs/CAPTAIN_ROUTING.md` and enforced
  in one place: mission target, then voyage assignment for the stage's persona, then stage target, then persona
  priorities, then persona default captain, then general buckets. Start at: `Models/PipelineStage.cs`,
  `docs/CAPTAIN_ROUTING.md:315`. Evidence:
- [ ] **ML-5.6** Routing buckets: `MissionCaptainRouter.FindRoutedCaptainAsync` (`:269`) builds a
  `RequestedTarget` bucket from the resolved target, ahead of persona buckets; `Strict` targets get no fallback, as
  strict captain overrides do today (`:315`). Eligibility reasons gain `LoginMismatch`, `LoginSignedOut`,
  `LoginConcurrencyLimit`, `LoginCooldown`. Evidence:
- [ ] **ML-5.7** Lowest usage across a target, per ML-0.9: when a target or persona sets `MostRemainingAllowance`,
  `UsageSelectionPolicy` (`:59`) ranks candidates by their login's remaining allowance across the whole requested
  bucket, groups captains that share a login so one busy account is not counted twice, breaks ties by active missions
  then outcome score, and places unknown-usage logins after known ones. `MostUsedAllowance` (drain one login before
  the next) gets the same per-login treatment. The server-wide default still applies when nothing overrides it.
  Evidence:
- [ ] **ML-5.8** Decisions explain themselves: each `CaptainSelectionDecision` records the target, the strategy and
  where it came from (mission, voyage, stage, persona, server), the chosen login and its band and remaining percent,
  and why each rejected login lost. `GET /api/v1/missions/{id}/captain-selection` and the preview endpoint return
  these fields. Evidence:
- [ ] **ML-5.9** Dispatch preview: `POST /api/v1/captain-selection/preview` accepts a target (and persona) without a
  mission and returns who would be picked now and the runner-up, so every form can show "will run on: Claude Code,
  foundation@example.org (62% left)" before submitting. Evidence:
- [ ] **ML-5.10** Escalation and rework: escalation and Judge rules (`review-author excluded`, Judge strength) still
  apply on top of a target; when the only candidate is the author, a strict target waits and a non-strict one falls
  back, with the reason recorded. Escalated-from captain and login are both recorded. Evidence:
- [ ] **ML-5.11** Tests: `Services.CaptainTarget` (validation, resolver for each kind), extended
  `Services.CaptainRouting` and `Services.PersonaCaptainPriority` (captain and login matchers, precedence order),
  `Services.UsageSelectionPolicy` (per-login ranking, unknown usage, Observe vs Enforce per ML-0.9),
  `Services.PipelineStageTargets`, `E2E.CaptainSelectionApi` extended, `Services.CaptainAdmissionParity`. Gates A3,
  A4 and A8 as automated scenarios with seeded usage. Evidence:

## Phase 6: Server and APIs

- [ ] **ML-6.1** REST, new `Routes/RuntimeLoginRoutes.cs`: `GET /api/v1/runtime-logins`, `POST .../enumerate`,
  `POST` create, `GET/PUT/DELETE /{id}`, `POST /{id}/verify`, `GET /{id}/sign-in`, `GET /{id}/usage`,
  `GET /{id}/captains`, `GET /discover`, `POST /adopt`. Typed request and response DTOs, paging and filter contracts
  per `BACKEND_ARCHITECTURE.md`, OpenAPI entries, request history capture. Authorization per `AUTHENTICATION.md`:
  read with captain read permission, write with captain write, tenant scoped, tested across tenants. Evidence:
- [ ] **ML-6.2** REST, changed: captain create, update and get carry `LoginId` and an expanded `Login` summary
  (name, runtime, host, account email per ML-0.4, status); mission create, voyage create and dispatch accept
  `CaptainTarget` and the extended assignments; persona and pipeline create and update carry the new matcher,
  strategy and stage target fields; selection endpoints return ML-5.8 fields; `/api/v1/captain-selection/preview`.
  `CaptainRoutes.cs` enumerate gains a `loginId` filter. Evidence:
- [ ] **ML-6.3** MCP: new tools `list_runtime_logins` (or the `runtime_logins` case in `enumerate`),
  `get_runtime_login`, `create_runtime_login`, `update_runtime_login`, `delete_runtime_login`,
  `verify_runtime_login`, `get_runtime_login_sign_in`, `discover_runtime_logins`, `preview_captain_selection`;
  `create_captain` and `update_captain` take `loginId`; `dispatch` and `create_mission` take `captainTarget`
  (fixing the 1.5.0 gaps: `create_mission` has no captain id and `dispatch` omits `strictCaptain` from
  `captainAssignments`, `McpVoyageTools.cs:102`); persona and pipeline tools take the new fields. Mark each tool
  captain or operator in `docs/MCP_API.md`. Evidence:
- [ ] **ML-6.4** WebSocket: `login.changed` and `login.deleted` events with visibility rules matching captains;
  `captain.changed` carries `LoginId`; commands `list_runtime_logins`, `get_runtime_login`, `verify_runtime_login`;
  descriptors in `WebSocket/WebSocketSurface.cs`; client constants in `Armada.Client/Socket/ArmadaEventTypes.cs`.
  Evidence:
- [ ] **ML-6.5** Settings: `RuntimeLogins` section (`ManagedRoot`, `VerifyIntervalMinutes`, `ProbeTimeoutSeconds`,
  `ExpiryWarningDays`, `IdentityDisplay`) with setter validation, documented defaults, minimum and maximum, in the
  runtime settings API and on the dashboard Settings page. Evidence:
- [ ] **ML-6.6** Telemetry per `TELEMETRY_REQUIREMENTS.md`: metrics `armada_runtime_logins` (by runtime and status),
  `armada_runtime_login_verifications_total` (by result), `armada_captain_selections_total` gains `target_kind` and
  `strategy` labels, `armada_login_cooldowns_total`; a Grafana panel group "Runtime logins" (status, usage percent per
  login, cooldowns, selections by strategy) under `docker/armada/observability/grafana`. Labels use login id and name,
  never the account email. Evidence:
- [ ] **ML-6.7** Health: `/api/v1/health` details report signed-out and expiring logins as a warning, not a failure.
  Evidence:
- [ ] **ML-6.8** Legacy static views (`src/Armada.Server/wwwroot/views/captains.html`, `dispatch.html`): show the login
  read-only, or record that these views are deprecated and not extended. Evidence:
- [ ] **ML-6.9** Tests: `E2E.RuntimeLogin` (every route, status codes, paging, filters), `E2E.McpToolSuite` and
  `E2E.McpTenantIsolation` extended, `E2E.WebSocket` and `E2E.WebSocketSecurity` extended, `E2E.ApiContract`,
  `E2E.ResponseShape` (`[CollectionBound]` on new response models), `E2E.SurfaceScoping`,
  `E2E.PersonaSurfaceParity`. Evidence:

## Phase 7: SDK, Postman and API surface

- [ ] **ML-7.1** `Armada.Client`: new `RuntimeLoginsClient` sub-client (`client.RuntimeLogins.ListAsync`, `GetAsync`,
  `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `VerifyAsync`, `GetSignInAsync`, `GetUsageAsync`, `DiscoverAsync`,
  `AdoptAsync`), `CaptainSelectionClient.PreviewAsync`, `CaptainTarget` on `DispatchRequest` and
  `VoyageCreateRequest` (and `RequestedCaptainId` and `Tier` on `DispatchRequest`, missing in 1.5.0), event constants.
  XML docs, `127.0.0.1` in samples. Evidence:
- [ ] **ML-7.2** Client tests: `Client.Contract.RuntimeLogins`, `Client.RouteSurface`, `Client.Coverage` and
  `Infrastructure/ClientRouteMap.cs` updated. Evidence:
- [ ] **ML-7.3** `docs/SDK_MIGRATION_1.6.md` (short: new sub-client, new optional fields, nothing removed). Evidence:
- [ ] **ML-7.4** Postman `Armada.postman_collection.json`: new "Runtime Logins" folder (every route, folder and
  request descriptions, `runtimeLoginId` variable, example bodies with example.com accounts), updated Captains,
  Missions, Voyages, Personas, Pipelines and Captain Selection requests, a "Dispatch to lowest usage Claude Code"
  example and an "Any Codex" example. `E2E.CollectionSurface` green; no em-dashes. Evidence:
- [ ] **ML-7.5** Regenerate `docs/api-surface-1.0.json` with `scripts/common/generate-api-surface.sh`; update
  `docs/API_SURFACE_1.0.md` if it lists surfaces by hand. Evidence:

## Phase 8: CLI and TUI

- [ ] **ML-8.1** Helm: `armada runtime-login list|show|add|update|remove|verify|sign-in|discover|adopt` (name per
  ML-0.3), registered in `Program.cs` beside `captain`, with help rows. `add` prints the sign-in command and offers to
  verify when the user presses Enter. Evidence:
- [ ] **ML-8.2** Helm, changed: `armada captain add|update --login <id|name>`, `captain list` shows a Login column and
  takes `--login`; `armada go`, `mission create` and `voyage create` take `--captain`, `--login`, `--runtime`,
  `--model`, `--strategy lowest-usage|first-available|drain`, and `--strict` (closing the 1.5.0 gap: `GoSettings`,
  `MissionCreateSettings`, `VoyageCreateSettings` have no captain flags); `armada persona` and `armada pipeline`
  commands that edit priorities and stages take the new fields; `armada mission selection` shows the login and
  strategy. Evidence:
- [ ] **ML-8.3** Helm tests: `Services.HelmRuntimeLogins`, extended `Services.HelmCaptainSelection`. Evidence:
- [ ] **ML-8.4** TUI: a Logins screen under Build (list with runtime, name, account, host, status, usage bars, captain
  count; detail with verify, sign-in command with copy, bound captains), registered in `BuildScreens.cs`, live on
  `login.changed`. Evidence:
- [ ] **ML-8.5** TUI, changed: `CaptainForm.cs` login picker filtered by runtime and host; `CaptainsScreen.cs` Login
  column; dispatch screens (`DispatchScreen.cs`, `PlanningDispatchPanel.cs`, `VoyageCreateScreen.cs`,
  `AskDispatchFormView.cs`) get a target selector with live preview; persona priority and pipeline stage editors get
  the new fields; `MissionSelectionPanel.cs` shows the login and strategy; `UsageLimitsScreen.cs` groups by login.
  Evidence:
- [ ] **ML-8.6** TUI tests: `Tui.RuntimeLogins`, extended `Tui.Build.Captains` and `Tui.CaptainSelection`; update
  `src/Armada.Tui/parity.json` and pass `scripts/tui/generate-parity-manifest.py --check`; add a
  `docs/tui-screens/logins-120x40.txt` capture with example accounts. Evidence:

## Phase 9: Dashboard

All dashboard work follows `DASHBOARD_STYLE_AND_USABILITY.md` and `FRONTEND_ARCHITECTURE.md`, goes through the shared
API client (`src/Armada.Dashboard/src/api/client.ts`), is localized in all nine locales (en, es, zh-Hans, zh-Hant,
yue-Hant, ja, de, fr, it) through `src/Armada.Server/wwwroot/i18n/armada.json`, and is checked in light and dark
themes at 1280, 768 and 390 pixels.

### Route inventory

| Route | User job | Primary role(s) | Backend resources | Table/filter needs | Actions and modals | Empty/error states |
| --- | --- | --- | --- | --- | --- | --- |
| `/captains/logins` (Captains hub tab "Logins") | See every vendor account Armada can run as, its health and headroom | Operator, admin, tenant admin; viewer read-only | `runtime-logins` enumerate, usage summary, discover | Runtime, host, status, name, account; sort by usage, expiry, name; paging above table; Select Columns | `+ Add login` wizard, View, Edit, Verify, Sign-in help, View JSON, Delete (confirm, reassign) | No logins yet (explain host default and offer Discover); host unreachable; CLI not installed on host |
| `/captains/logins/:id` | Inspect one login: who it is, which captains use it, usage history, verification log | Same | login, usage, captains, verification history | Bound captains table | Verify, Sign-in help with copy, Edit, Bind captains, Delete | Signed out (show the command), expired, never verified |
| `/captains` (existing) | Captains list now shows which login each captain uses | Same | captains enumerate with `loginId` filter | Login column and filter | Captain modal with Login picker | Captain on signed-out login (inline badge and fix link) |
| `/captains/:id` (existing) | Captain detail shows its login and that login's usage | Same | captain, login usage | none | Change login | Login missing or mismatched |
| `/dispatch`, `/voyages/new`, planning dispatch card (existing) | Choose who runs the work | Operator | preview endpoint, logins, captains | none | Target selector (This captain, Any on login, Any of runtime, Lowest usage of runtime), Strict toggle, live "will run on" preview | No eligible captain for the target (say why, link to fix) |
| `/configuration/personas/:name` (existing) | Prefer specific captains or logins, set the persona's strategy | Admin, tenant admin | persona | none | Priority editor with Captain and Login fields, Strategy select | Matcher matches nothing today (warning) |
| `/pipelines/:id` (existing) | Pin a stage to a target | Admin, tenant admin | pipeline | Stage table shows target | Stage target editor | Target matches nothing |
| `/missions/:id`, `/voyages/:id` (existing) | Understand why this captain and login ran the work | All | selection decision | none | Selection card shows target, strategy source, login, band | No decision recorded (older missions) |
| `/home` (existing) | Spot logins that need attention | All | health, logins summary | none | Attention item "2 logins signed out" linking to the Logins tab | none |
| `/server` Settings (existing) | Configure login verification and identity display | Admin | settings API | none | Runtime logins section | Restart-required annotations where applicable |

- [ ] **ML-9.1** API client methods and types for every Phase 6 route (`listRuntimeLogins`, `enumerateRuntimeLogins`,
  `getRuntimeLogin`, `createRuntimeLogin`, `updateRuntimeLogin`, `deleteRuntimeLogin`, `verifyRuntimeLogin`,
  `getRuntimeLoginSignIn`, `getRuntimeLoginUsage`, `discoverRuntimeLogins`, `adoptRuntimeLogin`,
  `previewCaptainSelection`) in `api/client.ts` and `types/models.ts`; live updates on `login.changed`. Evidence:
- [ ] **ML-9.2** Logins tab and detail page per the route inventory, using the shared DataTable, pagination,
  action menu (portaled), status badge (text plus color), copy button and JSON viewer. Usage shown as two compact
  bars (five-hour, weekly) with reset time and freshness, accessible labels, and color never the only signal.
  Account email rendered per ML-0.4 with a copy control. Evidence:
- [ ] **ML-9.3** `+ Add login` wizard (modal, not raw JSON): pick runtime (only installed harnesses on the chosen host
  are enabled), pick host (Admiral or Harbor), name it, choose "New managed directory" or "Adopt host default" or
  "Existing directory"; then a sign-in step with the exact command (POSIX and PowerShell tabs, copy), a "Open
  terminal on this Harbor" button when Harbor supports it, and a Verify step that polls until signed in or times out
  with a clear next action. Discovery results appear as one-click adopt cards. Evidence:
- [ ] **ML-9.4** Captain create and edit (`pages/Captains.tsx:516`, `lib/captainForm.ts`): Login picker filtered by
  runtime and host, "Host default login" option, inline status of the chosen login, mismatch errors at the field.
  `SetupWizard.tsx:516` offers logins when it creates a captain. `CaptainPicker.tsx` shows the login under each
  captain name. Evidence:
- [ ] **ML-9.5** Shared `CaptainTargetPicker` component used by Dispatch (`pages/Dispatch.tsx:300`), VoyageCreate
  (`pages/VoyageCreate.tsx:153`), the planning dispatch card, pipeline run and stage editors: a segmented choice of
  the four modes, the dependent fields, a Strict toggle with a tooltip that explains waiting versus falling back,
  and the live preview from ML-5.9. `lib/dispatchRequest.ts` builds `CaptainTarget`. Evidence:
- [ ] **ML-9.6** Persona editors (`PersonaPriorityEditor.tsx`, `Personas.tsx:230`, `PersonaDetail.tsx:265`): Captain
  and Login matcher fields and the persona Strategy select; a "matches now" count per priority row. Pipeline editor
  (`PipelineDetail.tsx:408`): stage target column and editor. Evidence:
- [ ] **ML-9.7** Selection explanations: `CaptainSelectionCard.tsx`, `SelectionRecoveryDialog.tsx`,
  `AssignmentBlockerCard.tsx` show the target, the strategy source, the chosen login with band and percent, and the
  new blocker reasons (`LoginSignedOut`, `LoginConcurrencyLimit`, `LoginCooldown`, `HarborLacksRuntimeLogins`) with
  the action that clears each. Evidence:
- [ ] **ML-9.8** Usage views: `components/usage/AccountLimitsTable.tsx`, `CaptainUsageCard.tsx` and
  `CaptainUsageCell.tsx` group by login; Home attention items for signed-out and expiring logins; Settings page
  Runtime logins section. Evidence:
- [ ] **ML-9.9** Navigation: add the Logins tab to the Captains hub (`pages/CaptainsHub.tsx`), breadcrumbs per
  `docs/DASHBOARD_NAVIGATION.md` (`Dashboard > Captains > Logins > {name}`), and the API Explorer picks up the new
  routes from OpenAPI. Evidence:
- [ ] **ML-9.10** i18n: every new string in all nine locales, pseudo-locale and missing-key checks pass, long German
  and CJK strings do not clip in the wizard, table headers or badges. Evidence:
- [ ] **ML-9.11** Vitest for every new page, component and lib function (Logins tab and detail, wizard steps,
  `CaptainTargetPicker`, request builders, priority editor fields, selection card states, empty, loading and error
  states); `npm run build` and `npm run test:run` green. Evidence:

## Phase 10: Harbor desktop app, mobile, VS Code and Proxy

- [ ] **ML-10.1** Harbor link: advertise the `runtime-logins` capability, the login candidates from discovery and the
  status of each login it hosts; handle `runtime-login.probe` (ML-3.2). Update `docs/HARBOR_PROTOCOL.md`. Evidence:
- [ ] **ML-10.2** Harbor app (Avalonia): a "Logins" view listing this machine's logins per harness with status and
  account, "Sign in" that opens the platform terminal (Terminal on macOS, Windows Terminal or PowerShell, the default
  terminal on Linux) in the login's directory with the sign-in command ready, "Verify", and "Adopt default". The tray
  menu shows a warning when a login is signed out. Evidence:
- [ ] **ML-10.3** Harbor tests: `Services.HarborRuntimeLogins` extended for capability and probe messages; window
  controls build-checked and, per Phase 14, viewed. Evidence:
- [ ] **ML-10.4** Mobile (`src/Armada.Mobile`): Logins tab in the Captains hub (list, detail, verify, copy sign-in
  command; no creation of directories from the phone beyond what the API supports), Login field in
  `CaptainFormSheet`, target selector in `screens/operations/DispatchForm.tsx`, `VoyageCreateScreen.tsx` and
  `components/ask/DispatchForm.tsx`, persona priority sheet fields, selection explanations, push notification for a
  login signing out (respecting notification categories). Localized and accessible; `parity.json` updated. Evidence:
- [ ] **ML-10.5** Mobile tests: `npm run typecheck`, `npm run lint`, `npm run test:ci`, `npm run test:android`, routes
  check, parity `--forbid-planned`, and a Maestro flow `e2e/10-runtime-logins.yaml`. Evidence:
- [ ] **ML-10.6** VS Code extension (`src/Armada.VSCode`): Logins resource in `resourceCatalog.ts`, login shown in
  captain tree items and `pickCaptain`, dispatch commands offer the target modes, dashboard pages in editor tabs pick
  up the Logins tab; `parity.json`, unit tests, `CHANGELOG.md` and `README.md` updated. Evidence:
- [ ] **ML-10.7** Proxy: confirm the new REST, WebSocket and MCP routes pass through the tunnel and portal unchanged
  (route allow-lists, payload limits); update `docs/PROXY_API.md` only if something changes. Evidence:

## Phase 11: Seeds, demo and screenshots

- [ ] **ML-11.1** `Armada.DemoSeed`: three Claude Code logins (`personal@example.com`, `foundation@example.org`,
  `work@example.net`) with different usage, two Codex logins, one signed-out Gemini login; six captains bound across
  them; a persona with login priorities; a pipeline with a "lowest usage" Judge stage; selection history that shows
  each strategy. Update `docs/DEMO_ADMIRAL.md`. Evidence:
- [ ] **ML-11.2** `Armada.PerfSeed`: logins at scale (50 logins, 500 captains) so the Logins tab, captain enumerate
  with `loginId` and selection ranking are covered by the performance baseline; record numbers in
  `docs/PERFORMANCE.md`. Evidence:
- [ ] **ML-11.3** Refresh store and docs screenshots that show captains, dispatch or personas, from the demo seed,
  with example accounts only. Evidence:

## Phase 12: Documentation

Documents written for people follow `WRITING_DOCUMENTS.md`: prose that explains, not only lists; no em-dashes; a
humanizer pass and a manual check against the Signs of AI writing page before an item is complete.

- [ ] **ML-12.1** New `docs/RUNTIME_LOGINS.md`: why logins exist, how to set up three Claude accounts on one machine
  end to end, Codex, Gemini and Cursor specifics from ML-0.12, split mode, adopting the host default, what Armada
  stores and what it never touches, what happens when a login signs out or hits its limit, and troubleshooting.
  Evidence:
- [ ] **ML-12.2** `docs/CAPTAIN_ROUTING.md`: captain targets, the precedence order from ML-5.5, per-login lowest usage
  and drain, strict versus fallback, new eligibility and blocker reasons, worked examples. Evidence:
- [ ] **ML-12.3** `docs/CAPTAINS.md` (login field, "How Armada launches each runtime" at `:195` gains the
  environment each login sets, the isolation change), `docs/CAPTAIN_ACTIVE.md` if affected. Evidence:
- [ ] **ML-12.4** `docs/PERSONAS.md`, `docs/PERSONAS_GUIDE.md`, `docs/PIPELINES.md`: captain and login matchers,
  persona strategy, stage targets. Evidence:
- [ ] **ML-12.5** `docs/REST_API.md`, `docs/MCP_API.md`, `docs/WEBSOCKET_API.md`, `docs/HARBOR_PROTOCOL.md`,
  `docs/PROXY_API.md` if changed. Evidence:
- [ ] **ML-12.6** `docs/OPERATIONS.md` (every new setting with default, minimum, maximum and effect),
  `docs/UPGRADING.md` (migrations 144 onward, Mux config directory migration, behavior changes, Harbor version
  advice), the deprecation doc (Mux `ConfigDirectory` option, `CaptainOverridesJson` per ML-0.10). Evidence:
- [ ] **ML-12.7** `docs/TELEMETRY.md` (new metrics and panels), `docs/TESTING.md` (new suites and the live login
  scenario), `docs/HARBOR.md` (Logins view), `docs/TUI.md`, `docs/MOBILE.md`, `docs/DASHBOARD_NAVIGATION.md`,
  `src/Armada.VSCode/README.md`, `docs/OPEN_MODELS.md` (mixing logins with open model endpoints). Evidence:
- [ ] **ML-12.8** `docs/INSTRUCTIONS_FOR_*.md` and `docs/*_AS_ORCHESTRATOR.md`: how an orchestrator asks for a
  target through MCP `dispatch`. Evidence:
- [ ] **ML-12.9** `docs/usage-selection/` and `docs/captain-usage-plan/`: per-login account keys and capture.
  Evidence:
- [ ] **ML-12.10** `README.md`: a "Multiple logins per harness" section near "Persona Captain Priorities" (`:638`) and
  the runtime comparison (`:97`), linking `docs/RUNTIME_LOGINS.md`. `DOCKERHUB_README.md`, `GETTING_STARTED.md`,
  `FAST_TRACK_SETUP.md`: one paragraph each and the link. Evidence:
- [ ] **ML-12.11** `docs/BACKLOG.md`: Antigravity runtime (ML-0.7), API-key logins (ML-0.6), anything deferred by the
  UI/UX passes, each with a reason. Evidence:
- [ ] **ML-12.12** `CHANGELOG.md`: a `## Unreleased` (or `## v1.6.0`, after ML-0.2) section covering every
  user-visible change, setting, migration, deprecation and behavior change, in the style of the 1.5.0 entry. Evidence:
- [ ] **ML-12.13** `postman/README.md`: the login examples and how to point them at a test Admiral. Evidence:
- [ ] **ML-12.14** `CLAUDE.md` and `AGENTS.md`: add `RuntimeLogin` and captain targets to Key Concepts and the routing
  note, and `rlg_` to the prefix list. [DECISION] permission to edit `AGENTS.md`, kept in sync in the same commit.
  Answer: Evidence:

## Phase 13: Verification

- [ ] **ML-13.1** Full local run: warning-free Release build; `Test.Automated` on net8.0 and net10.0; xUnit and NUnit
  adapters; `Test.Smoke` (add `Database.RuntimeLogin` and `E2E.RuntimeLogin` to `SmokeSuites.cs`); dashboard; mobile;
  TUI parity; VS Code. Record counts against the ML-0.11 baseline. Evidence:
- [ ] **ML-13.2** CI: `ci.yml`, `full.yml`, `security.yml`, `db-parity.yml` (`RESULT: PARITY OK`), `install-verify.yml`
  and `vscode-extension.yml` green on the release candidate commit. Evidence:
- [ ] **ML-13.3** Upgrade test from the v1.5.0 commit on all four providers
  (`scripts/common/run-upgrade-test.sh --from-ref <v1.5.0 commit> --providers all`), with seeded Mux captains (ML-2.6
  backfill), voyages with JSON overrides (ML-0.10 backfill) and persona priorities, proving gate A9. Evidence:
- [ ] **ML-13.4** Split-mode end to end (`scripts/common/e2e-split-mode.sh`) with a Harbor login, a 1.5.0 Harbor
  (degraded with a clear message) and a 1.6.0 Harbor against a 1.5.0 Admiral (capability ignored). Gate A7. Evidence:
- [ ] **ML-13.5** [HUMAN] Live run, gates A1 to A6 and A8: Joel signs in three Claude Code logins and two Codex logins
  on the maintainer machine using the Add login wizard; the agent runs the scenarios, records results in
  `benchmarks/results/1.6.0/runtime-logins.local.md` (git-ignored, real accounts allowed there only) and a redacted
  summary in `benchmarks/VERIFICATION_1.6.0.md`. Evidence:
- [ ] **ML-13.6** Simulated user testing per `SIMULATED_USER_TESTING.md` on an isolated `armada-usertest` stack, with
  at least these users: an operator with three Claude subscriptions setting them up from `docs/RUNTIME_LOGINS.md`
  alone; a team lead building a mixed pipeline (lowest-usage Claude Worker, any-Codex Judge, Qwen on an endpoint for
  research); an operator whose login signs out mid-voyage. Findings ranked and surfaced to Joel; Joel decides what is
  fixed; fixes verified in a focused session. Evidence:
- [ ] **ML-13.7** Security review of the diff: no token or keychain read reaches the Admiral, the Harbor link, logs,
  telemetry or the black box; config directory paths are validated (no traversal outside the host user's home or
  `ManagedRoot` unless the operator chose an existing directory explicitly); tenant isolation on every new route and
  event; probe commands cannot be steered by user input into arbitrary execution. Run `/security-review`. Evidence:
- [ ] **ML-13.8** Final diff review for requirements compliance, em-dashes (`grep -rnP '\x{2014}'`), private data
  (`grep` for real account emails and usernames), documentation drift, and unintended edits. Evidence:

## Phase 14: UI/UX pass 1, requirements conformance

Pass 1 asks one question: does every surface do what this plan and the requirements say, for every role, state, theme,
width and locale? It is a checklist pass, run after Phases 6 to 11 are complete, by someone who did not build the
screen under review where possible. Each finding gets an id (`UX1-<n>`), a severity (S1 blocks release, S2 must fix,
S3 should fix, S4 polish), steps, expected, actual, and a screenshot path. Fixes happen after the pass, then each
finding is re-verified.

- [ ] **ML-14.1** Requirements walkthrough of every route in the [route inventory](#route-inventory) against
  `DASHBOARD_STYLE_AND_USABILITY.md` "Acceptance Criteria": `+ Add`, row actions (View, Edit, Verify, View JSON,
  Delete), above-table pagination with totals and jump, Select Columns, backend filters and sorting, portaled action
  menus, custom confirm dialogs, copy controls with checkmark feedback, human-readable labels, loading, empty and
  error states with retry. Evidence:
- [ ] **ML-14.2** Playwright capture of each route, the Add login wizard (every step and failure path), the captain
  modal with a login, the target picker in each mode with its preview, the persona priority editor and the stage
  target editor, at 1280, 768 and 390 pixels, light and dark, in English plus German (long strings) and Japanese
  (CJK). Check for horizontal overflow, clipped text, modal overflow and console errors. Screenshots kept under
  `benchmarks/results/1.6.0/ux-pass-1/` (example data only). Evidence:
- [ ] **ML-14.3** Role pass: admin, tenant admin, operator and viewer each see and can do exactly what
  `AUTHENTICATION.md` allows (viewer sees logins but no Verify, Edit or Delete; backend refuses regardless). Evidence:
- [ ] **ML-14.4** Accessibility pass: keyboard-only through the wizard and target picker, focus order, visible focus,
  `aria-label` on icon buttons, status not by color alone, screen reader text for usage bars, reduced motion for the
  verify spinner. Evidence:
- [ ] **ML-14.5** Cross-surface parity: the same three-login demo seed viewed on dashboard, TUI, Helm, mobile, VS Code
  and Harbor shows the same logins, statuses, account display and selection explanations; terminology matches
  ML-0.3 everywhere. Evidence:
- [ ] **ML-14.6** Fix every S1 and S2 finding, and every S3 that Joel picks; re-verify each with a screenshot; record
  the rest in `docs/BACKLOG.md`. Findings table appended to the Validation log. Evidence:

## Phase 15: UI/UX pass 2, layout, aesthetic and usability

Pass 2 starts only after pass 1 fixes are in, and asks a harder question: is this the best version of these screens,
or only a compliant one? It is judged against the design bar in `DASHBOARD_STYLE_AND_USABILITY.md` ("Design Bar for
New Dashboards") and against the rest of Armada, which already sets a high bar. The reviewer uses the product to do
real jobs, times them, and looks for friction rather than defects.

- [ ] **ML-15.1** Task timing with fresh eyes, recorded with clicks and seconds: (a) add a second Claude login from
  nothing and bind a captain to it; (b) dispatch a mission to "lowest usage Claude Code" and understand from the
  mission page why that login was chosen; (c) find out which login is closest to its weekly limit; (d) recover after
  a login signs out. Targets: (a) under 2 minutes excluding the vendor's own sign-in, (b) and (c) under 30 seconds,
  (d) under 1 minute, with no documentation open. Any miss is an S2 finding. Evidence:
- [ ] **ML-15.2** Visual hierarchy and density: the Logins tab reads at a glance (who, which harness, healthy or not,
  how much is left), the account identity is the most prominent text in a row, usage bars align and share a scale,
  status badges match the existing Captains tab, no nested card clutter, spacing and typography match neighboring
  hub tabs, dark theme looks designed rather than inverted. Evidence:
- [ ] **ML-15.3** Interaction quality: the target picker explains itself without a tooltip for the common case, the
  live preview updates without layout shift, the wizard never strands the user (every step has back, cancel and a
  clear next action), verify polling gives progress and a useful timeout message, destructive actions say exactly
  which captains are affected. Evidence:
- [ ] **ML-15.4** Copy review in all nine locales: plain operator language ("Runs as foundation@example.org",
  "62% of the 5-hour limit left"), consistent terms, no implementation names, no em-dashes, error messages that say
  what to do next. A native or fluent check for at least Spanish, German and Japanese, or a recorded reason why not.
  Evidence:
- [ ] **ML-15.5** Terminal and desktop polish: TUI Logins screen at 80x24 and 120x40 (no wrapping of the account
  column, usage bars readable in monochrome), Helm table output and `--json`, Harbor Logins view at the minimum window
  size and on Windows, macOS and Linux, mobile at small and large phones and tablet. Evidence:
- [ ] **ML-15.6** Comparison review: put the new screens side by side with the best existing Armada screens
  (Captains, Model Profiles, Reliability) and with one reference dashboard from the `DASHBOARD_STYLE_AND_USABILITY.md`
  Pattern Reference Map chosen for account and quota display; list where the new screens fall short and fix them.
  Evidence:
- [ ] **ML-15.7** Fix every S1 and S2 finding (`UX2-<n>`) and the S3 and S4 items Joel picks; re-verify with
  screenshots under `benchmarks/results/1.6.0/ux-pass-2/`. Evidence:
- [ ] **ML-15.8** Confirmation pass (pass 3, short): rerun the ML-14.2 capture matrix and the ML-15.1 timings on the
  final build, to prove pass 2 fixes did not break pass 1 conformance. Required before Phase 16. Evidence:

## Phase 16: Release (gated)

Follow `docs/RELEASING.md`. Stop at each approval point. Nothing here starts without a new instruction from Joel.

- [ ] **ML-16.1** [DECISION] Release shape (tag, GitHub Release with installers, Docker Hub, VS Code, mobile store
  builds). Answer:
- [ ] **ML-16.2** Apply the ML-0.2 version change across every location, `Services.ReleaseVersion` green; move
  `## Unreleased` under `## v1.6.0 (<date>)`. Evidence:
- [ ] **ML-16.3** Merge `feature/v1.6.0` to `main` when Joel asks (pull request, CI green). Evidence:
- [ ] **ML-16.4** Images, installers, tag and release page per `INSTALLERS.md`, after Joel's go-ahead on the
  candidate commit. Evidence:
- [ ] **ML-16.5** VS Code extension and mobile builds per the runbook, only if ML-16.1 includes them. Evidence:

## Decisions log

Record each [DECISION] answer here as well as inline, with the date.

| Item | Question | Recommendation | Answer | Date |
|---|---|---|---|---|
| ML-0.2 | Release version | 1.6.0 | | |
| ML-0.3 | Name in code and UI | `RuntimeLogin`, "Logins" tab, `armada runtime-login` | | |
| ML-0.4 | Show account email | Store and show, setting to mask, never in telemetry | | |
| ML-0.5 | Runtimes with logins | Claude, Codex, Gemini, Cursor; OpenCode and Pi directory only; Mux migrated | | |
| ML-0.6 | API-key logins | Not in 1.6.0 | | |
| ML-0.7 | Antigravity | Gemini CLI logins now, runtime in backlog | | |
| ML-0.8 | Sign-in flow | Show command, Harbor opens terminal, Verify; no OAuth driving | | |
| ML-0.9 | Lowest usage under Observe mode | Explicit strategy ranks in any mode except Disabled; gates only in Enforce | | |
| ML-0.10 | Voyage assignment storage | New child table with backfill; JSON column deprecated | | |
| ML-12.14 | Edit `AGENTS.md` | Yes, in sync with `CLAUDE.md` | | |
| ML-16.1 | Release shape | Decide at release time | | |

## Validation log

Append entries newest last: date, item ids, exact command, result counts, log path. Record failures and their fixes
too; a validation entry is not complete until the command passes.

| Date | Items | Command | Result | Log |
|---|---|---|---|---|
| | | | | |
