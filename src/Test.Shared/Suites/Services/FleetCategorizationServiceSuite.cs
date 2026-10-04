namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for background discovery and captain-driven fleet categorization with a stubbed captain runner:
    /// background discovery (202-style response, Discovered on completion, synchronous validation), categorization
    /// success (manifest contents, appended output contract, captain reserved then released, structured
    /// recommendations), missing and invalid JSON, unknown and omitted vessel identifiers (Uncategorized bucket),
    /// cancellation through the job, the time limit, a busy captain, auto-apply, request validation, apply (fleet name
    /// reuse, Uncategorized skipped, foreign and duplicate vessels rejected, tenant isolation), retry, reply fallback,
    /// and restart recovery.
    /// </summary>
    public sealed class FleetCategorizationServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.FleetCategorization";
        private static readonly Regex _VesselIdLine = new Regex(@"Vessel ID: (vsl_\S+)", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("background_discovery_completes", "Background discovery returns immediately with a Discovering batch and job, then reaches Discovered", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "one"), "One", "A web API.");
                MakeRepo(Path.Combine(h.Root, "two"), "Two", "A CLI.");

                VesselDiscoveryRequest request = new VesselDiscoveryRequest();
                request.Roots = new List<string> { h.Root };
                request.RunInBackground = true;
                VesselImportDiscoverResponse response = await h.Import.DiscoverAsync(Constants.DefaultTenantId, Constants.DefaultUserId, request).ConfigureAwait(false);
                AssertTrue(response.RunsInBackground, "runs in background");
                AssertStartsWith("job_", response.JobId ?? "", "job id");
                AssertEqual(0, response.Candidates.Count, "no candidates yet");
                AssertEqual(VesselImportBatchStatusEnum.Discovering, response.Batch!.Status);
                AssertEqual(response.JobId, response.Batch.DiscoveryJobId);

                VesselImportBatch batch = await WaitForBatchAsync(testDb.Driver, response.BatchId, b => b.Status != VesselImportBatchStatusEnum.Discovering).ConfigureAwait(false);
                AssertEqual(VesselImportBatchStatusEnum.Discovered, batch.Status);
                AssertEqual(2, batch.CandidateCount);
                VesselImportBatchDetail? detail = await h.Import.ReadBatchAsync(Constants.DefaultTenantId, response.BatchId).ConfigureAwait(false);
                AssertEqual(2, detail!.Items.Count);

                Job? job = await JobWait.ForTerminalAsync(testDb.Driver, response.JobId!).ConfigureAwait(false);
                AssertEqual(JobKindEnum.VesselDiscovery, job!.Kind);
                AssertEqual(JobStatusEnum.Succeeded, job.Status);
                AssertContains("\"candidateCount\":2", job.ResultJson ?? "");
            }));

            cases.Add(CaseAsync("background_discovery_validates_synchronously", "Background discovery rejects bad input before creating a batch", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                VesselDiscoveryRequest outside = new VesselDiscoveryRequest();
                outside.Roots = new List<string> { Path.GetPathRoot(Path.GetTempPath()) ?? "/" };
                outside.RunInBackground = true;
                await AssertThrowsAsync<VesselImportPathNotAllowedException>(() => h.Import.DiscoverAsync(Constants.DefaultTenantId, null, outside), "outside allowed roots").ConfigureAwait(false);

                VesselDiscoveryRequest empty = new VesselDiscoveryRequest();
                empty.RunInBackground = true;
                await AssertThrowsAsync<ArgumentException>(() => h.Import.DiscoverAsync(Constants.DefaultTenantId, null, empty), "no paths").ConfigureAwait(false);
                AssertEqual(0L, (await h.Import.EnumerateBatchesAsync(Constants.DefaultTenantId, null).ConfigureAwait(false)).TotalRecords, "no batch created");
            }));

            cases.Add(CaseAsync("import_rejected_while_discovering", "Import of a batch that is still Discovering is rejected", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                VesselImportBatch batch = new VesselImportBatch { TenantId = Constants.DefaultTenantId, Status = VesselImportBatchStatusEnum.Discovering };
                batch = await testDb.Driver.VesselImportBatches.CreateAsync(batch).ConfigureAwait(false);
                VesselImportRequest request = new VesselImportRequest { BatchId = batch.Id, Paths = new List<string> { "/x" } };
                await AssertThrowsAsync<InvalidOperationException>(() => h.Import.ImportAsync(Constants.DefaultTenantId, null, request), "discovering").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("categorization_success", "Import with categorization runs the captain on a manifest and stores structured recommendations", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "pay-api"), "Payments API", "Handles card payments.", "package.json");
                MakeRepo(Path.Combine(h.Root, "pay-ledger"), "Ledger", "Double-entry ledger service.", "go.mod");
                MakeRepo(Path.Combine(h.Root, "dev-cli"), "Dev CLI", "Internal developer tooling.", "Cargo.toml");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);

                CaptainStateEnum? stateDuringRun = null;
                h.Runner.OnRunStarted = async (Captain c) => stateDuringRun = (await testDb.Driver.Captains.ReadAsync(c.Id).ConfigureAwait(false))!.State;
                h.Runner.Behavior = async (string dir, string prompt, TimeSpan timeout, CancellationToken token) =>
                {
                    Dictionary<string, string> ids = await ReadVesselIdsByNameAsync(dir).ConfigureAwait(false);
                    string json = JsonSerializer.Serialize(new
                    {
                        fleets = new object[]
                        {
                            new { name = "Payments", description = "Money movement", rationale = "Both handle payments", vesselIds = new[] { ids["Payments API"], ids["Ledger"] } },
                            new { name = "Developer Tooling", description = "Tools", rationale = "Internal tools", vesselIds = new[] { ids["Dev CLI"] } }
                        }
                    });
                    await File.WriteAllTextAsync(Path.Combine(dir, FleetCategorizationService.OutputFileName), json).ConfigureAwait(false);
                    return new CaptainPromptResult { ExitCode = 0 };
                };

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, "Group by business domain please.", false).ConfigureAwait(false);
                AssertEqual(3, response.Batch!.CreatedCount);

                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Completed, batch.CategorizationStatus, "status (error: " + batch.CategorizationError + ")");
                AssertEqual(captain.Id, batch.CategorizationCaptainId);
                AssertEqual("Group by business domain please.", batch.CategorizationPrompt);
                AssertNotNull(batch.CategorizationStartedUtc, "started");
                AssertNotNull(batch.CategorizationCompletedUtc, "completed");

                AssertEqual(1, h.Runner.Calls, "runner calls");
                AssertEqual(CaptainStateEnum.Analyzing, stateDuringRun, "captain reserved while running");
                AssertEqual(CaptainStateEnum.Idle, (await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.State, "captain released");
                AssertNull((await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.ProcessId, "process cleared");

                AssertContains("Group by business domain please.", h.Runner.LastPrompt!, "user instructions");
                AssertContains("Output contract", h.Runner.LastPrompt!, "contract appended");
                AssertContains(FleetCategorizationService.OutputFileName, h.Runner.LastPrompt!);
                AssertContains("Handles card payments.", h.Runner.LastManifest!, "README excerpt in manifest");
                AssertContains("package.json", h.Runner.LastManifest!, "manifests listed");
                AssertContains("Go", h.Runner.LastManifest!, "language detected");
                AssertTrue(h.Runner.LastWorkingDirectory!.StartsWith(h.Settings.DataDirectory, StringComparison.Ordinal), "scratch dir under the data directory");
                AssertFalse(Directory.Exists(h.Runner.LastWorkingDirectory), "scratch dir removed after success");

                VesselImportBatchDetail? detail = await h.Import.ReadBatchAsync(Constants.DefaultTenantId, response.BatchId).ConfigureAwait(false);
                AssertEqual(2, detail!.FleetRecommendations.Count);
                AssertEqual("Payments", detail.FleetRecommendations[0].Name);
                AssertEqual("Both handle payments", detail.FleetRecommendations[0].Rationale);
                AssertEqual(2, detail.FleetRecommendations[0].VesselIds.Count);
                AssertEqual("Developer Tooling", detail.FleetRecommendations[1].Name);

                Job? job = await JobWait.ForTerminalAsync(testDb.Driver, batch.CategorizationJobId!).ConfigureAwait(false);
                AssertEqual(JobKindEnum.FleetCategorization, job!.Kind);
                AssertEqual(JobStatusEnum.Succeeded, job.Status);
                AssertContains("\"fleetCount\":2", job.ResultJson ?? "");

                List<Fleet> fleets = await testDb.Driver.Fleets.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertFalse(fleets.Any(f => f.Name == "Payments"), "nothing applied without auto-apply");
            }));

            cases.Add(CaseAsync("default_prompt_from_template", "An empty prompt uses the import.fleet_categorization template and the contract is still appended", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "solo"), "Solo", "A thing.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                h.Runner.Behavior = WriteSingleFleet("Everything");

                string template = await h.Categorization.GetDefaultPromptAsync().ConfigureAwait(false);
                AssertContains("REPOSITORIES.md", template, "template text");

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, false).ConfigureAwait(false);
                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Completed, batch.CategorizationStatus);
                AssertEqual(template, batch.CategorizationPrompt, "template stored as the prompt used");
                AssertContains("Output contract", h.Runner.LastPrompt!);
            }));

            cases.Add(CaseAsync("missing_output_fails", "A captain that writes no file fails the categorization and the job with a clear error", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                h.Runner.Behavior = (string dir, string prompt, TimeSpan timeout, CancellationToken token) => Task.FromResult(new CaptainPromptResult { ExitCode = 1, Output = "I could not decide." });

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, false).ConfigureAwait(false);
                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Failed, batch.CategorizationStatus);
                AssertContains("without writing " + FleetCategorizationService.OutputFileName, batch.CategorizationError ?? "");
                AssertContains("I could not decide.", batch.CategorizationError ?? "", "output tail included");
                Job? job = await JobWait.ForTerminalAsync(testDb.Driver, batch.CategorizationJobId!).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Failed, job!.Status);
                AssertEqual(CaptainStateEnum.Idle, (await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.State, "captain released");
                AssertEqual(0, (await testDb.Driver.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, response.BatchId).ConfigureAwait(false)).Count);
            }));

            cases.Add(CaseAsync("invalid_json_fails", "Malformed or empty JSON fails the categorization", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                h.Runner.Behavior = async (string dir, string prompt, TimeSpan timeout, CancellationToken token) =>
                {
                    await File.WriteAllTextAsync(Path.Combine(dir, FleetCategorizationService.OutputFileName), "{ fleets: not json").ConfigureAwait(false);
                    return new CaptainPromptResult { ExitCode = 0 };
                };

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, false).ConfigureAwait(false);
                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Failed, batch.CategorizationStatus);
                AssertContains("not valid JSON", batch.CategorizationError ?? "");

                List<Vessel> vessels = new List<Vessel> { new Vessel { Name = "x" } };
                List<string> warnings = new List<string>();
                AssertThrows<FleetRecommendationFormatException>(() => h.Categorization.ParseRecommendations("{\"fleets\":[]}", vessels, warnings), "empty fleets");
                AssertThrows<FleetRecommendationFormatException>(() => h.Categorization.ParseRecommendations("  ", vessels, warnings), "blank");
                AssertThrows<FleetRecommendationFormatException>(() => h.Categorization.ParseRecommendations("{\"fleets\":[{\"name\":\"A\",\"vesselIds\":[\"vsl_nope\"]}]}", vessels, warnings), "no known vessels");
            }));

            cases.Add(CaseAsync("unknown_and_missing_vessels", "Unknown vessel ids are dropped with a warning and unassigned vessels land in Uncategorized", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                MakeRepo(Path.Combine(h.Root, "b"), "B", "Beta.");
                MakeRepo(Path.Combine(h.Root, "c"), "C", "Gamma.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                h.Runner.Behavior = async (string dir, string prompt, TimeSpan timeout, CancellationToken token) =>
                {
                    Dictionary<string, string> ids = await ReadVesselIdsByNameAsync(dir).ConfigureAwait(false);
                    string json = "```json\n" + JsonSerializer.Serialize(new
                    {
                        fleets = new object[]
                        {
                            new { name = "Core", vesselIds = new[] { ids["A"], "vsl_invented", ids["A"] } },
                            new { name = "core", vesselIds = new[] { ids["B"] } },
                            new { name = "Ghost", vesselIds = new[] { "vsl_ghost" } }
                        }
                    }) + "\n```";
                    await File.WriteAllTextAsync(Path.Combine(dir, FleetCategorizationService.OutputFileName), json).ConfigureAwait(false);
                    return new CaptainPromptResult { ExitCode = 0 };
                };

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, false).ConfigureAwait(false);
                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Completed, batch.CategorizationStatus, "status (error: " + batch.CategorizationError + ")");

                List<VesselImportFleetRecommendation> recs = await testDb.Driver.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, response.BatchId).ConfigureAwait(false);
                AssertEqual(2, recs.Count, "Core (merged) plus Uncategorized; Ghost dropped");
                AssertEqual("Core", recs[0].Name);
                AssertEqual(2, recs[0].VesselIds.Count, "A and B, duplicate A dropped");
                AssertEqual(FleetCategorizationService.UncategorizedFleetName, recs[1].Name);
                AssertEqual(1, recs[1].VesselIds.Count, "C was not assigned");

                Job? job = await JobWait.ForTerminalAsync(testDb.Driver, batch.CategorizationJobId!).ConfigureAwait(false);
                string result = job!.ResultJson ?? "";
                AssertContains("unknown vessel vsl_invented", result);
                AssertContains("\"uncategorizedCount\":1", result);
            }));

            cases.Add(CaseAsync("cancel_via_job", "Cancelling the job stops the captain, fails the categorization, and releases the captain", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                TaskCompletionSource<bool> started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                bool sawCancellation = false;
                h.Runner.Behavior = async (string dir, string prompt, TimeSpan timeout, CancellationToken token) =>
                {
                    started.TrySetResult(true);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        sawCancellation = true;
                        return new CaptainPromptResult { Cancelled = true };
                    }

                    return new CaptainPromptResult { ExitCode = 0 };
                };

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, false).ConfigureAwait(false);
                await Task.WhenAny(started.Task, Task.Delay(10000)).ConfigureAwait(false);
                AssertTrue(started.Task.IsCompleted, "runner started");

                VesselImportBatch running = (await testDb.Driver.VesselImportBatches.ReadAsync(response.BatchId).ConfigureAwait(false))!;
                AssertEqual(VesselImportCategorizationStatusEnum.Running, running.CategorizationStatus);
                Job job = (await testDb.Driver.Jobs.ReadAsync(running.CategorizationJobId!).ConfigureAwait(false))!;
                await h.Jobs.CancelAsync(job).ConfigureAwait(false);

                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertTrue(sawCancellation, "runner token cancelled");
                AssertEqual(VesselImportCategorizationStatusEnum.Failed, batch.CategorizationStatus);
                AssertContains("cancelled", batch.CategorizationError ?? "");
                AssertEqual(JobStatusEnum.Cancelled, (await testDb.Driver.Jobs.ReadAsync(job.Id).ConfigureAwait(false))!.Status, "job stays Cancelled");
                await WaitForCaptainStateAsync(testDb.Driver, captain.Id, CaptainStateEnum.Idle).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("timeout_fails_with_message", "A run that exceeds the time limit fails with a timeout message", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                h.Categorization.TimeoutOverride = TimeSpan.FromSeconds(1);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                h.Runner.Behavior = (string dir, string prompt, TimeSpan timeout, CancellationToken token) => Task.FromResult(new CaptainPromptResult { TimedOut = true });

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, false).ConfigureAwait(false);
                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(TimeSpan.FromSeconds(1), h.Runner.LastTimeout, "timeout passed to the runner");
                AssertEqual(VesselImportCategorizationStatusEnum.Failed, batch.CategorizationStatus);
                AssertContains("did not finish within", batch.CategorizationError ?? "");
                AssertEqual(20, new ArmadaSettings().Import.CategorizationTimeoutMinutes, "default timeout setting");
            }));

            cases.Add(CaseAsync("busy_captain_fails", "A captain that is not Idle is not used and the categorization fails clearly", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                captain.State = CaptainStateEnum.Working;
                await testDb.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, false).ConfigureAwait(false);
                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Failed, batch.CategorizationStatus);
                AssertContains("is not idle", batch.CategorizationError ?? "");
                AssertEqual(0, h.Runner.Calls, "runner not called");
                AssertEqual(CaptainStateEnum.Working, (await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.State, "busy captain untouched");
            }));

            cases.Add(CaseAsync("auto_apply", "ApplyAutomatically creates fleets and assigns vessels; Uncategorized is not created", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "web"), "Web", "Website.");
                MakeRepo(Path.Combine(h.Root, "infra"), "Infra", "Terraform.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                h.Runner.Behavior = async (string dir, string prompt, TimeSpan timeout, CancellationToken token) =>
                {
                    Dictionary<string, string> ids = await ReadVesselIdsByNameAsync(dir).ConfigureAwait(false);
                    string json = JsonSerializer.Serialize(new { fleets = new object[] { new { name = "Frontend", description = "UI", vesselIds = new[] { ids["Web"] } } } });
                    await File.WriteAllTextAsync(Path.Combine(dir, FleetCategorizationService.OutputFileName), json).ConfigureAwait(false);
                    return new CaptainPromptResult { ExitCode = 0 };
                };

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, true).ConfigureAwait(false);
                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Applied, batch.CategorizationStatus, "status (error: " + batch.CategorizationError + ")");

                List<Fleet> fleets = await testDb.Driver.Fleets.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                Fleet? frontend = fleets.FirstOrDefault(f => f.Name == "Frontend");
                AssertNotNull(frontend, "Frontend fleet created");
                AssertEqual("UI", frontend!.Description);
                AssertFalse(fleets.Any(f => f.Name == FleetCategorizationService.UncategorizedFleetName), "no Uncategorized fleet");

                List<Vessel> vessels = await testDb.Driver.Vessels.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertEqual(frontend.Id, vessels.Single(v => v.Name == "web").FleetId, "web assigned");
                AssertNull(vessels.Single(v => v.Name == "infra").FleetId, "infra left without a fleet");

                List<VesselImportFleetRecommendation> recs = await testDb.Driver.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, response.BatchId).ConfigureAwait(false);
                AssertEqual(frontend.Id, recs.Single(r => r.Name == "Frontend").AppliedFleetId);
                AssertContains("\"applied\":true", (await JobWait.ForTerminalAsync(testDb.Driver, batch.CategorizationJobId!).ConfigureAwait(false)).ResultJson ?? "");
            }));

            cases.Add(CaseAsync("request_validation", "Categorization requires an existing captain in the caller's tenant", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                TenantMetadata other = new TenantMetadata("Other " + Guid.NewGuid().ToString("N").Substring(0, 6));
                await testDb.Driver.Tenants.CreateAsync(other).ConfigureAwait(false);
                Captain foreign = await CreateCaptainAsync(testDb.Driver, other.Id).ConfigureAwait(false);
                VesselImportDiscoverResponse discovered = await DiscoverAsync(h).ConfigureAwait(false);

                foreach (string? captainId in new string?[] { null, "cpt_missing", foreign.Id })
                {
                    VesselImportRequest request = SelectAll(discovered);
                    request.Categorization = new VesselImportCategorizationRequest { Enabled = true, CaptainId = captainId };
                    await AssertThrowsAsync<ArgumentException>(() => h.Import.ImportAsync(Constants.DefaultTenantId, null, request), "captain " + (captainId ?? "(none)")).ConfigureAwait(false);
                }

                AssertEqual(0, (await testDb.Driver.Vessels.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false)).Count, "validation happens before any vessel is created");
                VesselImportRequest disabled = SelectAll(discovered);
                disabled.Categorization = new VesselImportCategorizationRequest { Enabled = false, CaptainId = "cpt_missing" };
                VesselImportResponse ok = await h.Import.ImportAsync(Constants.DefaultTenantId, null, disabled).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.None, ok.Batch!.CategorizationStatus, "disabled request ignored");
            }));

            cases.Add(CaseAsync("apply_reuses_fleets_and_validates", "Apply reuses tenant fleets by name case-insensitively, skips Uncategorized, and rejects foreign or duplicate vessels", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                MakeRepo(Path.Combine(h.Root, "b"), "B", "Beta.");
                MakeRepo(Path.Combine(h.Root, "c"), "C", "Gamma.");
                Fleet existing = await testDb.Driver.Fleets.CreateAsync(new Fleet { TenantId = Constants.DefaultTenantId, Name = "Backend Services" }).ConfigureAwait(false);
                Vessel outsider = await testDb.Driver.Vessels.CreateAsync(new Vessel { TenantId = Constants.DefaultTenantId, Name = "outsider", RepoUrl = "https://example.com/o.git" }).ConfigureAwait(false);

                VesselImportResponse response = await h.Import.ImportAsync(Constants.DefaultTenantId, null, SelectAll(await DiscoverAsync(h).ConfigureAwait(false))).ConfigureAwait(false);
                Dictionary<string, string> ids = response.Items.ToDictionary(i => Path.GetFileName(i.Path), i => i.VesselId!);

                FleetRecommendationApplyRequest duplicate = new FleetRecommendationApplyRequest();
                duplicate.Fleets.Add(new FleetRecommendationApplyFleet { Name = "X", VesselIds = new List<string> { ids["a"] } });
                duplicate.Fleets.Add(new FleetRecommendationApplyFleet { Name = "Y", VesselIds = new List<string> { ids["a"] } });
                await AssertThrowsAsync<ArgumentException>(() => h.Categorization.ApplyAsync(Constants.DefaultTenantId, response.BatchId, null, duplicate), "duplicate vessel").ConfigureAwait(false);

                FleetRecommendationApplyRequest foreignVessel = new FleetRecommendationApplyRequest();
                foreignVessel.Fleets.Add(new FleetRecommendationApplyFleet { Name = "X", VesselIds = new List<string> { outsider.Id } });
                await AssertThrowsAsync<ArgumentException>(() => h.Categorization.ApplyAsync(Constants.DefaultTenantId, response.BatchId, null, foreignVessel), "vessel outside the batch").ConfigureAwait(false);

                FleetRecommendationApplyRequest unnamed = new FleetRecommendationApplyRequest();
                unnamed.Fleets.Add(new FleetRecommendationApplyFleet { Name = "  ", VesselIds = new List<string> { ids["a"] } });
                await AssertThrowsAsync<ArgumentException>(() => h.Categorization.ApplyAsync(Constants.DefaultTenantId, response.BatchId, null, unnamed), "unnamed fleet").ConfigureAwait(false);

                TenantMetadata other = new TenantMetadata("Other " + Guid.NewGuid().ToString("N").Substring(0, 6));
                await testDb.Driver.Tenants.CreateAsync(other).ConfigureAwait(false);
                FleetRecommendationApplyRequest valid = new FleetRecommendationApplyRequest();
                valid.Fleets.Add(new FleetRecommendationApplyFleet { Name = "backend services", Description = "ignored for reuse", VesselIds = new List<string> { ids["a"], ids["b"] } });
                valid.Fleets.Add(new FleetRecommendationApplyFleet { Name = "Brand New", Description = "fresh", VesselIds = new List<string> { ids["c"] } });
                valid.Fleets.Add(new FleetRecommendationApplyFleet { Name = "Empty", VesselIds = new List<string>() });
                await AssertThrowsAsync<KeyNotFoundException>(() => h.Categorization.ApplyAsync(other.Id, response.BatchId, null, valid), "cross-tenant batch").ConfigureAwait(false);

                FleetRecommendationApplyResult result = await h.Categorization.ApplyAsync(Constants.DefaultTenantId, response.BatchId, Constants.DefaultUserId, valid).ConfigureAwait(false);
                AssertEqual(2, result.Fleets.Count, "two fleets used, empty skipped");
                AssertEqual(existing.Id, result.Fleets[0].Id, "existing fleet reused case-insensitively");
                AssertEqual(1, result.CreatedFleetIds.Count, "one fleet created");
                AssertEqual(3, result.Assignments.Count);
                AssertEqual(VesselImportCategorizationStatusEnum.Applied, result.Batch!.CategorizationStatus);

                List<Fleet> fleets = await testDb.Driver.Fleets.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertEqual(1, fleets.Count(f => String.Equals(f.Name, "Backend Services", StringComparison.OrdinalIgnoreCase)), "no duplicate fleet");
                Fleet brandNew = fleets.Single(f => f.Name == "Brand New");
                AssertEqual("fresh", brandNew.Description);
                AssertEqual(existing.Id, (await testDb.Driver.Vessels.ReadAsync(ids["a"]).ConfigureAwait(false))!.FleetId);
                AssertEqual(brandNew.Id, (await testDb.Driver.Vessels.ReadAsync(ids["c"]).ConfigureAwait(false))!.FleetId);
                AssertEqual(0, (await testDb.Driver.Fleets.EnumerateAsync(other.Id).ConfigureAwait(false)).Count, "no fleets in the other tenant");

                FleetRecommendationApplyRequest uncategorized = new FleetRecommendationApplyRequest();
                uncategorized.Fleets.Add(new FleetRecommendationApplyFleet { Name = "uncategorized", VesselIds = new List<string> { ids["c"] } });
                FleetRecommendationApplyResult second = await h.Categorization.ApplyAsync(Constants.DefaultTenantId, response.BatchId, null, uncategorized).ConfigureAwait(false);
                AssertEqual(0, second.Fleets.Count, "Uncategorized is never created");
                AssertEqual(brandNew.Id, (await testDb.Driver.Vessels.ReadAsync(ids["c"]).ConfigureAwait(false))!.FleetId, "vessel keeps its fleet");
            }));

            cases.Add(CaseAsync("retry_reuses_previous_settings", "CategorizeAsync re-runs with the previous captain and prompt and rejects unfinished imports", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                h.Runner.Behavior = (string dir, string prompt, TimeSpan timeout, CancellationToken token) => Task.FromResult(new CaptainPromptResult { ExitCode = 1 });

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, "Custom instructions.", false).ConfigureAwait(false);
                VesselImportBatch failed = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Failed, failed.CategorizationStatus);

                h.Runner.Behavior = WriteSingleFleet("Retry Fleet");
                VesselImportBatch pending = await h.Categorization.CategorizeAsync(Constants.DefaultTenantId, response.BatchId, null, null).ConfigureAwait(false);
                AssertNotEqual(failed.CategorizationJobId, pending.CategorizationJobId, "new job");
                VesselImportBatch done = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Completed, done.CategorizationStatus, "status (error: " + done.CategorizationError + ")");
                AssertContains("Custom instructions.", h.Runner.LastPrompt!, "previous prompt reused");
                AssertEqual(captain.Id, h.Runner.LastCaptain!.Id, "previous captain reused");

                VesselImportDiscoverResponse fresh = await DiscoverAsync(h).ConfigureAwait(false);
                await AssertThrowsAsync<InvalidOperationException>(() => h.Categorization.CategorizeAsync(Constants.DefaultTenantId, fresh.BatchId, null,
                    new VesselImportCategorizationRequest { Enabled = true, CaptainId = captain.Id }), "import not finished").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => h.Categorization.CategorizeAsync(Constants.DefaultTenantId, "vib_missing", null, null), "missing batch").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("reply_fallback", "When the file is missing but the captain's reply contains the JSON, the reply is used with a warning", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                MakeRepo(Path.Combine(h.Root, "a"), "A", "Alpha.");
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                h.Runner.Behavior = async (string dir, string prompt, TimeSpan timeout, CancellationToken token) =>
                {
                    Dictionary<string, string> ids = await ReadVesselIdsByNameAsync(dir).ConfigureAwait(false);
                    string reply = "Here you go: {\"fleets\":[{\"name\":\"All {braces} \\\"quoted\\\"\",\"vesselIds\":[\"" + ids["A"] + "\"]}]} Done.";
                    return new CaptainPromptResult { ExitCode = 0, Output = reply };
                };

                VesselImportResponse response = await ImportAllAsync(h, captain.Id, null, false).ConfigureAwait(false);
                VesselImportBatch batch = await WaitForCategorizationAsync(testDb.Driver, response.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportCategorizationStatusEnum.Completed, batch.CategorizationStatus, "status (error: " + batch.CategorizationError + ")");
                List<VesselImportFleetRecommendation> recs = await testDb.Driver.VesselImportFleetRecommendations.EnumerateByBatchAsync(Constants.DefaultTenantId, response.BatchId).ConfigureAwait(false);
                AssertEqual("All {braces} \"quoted\"", recs.Single().Name);
            }));

            cases.Add(CaseAsync("recover_after_restart", "RecoverAsync fails orphaned discovery and categorization and returns Analyzing captains to Idle", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                Captain captain = await CreateCaptainAsync(testDb.Driver, Constants.DefaultTenantId).ConfigureAwait(false);
                await testDb.Driver.Captains.TryReserveAsync(Constants.DefaultTenantId, captain.Id, CaptainStateEnum.Analyzing).ConfigureAwait(false);

                Job job = await h.Jobs.EnqueueAsync("Fleet categorization", JobKindEnum.FleetCategorization, Constants.DefaultTenantId, null).ConfigureAwait(false);
                job.Status = JobStatusEnum.Running;
                await testDb.Driver.Jobs.UpdateAsync(job).ConfigureAwait(false);

                VesselImportBatch categorizing = new VesselImportBatch { TenantId = Constants.DefaultTenantId, Status = VesselImportBatchStatusEnum.Completed, CategorizationStatus = VesselImportCategorizationStatusEnum.Running, CategorizationJobId = job.Id };
                categorizing = await testDb.Driver.VesselImportBatches.CreateAsync(categorizing).ConfigureAwait(false);
                VesselImportBatch discovering = new VesselImportBatch { TenantId = Constants.DefaultTenantId, Status = VesselImportBatchStatusEnum.Discovering };
                discovering = await testDb.Driver.VesselImportBatches.CreateAsync(discovering).ConfigureAwait(false);

                await h.Import.RecoverAsync().ConfigureAwait(false);

                VesselImportBatch c = (await testDb.Driver.VesselImportBatches.ReadAsync(categorizing.Id).ConfigureAwait(false))!;
                AssertEqual(VesselImportCategorizationStatusEnum.Failed, c.CategorizationStatus);
                AssertContains("restarted", c.CategorizationError ?? "");
                AssertEqual(JobStatusEnum.Failed, (await testDb.Driver.Jobs.ReadAsync(job.Id).ConfigureAwait(false))!.Status);
                VesselImportBatch d = (await testDb.Driver.VesselImportBatches.ReadAsync(discovering.Id).ConfigureAwait(false))!;
                AssertEqual(VesselImportBatchStatusEnum.Failed, d.Status);
                AssertContains("restarted", d.ErrorMessage ?? "");
                AssertEqual(CaptainStateEnum.Idle, (await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.State);
            }));

            cases.Add(CaseAsync("hints_rebuilt_on_read", "ReadBatchAsync rebuilds discovery hints from the stored batch", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetCategorizationHarness h = NewHarness(testDb);
                VesselImportBatch batch = new VesselImportBatch { TenantId = Constants.DefaultTenantId, Truncated = true };
                batch = await testDb.Driver.VesselImportBatches.CreateAsync(batch).ConfigureAwait(false);
                await testDb.Driver.VesselImportItems.CreateAsync(new VesselImportItem { TenantId = Constants.DefaultTenantId, BatchId = batch.Id, Path = "/nope", ProposedName = "nope", CandidateStatus = VesselImportCandidateStatusEnum.NotFound }).ConfigureAwait(false);
                VesselImportBatchDetail detail = (await h.Import.ReadBatchAsync(Constants.DefaultTenantId, batch.Id).ConfigureAwait(false))!;
                AssertTrue(detail.Hints.Any(x => x.Code == VesselImportCodes.CandidateLimitReached), "candidate limit hint");
                AssertTrue(detail.Hints.Any(x => x.Code == VesselImportCodes.PathNotVisibleToAdmiral), "not visible hint");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Fleet Categorization Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static FleetCategorizationHarness NewHarness(TestDatabase testDb)
        {
            FleetCategorizationHarness h = new FleetCategorizationHarness();
            h.Root = TestTemp.NewDirectory("categorize");
            ArmadaSettings settings = new ArmadaSettings();
            string data = TestTemp.NewDirectory("categorize_data");
            settings.DataDirectory = data;
            settings.ReposDirectory = Path.Combine(data, "repos");
            settings.DocksDirectory = Path.Combine(data, "docks");
            settings.Import.AllowedRoots = new List<string> { h.Root };
            h.Settings = settings;

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            DatabaseDriver db = testDb.Driver;
            h.Jobs = new JobService(db, logging);
            h.Runner = new StubCaptainPromptRunner();
            h.Categorization = new FleetCategorizationService(db, settings, h.Jobs, h.Runner, new PromptTemplateService(db, logging), logging);
            h.Categorization.JobPollIntervalMs = 50;
            h.Import = new VesselImportService(db, settings, new VesselDiscoveryService(db, settings), new VesselService(db), h.Jobs, logging, h.Categorization);
            h.Import.DiscoveryPollIntervalMs = 50;
            return h;
        }

        private static string MakeRepo(string path, string title, string description, params string[] manifests)
        {
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            File.WriteAllText(Path.Combine(path, "README.md"), "# " + title + "\n\n" + description + "\n");
            foreach (string manifest in manifests) File.WriteAllText(Path.Combine(path, manifest), "{}");
            return path;
        }

        private static async Task<Captain> CreateCaptainAsync(DatabaseDriver db, string tenantId)
        {
            Captain captain = new Captain("categorizer-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            captain.TenantId = tenantId;
            return await db.Captains.CreateAsync(captain).ConfigureAwait(false);
        }

        private static Task<VesselImportDiscoverResponse> DiscoverAsync(FleetCategorizationHarness h)
        {
            VesselDiscoveryRequest request = new VesselDiscoveryRequest();
            request.Roots = new List<string> { h.Root };
            return h.Import.DiscoverAsync(Constants.DefaultTenantId, Constants.DefaultUserId, request);
        }

        private static VesselImportRequest SelectAll(VesselImportDiscoverResponse discovered)
        {
            VesselImportRequest request = new VesselImportRequest();
            request.BatchId = discovered.BatchId;
            request.Paths = discovered.Candidates.Select(c => c.Path).ToList();
            return request;
        }

        private static async Task<VesselImportResponse> ImportAllAsync(FleetCategorizationHarness h, string captainId, string? prompt, bool applyAutomatically)
        {
            VesselImportDiscoverResponse discovered = await DiscoverAsync(h).ConfigureAwait(false);
            VesselImportRequest request = SelectAll(discovered);
            request.Categorization = new VesselImportCategorizationRequest
            {
                Enabled = true,
                CaptainId = captainId,
                Prompt = prompt,
                ApplyAutomatically = applyAutomatically
            };
            return await h.Import.ImportAsync(Constants.DefaultTenantId, Constants.DefaultUserId, request).ConfigureAwait(false);
        }

        private static Func<string, string, TimeSpan, CancellationToken, Task<CaptainPromptResult>> WriteSingleFleet(string name)
        {
            return async (string dir, string prompt, TimeSpan timeout, CancellationToken token) =>
            {
                Dictionary<string, string> ids = await ReadVesselIdsByNameAsync(dir).ConfigureAwait(false);
                string json = JsonSerializer.Serialize(new { fleets = new object[] { new { name = name, vesselIds = ids.Values.ToArray() } } });
                await File.WriteAllTextAsync(Path.Combine(dir, FleetCategorizationService.OutputFileName), json).ConfigureAwait(false);
                return new CaptainPromptResult { ExitCode = 0 };
            };
        }

        private static async Task<Dictionary<string, string>> ReadVesselIdsByNameAsync(string dir)
        {
            string manifest = await File.ReadAllTextAsync(Path.Combine(dir, FleetCategorizationService.ManifestFileName)).ConfigureAwait(false);
            Dictionary<string, string> ids = new Dictionary<string, string>(StringComparer.Ordinal);
            string? currentReadmeTitle = null;
            string? currentId = null;
            foreach (string line in manifest.Split('\n'))
            {
                Match match = _VesselIdLine.Match(line);
                if (match.Success) currentId = match.Groups[1].Value;
                if (line.StartsWith("# ", StringComparison.Ordinal) && currentId != null && !line.StartsWith("# Repositories", StringComparison.Ordinal))
                {
                    currentReadmeTitle = line.Substring(2).Trim();
                    ids[currentReadmeTitle] = currentId;
                    currentId = null;
                }
            }

            return ids;
        }

        private static async Task<VesselImportBatch> WaitForBatchAsync(DatabaseDriver db, string batchId, Func<VesselImportBatch, bool> done)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            VesselImportBatch? batch = null;
            while (DateTime.UtcNow < deadline)
            {
                batch = await db.VesselImportBatches.ReadAsync(batchId).ConfigureAwait(false);
                if (batch != null && done(batch)) return batch;
                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new AssertionException("Timed out waiting for batch " + batchId + " (status " + batch?.Status + ", categorization " + batch?.CategorizationStatus + ")");
        }

        private static async Task<VesselImportBatch> WaitForCategorizationAsync(DatabaseDriver db, string batchId)
        {
            // The worker writes the batch (Completed, then Applied when auto-apply is on) before it finishes the job, so
            // the batch leaving Running is not the end: wait for the categorization job to finish, then read the batch.
            VesselImportBatch batch = await WaitForBatchAsync(db, batchId, b =>
                b.CategorizationStatus != VesselImportCategorizationStatusEnum.Pending
                && b.CategorizationStatus != VesselImportCategorizationStatusEnum.Running).ConfigureAwait(false);
            if (String.IsNullOrEmpty(batch.CategorizationJobId)) return batch;
            await JobWait.ForTerminalAsync(db, batch.CategorizationJobId!).ConfigureAwait(false);
            return (await db.VesselImportBatches.ReadAsync(batchId).ConfigureAwait(false)) ?? batch;
        }

        private static async Task WaitForCaptainStateAsync(DatabaseDriver db, string captainId, CaptainStateEnum state)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                Captain? captain = await db.Captains.ReadAsync(captainId).ConfigureAwait(false);
                if (captain != null && captain.State == state) return;
                await Task.Delay(50).ConfigureAwait(false);
            }

            throw new AssertionException("Captain " + captainId + " did not reach state " + state);
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
