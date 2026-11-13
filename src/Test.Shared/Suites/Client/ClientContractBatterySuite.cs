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
                    BatteryPackDetail pack = (await c.CreateBatteryPackAsync(new BatteryPackEditRequest { PackId = "client-demo", ManifestJson = "{}" }))!;
                    AssertFalse(pack.Info.Valid);
                    pack = (await c.GetBatteryPackAsync("client-demo"))!;
                    AssertEqual("{}", pack.ManifestJson);
                    pack = (await c.WriteBatteryPackFileAsync("client-demo", "fixture/demo.txt", "hello"))!;
                    AssertTrue(pack.Files.Any(f => f.Path == "fixture/demo.txt"));
                    BatteryPackTextFile text = (await c.ReadBatteryPackFileAsync("client-demo", "fixture/demo.txt"))!;
                    AssertEqual("hello", text.Content);
                    pack = (await c.UpdateBatteryPackManifestAsync("client-demo", "{ \"id\": \"client-demo\" }"))!;
                    AssertFalse(pack.Info.Valid);
                    AssertFalse((await c.ValidateBatteryPackAsync("client-demo"))!.Valid);
                    await c.DeleteBatteryPackFileAsync("client-demo", "fixture/demo.txt");
                    await c.DeleteBatteryPackAsync("client-demo");
                    await ClientContract.ExpectErrorAsync(() => c.GetBatteryPackAsync("client-demo"), "deleted pack", 404, 404);
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

                EnumerationResult<BatteryPackInfo> packs = (await c.ListBatteryPacksAsync())!;
                AssertTrue(packs.Objects.Any(p => p.Id == "csharp" && p.Valid), "csharp listed");
                EnumerationResult<BatteryPackInfo> one = (await c.ListBatteryPacksAsync(1, 1))!;
                AssertEqual(1, one.PageSize, "page size sent");
                AssertEqual(1, one.Objects.Count, "one pack");

                BatteryRun queued = (await c.RunBatteryAsync(new BatteryRunRequest { CaptainId = captain.Id, Packs = new List<string> { "csharp" }, RunCount = 1, Seed = 7, CreateFixtureVessel = true }))!;
                AssertEqual(captain.Id, queued.CaptainId, "captain sent");
                AssertEqual((int?)7, queued.Seed, "seed sent");
                AssertEqual(1, queued.RunCount, "run count sent");
                await ClientContract.ExpectErrorAsync(() => c.RunBatteryAsync(new BatteryRunRequest { CaptainId = captain.Id, Packs = new List<string> { "nope" } }), "unknown pack", 400, 400);
                await ClientContract.ExpectErrorAsync(() => c.RunBatteryAsync(new BatteryRunRequest { CaptainId = "cpt_missing", Packs = new List<string> { "csharp" } }), "unknown captain", 404, 404);

                DateTime deadline = DateTime.UtcNow.AddSeconds(120);
                BatteryRun run = (await c.GetBatteryRunAsync(queued.Id))!;
                while (run.Status != BatteryRunStatusEnum.Completed && run.Status != BatteryRunStatusEnum.Failed)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("run still " + run.Status);
                    await Task.Delay(200).ConfigureAwait(false);
                    run = (await c.GetBatteryRunAsync(queued.Id))!;
                }

                AssertEqual(BatteryRunStatusEnum.Completed, run.Status, run.FailureReason);
                AssertEqual(0, run.Results.Count, "the run read does not embed results");
                EnumerationResult<BatteryTaskResult> first = (await c.EnumerateBatteryRunResultsAsync(run.Id, new EnumerationQuery { PageNumber = 1, PageSize = 4, Order = EnumerationOrderEnum.CreatedAscending }))!;
                AssertEqual(10L, first.TotalRecords, "results total");
                AssertEqual(3, first.TotalPages, "results pages");
                AssertEqual(4, first.Objects.Count, "first page");
                EnumerationResult<BatteryTaskResult> last = (await c.EnumerateBatteryRunResultsAsync(run.Id, new EnumerationQuery { PageNumber = 3, PageSize = 4, Order = EnumerationOrderEnum.CreatedAscending }))!;
                AssertEqual(2, last.Objects.Count, "final partial page");
                EnumerationResult<BatteryTaskResult> viaGet = (await c.ListBatteryRunResultsAsync(run.Id, 1, 1000, null))!;
                AssertEqual(10, viaGet.Objects.Count, "GET twin");
                await ClientContract.ExpectErrorAsync(() => c.GetBatteryRunAsync("bat_missing"), "unknown run", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.EnumerateBatteryRunResultsAsync("bat_missing"), "unknown run results", 404, 404);
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
