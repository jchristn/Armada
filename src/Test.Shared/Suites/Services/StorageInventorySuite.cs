namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Core.Storage;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Storage accounting, bounds, cancellation and background lifecycle, using isolated temporary data.</summary>
    public sealed class StorageInventorySuite : IArmadaTestSuite
    {
        /// <summary>Build storage test descriptors.</summary>
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor("Services.Storage", "Storage inventory", new List<TestCaseDescriptor>
            {
                Case("history_breakdowns_fold", ct =>
                {
                    Dictionary<string, long> wide = new Dictionary<string, long>();
                    for (int i = 0; i < 40; i++) wide["hbr_" + i.ToString("D2")] = 1000 + i;
                    Dictionary<string, long> folded = StorageHistoryPoint.Fold(wide);
                    AssertEqual(StorageHistoryPoint.MaxBreakdownEntries, folded.Count, "bounded");
                    AssertEqual(wide.Values.Sum(), folded.Values.Sum(), "nothing dropped: the total is kept");
                    AssertTrue(folded.ContainsKey("hbr_39") && !folded.ContainsKey("hbr_00"), "largest kept, smallest folded");
                    long expectedOther = wide.OrderByDescending(kv => kv.Value).Skip(StorageHistoryPoint.MaxBreakdownEntries - 1).Sum(kv => kv.Value);
                    AssertEqual(expectedOther, folded[StorageHistoryPoint.OtherKey], "other is the sum of the rest");
                    Dictionary<string, long> small = new Dictionary<string, long> { ["a"] = 1, ["b"] = 2 };
                    AssertEqual(2, StorageHistoryPoint.Fold(small).Count, "a small breakdown is unchanged");
                    return Task.CompletedTask;
                }),
                Case("overlap", async ct =>
                {
                    string root = Temp("storage_overlap");
                    string logs = Path.Combine(root, "logs");
                    Directory.CreateDirectory(logs);
                    await File.WriteAllBytesAsync(Path.Combine(root, "settings.json"), new byte[7], ct).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(Path.Combine(logs, "mission.log"), new byte[13], ct).ConfigureAwait(false);
                    StorageHostReport report = await new StorageFileCollector().CollectAsync(new[]
                    {
                        new StorageLocation { Path = root, Category = "Other" },
                        new StorageLocation { Path = logs, Category = "Logs" },
                        new StorageLocation { Path = logs, Category = "Duplicate" }
                    }, new StorageSettings(), ct).ConfigureAwait(false);
                    AssertEqual(2, report.Locations.Count);
                    AssertEqual(20L, report.Locations.Sum(l => l.Bytes ?? 0));
                    AssertEqual(7L, report.Locations.Single(l => l.Category == "Other").Bytes!.Value);
                    AssertEqual(13L, report.Locations.Single(l => l.Category == "Logs").Bytes!.Value);
                    AssertEqual("Complete", report.Status);
                    AssertEqual(1L, report.Locations[0].FileCount);
                    AssertNotNull(report.Locations[0].NewestWriteUtc);
                    AssertEqual(13L, report.Locations[0].LargestFiles[0].Bytes);
                }),
                Case("links_missing_and_limits", async ct =>
                {
                    string root = Temp("storage_links");
                    string outside = Temp("storage_external");
                    await File.WriteAllBytesAsync(Path.Combine(outside, "secret"), new byte[99], ct).ConfigureAwait(false);
                    Directory.CreateSymbolicLink(Path.Combine(root, "link"), outside);
                    for (int i = 0; i < 150; i++) await File.WriteAllBytesAsync(Path.Combine(root, i + ".log"), new byte[1], ct).ConfigureAwait(false);
                    StorageHostReport report = await new StorageFileCollector().CollectAsync(new[]
                    {
                        new StorageLocation { Path = Path.Combine(root, "link"), Category = "Link" },
                        new StorageLocation { Path = Path.Combine(outside, "missing"), Category = "Missing" },
                        new StorageLocation { Path = root, Category = "Files" }
                    }, new StorageSettings { MaxEntries = 100, LargestFileLimit = 2 }, ct).ConfigureAwait(false);
                    AssertEqual("Skipped", report.Locations.Single(l => l.Category == "Link").Status);
                    AssertEqual("Missing", report.Locations.Single(l => l.Category == "Missing").Status);
                    StorageLocation files = report.Locations.Single(l => l.Category == "Files");
                    AssertEqual("Partial", files.Status);
                    AssertTrue(files.FileCount < 150);
                    AssertTrue(files.Bytes < 99 || files.FileCount == files.Bytes);
                    AssertTrue(files.LargestFiles.Count <= 2);
                    AssertTrue(files.Warnings.Contains("BudgetExceeded"));
                }),
                Case("harbor_wire_identity_and_stale_cache", async ct =>
                {
                    HarborHeartbeat? legacy = HarborProtocol.Deserialize("{\"type\":\"heartbeat\",\"liveJobIds\":[]}") as HarborHeartbeat;
                    AssertNotNull(legacy);
                    AssertTrue(legacy!.StorageReport == null);
                    using (TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        ArmadaSettings settings = new ArmadaSettings { DataDirectory = Temp("storage_remote") };
                        LoggingModule logging = new LoggingModule();
                        logging.Settings.EnableConsole = false;
                        HarborService service = new HarborService(db.Driver, logging, settings.Harbor);
                        HarborConnectionManager connections = new HarborConnectionManager(service, logging, null);
                        using (StorageInventoryService inventory = new StorageInventoryService(db.Driver, settings, connections.IsConnected))
                        {
                            connections.StorageReportReceived = inventory.Receive;
                            string id = "hbr_storage_test";
                            await connections.OnHandshakeAsync(new HarborHandshake { HarborId = id, Name = "Storage Harbor", MaxConcurrentJobs = 1 }, "ten_default", "usr_default", (_, _) => Task.CompletedTask, ct).ConfigureAwait(false);
                            inventory.RequestRefresh(); await WaitAsync(inventory, ct).ConfigureAwait(false);
                            StorageHostReport remote = new StorageHostReport { HostId = "spoof", MeasuredUtc = DateTime.UtcNow, Status = "Complete", Locations = new List<StorageLocation> { new StorageLocation { Path = "/remote/logs", Category = "Logs/harbor", Bytes = 42, Status = "Complete" } } };
                            HarborHeartbeat heartbeat = new HarborHeartbeat { StorageReport = remote };
                            HarborHeartbeat? roundtrip = HarborProtocol.Deserialize(HarborProtocol.Serialize(heartbeat)) as HarborHeartbeat;
                            AssertEqual(42L, roundtrip!.StorageReport!.Locations[0].Bytes!.Value);
                            await connections.OnMessageAsync(id, heartbeat, ct).ConfigureAwait(false);
                            StorageHostReport visible = inventory.Read().Hosts.Single(h => h.HostId == id);
                            AssertEqual(42L, visible.Locations[0].Bytes!.Value);
                            AssertTrue(!visible.Stale);
                            AssertTrue(!inventory.Read().Hosts.Any(h => h.HostId == "spoof"));
                            await connections.OnMessageAsync("hbr_not_linked", heartbeat, ct).ConfigureAwait(false);
                            AssertTrue(!inventory.Read().Hosts.Any(h => h.HostId == "hbr_not_linked"));
                            remote.Locations[0].Bytes = -1;
                            await connections.OnMessageAsync(id, heartbeat, ct).ConfigureAwait(false);
                            AssertEqual(42L, inventory.Read().Hosts.Single(h => h.HostId == id).Locations[0].Bytes!.Value);

                            // Hosts are paged, Admiral first; the snapshot carries totals and serializes no host list.
                            StorageSnapshot totals = inventory.Read();
                            AssertEqual(totals.Hosts.Count, totals.HostCount, "host count");
                            AssertFalse(System.Text.Json.JsonSerializer.Serialize(totals).Contains("\"Hosts\":[", StringComparison.Ordinal), "no embedded host list");
                            Armada.Core.Models.EnumerationResult<StorageHostReport> first = inventory.EnumerateHosts(new Armada.Core.Models.EnumerationQuery { PageNumber = 1, PageSize = 1 });
                            AssertEqual("admiral", first.Objects.Single().HostId, "Admiral first");
                            AssertEqual((long)totals.HostCount, first.TotalRecords, "exact total");
                            Armada.Core.Models.EnumerationResult<StorageHostReport> second = inventory.EnumerateHosts(new Armada.Core.Models.EnumerationQuery { PageNumber = 2, PageSize = 1 });
                            AssertEqual(id, second.Objects.Single().HostId, "then the Harbor");
                        }
                    }
                }),
                Case("settings_and_cancellation", async ct =>
                {
                    StorageSettings settings = new StorageSettings { IntervalSeconds = -1, HistoryCapacity = 99999, MaxEntries = 0 };
                    AssertEqual(30, settings.IntervalSeconds);
                    AssertEqual(CollectionLimits.MaxSummaryBuckets, settings.HistoryCapacity);
                    AssertEqual(100, settings.MaxEntries);
                    using (CancellationTokenSource cancelled = new CancellationTokenSource())
                    {
                        cancelled.Cancel();
                        bool threw = false;
                        try { await new StorageFileCollector().CollectAsync(Array.Empty<StorageLocation>(), settings, cancelled.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { threw = true; }
                        AssertTrue(threw);
                    }
                }),
                Case("background_refresh_and_remote_identity", async ct =>
                {
                    using (TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        string root = Temp("storage_inventory");
                        ArmadaSettings settings = new ArmadaSettings { DataDirectory = root, LogDirectory = Path.Combine(root, "logs"), DocksDirectory = Path.Combine(root, "docks"), ReposDirectory = Path.Combine(root, "repos") };
                        settings.Storage.TimeoutSeconds = 60;
                        settings.Storage.HistoryCapacity = 2;
                        using (StorageInventoryService inventory = new StorageInventoryService(db.Driver, settings, _ => false))
                        {
                            AssertTrue(inventory.Read().MeasuredUtc == null);
                            inventory.RequestRefresh(true);
                            await WaitAsync(inventory, ct).ConfigureAwait(false);
                            StorageSnapshot first = inventory.Read();
                            AssertTrue(first.MeasuredUtc.HasValue, first.LastError);
                            AssertEqual(1, first.History.Count);
                            AssertTrue(first.Database.Tables.Count > 0);
                            first.Hosts.Clear();
                            AssertTrue(inventory.Read().Hosts.Count > 0, "published snapshots are detached");
                            inventory.RequestRefresh(); await WaitAsync(inventory, ct).ConfigureAwait(false);
                            inventory.RequestRefresh(); await WaitAsync(inventory, ct).ConfigureAwait(false);
                            AssertEqual(2, inventory.Read().History.Count);
                            AssertTrue(inventory.Read().Retention.ContainsKey("BuildSlots"));
                        }
                    }
                })
            });
        }

        private static string Temp(string name)
        {
            string path = TestTemp.NewDirectory(name);
            // macOS exposes its temporary directory through the operating system's /var link.
            return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + path : path;
        }

        private static TestCaseDescriptor Case(string id, Func<CancellationToken, Task> run)
        {
            return new TestCaseDescriptor("Services.Storage", id, id, run);
        }

        private static async Task WaitAsync(StorageInventoryService inventory, CancellationToken token)
        {
            for (int i = 0; i < 1000 && inventory.Read().Collecting; i++) await Task.Delay(20, token).ConfigureAwait(false);
            AssertTrue(!inventory.Read().Collecting, "background collection completed");
        }
    }
}
