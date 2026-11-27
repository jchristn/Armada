namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract for the fixture battery: ListBatteryPacksAsync, RunBatteryAsync and GetBatteryRunAsync
    /// against a live Admiral with a canned dispatcher.
    /// </summary>
    public sealed class ClientContractBatterySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.Battery";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("pack_management", "Create, inspect, edit, validate and delete an on-disk pack through the client", async (c, fx) =>
            {
                string? original = fx.Settings.Evaluation.BatteryDirectory;
                string directory = Path.Combine(fx.TempDir, "client-battery");
                Directory.CreateDirectory(directory);
                fx.Settings.Evaluation.BatteryDirectory = directory;
                try
                {
                    BatteryPackDetail pack = (await c.Battery.CreatePackAsync(new BatteryPackEditRequest { PackId = "client-demo", ManifestJson = "{}" }))!;
                    AssertFalse(pack.Info.Valid);
                    pack = (await c.Battery.GetPackAsync("client-demo"))!;
                    AssertEqual("{}", pack.ManifestJson);
                    pack = (await c.Battery.WritePackFileAsync("client-demo", "fixture/demo.txt", "hello"))!;
                    AssertTrue(pack.Files.Any(f => f.Path == "fixture/demo.txt"));
                    BatteryPackTextFile text = (await c.Battery.ReadPackFileAsync("client-demo", "fixture/demo.txt"))!;
                    AssertEqual("hello", text.Content);
                    pack = (await c.Battery.UpdatePackManifestAsync("client-demo", "{ \"id\": \"client-demo\" }"))!;
                    AssertFalse(pack.Info.Valid);
                    AssertFalse((await c.Battery.ValidatePackAsync("client-demo"))!.Valid);
                    await c.Battery.DeletePackFileAsync("client-demo", "fixture/demo.txt");
                    await c.Battery.DeletePackAsync("client-demo");
                    await ClientContract.ExpectErrorAsync(() => c.Battery.GetPackAsync("client-demo"), "deleted pack", 404, 404);
                }
                finally { fx.Settings.Evaluation.BatteryDirectory = original; }
            }));

            cases.Add(Case("battery", "List packs, run the C# pack, get the run; unknown packs and runs fail", async (c, fx) =>
            {
                Captain captain;
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
                using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
                {
                    await db.InitializeAsync().ConfigureAwait(false);
                    captain = await db.Captains.CreateAsync(new Captain { Name = "battery-contract-" + ClientContract.Suffix(), Runtime = AgentRuntimeEnum.ClaudeCode, TenantId = Armada.Core.Constants.DefaultTenantId }).ConfigureAwait(false);
                }

                EnumerationResult<BatteryPackInfo> packs = (await c.Battery.ListPacksAsync())!;
                AssertTrue(packs.Objects.Any(p => p.Id == "csharp" && p.Valid), "csharp listed");
                EnumerationResult<BatteryPackInfo> one = (await c.Battery.ListPacksAsync(1, 1))!;
                AssertEqual(1, one.PageSize, "page size sent");
                AssertEqual(1, one.Objects.Count, "one pack");

                BatteryRun queued = (await c.Battery.RunAsync(new BatteryRunRequest { CaptainId = captain.Id, Packs = new List<string> { "csharp" }, RunCount = 1, Seed = 7, CreateFixtureVessel = true }))!;
                AssertEqual(captain.Id, queued.CaptainId, "captain sent");
                AssertEqual((int?)7, queued.Seed, "seed sent");
                AssertEqual(1, queued.RunCount, "run count sent");
                await ClientContract.ExpectErrorAsync(() => c.Battery.RunAsync(new BatteryRunRequest { CaptainId = captain.Id, Packs = new List<string> { "nope" } }), "unknown pack", 400, 400);
                await ClientContract.ExpectErrorAsync(() => c.Battery.RunAsync(new BatteryRunRequest { CaptainId = "cpt_missing", Packs = new List<string> { "csharp" } }), "unknown captain", 404, 404);

                DateTime deadline = DateTime.UtcNow.AddSeconds(120);
                BatteryRun run = (await c.Battery.GetRunAsync(queued.Id))!;
                while (run.Status != BatteryRunStatusEnum.Completed && run.Status != BatteryRunStatusEnum.Failed)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("run still " + run.Status);
                    await Task.Delay(200).ConfigureAwait(false);
                    run = (await c.Battery.GetRunAsync(queued.Id))!;
                }

                AssertEqual(BatteryRunStatusEnum.Completed, run.Status, run.FailureReason);
                AssertEqual(0, run.Results.Count, "the run read does not embed results");
                EnumerationResult<BatteryTaskResult> first = (await c.Battery.EnumerateRunResultsAsync(run.Id, new EnumerationQuery { PageNumber = 1, PageSize = 4, Order = EnumerationOrderEnum.CreatedAscending }))!;
                AssertEqual(10L, first.TotalRecords, "results total");
                AssertEqual(3, first.TotalPages, "results pages");
                AssertEqual(4, first.Objects.Count, "first page");
                EnumerationResult<BatteryTaskResult> last = (await c.Battery.EnumerateRunResultsAsync(run.Id, new EnumerationQuery { PageNumber = 3, PageSize = 4, Order = EnumerationOrderEnum.CreatedAscending }))!;
                AssertEqual(2, last.Objects.Count, "final partial page");
                EnumerationResult<BatteryTaskResult> viaGet = (await c.Battery.ListRunResultsAsync(run.Id, 1, 1000, null))!;
                AssertEqual(10, viaGet.Objects.Count, "GET twin");
                await ClientContract.ExpectErrorAsync(() => c.Battery.GetRunAsync("bat_missing"), "unknown run", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.Battery.EnumerateRunResultsAsync("bat_missing"), "unknown run results", 404, 404);

                EnumerationResult<BatteryRun> listed = (await c.Battery.ListRunsAsync(new BatteryRunQuery { CaptainId = captain.Id, Status = BatteryRunStatusEnum.Completed, PageSize = 5 }))!;
                AssertEqual(run.Id, listed.Objects.Single().Id, "list filters by captain and status");
                AssertEqual(5, listed.PageSize, "page size sent");
                AssertEqual(0, listed.Objects.Single().Results.Count, "the list does not embed results");
                BatteryRun unchanged = (await c.Battery.CancelRunAsync(run.Id, "too late"))!;
                AssertEqual(BatteryRunStatusEnum.Completed, unchanged.Status, "cancelling a finished run changes nothing");
                await ClientContract.ExpectErrorAsync(() => c.Battery.CancelRunAsync("bat_missing"), "cancel unknown run", 404, 404);
            }));

            cases.Add(Case("cancel_interrupted", "Cancel a run recorded Running that this Admiral is not executing: it ends Cancelled with the reason", async (c, fx) =>
            {
                BatteryRun orphan;
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
                using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
                {
                    await db.InitializeAsync().ConfigureAwait(false);
                    orphan = await db.BatteryRuns.CreateAsync(new BatteryRun { TenantId = Armada.Core.Constants.DefaultTenantId, Packs = new List<string> { "csharp" }, Status = BatteryRunStatusEnum.Running, TotalTasks = 10 }).ConfigureAwait(false);
                }

                BatteryRun cancelled = (await c.Battery.CancelRunAsync(orphan.Id, "Gate client stopped"))!;
                AssertEqual(BatteryRunStatusEnum.Cancelled, cancelled.Status, "cancelled");
                AssertEqual("Gate client stopped", cancelled.FailureReason, "reason sent");
                EnumerationResult<BatteryRun> page = (await c.Battery.ListRunsAsync(new BatteryRunQuery { Status = BatteryRunStatusEnum.Cancelled, PageSize = 100 }))!;
                AssertTrue(page.Objects.Any(r => r.Id == orphan.Id), "listed as Cancelled");
                AssertNotNull(await c.Battery.ListRunsAsync(), "no query");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: fixture battery", cases: cases);
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body, fx => fx.Server.BatteryTaskDispatcherOverride = CannedBatteryTaskDispatcher.ForCSharpPack());
        }

        #endregion
    }
}
