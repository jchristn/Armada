namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The built-in fleet action catalog and the engine features it relies on: seeding and upgrading unedited
    /// built-ins, applicability checks, report-only missions, follow-up runs, package inputs, resolved ecosystem
    /// commands, and parser-less scan summaries.
    /// </summary>
    public sealed class FleetActionBuiltInSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.FleetActionBuiltIns";
        private const int RunWaitMs = 60000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("catalog_definitions_valid", "Every built-in has a unique key, valid templates and inputs, a known parser, and a scan source that exists", TestTags.Positive, () =>
            {
                foreach (bool windows in new[] { false, true })
                {
                    List<FleetAction> actions = FleetActionSeedService.BuildDefaults(windows);
                    AssertEqual(25, actions.Count, "built-in count");
                    AssertEqual(actions.Count, actions.Select(a => a.BuiltInKey).Distinct(StringComparer.Ordinal).Count(), "unique keys");
                    HashSet<string> keys = new HashSet<string>(actions.Select(a => a.BuiltInKey!), StringComparer.Ordinal);
                    foreach (FleetAction action in actions)
                    {
                        string label = action.BuiltInKey + (windows ? " (windows)" : "");
                        AssertTrue(action.IsBuiltIn, label + " is built-in");
                        FleetActionInputValidator.ValidateDefinitions(action.Inputs);
                        List<string> inputNames = action.Inputs.Select(i => i.Name).ToList();
                        FleetActionTemplateRenderer.Validate(action.CommandText, inputNames);
                        FleetActionTemplateRenderer.Validate(action.CommandWindows, inputNames);
                        FleetActionTemplateRenderer.Validate(action.PromptTemplate, inputNames);
                        FleetActionTemplateRenderer.Validate(action.ApplicabilityCommand, inputNames);
                        FleetActionTemplateRenderer.Validate(action.ApplicabilityCommandWindows, inputNames);
                        if (action.OutputParser != null) AssertTrue(Armada.Core.Services.FleetActionOutputParsers.FleetActionParserRegistry.IsKnown(action.OutputParser), label + " parser");
                        if (action.ScanSourceActionKey != null) AssertTrue(keys.Contains(action.ScanSourceActionKey), label + " scan source exists");
                        if (action.Kind == FleetActionKindEnum.Command)
                        {
                            AssertFalse(String.IsNullOrWhiteSpace(action.CommandText), label + " command");
                            AssertEqual(MissionModeEnum.Implementation, action.MissionMode, label + " command mode");
                        }
                        else
                        {
                            AssertFalse(String.IsNullOrWhiteSpace(action.PromptTemplate), label + " prompt");
                        }

                        foreach (string? text in new[] { action.Name, action.Description, action.CommandText, action.CommandWindows, action.PromptTemplate })
                            AssertFalse((text ?? "").Contains('\u2014'), label + " has no em-dash");
                    }

                    AssertEqual(MissionModeEnum.Audit, actions.Single(a => a.BuiltInKey == FleetActionSeedService.SecurityReviewKey).MissionMode, "security review is report-only");
                    AssertEqual(FleetActionSeedService.RunTestsKey, actions.Single(a => a.BuiltInKey == FleetActionSeedService.FixFailingTestsKey).ScanSourceActionKey, "fix tests pairs with run tests");
                    AssertFalse(String.IsNullOrWhiteSpace(actions.Single(a => a.BuiltInKey == FleetActionSeedService.UpdatePackageKey).ApplicabilityCommand), "update package checks applicability");
                }

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("seed_adds_new_and_upgrades_unedited", "Seeding an existing tenant adds the new built-ins, upgrades unedited ones, and leaves edited and deleted ones alone", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                bool windows = OperatingSystem.IsWindows();
                List<FleetAction> current = FleetActionSeedService.BuildDefaults(windows);
                List<FleetAction> previous = FleetActionBuiltInCatalog.PreviousVersions(windows);

                // A tenant seeded by v1.2: the two unchanged built-ins plus the three earlier definitions.
                foreach (string key in new[] { FleetActionSeedService.FastForwardKey, FleetActionSeedService.PruneMergedBranchesKey })
                    await CreateRowAsync(h, current.Single(a => a.BuiltInKey == key)).ConfigureAwait(false);
                FleetAction build = await CreateRowAsync(h, previous.Single(a => a.BuiltInKey == FleetActionSeedService.BuildKey)).ConfigureAwait(false);
                FleetAction updateDeps = await CreateRowAsync(h, previous.Single(a => a.BuiltInKey == FleetActionSeedService.UpdateDependenciesKey)).ConfigureAwait(false);
                FleetAction addTests = await CreateRowAsync(h, previous.Single(a => a.BuiltInKey == FleetActionSeedService.AddTestProjectKey)).ConfigureAwait(false);

                // The tenant edited one and deleted another.
                await h.Service.UpdateActionAsync(h.Admin, updateDeps.Id, new FleetActionUpsertRequest { Description = "our own wording" }).ConfigureAwait(false);
                await h.Service.DeleteActionAsync(h.Admin, addTests.Id).ConfigureAwait(false);

                int created = await new FleetActionSeedService(h.Db.Driver, h.Logging).SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertEqual(current.Count - 5, created, "only the new built-ins are created");

                FleetAction upgradedBuild = (await h.Db.Driver.FleetActions.ReadAsync(Constants.DefaultTenantId, build.Id).ConfigureAwait(false))!;
                AssertTrue(FleetActionSeedService.MatchesDefinition(upgradedBuild, current.Single(a => a.BuiltInKey == FleetActionSeedService.BuildKey)), "unedited Build takes the current definition");
                AssertEqual(build.Id, upgradedBuild.Id, "the upgrade keeps the row");

                FleetAction editedDeps = (await h.Db.Driver.FleetActions.ReadAsync(Constants.DefaultTenantId, updateDeps.Id).ConfigureAwait(false))!;
                AssertEqual("our own wording", editedDeps.Description, "an edited built-in keeps its edits");
                AssertNull(editedDeps.ScanSourceActionKey, "an edited built-in is not upgraded");

                FleetAction deleted = (await h.Db.Driver.FleetActions.ReadAsync(Constants.DefaultTenantId, addTests.Id).ConfigureAwait(false))!;
                AssertFalse(deleted.Active, "a deleted built-in stays deleted");
                AssertTrue(FleetActionSeedService.MatchesDefinition(deleted, previous.Single(a => a.BuiltInKey == FleetActionSeedService.AddTestProjectKey)), "a deleted built-in is not upgraded");

                AssertEqual(0, await new FleetActionSeedService(h.Db.Driver, h.Logging).SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false), "seeding again creates nothing");
            }));

            cases.Add(CaseAsync("applicability_skips_mission_targets", "Update a specific dependency dispatches only to vessels whose manifests mention the package", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                await new FleetActionSeedService(h.Db.Driver, h.Logging).SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                Vessel uses = await h.CreateRepoVesselAsync("uses").ConfigureAwait(false);
                Vessel other = await h.CreateRepoVesselAsync("other").ConfigureAwait(false);
                await TrackFileAsync(uses.WorkingDirectory!, "package.json", "{ \"dependencies\": { \"left-pad\": \"1.0.0\" } }").ConfigureAwait(false);
                await TrackFileAsync(other.WorkingDirectory!, "package.json", "{ \"dependencies\": { \"right-pad\": \"1.0.0\" } }").ConfigureAwait(false);

                FleetAction update = (await h.Db.Driver.FleetActions.ReadByBuiltInKeyAsync(Constants.DefaultTenantId, FleetActionSeedService.UpdatePackageKey).ConfigureAwait(false))!;
                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, update.Id, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { uses.Id, other.Id },
                    InputValues = new Dictionary<string, string> { ["package"] = "left-pad", ["version"] = "1.3.0" }
                }).ConfigureAwait(false);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);

                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                FleetActionRunTarget skipped = targets.Single(t => t.VesselId == other.Id);
                AssertEqual(FleetActionTargetStatusEnum.Skipped, skipped.Status, "vessel without the package: " + skipped.ErrorText);
                AssertEqual(FleetActionReasonCodes.NotApplicable, skipped.SkipReason);
                AssertEqual(FleetActionTargetStatusEnum.Running, targets.Single(t => t.VesselId == uses.Id).Status, "vessel with the package is dispatched");
                AssertTrue(h.Dispatcher.PromptsByVessel.ContainsKey(uses.Id), "one dispatch");
                AssertFalse(h.Dispatcher.PromptsByVessel.ContainsKey(other.Id), "no captain for the other vessel");
                AssertContains("left-pad", h.Dispatcher.PromptsByVessel[uses.Id]);
                AssertContains("1.3.0", h.Dispatcher.PromptsByVessel[uses.Id]);
            }));

            cases.Add(CaseAsync("applicability_skips_command_targets", "A Command action's applicability check skips vessels where it exits non-zero", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel yes = await h.CreateRepoVesselAsync("yes").ConfigureAwait(false);
                Vessel no = await h.CreateRepoVesselAsync("no").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(yes.WorkingDirectory!, "marker.txt"), "x").ConfigureAwait(false);

                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { yes.Id, no.Id },
                    Definition = new FleetActionUpsertRequest { Name = "check", Kind = FleetActionKindEnum.Command, CommandText = "echo ran", ApplicabilityCommand = "test -f marker.txt", RequiresCleanWorkingTree = false }
                }).ConfigureAwait(false);
                await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);

                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                AssertEqual(FleetActionTargetStatusEnum.Succeeded, targets.Single(t => t.VesselId == yes.Id).Status);
                FleetActionRunTarget skipped = targets.Single(t => t.VesselId == no.Id);
                AssertEqual(FleetActionTargetStatusEnum.Skipped, skipped.Status);
                AssertEqual(FleetActionReasonCodes.NotApplicable, skipped.SkipReason);
            }));

            cases.Add(CaseAsync("applicability_requires_tenant_admin", "A non-admin cannot create a Mission action with an applicability command", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => h.Service.CreateActionAsync(h.Regular, new FleetActionUpsertRequest
                {
                    Name = "m",
                    Kind = FleetActionKindEnum.Mission,
                    PromptTemplate = "do it",
                    ApplicabilityCommand = "true"
                })).ConfigureAwait(false);
                FleetAction plain = await h.Service.CreateActionAsync(h.Regular, new FleetActionUpsertRequest { Name = "m2", Kind = FleetActionKindEnum.Mission, PromptTemplate = "do it" }).ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => h.Service.UpdateActionAsync(h.Regular, plain.Id, new FleetActionUpsertRequest { ApplicabilityCommand = "true" })).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("mission_mode_validation", "MissionMode is rejected on Command actions and round-trips on Mission actions", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => h.Service.CreateActionAsync(h.Admin, new FleetActionUpsertRequest
                {
                    Name = "c",
                    Kind = FleetActionKindEnum.Command,
                    CommandText = "echo",
                    MissionMode = MissionModeEnum.Audit
                })).ConfigureAwait(false);
                FleetAction report = await h.Service.CreateActionAsync(h.Admin, new FleetActionUpsertRequest { Name = "r", Kind = FleetActionKindEnum.Mission, PromptTemplate = "review", MissionMode = MissionModeEnum.Research }).ConfigureAwait(false);
                AssertEqual(MissionModeEnum.Research, (await h.Service.ReadActionAsync(h.Admin, report.Id).ConfigureAwait(false)).MissionMode);
                FleetAction auto = await h.Service.CreateActionAsync(h.Admin, new FleetActionUpsertRequest { Name = "a", Kind = FleetActionKindEnum.Command, CommandText = "{{vessel.auditCommand}}", OutputParser = "auto" }).ConfigureAwait(false);
                AssertEqual("auto", auto.OutputParser, "the auto parser is accepted");
            }));

            cases.Add(CaseAsync("report_only_dispatch_and_report", "A report-only Mission dispatches read-only with landing None and records the captain's report on the target", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateVesselAsync("report", null).ConfigureAwait(false);
                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { vessel.Id },
                    Definition = new FleetActionUpsertRequest { Name = "review", Kind = FleetActionKindEnum.Mission, PromptTemplate = "Review {{vessel.name}}", MissionMode = MissionModeEnum.Audit }
                }).ConfigureAwait(false);
                AssertEqual(MissionModeEnum.Audit, run.MissionMode, "run snapshot");
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);

                FleetActionMissionDispatch dispatch = h.Dispatcher.DispatchesByVessel[vessel.Id];
                AssertEqual(MissionModeEnum.Audit, dispatch.MissionMode, "dispatched read-only");
                AssertEqual(LandingModeEnum.None, dispatch.LandingMode, "nothing lands");

                string voyageId = h.Dispatcher.RunningVoyages().Single();
                h.Dispatcher.ReportsByVoyage[voyageId] = "# Findings\n1. Something to fix.";
                h.Dispatcher.SetOutcome(voyageId, FleetActionVoyageOutcomeEnum.Succeeded);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);

                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Succeeded, target.Status);
                AssertEqual("# Findings\n1. Something to fix.", target.OutputText, "the report is the target output");
            }));

            cases.Add(CaseAsync("implementation_dispatch_keeps_vessel_landing", "An implementation Mission keeps the vessel's landing mode and records the final message", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateVesselAsync("impl", null).ConfigureAwait(false);
                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { vessel.Id },
                    Definition = new FleetActionUpsertRequest { Name = "fix", Kind = FleetActionKindEnum.Mission, PromptTemplate = "Fix {{vessel.name}}" }
                }).ConfigureAwait(false);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);

                FleetActionMissionDispatch dispatch = h.Dispatcher.DispatchesByVessel[vessel.Id];
                AssertEqual(MissionModeEnum.Implementation, dispatch.MissionMode);
                AssertNull(dispatch.LandingMode, "the vessel's own landing mode applies");
                string voyageId = h.Dispatcher.RunningVoyages().Single();
                h.Dispatcher.ReportsByVoyage[voyageId] = "Fixed it.";
                h.Dispatcher.SetOutcome(voyageId, FleetActionVoyageOutcomeEnum.Failed);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Failed, target.Status);
                AssertEqual("Fixed it.", target.OutputText, "a failed voyage still records the captain's last message");
            }));

            cases.Add(CaseAsync("follow_up_targets_selected_statuses", "A follow-up run targets the earlier run's Findings and Failed vessels, or the statuses asked for", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel pass = await h.CreateRepoVesselAsync("pass").ConfigureAwait(false);
                Vessel fail = await h.CreateRepoVesselAsync("fail").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(pass.WorkingDirectory!, "ok.txt"), "x").ConfigureAwait(false);
                FleetActionRun scan = await h.StartCommandAsync("test -f ok.txt", new List<string> { pass.Id, fail.Id }, requiresClean: false).ConfigureAwait(false);
                await h.Runner.WaitForRunAsync(scan.Id, RunWaitMs).ConfigureAwait(false);

                FleetActionUpsertRequest fix = new FleetActionUpsertRequest { Name = "fix", Kind = FleetActionKindEnum.Mission, PromptTemplate = "Fix {{vessel.name}}" };
                FleetActionRun followUp = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest { FromRunId = scan.Id, Definition = fix }).ConfigureAwait(false);
                List<FleetActionRunTarget> targets = await h.TargetsAsync(followUp.Id).ConfigureAwait(false);
                AssertEqual(1, targets.Count, "only the failed vessel");
                AssertEqual(fail.Id, targets[0].VesselId);

                FleetActionRun succeeded = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    FromRunId = scan.Id,
                    FromTargetStatuses = new List<FleetActionTargetStatusEnum> { FleetActionTargetStatusEnum.Succeeded },
                    Definition = fix
                }).ConfigureAwait(false);
                AssertEqual(pass.Id, (await h.TargetsAsync(succeeded.Id).ConfigureAwait(false)).Single().VesselId, "explicit statuses");

                await AssertThrowsAsync<ArgumentException>(() => h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest { FromRunId = scan.Id, VesselIds = new List<string> { pass.Id }, Definition = fix })).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    FromRunId = scan.Id,
                    FromTargetStatuses = new List<FleetActionTargetStatusEnum> { FleetActionTargetStatusEnum.TimedOut },
                    Definition = fix
                })).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { pass.Id },
                    FromTargetStatuses = new List<FleetActionTargetStatusEnum> { FleetActionTargetStatusEnum.Failed },
                    Definition = fix
                })).ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest { FromRunId = "far_missing", Definition = fix })).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("parserless_output_becomes_scan_summary", "A Command without a parser stores its exit code and output tail as the scan summary a fix mission cites", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateRepoVesselAsync("tests").ConfigureAwait(false);
                FleetAction scan = await h.Service.CreateActionAsync(h.Admin, new FleetActionUpsertRequest { Name = "tests", Kind = FleetActionKindEnum.Command, CommandText = "echo 'FAILED ParserTests.Empty'; exit 3", RequiresCleanWorkingTree = false }).ConfigureAwait(false);
                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, scan.Id, new FleetActionRunRequest { VesselIds = new List<string> { vessel.Id } }).ConfigureAwait(false);
                await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Failed, target.Status);
                AssertContains("Exit code 3.", target.ScanSummary ?? "");
                AssertContains("FAILED ParserTests.Empty", target.ScanSummary ?? "");

                FleetActionRun fix = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    FromRunId = run.Id,
                    Definition = new FleetActionUpsertRequest { Name = "fix", Kind = FleetActionKindEnum.Mission, PromptTemplate = "Result:{{scan.summary}}", ScanSourceActionKey = scan.Id }
                }).ConfigureAwait(false);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                AssertContains("FAILED ParserTests.Empty", (await h.TargetsAsync(fix.Id).ConfigureAwait(false)).Single().RenderedText ?? "");
            }));

            cases.Add(CaseAsync("unresolved_ecosystem_command_skipped", "A Command naming an ecosystem command that resolves to nothing is skipped with NoMatchingEcosystem", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateRepoVesselAsync("plain").ConfigureAwait(false);
                FleetActionRun run = await h.StartCommandAsync("{{vessel.lintCommand}}", new List<string> { vessel.Id }, requiresClean: false).ConfigureAwait(false);
                await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Skipped, target.Status, "target: " + target.ErrorText);
                AssertEqual(FleetActionReasonCodes.NoMatchingEcosystem, target.SkipReason);
            }));

            cases.Add(CaseAsync("build_command_falls_back_to_ecosystem", "{{vessel.buildCommand}} resolves from the detected ecosystem when the vessel has no build command", TestTags.Positive, () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "armada-fa-eco-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                try
                {
                    File.WriteAllText(Path.Combine(dir, "go.mod"), "module example.com/demo\n\ngo 1.22\n");
                    Vessel vessel = new Vessel("eco", "https://example.com/eco.git") { WorkingDirectory = dir };
                    FleetActionTemplateContext context = FleetActionTemplateContext.FromVessel(vessel);
                    AssertContains("go build", context.BuildCommand);
                    AssertContains("go list -m -u all", context.OutdatedCommand);
                    vessel.DefinitionOfDoneBuildCommand = "make all";
                    AssertEqual("make all", FleetActionTemplateContext.FromVessel(vessel).BuildCommand, "the vessel's own build command wins");
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (IOException) { }
                }

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("package_input_validation", "Package inputs accept identifiers across ecosystems and reject shell metacharacters", TestTags.Negative, () =>
            {
                List<FleetActionInput> inputs = new List<FleetActionInput> { new FleetActionInput { Name = "package", Type = "package", Required = true } };
                foreach (string ok in new[] { "Newtonsoft.Json", "@scope/name", "com.example:artifact", "github.com/org/repo", "left-pad", "c++utils" })
                    AssertEqual(ok, FleetActionInputValidator.Resolve(inputs, new Dictionary<string, string> { ["package"] = ok })["package"], ok);
                AssertEqual(200, FleetActionInputValidator.Resolve(inputs, new Dictionary<string, string> { ["package"] = new string('a', 200) })["package"].Length, "200 characters is the limit");
                foreach (string bad in new[] { "a;rm -rf /", "$(whoami)", "pkg name", "'quoted'", "a|b", "-flag", "a`b", new string('a', 201) })
                    AssertThrows<ArgumentException>(() => FleetActionInputValidator.Resolve(inputs, new Dictionary<string, string> { ["package"] = bad }));
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Fleet action built-ins",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<FleetAction> CreateRowAsync(FleetActionTestHarness h, FleetAction definition)
        {
            definition.TenantId = Constants.DefaultTenantId;
            return await h.Db.Driver.FleetActions.CreateAsync(definition).ConfigureAwait(false);
        }

        private static async Task TrackFileAsync(string workingDirectory, string name, string content)
        {
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, name), content).ConfigureAwait(false);
            HostCommandResult result = await new LocalHostCommandExecutor().RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string> { "add", name }
            }).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new InvalidOperationException("git add failed: " + result.StandardError);
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
    }
}
