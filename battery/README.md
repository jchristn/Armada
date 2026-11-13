# Fixture battery packs

The fixture battery measures Armada and the captains it runs on fixed, versioned tasks (docs/MODEL_HETEROGENEITY.md,
Part 2). It is organized as **packs**. A pack is one fixture repository plus its tasks. Packs are data, not code:
to add a language, or a domain that is not code at all, you add a folder here. You do not change Armada.

```
battery/
  README.md                 this file (the manifest schema)
  <pack-id>/
    battery-pack.json       the manifest
    fixture/                files that become the fixture repository
    tasks/<task-id>/...     attachments, setup files, grading overlays, expected files, rubrics
```

The Admiral reads packs from `Evaluation.BatteryDirectory`. When that setting is empty, it uses the `battery` folder
next to the Admiral binary (the build copies this folder there) or in a parent folder of a source checkout.
`list_battery_packs` / `GET /api/v1/battery/packs` lists every pack. Malformed packs are listed with `valid: false`
and their validation errors. Only valid packs can run.

## How a run works

`run_battery` / `POST /api/v1/battery/runs` takes the packs, a captain and a run count (default 3). For each pack it
runs every task once per run, in manifest order:

1. **Reset.** The fixture repository is a local bare repository at `DATA/battery/<pack>/origin.git`. It is also the
   remote of the pack's fixture vessel. The reset adds a new commit on the fixture's default branch. That commit's
   tree is exactly the pack's `fixture/` folder plus the task's `setup` folder. Resets only move forward, so every
   clone picks them up with a plain fetch, and anything a previous task pushed is gone from the tree.
2. **Dispatch.** The task becomes one voyage on the fixture vessel under the built-in `ModelEvaluation` pipeline. It
   runs with the task's persona and is pinned to the captain for every stage. Its landing mode is `None`, so the work
   stays in its dock. The mission description is the task `prompt` followed by each attachment in a fenced block.
   This is real dispatch, Harbor execution, stage checks and completion parsing.
3. **Grade.** First the toolchain is probed in the mission's worktree, on the host that ran it. A missing tool makes
   the task `CouldNotRun`, never `Failed`. Later tasks of the run that need the same tool are not dispatched. The
   grader then runs per the manifest (see below).

The fixture repository is a path on the Admiral's machine, so the Harbor that runs a battery task must be able to
read that path. In practice that means a Harbor on the same machine as the Admiral, or the Admiral itself.

The fixture vessel is named `battery-<pack>` in the default tenant, or `battery-<pack>-<tenantId>` in other tenants.
It is created only on request (`createFixtureVessel: true`). Its default pipeline is `ModelEvaluation`, and that is
how battery missions are tagged. Every stage outcome of a mission on such a vessel is recorded with
`isEvaluation: true`, so it never counts in production stage outcomes or scorecards.

Each run records its seed (given or picked at random), sampling settings (temperature, top_p, max tokens, context
length, reasoning effort), run count, battery version (`<pack>@<version>` for each pack) and the full configuration
revision. The revision is the first 16 hex digits of the SHA-256 of a configuration document. That document holds
the Armada version, the captain's runtime, model and reasoning effort, the endpoint and its inference options, the
pipeline, and each pack's version and manifest hash.

Runs share Evaluate's concurrency limits: `Evaluation.MaxConcurrentRuns` overall and
`Evaluation.MaxConcurrentRunsPerHost` per inference host. A task's mission is cancelled and the task fails when it
does not finish within `Evaluation.BatteryTaskTimeoutMinutes` (default 60). A task's `timeoutMinutes` overrides that.

## Manifest schema (schemaVersion 1)

`battery-pack.json` is UTF-8 JSON. Property names are case-insensitive, and comments and trailing commas are
allowed. Every path is relative to the pack folder and must stay inside it.

| Field | Type | Required | Meaning |
|---|---|---|---|
| `schemaVersion` | int | yes | Must be `1`. |
| `id` | string | yes | Lowercase letters, digits and dashes, for example `csharp`. |
| `name` | string | yes | Display name. |
| `version` | int | yes | Battery version, 1 or more. Raise it whenever the fixture, a task or a grader changes, so results from different versions are never compared as equal. |
| `domain` | string | no | For example `code` or `documentation`. |
| `description` | string | no | Shown in the list. |
| `fixture.path` | string | yes | Folder whose files become the fixture repository. It must not be empty. |
| `fixture.defaultBranch` | string | no | Default `main`. |
| `toolchain[]` | object | no | Tools the Harbor needs. Each has a `name` (unique), an `executable`, `arguments` (exit code 0 means installed) and a `hint` (what to install). |
| `stageChecks` | object | no | `buildCommand` and `testCommand`, recorded on the fixture vessel for reference. |
| `tasks[]` | object | yes | At least one task. |

Task fields:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `id` | string | yes | Unique in the pack, lowercase letters, digits and dashes. |
| `title` | string | yes | Also the mission title. |
| `persona` | string | yes | `Worker`, `Architect`, `Product Manager`, `Usability Engineer`, `Test Engineer`, `Judge`, `Linter` or `Recorder`. |
| `difficulty` | string | no | Free label. |
| `prompt` | string | yes | The mission description. |
| `attachments[]` | string | no | Files appended to the description in fenced blocks (a `.diff` gets a `diff` fence). |
| `setup` | string | no | Folder copied over the fixture when it is reset for this task, for example to add the failing test of a bug-fix task. |
| `toolchain[]` | string | no | Toolchain names the task needs. Omit it to need every pack entry. Use `[]` to need none. |
| `timeoutMinutes` | int | no | 1 to 1440. |
| `grader` | object | yes | See below. |

## Graders

`grader.type` is one of the following.

**`command`**: runs `commands[]` in the worktree, in order. Each command has an `executable`, `arguments`,
`expectedExitCode` (default 0) and `timeoutSeconds` (default 600). The task passes when every command exits with its
expected code in time. If `overlay` is set, that folder is copied into the worktree first (hidden acceptance checks
the captain never saw). Overlay files are written as UTF-8 text.

**`byteComparison`**: each `files[]` entry has a worktree `path` and an `expected` file in the pack. The worktree file's
git blob hash (`git hash-object --no-filters`, with no line-ending or encoding conversion) must equal the hash of the
expected bytes. When it does not, the report says whether the byte order mark was dropped, CRLF was converted to LF,
both, or the content differs.

**`mutation`**: runs `commands[]` as a test run that must pass. Then each `mutants[]` entry (`id`, `file`, `find`,
`replace`) is applied, the commands run again, and the file is restored. A mutant is killed when the commands fail
with it in place. The task passes when every mutant is killed. A mutant whose `find` text is no longer in the file
counts as not killed. The score is the fraction of mutants killed.

**`seededSet`**: grades a reviewer's output against `seeded[]`. Each item has an `id`, a `description` and `keywords`
(case-insensitive phrases). The grader extracts findings from the output:

- With `blockingSections` (for example `["Affected Case"]`), each section under such a heading is one finding. The
  section is split at numbered items or sub-headings.
- Without `blockingSections`, every list item outside code fences is a finding, except items in sections whose heading
  says optional, suggestion, nits, improvement, strength, resolved or praise.

A seeded item is found when a finding mentions one of its keywords. With `matchAnywhere: true`, any non-code line of the
output counts. A finding that mentions no seeded item is a **false positive**. The task passes when:

- at least `minMatched` items are found (default: all of them);
- there are at most `maxFalsePositives` false positives (default 0);
- the Judge verdict is one of `expectedVerdicts` (`PASS`, `FAIL`, `NEEDS_REVISION`, `SPEC_CONFLICT`; empty accepts
  any).

A positive control has no seeded items, expects `PASS` and allows no false positives.

**`rubric`**: graded later by the evaluator captain (Phase 5) against the `rubric` file. The task is recorded as
`PendingEvaluation` with the captain's raw output (up to 64 KB) and the rubric text. If `schema: "armada-plan"` is
set, the output must first contain a valid `armada-plan` block, otherwise the task fails.

## Outcomes

| Outcome | Meaning |
|---|---|
| `Passed` | The grader accepted the work. |
| `Failed` | The grader rejected it, the mission failed, was cancelled or blocked, or it timed out. |
| `CouldNotRun` | The Harbor lacks the toolchain, the fixture could not be reset, or there was no worktree to grade. Never counted as a failure. |
| `PendingEvaluation` | Rubric-graded; waiting for the evaluator. |

## The C# pack (`csharp`)

The fixture is a small inventory library (`src/Inventory`) with a dependency-free test runner
(`tests/Inventory.Tests`). A test runs only when it is registered in `TestRegistry.cs`. Run the tests with
`dotnet run --project tests/Inventory.Tests` (exit 0 when all pass, 1 when one fails, 2 when a `--require NAME` test is
not registered). `src/Inventory/LegacyCodes.cs` is UTF-8 with a BOM and CRLF line endings. The fixture's own
`.gitattributes` (`* -text`) and the repository's (`battery/** -text`) keep it byte-exact.

| Task | Persona | Graded by |
|---|---|---|
| `add-member` | Worker | Build, tests and a hidden check (`Item.TotalValue`) |
| `two-place-edit` | Worker | Build and tests with `--require Stock_Remove_MoreThanAvailable_Throws` (the method must also be registered) |
| `fix-bug` | Worker | Tests; the task setup adds the failing `PriceCalculator_ApplyDiscount_TenPercent` |
| `bom-crlf` | Worker | Byte comparison: the BOM and CRLF are kept |
| `write-tests` | Test Engineer | Tests plus four mutants of `TextUtil.Truncate` (null guard, guard exception, boundary, cut length) |
| `judge-negative` | Judge | Three seeded defects (off-by-one, null dereference, integer division); false positives count |
| `judge-positive` | Judge | `PASS` with no blocking finding |
| `architect-plan` | Architect | `armada-plan` schema check, then the evaluator's rubric |
| `lint-diff` | Linter | Four seeded style violations (`var`, missing doc, field name, `Console.WriteLine`) |
| `summarize-voyage` | Recorder | The evaluator's rubric |

The `Battery.CSharpPack` test suite runs the real `dotnet` and `git` on this pack. For each worktree-graded task it
checks that the reset fixture fails the grader for the reason the task is about, and that a reference solution
passes.

## Language packs

Each language pack ports the same inventory fixture and the ten template tasks of the C# pack, with the same ids,
personas and grader types. Each fixture has a dependency-free test runner: a test runs only when it is registered,
`--require NAME` fails when NAME is not registered, and nothing is downloaded or installed. The
`Services.BatteryPacks` test suite validates every pack in this folder and checks every file it references. Where this
machine has a pack's toolchain, it also runs the baseline test command on the clean fixture and checks that each
command-graded task fails before the work is done. When the toolchain is missing, the pack's case is skipped and the
missing tool is named.

| Pack | Toolchain | Tests (registry) | Pack-specific tasks |
|---|---|---|---|
| `csharp` | `dotnet`, `git` | `dotnet run --project tests/Inventory.Tests` (`TestRegistry.cs`) | See above. |
| `javascript` | `node`, `git` | `node tests/run.js` (`tests/registry.js`); `npm test` runs the same | Hidden check `item.totalValue`; BOM+CRLF `src/legacyCodes.js`; mutants of `truncate` (null guard returns `''`, `RangeError` becomes `TypeError`, boundary, cut length); Judge defects off-by-one, `find` returning null dereferenced, `Math.floor(percentOff / 100)`; lint `var`, missing JSDoc, `!=`, `console.log` |
| `python` | `python3`, `git` | `python3 -B -m tests.run`, stdlib `unittest` only (`tests/registry.py`) | Hidden check `Item.total_value`; BOM+CRLF `inventory/legacy_codes.py`; mutants of `truncate` (None guard returns `""`, `ValueError` becomes `TypeError`, boundary, cut length); Judge defects off-by-one, `find` returning None dereferenced, `percent_off // 100`; lint missing docstring, missing type hints, bare `except:`, `print`. `-B` keeps stale bytecode out of mutation runs |
| `go` | `go`, `git` | `go test -count=1 .`, flags after `-args` (`registry_test.go`) | Hidden check `(*Item).TotalValueCents`; BOM+CRLF `legacy_codes.go`; mutants of `Truncate` (empty guard returns nil, wrong sentinel error, boundary, cut length); Judge defects off-by-one, nil `Find` result dereferenced, integer `percentOff / 100`; lint missing doc comment, `reorder_list`, `panic`, `fmt.Println` |
| `rust` | `cargo`, `git` | `cargo test --offline`, flags after `--` (`tests/registry.rs`, `harness = false`) | Hidden check `Item::total_value_cents` (an overlay example); BOM+CRLF `src/legacy_codes.rs`; mutants of `truncate` (empty guard returns `Ok`, wrong error variant, boundary, cut length), each run after `cargo clean -p inventory` so a coarse file timestamp cannot hide a mutant; Judge defects off-by-one, `unwrap()` on an unknown SKU, integer `percent_off / 100`; lint missing `///` doc, `reorderList`, `unwrap()`, `println!` |
| `java` | `javac`, `java`, `git` | `javac -encoding UTF-8 -d build -sourcepath . tests/TestRunner.java`, then `java -cp build tests.TestRunner`; no Maven, Gradle or JUnit (`tests/TestRegistry.java`; javac compiles only what the runner reaches) | Hidden check `Item.getTotalValue()`; BOM+CRLF `config/legacy-codes.properties` (javac rejects a BOM in source, so the file is data); mutants of `TextUtil.truncate` (null guard returns `""`, `IllegalArgumentException` becomes `IllegalStateException`, boundary, cut length); Judge defects off-by-one, null `find` result dereferenced, integer `percentOff / 100`; lint `var`, missing Javadoc, string compared with `!=`, `System.out.println` |
| `php` | `php`, `git` | `php -d zend.assertions=1 tests/run.php`, plain `assert()` and no Composer (`tests/Registry.php`); exit 3 when assertions are disabled | Hidden check `Item::getTotalValue()`; BOM+CRLF `config/legacy_codes.ini` (a BOM before `<?php` breaks `declare`/`namespace`, so the file is data); mutants of `TextUtil::truncate` (null guard returns `''`, `LengthException` becomes `InvalidArgumentException`, boundary, cut length); Judge defects off-by-one, null `find` result dereferenced, `intdiv($percentOff, 100)`; lint missing `declare(strict_types=1)`, missing docblock, `!=`, `echo` |
| `ruby` | `ruby` (with its bundled minitest), `git` | `ruby tests/run.rb`, stdlib minitest (`tests/registry.rb`); a test file that requires `minitest/autorun` does not run unregistered tests | Hidden check `Item#total_value`; BOM+CRLF `lib/inventory/legacy_codes.rb`; mutants of `truncate` (nil guard returns `''`, `ArgumentError` becomes `RangeError`, boundary, cut length); Judge defects off-by-one range, nil `find` result dereferenced, Integer `percent_off / 100`; lint missing `# frozen_string_literal: true`, missing comment, camelCase `lowItems`, `puts` |

## Adding a pack

1. Create `battery/<id>/fixture/` with the fixture repository content. Add a `.gitattributes` there if any file must
   stay byte-exact.
2. Write `battery/<id>/battery-pack.json` with the template tasks adapted to the language or domain. Set `toolchain`
   to the tools the graders run.
3. Check it with `list_battery_packs`. It must show `valid: true`.
4. Add a real-toolchain test like `Battery.CSharpPack`, so the baseline fails and a reference solution passes for each
   task.

## Managing packs in the dashboard

Open **Battery packs** (`/battery`) near Captains, or use **Manage packs** in the Full Evaluate pack picker.
The paged list shows the directory id, name, domain/language, task count and validation state; hover over Invalid
for errors. View/Edit opens the manifest JSON editor and a relative file tree with byte sizes. Global admins can
create packs, save the manifest, and create, edit or delete UTF-8 text files under `fixture/` and `tasks/`.
Deleting a file or pack requires confirmation. Other signed-in people can inspect and validate packs.

New pack takes a safe lowercase directory slug and raw manifest JSON, and creates `battery-pack.json`, `fixture/`
and `tasks/`. Save manifest accepts parseable JSON even if validation fails, so a pack can be built in steps.
Validate checks the saved on-disk pack without writing; save your draft first. Invalid JSON is rejected without
replacing the manifest. File edits are capped at 1 MiB, binary content and symlinks are refused, and the manifest
cannot be deleted through the file editor. The management tree is limited to 256 files/directories.

Set `Evaluation.BatteryDirectory` to a writable persistent directory. A pack used by an in-process queued or
running battery cannot be changed or deleted (409); wait for the run to finish. This is a best-effort local check:
external filesystem writers, another Admiral sharing the directory, and a run starting concurrently with a save
can still race. Avoid editing a shared directory while runs are starting.
