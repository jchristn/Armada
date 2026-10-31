# Phase 1 test engineer findings

Reviewed Worker commit 798bd7b8cb856d40cf4ad754c0659ea86f49a33a.
The full prior handoff artifact hoa_mvwx5pti_P9sNz0tlcg1 has no numbered
findings. Its completion claim remains blocked by the cases below.

## AC1 / Services.UsageDefaults: explicit null captain settings

File: src/Armada.Core/Models/Captain.cs:155

Requirement: phase 1 explicitly requires empty/null/full captain settings to
preserve disabled/300/Auto defaults.

Reproduction:
JsonSerializer.Deserialize<Captain>(
  "{\"UsageCaptureEnabled\":null,\"UsageCaptureIntervalSeconds\":null,\"UsageCaptureMode\":null,\"UsageCaptureCommand\":null}")

Expected: disabled capture, interval 300, Auto mode, null command.
Actual: JsonException converting UsageCaptureEnabled to System.Boolean.
Required correction: implement explicit null handling for this settings contract
and test each field independently, plus the combined payload. Keep malformed
non-null values rejected.

## AC1 / D3: non-Custom command clearing depends on JSON order

File: src/Armada.Core/Models/Captain.cs:188

Requirement: the capture settings contract clears the command outside Custom.

Reproduction:
JsonSerializer.Deserialize<Captain>(
  "{\"UsageCaptureMode\":\"Auto\",\"UsageCaptureCommand\":\"quota\"}")

Expected: null command.
Actual: command equals "quota". Reversing the two properties returns null.
Required correction: enforce normalization independent of property order and
cover both Auto and Disabled, including transitions from Custom and persistence.

## Coverage and execution

Added three passing Services.UsageDefaults cases:
- usage_global_json_omitted_null_and_empty_keep_defaults
- usage_global_json_overrides_clamp_and_round_trip
- captain_usage_json_rejects_malformed_types

Commands executed:
- dotnet build src/Test.Automated -c Release -f net10.0 --nologo:
  exit 0, zero warnings/errors.
- ARMADA_TEST_SUITES=Services.UsageDefaults,Database.UsagePersistence
  dotnet run --project src/Test.Automated -c Release --framework net10.0:
  exit 0, 25 passed, zero failed/skipped, SQLite.
- Temporary regression descriptors for the two cases above, executed with
  ARMADA_TEST_SUITES=Services.UsageDefaults and the same runner command:
  exit 1, 14 passed, two failed. Assertions observed the non-null "quota"
  command and the System.Boolean conversion error. Temporary failing
  descriptors were removed before commit as required by verifier instructions.

## Unmet gates and prior evidence

The prior handoff itself reports that existing-database upgrades were run only
on SQLite. Its three server-provider fresh-database runs do not establish the
AC1 existing-database migration requirement. This gap is unresolved. No server
provider checks were rerun in this stage after discovering the product defects;
the prior provider pass claims are inherited evidence, not this stage's results.
The prior handoff reports missing phase 0 interface evidence; it remains an
unmet preceding-phase gate and is not replaced by phase 1 model tests.
The prior handoff reports tenant deletion cascade wiring deferred to phase 4;
this stage does not establish end-to-end tenant deletion.
Full solution, dashboard and full adapter suites were not rerun: this stage
changes only scoped configuration tests and stops on confirmed product defects.
No production code was changed.

## Rework 1 resolution

Both blocking findings are resolved in src/Armada.Core/Models/Captain.cs.

- Explicit null settings: property-level converters
  (Services/UsageCaptureEnabledJsonConverter.cs, UsageCaptureIntervalJsonConverter.cs,
  UsageCaptureModeJsonConverter.cs) read JSON null as false / 300 / Auto. Non-null
  malformed tokens (strings, numbers for the flag, fractions, out-of-range or boolean
  intervals, empty or unknown mode names, objects) still throw JsonException.
  Regression: Services.UsageDefaults captain_explicit_null_settings_keep_defaults
  (each field alone, combined, default and Web options, round trip) and extra
  malformed payloads in captain_usage_json_rejects_malformed_types.
- Order-independent command clearing: UsageCaptureCommand reads null outside Custom
  whatever the assignment order, and Captain implements IJsonOnDeserialized so a
  JSON command supplied for Auto or Disabled is dropped and cannot be revived by a
  later switch to Custom. Regressions: Services.UsageDefaults
  captain_command_clearing_is_order_independent (both orders, Auto and Disabled,
  Custom transitions, assignment order) and Database.UsagePersistence
  captain_non_custom_json_command_is_not_persisted (both orders, create, read,
  update to Custom, all-null payload).

The three new cases failed against commit 69aaaf581 (System.Boolean conversion
error; command "quota" retained) and pass with the fix. Existing-database upgrades
on the server providers, phase 0 interface evidence and tenant deletion cascade
remain the unverified gaps listed above.

## Rework 1 test engineer verification

Reviewed worker commit 3f480eaf133ea05d096b2868a241bf1fc7a45e5a.
Executed:

```
ARMADA_TEST_SUITES=Services.UsageDefaults,Database.UsagePersistence dotnet run --project src/Test.Automated -c Release --framework net10.0
```

Result: exit 0; 28 passed, 0 failed, 0 skipped, SQLite. This independently
confirms both prior product findings are resolved: explicit null captain
settings preserve defaults, and non-Custom JSON commands are cleared in both
property orders and do not reach storage. Malformed types and unknown enums
remain rejected. No additional test descriptors or product changes were needed.

Unresolved acceptance gates:

- Phase 0 prerequisite: docs/captain-usage-plan/PROBE_INTERFACES.md is absent
  from this branch and local main (c52ca7be8). The ordered-phase mission rule
  requires preceding deliverables on the integration base. Supply and verify
  AC0 interface evidence before declaring dependent phase completion.
- AC1 existing-database migrations: UsagePersistenceSuite's upgrade case
  explicitly skips non-SQLite providers because they are provisioned fresh.
  Expected: actual PostgreSQL, MySQL and SQL Server v110-to-v111 upgrades with
  existing captain/token rows retained. Actual: this stage establishes only
  SQLite upgrade behavior; no server-provider upgrades were run. Add and run
  provider-specific existing-database fixtures before AC1 can pass.
- Tenant deletion cascade remains unverified end to end. The SQLite upgrade
  case's usage-row deletion check does not establish the tenant service path.

Prior server-provider fresh-database results remain inherited evidence only.
No full solution, adapter or dashboard runs were performed in this stage;
Armada runs post-stage checks. No baseline failures occurred in the scoped run.
Stage outcome: BLOCKED on the prerequisite and unmet AC1 provider upgrade gate.

## Rework 2 resolution and executed checks

Rebased this branch onto fetched main f1e4cff2a before continuing phase 1.

Finding 1 (server-provider existing-database upgrades): resolved.
`Database.UsagePersistence.usage_existing_database_upgrades_to_v111` now runs
without provider skips. It creates an isolated database, replays the selected
provider's migrations through v110 using its migration executor, records each
version, inserts legacy captain/token rows, verifies version 110 and pending
migrations, then calls the production InitializeAsync. The provider-specific
executor is accessed reflectively only during fixture setup to preserve its
statement splitting and duplicate-column handling without changing production
migration interfaces. The fixture keeps the built-in tenant where present and
adds it where absent, matching an initialized legacy database.

The shared assertions check the upgraded schema, zero pending migrations,
legacy captain identity and disabled/300/Auto/null settings, retained input,
output, cached and total tokens, null provider/account/host/membership/cost/
currency attribution, and new snapshot/member writes and reads. The isolated
server database is dropped in finally. SQLite still replays its own migrations
and now removes its temporary file. The test's tenant deletion assertion is
labeled as storage deletion, not proof of the tenant service cascade.

Finding 2 (missing phase 0 prerequisite): resolved by integration-base changes,
not by recreating phase 0 in this mission. Fetched main contains
`docs/captain-usage-plan/PROBE_INTERFACES.md`, its fixtures and
`Usage.InterfaceEvidence`. After rebase, all 13 evidence checks passed. The
phase 0 document explicitly leaves unverified provider adapters disabled;
these checks validate the delivered evidence and fixtures, not live provider
quota access.

Executed on the final fixture revision:

- `dotnet build src/Test.Automated -c Release -f net10.0 --nologo`:
  exit 0, zero warnings, zero errors.
- `ARMADA_TEST_SUITES=Usage.InterfaceEvidence,Services.UsageDefaults,Database.UsagePersistence dotnet run --project src/Test.Automated -c Release --framework net10.0 --no-build`:
  SQLite, exit 0, 41 passed, zero failed, zero skipped.
- `ARMADA_TEST_SUITES=Services.UsageDefaults,Database.UsagePersistence` with
  the same runner and `ARMADA_TEST_DB_TYPE=postgresql`, `mysql`, and
  `sqlserver`, respectively: each exit 0, 28 passed, zero failed, zero skipped.
  Actual local containers: PostgreSQL 17, MySQL 8.4, SQL Server 2022.
  Host/port/user/password were supplied through the shared harness environment;
  no credentials or connection strings are included in this evidence.
- `git diff --check`: exit 0.

Earlier development runs exposed fixture setup issues: PostgreSQL's container
uses a nondefault username, and providers differ in whether old migrations seed
the built-in tenant. Corrected the invocation and made fixture tenant insertion
conditional; all four providers were rerun after the final correction. These
were setup failures, not inherited baseline product failures. Prior explicit
null and property-order regressions remain in the passing defaults/persistence
suites on every provider.

Remaining limits: end-to-end tenant-service deletion is not established by
storage deletion and remains for the later service/retention phases. No live
provider quota requests, enabled adapters, full solution/adapter/dashboard
runs, API publication, deployment or release checks were performed. This
rework changes only the scoped upgrade regression and this findings record;
post-stage checks and integration delivery remain Armada's responsibility.

## Rework 2 independent test engineer verification

Reviewed worker commit 316192f49a177ff6bd3dfa855541d15ced43a1a3 and its
complete handoff artifact hoa_mvwxspjx_2xghec0gxgW. The artifact contains no
numbered findings. Fetched origin/main f1e4cff2a8927061712f11ad3287740af66fc62f
is already an ancestor of this branch; no additional rebase was necessary.

Prior findings rechecked:

- Resolved: explicit null captain settings. The named
  captain_explicit_null_settings_keep_defaults case passes on every provider.
- Resolved: command property-order dependence. The named
  captain_command_clearing_is_order_independent and
  captain_non_custom_json_command_is_not_persisted cases pass on every provider.
- Resolved: absent phase 0 prerequisite. PROBE_INTERFACES.md and the fixtures
  are on the integration base; Usage.InterfaceEvidence passes all 13 cases.
  This establishes delivered evidence, not live provider quota access.
- Resolved: server-provider upgrade coverage. Inspected and independently ran
  usage_existing_database_upgrades_to_v111 on PostgreSQL, MySQL and SQL Server,
  as well as SQLite. The test builds v110 data, checks a pending migration,
  invokes production initialization and verifies retained captain/token rows
  with disabled defaults and nullable attribution/cost.
- Unverified, unchanged scope boundary: tenant-service deletion end to end.
  Storage deletion coverage is not evidence of the later service cascade.

### Coverage added

AC1 / Services.UsageDefaults adds
captain_custom_blank_command_normalizes_in_both_json_orders. Empty, space and
control-whitespace commands normalize to null in Custom mode, in both property
orders, and remain null after serialization. Capture remains disabled and the
interval remains 300 when omitted. Existing blank-command JSON assertions used
Auto mode, whose getter hides commands; this case exposes the stored command.

Sensitivity check: temporarily replaced the blank fixtures in this new case
with the nonblank string quota. Services.UsageDefaults exited 1: 16 passed,
1 failed, zero skipped, specifically because the command was quota rather than
null. Restored the blank fixtures immediately. No production code was changed
and no deliberately failing fixture was committed.

### Executed checks

- dotnet build src/Test.Automated -c Release -f net10.0 --nologo:
  exit 0, zero warnings and errors.
- ARMADA_TEST_SUITES=Usage.InterfaceEvidence,Services.UsageDefaults,Database.UsagePersistence
  dotnet run --project src/Test.Automated -c Release --framework net10.0 --no-build:
  SQLite exit 0, 42 passed, zero failed/skipped.
- ARMADA_TEST_SUITES=Services.UsageDefaults,Database.UsagePersistence with
  ARMADA_TEST_DB_TYPE=postgresql, mysql and sqlserver, using the same runner:
  each exit 0, 29 passed, zero failed/skipped. Used actual local PostgreSQL 17,
  MySQL 8.4 and SQL Server 2022 containers on loopback. Connection settings were
  supplied through the existing harness environment and are not stored here.
- After the sensitivity check, rebuilt and reran Services.UsageDefaults with
  its original blank fixtures: exit 0, 17 passed, zero failed/skipped.
- git diff --check: exit 0.

AC1 / Database.UsagePersistence covers fresh and existing migrations, nullable
snapshot/token fields, tenant isolation, effective-dated memberships, member
links, idempotent rollups and separate latest-error/last-good state. Negative
paths include malformed settings, unknown enums, foreign-tenant reads/deletion
and absent/empty windows. No baseline failures occurred in these scoped runs.

### Residual risks

Full solution, smoke, dashboard, other runner adapters, deployment and release
checks were not rerun; Armada performs post-stage checks. Provider quota access,
phase 2 scheduler/process cleanup and phase 7 tenant-service retention remain
outside this stage. No unsupported adapters were enabled. No known production
defect blocks this scoped verification.
