namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Core.Storage;
    using Armada.Tui.Screens.Admin;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Terminal storage inventory uses cached reads and renders measurement units without exposing file contents.</summary>
    public sealed class TuiStorageSuite : IArmadaTestSuite
    {
        /// <summary>Build terminal storage smoke tests.</summary>
        public TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor("Tui.Storage", "Terminal storage", new List<TestCaseDescriptor>
            {
                TuiCase.Sync("Tui.Storage", "cached_inventory", "Storage renders cached file and database measurements", () =>
                {
                    StubHttpHandler stub = TuiFixtures.SignedInServer();
                    StorageSnapshot snapshot = new StorageSnapshot { HostCount = 1 };
                    EnumerationResult<StorageHostReport> hosts = new EnumerationResult<StorageHostReport>
                    {
                        PageNumber = 1, PageSize = 100, TotalPages = 1, TotalRecords = 1,
                        Objects = new List<StorageHostReport>
                        {
                            new StorageHostReport { HostId = "admiral", Name = "Test host", MeasuredUtc = DateTime.UtcNow, Locations = new List<StorageLocation> { new StorageLocation { Path = "/data/missions", Category = "Logs/missions", Bytes = 42, Status = "Complete" } } }
                        }
                    };
                    stub.Json("GET", "/api/v1/storage", ArmadaJson.Serialize(snapshot));
                    stub.Json("POST", "/api/v1/storage/hosts/enumerate", ArmadaJson.Serialize(hosts));
                    using (TuiTestHost host = new TuiTestHost(140, 45, stub, "http://127.0.0.1:9", options => { options.Token = "tok"; options.StartRoute = "/storage"; }))
                    {
                        host.Start();
                        host.PumpUntil(() => host.Tui.Shell.Screen is StorageScreen && stub.RequestsFor("GET", "/api/v1/storage").Count > 0);
                        host.PumpUntil(() => host.Screen().Contains("/data/missions", StringComparison.Ordinal));
                        TuiCase.Contains(host.Screen(), "42 B", "logical byte measurement");
                        AssertEqual(0, stub.RequestsFor("POST", "/api/v1/storage/refresh").Count, "opening a screen does not scan");
                        AssertTrue(stub.RequestsFor("POST", "/api/v1/storage/hosts/enumerate").Count > 0, "host reports are read through the paged hosts route");
                    }
                })
            });
        }
    }
}
