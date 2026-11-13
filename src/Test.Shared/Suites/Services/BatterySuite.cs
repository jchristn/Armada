namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Fixture battery (docs/MODEL_HETEROGENEITY.md, Part 2): manifest validation (the C# pack, minimal and malformed
    /// manifests), every grader type (seeded-set matching with false positives, byte comparison, commands, rubric and
    /// schema check), the could-not-run path, fixture reset, the runner end to end with a canned dispatcher, and
    /// evaluation tagging of battery stage outcomes.
    /// </summary>
    public sealed class BatterySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.Battery";
        private static readonly byte[] _Bom = new byte[] { 0xEF, 0xBB, 0xBF };

        private const string NegativeAllFound =
            "## Correctness\n" +
            "The loop in ValueOf starts at index 1, so it skips the first SKU (off-by-one).\n\n" +
            "## Affected Case\nFile: src/Inventory/Stock.cs:89\nScenario: ValueOf([\"A\"]) returns 0; the first SKU is skipped (off-by-one).\n\n" +
            "## Affected Case\nScenario: an unknown SKU makes Find return null and `item!` throws NullReferenceException instead of KeyNotFoundException.\n\n" +
            "## Affected Case\nScenario: percentOff / 100 is integer division, so the rate is always 0 below 100.\n\n" +
            "## Verdict\n[ARMADA:VERDICT] FAIL\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ---- Manifest validation ----

            cases.Add(Case("csharp_pack_is_valid", "The C# pack loads and validates: ten tasks, the template personas and every grader type", TestTags.Positive, () =>
            {
                BatteryPack pack = CSharpPack();
                AssertTrue(pack.IsValid, "valid: " + String.Join(" | ", pack.Errors));
                AssertEqual("csharp", pack.Id);
                AssertEqual(1, pack.Manifest!.Version);
                AssertEqual(10, pack.Manifest.Tasks.Count, "ten template tasks");
                List<string> personas = pack.Manifest.Tasks.Select(t => t.Persona!).ToList();
                AssertEqual(4, personas.Count(p => p == "Worker"), "four Worker tasks");
                foreach (string persona in new[] { "Test Engineer", "Architect", "Linter", "Recorder" })
                    AssertEqual(1, personas.Count(p => p == persona), persona);
                AssertEqual(2, personas.Count(p => p == "Judge"), "positive and negative controls");
                foreach (BatteryGraderTypeEnum type in Enum.GetValues(typeof(BatteryGraderTypeEnum)))
                    AssertTrue(pack.Manifest.Tasks.Any(t => t.Grader!.Type == type), "grader type used: " + type);
                BatteryPackInfo info = pack.ToInfo();
                AssertEqual(64, info.ManifestHash!.Length, "sha-256 hash");
                AssertEqual("dotnet,git", String.Join(",", info.Toolchain));
                AssertEqual(0, info.Tasks.Single(t => t.Id == "judge-negative").Toolchain.Count, "Judge task needs no toolchain");
                AssertEqual(2, info.Tasks.Single(t => t.Id == "add-member").Toolchain.Count, "Worker task needs the pack toolchain");
            }));

            cases.Add(Case("minimal_manifest_reports_errors", "A minimal {} manifest is reported with every missing field, without throwing", TestTags.Negative, () =>
            {
                using TempDir dir = new TempDir();
                string pack = dir.Write("min/battery-pack.json", "{}");
                BatteryPack loaded = BatteryPackLoader.Load(Path.GetDirectoryName(pack)!);
                AssertFalse(loaded.IsValid, "invalid");
                AssertEqual("min", loaded.Id, "falls back to the directory name");
                string all = String.Join("\n", loaded.Errors);
                foreach (string expected in new[] { "schemaVersion must be 1", "id is required", "name is required", "version must be 1 or more", "fixture is required", "tasks must list at least one task" })
                    AssertContains(expected, all);
                AssertFalse(loaded.ToInfo().Valid, "info not valid");
            }));

            cases.Add(Case("invalid_json_and_missing_manifest", "Invalid JSON and a missing manifest are reported, not thrown", TestTags.Negative, () =>
            {
                using TempDir dir = new TempDir();
                dir.Write("broken/battery-pack.json", "{ \"id\": ");
                BatteryPack broken = BatteryPackLoader.Load(Path.Combine(dir.Path, "broken"));
                AssertFalse(broken.IsValid);
                AssertContains("not valid JSON", broken.Errors.Single());
                Directory.CreateDirectory(Path.Combine(dir.Path, "empty"));
                BatteryPack empty = BatteryPackLoader.Load(Path.Combine(dir.Path, "empty"));
                AssertContains("has no battery-pack.json", empty.Errors.Single());
                AssertEqual(1, BatteryPackLoader.LoadAll(dir.Path).Count, "LoadAll lists only folders with a manifest");
            }));

            cases.Add(Case("malformed_fields_are_named", "Each malformed field is reported: persona, ids, escaping paths, missing files, grader fields, toolchain names", TestTags.Negative, () =>
            {
                using TempDir dir = new TempDir();
                dir.Write("bad/fixture/a.txt", "a");
                dir.Write("bad/battery-pack.json", @"{
                  ""schemaVersion"": 1, ""id"": ""Bad Pack"", ""name"": ""Bad"", ""version"": 1,
                  ""fixture"": { ""path"": ""../outside"", ""defaultBranch"": ""main"" },
                  ""toolchain"": [ { ""name"": ""git"", ""executable"": ""git"" }, { ""name"": ""git"" } ],
                  ""tasks"": [
                    { ""id"": ""t1"", ""title"": ""T"", ""persona"": ""Wizard"", ""prompt"": ""p"", ""toolchain"": [ ""cargo"" ], ""attachments"": [ ""missing.diff"" ],
                      ""grader"": { ""type"": ""seededSet"", ""seeded"": [ { ""id"": ""s"", ""keywords"": [] } ], ""expectedVerdicts"": [ ""MAYBE"" ] } },
                    { ""id"": ""t1"", ""title"": ""T"", ""persona"": ""Worker"", ""prompt"": ""p"", ""grader"": { ""type"": ""mutation"", ""commands"": [ { ""executable"": ""dotnet"" } ] } },
                    { ""id"": ""t3"", ""title"": ""T"", ""persona"": ""Worker"", ""prompt"": ""p"", ""grader"": { } },
                    { ""id"": ""t4"", ""title"": ""T"", ""persona"": ""Architect"", ""prompt"": ""p"", ""grader"": { ""type"": ""rubric"", ""schema"": ""yaml"" } },
                    { ""id"": ""t5"", ""title"": ""T"", ""persona"": ""Worker"", ""prompt"": ""p"", ""grader"": { ""type"": ""byteComparison"", ""files"": [ { ""path"": ""/etc/passwd"", ""expected"": ""fixture/a.txt"" } ] } },
                    { ""id"": ""t6"", ""title"": ""T"", ""persona"": ""Worker"", ""grader"": { ""type"": ""command"", ""commands"": [] } }
                  ]
                }");
                BatteryPack pack = BatteryPackLoader.Load(Path.Combine(dir.Path, "bad"));
                AssertFalse(pack.IsValid);
                string all = String.Join("\n", pack.Errors);
                foreach (string expected in new[]
                {
                    "id 'Bad Pack' must be lowercase", "fixture.path must be a relative path inside the pack",
                    "toolchain[1].name 'git' is used twice", "toolchain[1].executable is required",
                    "persona 'Wizard' is not one of", "names 'cargo', which is not in the pack's toolchain",
                    "attachments[0] 'missing.diff' does not exist", "keywords must list at least one", "expectedVerdicts 'MAYBE'",
                    ".id is used twice", "mutants must list at least one mutant", "grader.type is required",
                    "schema 'yaml' is unknown", "rubric must be a relative path", "path must be a relative path inside the worktree",
                    "tasks[5] (t6).prompt is required", "commands must list at least one command"
                })
                {
                    AssertContains(expected, all);
                }
            }));

            cases.Add(Case("manifest_allows_bom_comments_trailing_commas", "A manifest with a BOM, comments and trailing commas parses", TestTags.Positive, () =>
            {
                using TempDir dir = new TempDir();
                string json = "// pack\n{ \"schemaVersion\": 1, \"id\": \"x\", }";
                byte[] bytes = _Bom.Concat(Encoding.UTF8.GetBytes(json)).ToArray();
                BatteryPackManifest? manifest = BatteryPackLoader.Parse(bytes);
                AssertNotNull(manifest);
                AssertEqual("x", manifest!.Id);
                AssertEqual(0, manifest.Tasks.Count, "missing lists default to empty");
                AssertEqual("main", new BatteryFixtureDefinition().DefaultBranch, "default branch");
            }));

            cases.Add(Case("catalog_lists_good_and_malformed", "The catalog lists valid and malformed packs from a configured folder; Find returns by id", TestTags.Positive, () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn");
                dir.Write("zbad/battery-pack.json", "{\"schemaVersion\":2}");
                BatteryPackCatalog catalog = new BatteryPackCatalog(() => dir.Path);
                List<BatteryPack> packs = catalog.LoadAll();
                AssertEqual("syn,zbad", String.Join(",", packs.Select(p => p.Id)));
                AssertTrue(packs[0].IsValid, "synthetic valid: " + String.Join(" | ", packs[0].Errors));
                AssertFalse(packs[1].IsValid, "malformed listed");
                AssertNotNull(catalog.Find("syn"));
                AssertNull(catalog.Find("nope"));
                AssertNotNull(new BatteryPackCatalog(() => null).ResolveDirectory(), "the repository battery folder is found without configuration");
            }));

            cases.Add(Case("catalog_pages_from_disk", "The catalog reads one page of pack directories at a time with an exact total, a partial last page and an empty page past the end", TestTags.Positive, () =>
            {
                using TempDir dir = new TempDir();
                foreach (string id in new[] { "pa", "pb", "pc" }) WriteSyntheticPack(dir, id);
                dir.Write("pd/battery-pack.json", "{\"schemaVersion\":2}");
                Directory.CreateDirectory(Path.Combine(dir.Path, "no-manifest"));
                BatteryPackCatalog catalog = new BatteryPackCatalog(() => dir.Path);
                EnumerationResult<BatteryPack> first = catalog.LoadPage(1, 3);
                AssertEqual(4L, first.TotalRecords, "directories with a manifest");
                AssertEqual(2, first.TotalPages);
                AssertEqual("pa,pb,pc", String.Join(",", first.Objects.Select(p => p.Id)));
                EnumerationResult<BatteryPack> last = catalog.LoadPage(2, 3);
                AssertEqual("pd", String.Join(",", last.Objects.Select(p => p.Id)), "partial last page");
                AssertEqual(0, catalog.LoadPage(3, 3).Objects.Count, "past the end");
                AssertEqual(1000, catalog.LoadPage(1, 5000).PageSize, "oversized page size normalized");
            }));

            cases.Add(Case("manifest_over_limits_is_malformed", "A manifest with more than 256 tasks is malformed with one error naming the limit; its info lists 256 tasks and the true count", TestTags.Negative, () =>
            {
                using TempDir dir = new TempDir();
                dir.Write("big/fixture/a.txt", "a");
                System.Text.StringBuilder tasks = new System.Text.StringBuilder();
                for (int i = 0; i < 300; i++)
                {
                    if (i > 0) tasks.Append(',');
                    tasks.Append("{ \"id\": \"t").Append(i).Append("\", \"title\": \"T\", \"persona\": \"Worker\", \"prompt\": \"p\", \"grader\": { \"type\": \"rubric\", \"rubric\": \"r.md\" } }");
                }

                dir.Write("big/battery-pack.json", "{ \"schemaVersion\": 1, \"id\": \"big\", \"name\": \"Big\", \"version\": 1, \"fixture\": { \"path\": \"fixture\" }, \"tasks\": [" + tasks + "] }");
                BatteryPack pack = BatteryPackLoader.Load(Path.Combine(dir.Path, "big"));
                AssertFalse(pack.IsValid, "over the limit is malformed");
                AssertEqual(1, pack.Errors.Count, "one bounded error");
                AssertContains("Tasks has 300 entries; the most allowed is 256", pack.Errors[0]);
                BatteryPackInfo info = pack.ToInfo();
                AssertEqual(256, info.Tasks.Count, "listed tasks bounded");
                AssertEqual(300, info.TaskCount, "true count recorded");
                CollectionBounds.Validate(info);
            }));

            cases.Add(Case("seeded_false_positives_bounded_and_counted", "More than 256 false positives keep the first 256 in the evidence and count every one for the grade", TestTags.Negative, () =>
            {
                BatteryGraderDefinition grader = CSharpTask("lint-diff").Grader!;
                System.Text.StringBuilder output = new System.Text.StringBuilder("Violations:\n");
                for (int i = 0; i < 300; i++) output.Append("- Invented rule ").Append(i).Append(": the widget is shaped oddly.\n");
                BatteryGrade grade = BatterySeededSetGrader.Grade(output.ToString(), grader);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, grade.Outcome);
                AssertEqual(300, grade.Details.FalsePositiveCount, "every false positive counted");
                AssertEqual(256, grade.Details.FalsePositives.Count, "evidence bounded");
                AssertContains("300 false positives", grade.Reason!);
                CollectionBounds.Validate(grade.Details);
            }));

            // ---- Seeded-set grading ----

            cases.Add(Case("seeded_negative_control_all_found", "Negative control: three seeded defects found, no false positives, FAIL verdict passes", TestTags.Positive, () =>
            {
                BatteryGrade grade = BatterySeededSetGrader.Grade(NegativeAllFound, CSharpTask("judge-negative").Grader!);
                AssertEqual(BatteryTaskOutcomeEnum.Passed, grade.Outcome, grade.Reason);
                AssertEqual(3, grade.Details.Matched.Count);
                AssertEqual(0, grade.Details.FalsePositives.Count);
                AssertEqual(3, grade.Details.FindingCount, "three Affected Case findings");
                AssertEqual("FAIL", grade.Details.Verdict);
                AssertEqual((double?)1.0, grade.Score);
            }));

            cases.Add(Case("seeded_false_positive_counts", "Negative control: an invented blocking finding is a false positive and fails the task", TestTags.Negative, () =>
            {
                string output = NegativeAllFound.Replace("## Verdict", "## Affected Case\nScenario: the class should be sealed for performance.\n\n## Verdict");
                BatteryGrade grade = BatterySeededSetGrader.Grade(output, CSharpTask("judge-negative").Grader!);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, grade.Outcome);
                AssertEqual(3, grade.Details.Matched.Count, "all seeded still found");
                AssertEqual(1, grade.Details.FalsePositives.Count, "one false positive");
                AssertContains("sealed", grade.Details.FalsePositives[0]);
                AssertContains("1 false positive", grade.Reason!);
            }));

            cases.Add(Case("seeded_missed_and_wrong_verdict", "Negative control: a missed defect and a PASS verdict both fail with reasons", TestTags.Negative, () =>
            {
                string missed = NegativeAllFound.Replace("percentOff / 100 is integer division, so the rate is always 0 below 100.", "the discount looks fine.");
                BatteryGrade one = BatterySeededSetGrader.Grade(missed, CSharpTask("judge-negative").Grader!);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, one.Outcome);
                AssertEqual("integer-division", one.Details.Missed.Single());
                AssertEqual(1, one.Details.FalsePositives.Count, "the vague finding matches nothing");
                AssertEqual((double?)(2.0 / 3.0), one.Score);

                BatteryGrade pass = BatterySeededSetGrader.Grade(NegativeAllFound.Replace("FAIL", "PASS"), CSharpTask("judge-negative").Grader!);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, pass.Outcome);
                AssertContains("Verdict PASS is not one of FAIL, NEEDS_REVISION", pass.Reason!);
            }));

            cases.Add(Case("seeded_positive_control", "Positive control: PASS with only optional notes passes; an invented Affected Case or a FAIL verdict fails", TestTags.Positive, () =>
            {
                BatteryGraderDefinition grader = CSharpTask("judge-positive").Grader!;
                string clean = "## Correctness\n- Contains checks for null and uses ContainsKey.\n\n## Optional improvements\n- Consider a test for a null SKU.\n\n[ARMADA:VERDICT] PASS\n";
                BatteryGrade ok = BatterySeededSetGrader.Grade(clean, grader);
                AssertEqual(BatteryTaskOutcomeEnum.Passed, ok.Outcome, ok.Reason);
                AssertEqual(0, ok.Details.FindingCount, "no blocking findings");
                AssertEqual((double?)1.0, ok.Score);

                string invented = clean.Replace("[ARMADA:VERDICT] PASS", "## Affected Case\nScenario: Contains should be case-insensitive.\n\n[ARMADA:VERDICT] PASS");
                BatteryGrade bad = BatterySeededSetGrader.Grade(invented, grader);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, bad.Outcome);
                AssertEqual(1, bad.Details.FalsePositives.Count, "invented blocking finding");

                BatteryGrade fail = BatterySeededSetGrader.Grade(clean.Replace("PASS", "NEEDS_REVISION"), grader);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, fail.Outcome);
                AssertEqual("NEEDS_REVISION", fail.Details.Verdict);
            }));

            cases.Add(Case("seeded_linter_list_items", "Linter: each list item is a finding; optional sections and code fences are ignored; extras are false positives", TestTags.Positive, () =>
            {
                BatteryGraderDefinition grader = CSharpTask("lint-diff").Grader!;
                string all =
                    "Violations:\n" +
                    "- Rule 1: `var result` should declare an explicit type.\n" +
                    "- Rule 2: public method Suggest has no XML doc comment.\n" +
                    "- Rule 3: private field `threshold` must be `_Threshold` (_PascalCase).\n" +
                    "- Rule 6: Console.WriteLine in library code.\n\n" +
                    "```csharp\n- var x = 1; // quoted code is not a finding\n```\n\n" +
                    "## Suggestions (optional)\n- Add a blank line before return.\n";
                BatteryGrade ok = BatterySeededSetGrader.Grade(all, grader);
                AssertEqual(BatteryTaskOutcomeEnum.Passed, ok.Outcome, ok.Reason);
                AssertEqual(4, ok.Details.FindingCount, "four findings");

                BatteryGrade extra = BatterySeededSetGrader.Grade(all.Replace("Violations:\n", "Violations:\n- Rule 5: the class should be split into two files.\n"), grader);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, extra.Outcome);
                AssertEqual(1, extra.Details.FalsePositives.Count);

                BatteryGrade empty = BatterySeededSetGrader.Grade("   ", grader);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, empty.Outcome);
                AssertEqual(4, empty.Details.Missed.Count, "everything missed on empty output");
            }));

            // ---- Byte comparison ----

            cases.Add(CaseAsync("byte_hash_matches_git", "GitBlobHash equals git hash-object --no-filters for BOM and CRLF content", TestTags.Positive, async () =>
            {
                using TempDir dir = new TempDir();
                byte[] bytes = _Bom.Concat(Encoding.UTF8.GetBytes("a\r\nb\r\n")).ToArray();
                string path = dir.WriteBytes("f.cs", bytes);
                string git = CannedBatteryTaskDispatcher.Git(dir.Path, "hash-object", "--no-filters", path).Trim();
                AssertEqual(git, BatteryByteComparison.GitBlobHash(bytes));
                await Task.CompletedTask.ConfigureAwait(false);
            }));

            cases.Add(Case("byte_compare_explains_differences", "Byte comparison names a dropped BOM, CRLF converted to LF, both, other changes, and a missing file", TestTags.Negative, () =>
            {
                byte[] expected = _Bom.Concat(Encoding.UTF8.GetBytes("x = 12;\r\ny = 1;\r\n")).ToArray();
                byte[] noBom = Encoding.UTF8.GetBytes("x = 12;\r\ny = 1;\r\n");
                byte[] lf = _Bom.Concat(Encoding.UTF8.GetBytes("x = 12;\ny = 1;\n")).ToArray();
                byte[] neither = Encoding.UTF8.GetBytes("x = 12;\ny = 1;\n");
                AssertTrue(BatteryByteComparison.Compare("f", expected, BatteryByteComparison.GitBlobHash(expected)).Matched, "identical");
                AssertEqual("The byte order mark was dropped (line endings kept).", BatteryByteComparison.Compare("f", expected, BatteryByteComparison.GitBlobHash(noBom)).Reason);
                AssertEqual("CRLF line endings were converted to LF (byte order mark kept).", BatteryByteComparison.Compare("f", expected, BatteryByteComparison.GitBlobHash(lf)).Reason);
                AssertEqual("The byte order mark was dropped and CRLF line endings were converted to LF.", BatteryByteComparison.Compare("f", expected, BatteryByteComparison.GitBlobHash(neither)).Reason);
                BatteryFileComparison other = BatteryByteComparison.Compare("f", expected, BatteryByteComparison.GitBlobHash(Encoding.UTF8.GetBytes("x = 8;")));
                AssertFalse(other.Matched);
                AssertContains("content differs", other.Reason!);
                AssertContains("missing", BatteryByteComparison.Compare("f", expected, null).Reason!);
            }));

            // ---- Rubric and schema ----

            cases.Add(CaseAsync("rubric_pending_and_schema_check", "Rubric tasks are PendingEvaluation with the rubric; the armada-plan schema check fails a missing plan", TestTags.Positive, async () =>
            {
                BatteryPack pack = CSharpPack();
                string plan = "```armada-plan\n{\"missions\":[{\"title\":\"Parser\",\"description\":\"parse csv\"},{\"title\":\"Import\",\"dependsOn\":1,\"waitForOtherMissions\":true}]}\n```\n";
                BatteryGrade ok = await BatteryTaskGrader.GradeAsync(pack, CSharpTask("architect-plan"), plan, null).ConfigureAwait(false);
                AssertEqual(BatteryTaskOutcomeEnum.PendingEvaluation, ok.Outcome, ok.Reason);
                AssertContains("Slicing", ok.Details.Rubric!);
                AssertContains("2 missions", ok.Summary!);

                BatteryGrade bad = await BatteryTaskGrader.GradeAsync(pack, CSharpTask("architect-plan"), "I would split this into a parser and an importer.", null).ConfigureAwait(false);
                AssertEqual(BatteryTaskOutcomeEnum.Failed, bad.Outcome);
                AssertNotNull(bad.Details.SchemaError, "schema error recorded");

                BatteryGrade summary = await BatteryTaskGrader.GradeAsync(pack, CSharpTask("summarize-voyage"), "Two missions landed; one dropped the BOM.", null).ConfigureAwait(false);
                AssertEqual(BatteryTaskOutcomeEnum.PendingEvaluation, summary.Outcome);
                AssertContains("Brevity", summary.Details.Rubric!);

                BatteryGrade noWorktree = await BatteryTaskGrader.GradeAsync(pack, CSharpTask("bom-crlf"), "done", null).ConfigureAwait(false);
                AssertEqual(BatteryTaskOutcomeEnum.CouldNotRun, noWorktree.Outcome, "command-style graders need a worktree");
            }));

            // ---- Toolchain: could not run ----

            cases.Add(CaseAsync("toolchain_missing_is_could_not_run", "A toolchain probe that cannot start reports the tool, its hint and the host", TestTags.Negative, async () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn", missingTool: true);
                BatteryPack pack = BatteryPackLoader.Load(Path.Combine(dir.Path, "syn"));
                AssertTrue(pack.IsValid, String.Join(" | ", pack.Errors));
                IBatteryWorkspace workspace = LocalWorkspace(dir.Path);
                BatteryToolchainCheck check = await BatteryTaskGrader.CheckToolchainAsync(pack, pack.Manifest!.Tasks.Single(t => t.Id == "check"), workspace).ConfigureAwait(false);
                AssertEqual("nosuchtool", check.Missing.Single());
                AssertContains("The Admiral host lacks the pack's toolchain: nosuchtool (install nosuchtool)", check.Reason!);
                BatteryToolchainCheck none = await BatteryTaskGrader.CheckToolchainAsync(pack, pack.Manifest.Tasks.Single(t => t.Id == "review"), workspace).ConfigureAwait(false);
                AssertEqual(0, none.Missing.Count, "a task with an empty toolchain probes nothing");
            }));

            cases.Add(CaseAsync("runner_missing_toolchain_never_fails", "Runner: tasks whose toolchain is missing are CouldNotRun (never Failed) and later ones are not dispatched", TestTags.Negative, async () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn", missingTool: true);
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedPipelineAsync(testDb.Driver).ConfigureAwait(false);
                using CannedBatteryTaskDispatcher dispatcher = new CannedBatteryTaskDispatcher { Captain = ScriptedCaptain(true) };
                BatteryRunService service = NewService(testDb.Driver, dir, dispatcher);
                BatteryRun started = await service.StartAsync(TestCaptain(), null, new List<string> { "syn" }, 2, 5, true, Constants.DefaultTenantId, null).ConfigureAwait(false);
                BatteryRun run = (await service.WaitAsync(started.Id).ConfigureAwait(false))!;

                AssertEqual(BatteryRunStatusEnum.Completed, run.Status, run.FailureReason);
                List<BatteryTaskResult> needing = run.Results.Where(r => r.TaskId == "edit" || r.TaskId == "check").ToList();
                AssertEqual(4, needing.Count, "two tasks x two runs");
                AssertTrue(needing.All(r => r.Outcome == BatteryTaskOutcomeEnum.CouldNotRun), "could not run: " + String.Join(", ", needing.Select(r => r.Outcome)));
                AssertTrue(needing.All(r => r.Reason!.Contains("nosuchtool", StringComparison.Ordinal)), "reason names the tool");
                AssertEqual(0, run.FailedCount, "never failed");
                AssertEqual(4, run.CouldNotRunCount);
                int dispatchedNeeding = dispatcher.Requests.Count(r => r.Title.Contains("/edit ", StringComparison.Ordinal) || r.Title.Contains("/check ", StringComparison.Ordinal));
                AssertEqual(1, dispatchedNeeding, "only the first task that needs the tool is dispatched");
                AssertTrue(run.Results.Where(r => r.TaskId == "review").All(r => r.Outcome == BatteryTaskOutcomeEnum.Passed), "tasks without the tool still run");
            }));

            cases.Add(CaseAsync("runner_prompt_variant_reaches_every_dispatch_and_configuration", "Runner: a run's prompt variant reaches every task dispatch and the run configuration; a run without one records none", TestTags.Positive, async () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn");
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedPipelineAsync(testDb.Driver).ConfigureAwait(false);

                using CannedBatteryTaskDispatcher leanDispatcher = new CannedBatteryTaskDispatcher { Captain = ScriptedCaptain(true) };
                BatteryRunService leanService = NewService(testDb.Driver, dir, leanDispatcher);
                BatteryRun leanStarted = await leanService.StartAsync(TestCaptain(), null, new List<string> { "syn" }, 1, 5, true, Constants.DefaultTenantId, null, PromptVariantEnum.Lean).ConfigureAwait(false);
                BatteryRun lean = (await leanService.WaitAsync(leanStarted.Id).ConfigureAwait(false))!;
                AssertEqual(BatteryRunStatusEnum.Completed, lean.Status, lean.FailureReason);
                AssertTrue(leanDispatcher.Requests.Count > 0, "tasks were dispatched");
                AssertTrue(leanDispatcher.Requests.All(r => r.PromptVariant == PromptVariantEnum.Lean), "every task dispatch carries the run's variant");
                AssertContains("\"promptVariant\":\"Lean\"", lean.ConfigurationJson!, "the run configuration records the variant");

                using CannedBatteryTaskDispatcher plainDispatcher = new CannedBatteryTaskDispatcher { Captain = ScriptedCaptain(true) };
                BatteryRunService plainService = NewService(testDb.Driver, dir, plainDispatcher);
                BatteryRun plainStarted = await plainService.StartAsync(TestCaptain(), null, new List<string> { "syn" }, 1, 5, true, Constants.DefaultTenantId, null).ConfigureAwait(false);
                BatteryRun plain = (await plainService.WaitAsync(plainStarted.Id).ConfigureAwait(false))!;
                AssertTrue(plainDispatcher.Requests.All(r => r.PromptVariant == null), "a run without a variant leaves it to the captain");
                AssertFalse(plain.ConfigurationJson!.Contains("promptVariant", StringComparison.Ordinal), "a Current run keeps its pre-variant configuration");
                AssertTrue(lean.ConfigurationRevision != plain.ConfigurationRevision, "runs that differ only in the variant have different configuration revisions");

                using CannedBatteryTaskDispatcher currentDispatcher = new CannedBatteryTaskDispatcher { Captain = ScriptedCaptain(true) };
                BatteryRunService currentService = NewService(testDb.Driver, dir, currentDispatcher);
                BatteryRun currentStarted = await currentService.StartAsync(TestCaptain(), null, new List<string> { "syn" }, 1, 5, true, Constants.DefaultTenantId, null, PromptVariantEnum.Current).ConfigureAwait(false);
                await currentService.WaitAsync(currentStarted.Id).ConfigureAwait(false);
                AssertEqual(plainStarted.ConfigurationRevision, currentStarted.ConfigurationRevision, "an explicit Current run has the same configuration as a run without a variant");

                Captain compactCaptain = TestCaptain();
                compactCaptain.RuntimeOptionsJson = "{\"promptVariant\":\"Compact\"}";
                using CannedBatteryTaskDispatcher captainDispatcher = new CannedBatteryTaskDispatcher { Captain = ScriptedCaptain(true) };
                BatteryRunService captainService = NewService(testDb.Driver, dir, captainDispatcher);
                BatteryRun captainStarted = await captainService.StartAsync(compactCaptain, null, new List<string> { "syn" }, 1, 5, true, Constants.DefaultTenantId, null).ConfigureAwait(false);
                await captainService.WaitAsync(captainStarted.Id).ConfigureAwait(false);
                AssertContains("\"promptVariant\":\"Compact\"", captainStarted.ConfigurationJson!, "a captain's own variant is recorded as the effective variant");

                await AssertThrowsAsync<ArgumentException>(() => leanService.StartAsync(TestCaptain(), null, new List<string> { "syn" }, 1, 5, true, Constants.DefaultTenantId, null, (PromptVariantEnum)7)).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("dispatcher_records_prompt_variant_on_voyage_override", "Dispatch: a request's prompt variant is recorded on the voyage's captain override", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("battery-variant", "file:///tmp/x.git") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Captain captain = await db.Captains.CreateAsync(new Captain("variant-captain") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                IAdmiralService admiral = RecordingBatteryAdmiral.Create(db);
                RecordingBatteryAdmiral recorder = (RecordingBatteryAdmiral)(object)admiral;
                AdmiralBatteryTaskDispatcher dispatcher = new AdmiralBatteryTaskDispatcher(admiral, db, Logging(), TimeSpan.FromMilliseconds(50));

                BatteryDispatchRequest request = new BatteryDispatchRequest(vessel, captain, "[syn/edit #1] Edit", "Edit this.", "Worker", 300)
                {
                    PromptVariant = PromptVariantEnum.Compact
                };
                await dispatcher.RunAsync(request).ConfigureAwait(false);

                object?[] args = recorder.Dispatches.Single();
                List<CaptainAssignmentOverride> overrides = MissionService.DeserializeCaptainOverrides((string)args[6]!);
                AssertEqual((PromptVariantEnum?)PromptVariantEnum.Compact, overrides.Single().PromptVariant, "the voyage override carries the run's variant");
                AssertEqual(PromptVariantEnum.Compact, PromptVariantResolver.Resolve("Worker", overrides, captain), "missions of the voyage resolve to the run's variant");
            }));

            // ---- Fixture reset ----

            cases.Add(CaseAsync("fixture_reset_restores_and_applies_setup", "Reset commits the exact fixture plus task setup as a fast-forward, undoing changes pushed since", TestTags.Positive, async () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn");
                BatteryPack pack = BatteryPackLoader.Load(Path.Combine(dir.Path, "syn"));
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                BatteryFixtureService fixtures = new BatteryFixtureService(testDb.Driver, Logging(), Path.Combine(dir.Path, "data"));

                string first = await fixtures.ResetAsync(pack, null, "one").ConfigureAwait(false);
                string origin = fixtures.OriginPathFor("syn");
                string clone = Path.Combine(dir.Path, "clone");
                CannedBatteryTaskDispatcher.Git(dir.Path, "clone", "--quiet", origin, clone);
                File.WriteAllText(Path.Combine(clone, "stray.txt"), "landed by accident");
                File.WriteAllText(Path.Combine(clone, "hello.txt"), "changed");
                CannedBatteryTaskDispatcher.Git(clone, "add", "--all");
                CannedBatteryTaskDispatcher.Git(clone, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "stray");
                CannedBatteryTaskDispatcher.Git(clone, "push", "--quiet", "origin", "HEAD:main");
                string stray = CannedBatteryTaskDispatcher.Git(clone, "rev-parse", "HEAD").Trim();

                BatteryTaskDefinition withSetup = pack.Manifest!.Tasks.Single(t => t.Id == "check");
                string second = await fixtures.ResetAsync(pack, withSetup, "two").ConfigureAwait(false);
                AssertNotEqual(first, second, "a new commit");
                CannedBatteryTaskDispatcher.Git(clone, "fetch", "--quiet", "origin");
                CannedBatteryTaskDispatcher.Git(clone, "merge-base", "--is-ancestor", stray, "origin/main");
                string tree = CannedBatteryTaskDispatcher.Git(clone, "ls-tree", "-r", "--name-only", "origin/main");
                AssertFalse(tree.Contains("stray.txt", StringComparison.Ordinal), "stray file removed: " + tree);
                AssertTrue(tree.Contains("setup-marker.txt", StringComparison.Ordinal), "task setup applied: " + tree);
                string hash = CannedBatteryTaskDispatcher.Git(clone, "rev-parse", "origin/main:hello.txt").Trim();
                byte[] fixtureBytes = File.ReadAllBytes(Path.Combine(dir.Path, "syn", "fixture", "hello.txt"));
                AssertEqual(BatteryByteComparison.GitBlobHash(fixtureBytes), hash, "BOM and CRLF fixture stored byte for byte");
            }));

            cases.Add(CaseAsync("fixture_vessel_on_request_and_tagging", "The fixture vessel is created only on request, under ModelEvaluation with no landing; the mission flag, not the pipeline, tags stage outcomes", TestTags.Positive, async () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn");
                BatteryPack pack = BatteryPackLoader.Load(Path.Combine(dir.Path, "syn"));
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Pipeline pipeline = await SeedPipelineAsync(db).ConfigureAwait(false);
                BatteryFixtureService fixtures = new BatteryFixtureService(db, Logging(), Path.Combine(dir.Path, "data"));

                await AssertThrowsAsync<ArgumentException>(() => fixtures.EnsureVesselAsync(pack, Constants.DefaultTenantId, null, false)).ConfigureAwait(false);
                Vessel vessel = await fixtures.EnsureVesselAsync(pack, Constants.DefaultTenantId, null, true).ConfigureAwait(false);
                AssertEqual("battery-syn", vessel.Name);
                AssertEqual(fixtures.OriginPathFor("syn"), vessel.RepoUrl);
                AssertEqual(pipeline.Id, vessel.DefaultPipelineId);
                AssertEqual((LandingModeEnum?)LandingModeEnum.None, vessel.LandingMode);
                Vessel again = await fixtures.EnsureVesselAsync(pack, Constants.DefaultTenantId, null, true).ConfigureAwait(false);
                AssertEqual(vessel.Id, again.Id, "reused, not duplicated");

                Vessel production = await db.Vessels.CreateAsync(new Vessel("prod", "https://example.invalid/prod.git") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Captain captain = await db.Captains.CreateAsync(new Captain("battery-captain") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Mission battery = await db.Missions.CreateAsync(new Mission("battery task") { VesselId = vessel.Id, TenantId = Constants.DefaultTenantId, IsEvaluation = true }).ConfigureAwait(false);
                Mission normal = await db.Missions.CreateAsync(new Mission("real task") { VesselId = production.Id, TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Mission unflagged = await db.Missions.CreateAsync(new Mission("unflagged task on the fixture vessel") { VesselId = vessel.Id, TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Mission flaggedElsewhere = await db.Missions.CreateAsync(new Mission("flagged task on a production vessel") { VesselId = production.Id, TenantId = Constants.DefaultTenantId, IsEvaluation = true }).ConfigureAwait(false);

                StageOutcomeService outcomes = new StageOutcomeService(db, Logging());
                StageOutcome? tagged = await outcomes.RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = battery.Id, Captain = captain, ExitCode = 0 }).ConfigureAwait(false);
                StageOutcome? untagged = await outcomes.RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = normal.Id, Captain = captain, ExitCode = 0 }).ConfigureAwait(false);
                AssertNotNull(tagged, "battery outcome written");
                AssertTrue(tagged!.IsEvaluation, "battery stage outcome tagged as evaluation");
                AssertFalse(untagged!.IsEvaluation, "production stage outcome not tagged");
                StageOutcome? pipelineOnly = await outcomes.RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = unflagged.Id, Captain = captain, ExitCode = 0 }).ConfigureAwait(false);
                StageOutcome? flagOnly = await outcomes.RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = flaggedElsewhere.Id, Captain = captain, ExitCode = 0 }).ConfigureAwait(false);
                AssertFalse(pipelineOnly!.IsEvaluation, "the ModelEvaluation pipeline alone does not tag an outcome");
                AssertTrue(flagOnly!.IsEvaluation, "the mission flag tags an outcome on any vessel");
            }));

            // ---- Runner end to end ----

            cases.Add(CaseAsync("runner_grades_and_records_provenance", "Runner: resets before each task, grades every type, and records seed, sampling, battery version and revision", TestTags.Positive, async () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn");
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedPipelineAsync(testDb.Driver).ConfigureAwait(false);
                using CannedBatteryTaskDispatcher dispatcher = new CannedBatteryTaskDispatcher { Captain = ScriptedCaptain(true, pushStray: true) };
                BatteryRunService service = NewService(testDb.Driver, dir, dispatcher);
                ModelEndpoint endpoint = new ModelEndpoint { Name = "ep", BaseUrl = "http://gb10.local:11434", Model = "qwen3:32b", InferenceOptions = new InferenceOptions { Temperature = 0.3, ContextLength = 32768 } };

                BatteryRun started = await service.StartAsync(TestCaptain(), endpoint, new List<string> { "syn" }, 2, 77, true, Constants.DefaultTenantId, null).ConfigureAwait(false);
                AssertEqual(BatteryRunStatusEnum.Queued, started.Status, "returned queued");
                BatteryRun run = (await service.WaitAsync(started.Id).ConfigureAwait(false))!;

                AssertEqual(BatteryRunStatusEnum.Completed, run.Status, run.FailureReason);
                AssertEqual("syn@1", run.BatteryVersion);
                AssertEqual(2, run.RunCount);
                AssertEqual((int?)77, run.Seed);
                AssertTrue(run.IsEvaluation, "evaluation");
                AssertEqual("ModelEvaluation", run.Pipeline);
                AssertEqual("gb10.local", run.InferenceHost);
                AssertContains("\"temperature\":0.3", run.SamplingJson!);
                AssertContains("\"contextLength\":32768", run.SamplingJson!);
                AssertContains("\"manifestHash\"", run.ConfigurationJson!);
                string expectedRevision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(run.ConfigurationJson!))).ToLowerInvariant().Substring(0, 16);
                AssertEqual(expectedRevision, run.ConfigurationRevision, "revision is the hash of the configuration");
                AssertEqual(8, run.TotalTasks);
                AssertEqual(8, run.Results.Count, "four tasks x two runs");
                AssertEqual(6, run.PassedCount, "edit, check and review pass twice: " + Describe(run));
                AssertEqual(2, run.PendingEvaluationCount, "plan pending evaluation twice");
                AssertEqual(0, run.FailedCount);
                AssertTrue(run.Results.Where(r => r.TaskId == "plan").All(r => r.Output!.Contains("armada-plan", StringComparison.Ordinal)), "raw output stored");
                AssertEqual(8, run.Results.Select(r => r.FixtureCommit).Distinct().Count(), "a fresh reset per task attempt");
                AssertEqual("1,1,1,1,2,2,2,2", String.Join(",", run.Results.Select(r => r.Attempt)));

                BatteryDispatchRequest review = dispatcher.Requests.First(r => r.Persona == "Judge");
                AssertContains("```diff", review.Description, "attachment fenced into the description");
                AssertContains("for (int i = 1;", review.Description);
                AssertEqual("cpt_battery", review.Captain.Id, "pinned to the captain");
                AssertEqual(60 * 60000, review.TimeoutMs, "default task timeout");
            }));

            cases.Add(CaseAsync("runner_failed_paths", "Runner: a BOM-dropping edit, a failed mission and missing output fail with reasons; a reclaimed worktree is could-not-run", TestTags.Negative, async () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn");
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedPipelineAsync(testDb.Driver).ConfigureAwait(false);
                using CannedBatteryTaskDispatcher dispatcher = new CannedBatteryTaskDispatcher { Captain = ScriptedCaptain(false) };
                BatteryRunService service = NewService(testDb.Driver, dir, dispatcher);
                BatteryRun run = (await service.WaitAsync((await service.StartAsync(TestCaptain(), null, new List<string> { "syn" }, 1, null, true, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertNotNull(run.Seed, "a seed is picked when none is given");
                BatteryTaskResult edit = run.Results.Single(r => r.TaskId == "edit");
                AssertEqual(BatteryTaskOutcomeEnum.Failed, edit.Outcome);
                AssertContains("byte order mark was dropped", edit.Reason!);
                BatteryTaskResult review = run.Results.Single(r => r.TaskId == "review");
                AssertEqual(BatteryTaskOutcomeEnum.Failed, review.Outcome, "no output");

                dispatcher.ReclaimWorktree = true;
                BatteryRun reclaimed = (await service.WaitAsync((await service.StartAsync(TestCaptain(), null, new List<string> { "syn" }, 1, null, false, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                BatteryTaskResult gone = reclaimed.Results.Single(r => r.TaskId == "edit");
                AssertEqual(BatteryTaskOutcomeEnum.CouldNotRun, gone.Outcome, "a reclaimed worktree is not the model's failure");
                AssertContains("no longer exists", gone.Reason!);
                dispatcher.ReclaimWorktree = false;

                dispatcher.Status = MissionStatusEnum.Failed;
                BatteryRun failed = (await service.WaitAsync((await service.StartAsync(TestCaptain(), null, new List<string> { "syn" }, 1, null, false, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertTrue(failed.Results.All(r => r.Outcome == BatteryTaskOutcomeEnum.Failed), "failed missions fail");
                AssertContains("The mission ended Failed", failed.Results[0].Reason!);
            }));

            cases.Add(CaseAsync("runner_rejects_bad_requests", "Runner: no packs, unknown or malformed packs, bad run counts and a missing fixture vessel are ArgumentException", TestTags.Negative, async () =>
            {
                using TempDir dir = new TempDir();
                WriteSyntheticPack(dir, "syn");
                dir.Write("bad/battery-pack.json", "{}");
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedPipelineAsync(testDb.Driver).ConfigureAwait(false);
                using CannedBatteryTaskDispatcher dispatcher = new CannedBatteryTaskDispatcher();
                BatteryRunService service = NewService(testDb.Driver, dir, dispatcher);
                Captain captain = TestCaptain();
                await AssertThrowsAsync<ArgumentException>(() => service.StartAsync(captain, null, new List<string>(), null, null, true, null, null)).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.StartAsync(captain, null, new List<string> { "nope" }, null, null, true, null, null)).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.StartAsync(captain, null, new List<string> { "bad" }, null, null, true, null, null)).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.StartAsync(captain, null, new List<string> { "syn" }, 0, null, true, null, null)).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.StartAsync(captain, null, new List<string> { "syn" }, 11, null, true, null, null)).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.StartAsync(captain, null, new List<string> { "syn" }, null, null, false, null, null)).ConfigureAwait(false);
                AssertEqual(0, dispatcher.Requests.Count, "nothing dispatched");
                AssertEqual(0L, (await testDb.Driver.BatteryRuns.EnumerateAsync(new BatteryRunQuery()).ConfigureAwait(false)).TotalRecords, "no run recorded");
            }));

            cases.Add(CaseAsync("runner_recovers_interrupted", "Runs left Queued or Running by an earlier process are marked Failed at startup", TestTags.Reliability, async () =>
            {
                using TempDir dir = new TempDir();
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using CannedBatteryTaskDispatcher dispatcher = new CannedBatteryTaskDispatcher();
                await testDb.Driver.BatteryRuns.CreateAsync(new BatteryRun { Status = BatteryRunStatusEnum.Running }).ConfigureAwait(false);
                await testDb.Driver.BatteryRuns.CreateAsync(new BatteryRun { Status = BatteryRunStatusEnum.Queued }).ConfigureAwait(false);
                await testDb.Driver.BatteryRuns.CreateAsync(new BatteryRun { Status = BatteryRunStatusEnum.Completed }).ConfigureAwait(false);
                BatteryRunService service = NewService(testDb.Driver, dir, dispatcher);
                AssertEqual(2, await service.RecoverInterruptedAsync().ConfigureAwait(false));
                EnumerationResult<BatteryRun> failed = await testDb.Driver.BatteryRuns.EnumerateAsync(new BatteryRunQuery { Status = BatteryRunStatusEnum.Failed }).ConfigureAwait(false);
                AssertEqual(2L, failed.TotalRecords);
                AssertContains("restarted", failed.Objects[0].FailureReason!);
            }));

            // ---- Real dispatch (AdmiralBatteryTaskDispatcher) ----

            cases.Add(CaseAsync("dispatcher_dispatches_and_waits", "Dispatch: one voyage under ModelEvaluation, persona set, strictly pinned to the captain, no landing; waits for the mission and grades its dock", TestTags.Positive, async () =>
            {
                using TempDir dir = new TempDir();
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("battery-dispatch", "file:///tmp/x.git") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Captain captain = await db.Captains.CreateAsync(new Captain("dispatch-captain") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                IAdmiralService admiral = RecordingBatteryAdmiral.Create(db);
                RecordingBatteryAdmiral recorder = (RecordingBatteryAdmiral)(object)admiral;
                AdmiralBatteryTaskDispatcher dispatcher = new AdmiralBatteryTaskDispatcher(admiral, db, Logging(), TimeSpan.FromMilliseconds(50));

                BatteryDispatchRequest request = new BatteryDispatchRequest(vessel, captain, "[syn/review #1] Review", "Review this.", "Judge", 30000);
                Task<BatteryDispatchResult> running = dispatcher.RunAsync(request);
                Mission mission = await WaitForMissionAsync(db, recorder).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.InProgress, mission.Status, "still running while the captain works");
                AssertFalse(running.IsCompleted, "the dispatcher waits for the mission");

                Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { CaptainId = captain.Id, WorktreePath = dir.Path }).ConfigureAwait(false);
                mission.Status = MissionStatusEnum.WorkProduced;
                mission.AgentOutput = "[ARMADA:VERDICT] PASS";
                mission.DockId = dock.Id;
                await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                BatteryDispatchResult result = await running.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                AssertNull(result.FailureReason, "no failure");
                AssertFalse(result.TimedOut, "finished in time");
                AssertEqual(mission.Id, result.Mission!.Id);
                AssertEqual("[ARMADA:VERDICT] PASS", result.Mission.AgentOutput);
                AssertNotNull(result.Workspace, "the mission's dock is the grading workspace");
                AssertEqual(dir.Path, result.Workspace!.Root);
                AssertEqual("the Admiral host", result.Workspace.HostLabel);

                object?[] args = recorder.Dispatches.Single();
                AssertEqual("Battery: [syn/review #1] Review", args[0]);
                AssertEqual(vessel.Id, args[2]);
                MissionDescription md = ((List<MissionDescription>)args[3]!).Single();
                AssertEqual("Judge", md.Persona, "the task persona replaces the stage persona");
                AssertEqual(captain.Id, md.RequestedCaptainId);
                AssertEqual("ModelEvaluation", args[4], "built-in pipeline");
                List<CaptainAssignmentOverride> overrides = System.Text.Json.JsonSerializer.Deserialize<List<CaptainAssignmentOverride>>((string)args[6]!)!;
                AssertEqual("*", overrides.Single().Persona);
                AssertEqual(captain.Id, overrides.Single().CaptainId);
                AssertTrue(overrides.Single().StrictCaptain, "strict: every stage waits for this captain");
                AssertNull(overrides.Single().PromptVariant, "no run variant: the captain's own prompt variant applies");
                AssertEqual((LandingModeEnum?)LandingModeEnum.None, (LandingModeEnum?)args[7], "no landing: the work stays in its dock");
                AssertEqual(11, args.Length, "dispatched through the overload that carries the evaluation flag");
                AssertEqual(true, args[10], "battery voyages are dispatched as evaluation work");
                AssertTrue(mission.IsEvaluation, "the battery mission is flagged as evaluation");
                AssertTrue((await db.Voyages.ReadAsync(result.VoyageId!).ConfigureAwait(false))!.IsEvaluation, "the battery voyage is flagged as evaluation");
                AssertEqual(0, recorder.Cancelled.Count, "nothing cancelled");
            }));

            cases.Add(CaseAsync("dispatcher_timeout_and_dispatch_error", "Dispatch: a mission that outlives the timeout is cancelled; a dispatch error is reported, not thrown", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("battery-timeout", "file:///tmp/x.git") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Captain captain = new Captain("timeout-captain") { TenantId = Constants.DefaultTenantId };
                IAdmiralService admiral = RecordingBatteryAdmiral.Create(db);
                RecordingBatteryAdmiral recorder = (RecordingBatteryAdmiral)(object)admiral;
                AdmiralBatteryTaskDispatcher dispatcher = new AdmiralBatteryTaskDispatcher(admiral, db, Logging(), TimeSpan.FromMilliseconds(50));

                BatteryDispatchResult timedOut = await dispatcher.RunAsync(new BatteryDispatchRequest(vessel, captain, "slow", "d", "Worker", 1000)).ConfigureAwait(false);
                AssertTrue(timedOut.TimedOut, "timed out");
                AssertEqual(1, recorder.Cancelled.Count, "the open mission is cancelled");
                AssertNull(timedOut.Workspace, "no dock");

                recorder.DispatchError = new InvalidOperationException("no captain available");
                BatteryDispatchResult failed = await dispatcher.RunAsync(new BatteryDispatchRequest(vessel, captain, "x", "d", "Worker", 1000)).ConfigureAwait(false);
                AssertNull(failed.Mission, "no mission");
                AssertEqual("Dispatch failed: no captain available", failed.FailureReason);

                dispatcher.OverrideSource = () => null;
                AssertTrue((await dispatcher.RunAsync(new BatteryDispatchRequest(vessel, captain, "x", "d", "Worker", 1000)).ConfigureAwait(false)).FailureReason != null, "a null override keeps real dispatch");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Services: fixture battery packs, grading and runner",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static BatteryPack CSharpPack()
        {
            BatteryPack? pack = new BatteryPackCatalog(() => null).Find("csharp");
            AssertNotNull(pack, "the C# pack is found from the test host");
            return pack!;
        }

        private static BatteryTaskDefinition CSharpTask(string id)
        {
            return CSharpPack().Manifest!.Tasks.Single(t => t.Id == id);
        }

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static IBatteryWorkspace LocalWorkspace(string path)
        {
            LoggingModule logging = Logging();
            return new DockBatteryWorkspace(new DockHostResolver(new GitService(logging), logging, null).Local, path);
        }

        private static BatteryRunService NewService(DatabaseDriver db, TempDir dir, CannedBatteryTaskDispatcher dispatcher)
        {
            LoggingModule logging = Logging();
            EvaluationSettings settings = new EvaluationSettings();
            return new BatteryRunService(db, logging, new BatteryPackCatalog(() => dir.Path), new BatteryFixtureService(db, logging, Path.Combine(dir.Path, "data")), dispatcher, () => settings);
        }

        private static async Task<Pipeline> SeedPipelineAsync(DatabaseDriver db)
        {
            Pipeline pipeline = new Pipeline(BatteryFixtureService.PipelineName) { IsBuiltIn = true, TenantId = Constants.DefaultTenantId };
            return await db.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
        }

        private static async Task<Mission> WaitForMissionAsync(DatabaseDriver db, RecordingBatteryAdmiral recorder)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (recorder.Dispatches.Count > 0)
                {
                    List<Mission> missions = await db.Missions.EnumerateAsync().ConfigureAwait(false);
                    if (missions.Count > 0) return missions[0];
                }

                await Task.Delay(20).ConfigureAwait(false);
            }

            throw new TimeoutException("no mission was dispatched");
        }

        private static Captain TestCaptain()
        {
            return new Captain("battery-captain") { Id = "cpt_battery", TenantId = Constants.DefaultTenantId, Runtime = AgentRuntimeEnum.ApiEndpoint, Model = "qwen3:32b" };
        }

        private static string Describe(BatteryRun run)
        {
            return String.Join("; ", run.Results.Select(r => r.TaskId + "#" + r.Attempt + "=" + r.Outcome + " " + r.Reason));
        }

        private static Func<BatteryDispatchRequest, string, string?> ScriptedCaptain(bool good, bool pushStray = false)
        {
            return (request, worktree) =>
            {
                // Every task starts from the reset fixture: nothing a previous task pushed is visible.
                AssertFalse(File.Exists(Path.Combine(worktree, "stray.txt")), "fixture reset before the task");
                if (request.Title.Contains("/edit ", StringComparison.Ordinal))
                {
                    byte[] body = Encoding.UTF8.GetBytes("hello world\r\nline two\r\n");
                    File.WriteAllBytes(Path.Combine(worktree, "hello.txt"), good ? _Bom.Concat(body).ToArray() : body);
                    if (pushStray)
                    {
                        File.WriteAllText(Path.Combine(worktree, "stray.txt"), "pushed by a task");
                        CannedBatteryTaskDispatcher.Git(worktree, "add", "--all");
                        CannedBatteryTaskDispatcher.Git(worktree, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "stray");
                        CannedBatteryTaskDispatcher.Git(worktree, "push", "--quiet", "origin", "HEAD:" + request.Vessel.DefaultBranch);
                        File.Delete(Path.Combine(worktree, "stray.txt"));
                    }

                    return "Changed hello.txt.\n[ARMADA:RESULT] COMPLETE";
                }

                if (request.Title.Contains("/check ", StringComparison.Ordinal)) return "Nothing to change.\n[ARMADA:RESULT] COMPLETE";
                if (!good) return null;
                if (request.Persona == "Judge") return "## Affected Case\nScenario: the loop is off-by-one and skips the first item.\n\n[ARMADA:VERDICT] FAIL";
                return "```armada-plan\n{\"missions\":[{\"title\":\"A\",\"description\":\"do a\"}]}\n```";
            };
        }

        private static void WriteSyntheticPack(TempDir dir, string id, bool missingTool = false)
        {
            string toolchain = "[ { \"name\": \"git\", \"executable\": \"git\", \"arguments\": [ \"--version\" ] }"
                + (missingTool ? ", { \"name\": \"nosuchtool\", \"executable\": \"armada-no-such-tool-7f3c\", \"arguments\": [ \"--version\" ], \"hint\": \"install nosuchtool\" }" : String.Empty)
                + " ]";
            dir.Write(id + "/battery-pack.json", @"{
              ""schemaVersion"": 1, ""id"": """ + id + @""", ""name"": ""Synthetic"", ""version"": 1, ""domain"": ""test"",
              ""fixture"": { ""path"": ""fixture"" },
              ""toolchain"": " + toolchain + @",
              ""tasks"": [
                { ""id"": ""edit"", ""title"": ""Edit hello"", ""persona"": ""Worker"", ""prompt"": ""Change hello to hello world, keep the BOM and CRLF."",
                  ""grader"": { ""type"": ""byteComparison"", ""files"": [ { ""path"": ""hello.txt"", ""expected"": ""expected/hello.txt"" } ] } },
                { ""id"": ""check"", ""title"": ""Check"", ""persona"": ""Worker"", ""prompt"": ""Do nothing."", ""setup"": ""setup"",
                  ""grader"": { ""type"": ""command"", ""commands"": [ { ""executable"": ""git"", ""arguments"": [ ""ls-files"", ""--error-unmatch"", ""setup-marker.txt"" ] } ] } },
                { ""id"": ""review"", ""title"": ""Review"", ""persona"": ""Judge"", ""toolchain"": [], ""prompt"": ""Review it."", ""attachments"": [ ""change.diff"" ],
                  ""grader"": { ""type"": ""seededSet"", ""expectedVerdicts"": [ ""FAIL"" ], ""blockingSections"": [ ""Affected Case"" ],
                    ""seeded"": [ { ""id"": ""obo"", ""keywords"": [ ""off-by-one"" ] } ] } },
                { ""id"": ""plan"", ""title"": ""Plan"", ""persona"": ""Architect"", ""toolchain"": [], ""prompt"": ""Plan it."",
                  ""grader"": { ""type"": ""rubric"", ""schema"": ""armada-plan"", ""rubric"": ""rubric.md"" } }
              ]
            }");
            dir.Write(id + "/fixture/.gitattributes", "* -text\n");
            dir.WriteBytes(id + "/fixture/hello.txt", _Bom.Concat(Encoding.UTF8.GetBytes("hello\r\nline two\r\n")).ToArray());
            dir.WriteBytes(id + "/expected/hello.txt", _Bom.Concat(Encoding.UTF8.GetBytes("hello world\r\nline two\r\n")).ToArray());
            dir.Write(id + "/setup/setup-marker.txt", "added by the task setup\n");
            dir.Write(id + "/change.diff", "+        for (int i = 1; i < items.Count; i++)\n");
            dir.Write(id + "/rubric.md", "Grade the plan.\n");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "armada-battery-" + Guid.NewGuid().ToString("N"));

            public TempDir()
            {
                Directory.CreateDirectory(Path);
            }

            public string Write(string relative, string content)
            {
                return WriteBytes(relative, Encoding.UTF8.GetBytes(content));
            }

            public string WriteBytes(string relative, byte[] content)
            {
                string full = System.IO.Path.Combine(Path, relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, content);
                return full;
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path)) Directory.Delete(Path, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        #endregion
    }
}
