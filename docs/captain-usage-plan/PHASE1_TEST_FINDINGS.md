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
