namespace Armada.Test.Unit.Suites.Routes
{
    using System.Net;
    using System.Net.Http.Headers;
    using System.Net.Sockets;
    using System.Text.Json;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Server;
    using Armada.Test.Common;
    using SyslogLogging;

    /// <summary>
    /// Route-level regression coverage for tenant filtering. REST routes must
    /// honor the authenticated scope, while MCP intentionally remains unscoped
    /// until it gains an authentication context.
    /// </summary>
    public class TenantFilteringTests : TestSuite
    {
        public override string Name => "REST Tenant Filtering";

        protected override async Task RunTestsAsync()
        {
            await RunTest("REST enumeration routes apply admin, tenant-admin, and user filters", async () =>
            {
                using ServerFixture fixture = await ServerFixture.CreateAsync().ConfigureAwait(false);

                foreach (EntityEnumerationSpec spec in GetEnumerationSpecs())
                {
                    await AssertEnumerationScopeAsync(fixture, spec).ConfigureAwait(false);
                }
            });

            await RunTest("REST read paths deny regular users outside their scoped entities", async () =>
            {
                using ServerFixture fixture = await ServerFixture.CreateAsync().ConfigureAwait(false);

                foreach (EntityReadSpec spec in GetReadSpecs())
                {
                    EntityIds ids = spec.IdsSelector(fixture.Data);

                    using (HttpResponseMessage ownResponse = await fixture.GetAsync(fixture.UserAuth, spec.PathBuilder(ids.UserOwnedId)).ConfigureAwait(false))
                    {
                        AssertStatusCode(HttpStatusCode.OK, ownResponse, spec.Name + " own entity should be readable");
                    }

                    using (HttpResponseMessage sameTenantResponse = await fixture.GetAsync(fixture.UserAuth, spec.PathBuilder(ids.TenantAdminOwnedId)).ConfigureAwait(false))
                    {
                        AssertStatusCode(HttpStatusCode.NotFound, sameTenantResponse, spec.Name + " same-tenant other-user entity should be hidden");
                    }

                    using (HttpResponseMessage otherTenantResponse = await fixture.GetAsync(fixture.UserAuth, spec.PathBuilder(ids.OtherTenantOwnedId)).ConfigureAwait(false))
                    {
                        AssertStatusCode(HttpStatusCode.NotFound, otherTenantResponse, spec.Name + " other-tenant entity should be hidden");
                    }
                }

                List<string> ownEventIds = await GetObjectIdsAsync(
                    fixture,
                    fixture.UserAuth,
                    "/api/v1/events?missionId=" + Uri.EscapeDataString(fixture.Data.EventMissionIds.UserOwnedId) + "&limit=100").ConfigureAwait(false);
                AssertIdSet("Event own filter", ownEventIds, fixture.Data.Events.UserOwnedId);

                List<string> sameTenantEventIds = await GetObjectIdsAsync(
                    fixture,
                    fixture.UserAuth,
                    "/api/v1/events?missionId=" + Uri.EscapeDataString(fixture.Data.EventMissionIds.TenantAdminOwnedId) + "&limit=100").ConfigureAwait(false);
                AssertIdSet("Event same-tenant other-user filter", sameTenantEventIds);

                List<string> otherTenantEventIds = await GetObjectIdsAsync(
                    fixture,
                    fixture.UserAuth,
                    "/api/v1/events?missionId=" + Uri.EscapeDataString(fixture.Data.EventMissionIds.OtherTenantOwnedId) + "&limit=100").ConfigureAwait(false);
                AssertIdSet("Event other-tenant filter", otherTenantEventIds);
            });

            await RunTest("MCP enumerate scope remains explicitly documented as admin-only", () =>
            {
                string registrarText = File.ReadAllText(ResolveRepoPath("src/Armada.Server/Mcp/McpToolRegistrar.cs"));
                AssertContains(
                    "TODO: MCP is currently unauthenticated and uses the default tenant context for all operations.",
                    registrarText,
                    "MCP registrar TODO should stay explicit");
                AssertContains(
                    "MCP authentication and per-tenant scoping is planned for a future phase.",
                    registrarText,
                    "MCP registrar should keep the future-scoping note");

                string enumerateText = File.ReadAllText(ResolveRepoPath("src/Armada.Server/Mcp/Tools/McpEnumerateTools.cs"));
                AssertContains(
                    "Enumerate operations use unscoped (admin-level) methods since MCP has no auth context.",
                    enumerateText,
                    "MCP enumerate tools should document the current admin-level scope");
                AssertContains(
                    "When MCP auth is added, these should switch to tenant-scoped overloads.",
                    enumerateText,
                    "MCP enumerate tools should track the future tenant-scoping change");
            });
        }

        private async Task AssertEnumerationScopeAsync(ServerFixture fixture, EntityEnumerationSpec spec)
        {
            EntityIds ids = spec.IdsSelector(fixture.Data);

            List<string> adminIds = await GetObjectIdsAsync(fixture, fixture.AdminAuth, spec.Path).ConfigureAwait(false);
            AssertIdSet(spec.Name + " admin scope", adminIds, ids.TenantAdminOwnedId, ids.UserOwnedId, ids.OtherTenantOwnedId);

            List<string> tenantAdminIds = await GetObjectIdsAsync(fixture, fixture.TenantAdminAuth, spec.Path).ConfigureAwait(false);
            AssertIdSet(spec.Name + " tenant-admin scope", tenantAdminIds, ids.TenantAdminOwnedId, ids.UserOwnedId);

            List<string> userIds = await GetObjectIdsAsync(fixture, fixture.UserAuth, spec.Path).ConfigureAwait(false);
            AssertIdSet(spec.Name + " user scope", userIds, ids.UserOwnedId);
        }

        private async Task<List<string>> GetObjectIdsAsync(ServerFixture fixture, AuthHeader auth, string path)
        {
            using HttpResponseMessage response = await fixture.GetAsync(auth, path).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, response, "GET " + path);

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement objects = GetPropertyIgnoreCase(document.RootElement, "objects");

            List<string> ids = new List<string>();
            foreach (JsonElement item in objects.EnumerateArray())
            {
                string? id = GetPropertyIgnoreCase(item, "id").GetString();
                AssertFalse(String.IsNullOrEmpty(id), path + " should return objects with ids");
                ids.Add(id!);
            }

            return ids;
        }

        private static JsonElement GetPropertyIgnoreCase(JsonElement element, string propertyName)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (String.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }

            throw new KeyNotFoundException("Property '" + propertyName + "' was not present in the JSON payload.");
        }

        private void AssertIdSet(string label, IReadOnlyCollection<string> actualIds, params string[] expectedIds)
        {
            AssertEqual(expectedIds.Length, actualIds.Count, label + " count");

            foreach (string expectedId in expectedIds)
            {
                AssertTrue(actualIds.Contains(expectedId), label + " should contain " + expectedId);
            }
        }

        private static IEnumerable<EntityEnumerationSpec> GetEnumerationSpecs()
        {
            yield return new EntityEnumerationSpec("Captains", "/api/v1/captains", data => data.Captains);
            yield return new EntityEnumerationSpec("Missions", "/api/v1/missions", data => data.Missions);
            yield return new EntityEnumerationSpec("Events", "/api/v1/events?limit=100", data => data.Events);
            yield return new EntityEnumerationSpec("Vessels", "/api/v1/vessels", data => data.Vessels);
            yield return new EntityEnumerationSpec("Voyages", "/api/v1/voyages", data => data.Voyages);
            yield return new EntityEnumerationSpec("Docks", "/api/v1/docks", data => data.Docks);
        }

        private static IEnumerable<EntityReadSpec> GetReadSpecs()
        {
            yield return new EntityReadSpec("Captains", data => data.Captains, id => "/api/v1/captains/" + id);
            yield return new EntityReadSpec("Missions", data => data.Missions, id => "/api/v1/missions/" + id);
            yield return new EntityReadSpec("Vessels", data => data.Vessels, id => "/api/v1/vessels/" + id);
            yield return new EntityReadSpec("Voyages", data => data.Voyages, id => "/api/v1/voyages/" + id);
            yield return new EntityReadSpec("Docks", data => data.Docks, id => "/api/v1/docks/" + id);
        }

        private static string ResolveRepoPath(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo? current = new DirectoryInfo(start);
                while (current != null)
                {
                    string candidate = Path.Combine(current.FullName, relativePath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    current = current.Parent;
                }
            }

            throw new DirectoryNotFoundException("Could not resolve repository path for " + relativePath);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings(string rootPath)
        {
            string databasePath = Path.Combine(rootPath, "armada-test.db");
            string docksPath = Path.Combine(rootPath, "docks");
            string reposPath = Path.Combine(rootPath, "repos");
            string logsPath = Path.Combine(rootPath, "logs");

            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(docksPath);
            Directory.CreateDirectory(reposPath);
            Directory.CreateDirectory(logsPath);

            ArmadaSettings settings = new ArmadaSettings();
            settings.DataDirectory = rootPath;
            settings.DatabasePath = databasePath;
            settings.LogDirectory = logsPath;
            settings.DocksDirectory = docksPath;
            settings.ReposDirectory = reposPath;
            settings.AdmiralPort = GetFreePort();
            settings.McpPort = GetFreePort();
            settings.WebSocketPort = GetFreePort();
            settings.ApiKey = "tenant-filtering-test-admin-key";
            settings.Database.Filename = databasePath;
            settings.Rest.Hostname = "localhost";
            return settings;
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private sealed record EntityEnumerationSpec(string Name, string Path, Func<SeedData, EntityIds> IdsSelector);

        private sealed record EntityReadSpec(string Name, Func<SeedData, EntityIds> IdsSelector, Func<string, string> PathBuilder);

        private sealed record EntityIds(string TenantAdminOwnedId, string UserOwnedId, string OtherTenantOwnedId);

        private sealed class AuthHeader
        {
            public string? ApiKey { get; init; }
            public string? BearerToken { get; init; }
        }

        private sealed class SeedData
        {
            public required EntityIds Captains { get; init; }
            public required EntityIds Missions { get; init; }
            public required EntityIds Events { get; init; }
            public required EntityIds Vessels { get; init; }
            public required EntityIds Voyages { get; init; }
            public required EntityIds Docks { get; init; }
            public required EntityIds EventMissionIds { get; init; }
            public required AuthHeader AdminAuth { get; init; }
            public required AuthHeader TenantAdminAuth { get; init; }
            public required AuthHeader UserAuth { get; init; }
        }

        private sealed class ServerFixture : IDisposable
        {
            private readonly string _RootPath;
            private readonly ArmadaServer _Server;

            public SeedData Data { get; }
            public AuthHeader AdminAuth => Data.AdminAuth;
            public AuthHeader TenantAdminAuth => Data.TenantAdminAuth;
            public AuthHeader UserAuth => Data.UserAuth;
            public HttpClient Client { get; }

            private ServerFixture(string rootPath, ArmadaServer server, SeedData data, HttpClient client)
            {
                _RootPath = rootPath;
                _Server = server;
                Data = data;
                Client = client;
            }

            public static async Task<ServerFixture> CreateAsync()
            {
                string rootPath = Path.Combine(Path.GetTempPath(), "armada_tenant_filtering_" + Guid.NewGuid().ToString("N"));
                ArmadaSettings settings = CreateSettings(rootPath);

                SeedData data = await SeedDatabaseAsync(settings, rootPath).ConfigureAwait(false);

                LoggingModule logging = CreateLogging();
                ArmadaServer server = new ArmadaServer(logging, settings, quiet: true);
                await server.StartAsync().ConfigureAwait(false);

                HttpClient client = new HttpClient
                {
                    BaseAddress = new Uri("http://localhost:" + settings.AdmiralPort),
                    Timeout = TimeSpan.FromSeconds(15)
                };

                await WaitForHealthAsync(client).ConfigureAwait(false);

                return new ServerFixture(rootPath, server, data, client);
            }

            public async Task<HttpResponseMessage> GetAsync(AuthHeader auth, string path)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path);
                if (!String.IsNullOrEmpty(auth.ApiKey))
                {
                    request.Headers.Add("X-Api-Key", auth.ApiKey);
                }

                if (!String.IsNullOrEmpty(auth.BearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.BearerToken);
                }

                return await Client.SendAsync(request).ConfigureAwait(false);
            }

            public void Dispose()
            {
                try
                {
                    Client.Dispose();
                }
                catch
                {
                }

                try
                {
                    _Server.Stop();
                    Thread.Sleep(200);
                }
                catch
                {
                }

                try
                {
                    if (Directory.Exists(_RootPath))
                    {
                        Directory.Delete(_RootPath, recursive: true);
                    }
                }
                catch
                {
                }
            }

            private static async Task WaitForHealthAsync(HttpClient client)
            {
                Exception? lastError = null;

                for (int i = 0; i < 50; i++)
                {
                    try
                    {
                        using HttpResponseMessage response = await client.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }

                throw new TimeoutException("Timed out waiting for Armada test server health endpoint", lastError);
            }

            private static async Task<SeedData> SeedDatabaseAsync(ArmadaSettings settings, string rootPath)
            {
                using SqliteDatabaseDriver db = new SqliteDatabaseDriver(settings.Database.GetConnectionString(), CreateLogging());
                await db.InitializeAsync().ConfigureAwait(false);

                TenantMetadata tenantA = new TenantMetadata("Tenant A");
                TenantMetadata tenantB = new TenantMetadata("Tenant B");
                await db.Tenants.CreateAsync(tenantA).ConfigureAwait(false);
                await db.Tenants.CreateAsync(tenantB).ConfigureAwait(false);

                UserMaster tenantAdmin = new UserMaster(tenantA.Id, "tenant-admin@example.com", "password");
                tenantAdmin.IsTenantAdmin = true;
                await db.Users.CreateAsync(tenantAdmin).ConfigureAwait(false);

                UserMaster tenantUser = new UserMaster(tenantA.Id, "tenant-user@example.com", "password");
                await db.Users.CreateAsync(tenantUser).ConfigureAwait(false);

                UserMaster otherTenantUser = new UserMaster(tenantB.Id, "other-tenant-user@example.com", "password");
                await db.Users.CreateAsync(otherTenantUser).ConfigureAwait(false);

                Credential tenantAdminCredential = new Credential(tenantA.Id, tenantAdmin.Id);
                Credential tenantUserCredential = new Credential(tenantA.Id, tenantUser.Id);
                Credential otherTenantCredential = new Credential(tenantB.Id, otherTenantUser.Id);
                await db.Credentials.CreateAsync(tenantAdminCredential).ConfigureAwait(false);
                await db.Credentials.CreateAsync(tenantUserCredential).ConfigureAwait(false);
                await db.Credentials.CreateAsync(otherTenantCredential).ConfigureAwait(false);

                Fleet fleetTenantAdmin = new Fleet("fleet-tenant-admin")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantAdmin.Id
                };
                Fleet fleetTenantUser = new Fleet("fleet-tenant-user")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantUser.Id
                };
                Fleet fleetOtherTenant = new Fleet("fleet-other-tenant")
                {
                    TenantId = tenantB.Id,
                    UserId = otherTenantUser.Id
                };
                await db.Fleets.CreateAsync(fleetTenantAdmin).ConfigureAwait(false);
                await db.Fleets.CreateAsync(fleetTenantUser).ConfigureAwait(false);
                await db.Fleets.CreateAsync(fleetOtherTenant).ConfigureAwait(false);

                Vessel vesselTenantAdmin = new Vessel("vessel-tenant-admin", "https://example.com/tenant-admin.git")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantAdmin.Id,
                    FleetId = fleetTenantAdmin.Id
                };
                Vessel vesselTenantUser = new Vessel("vessel-tenant-user", "https://example.com/tenant-user.git")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantUser.Id,
                    FleetId = fleetTenantUser.Id
                };
                Vessel vesselOtherTenant = new Vessel("vessel-other-tenant", "https://example.com/other-tenant.git")
                {
                    TenantId = tenantB.Id,
                    UserId = otherTenantUser.Id,
                    FleetId = fleetOtherTenant.Id
                };
                await db.Vessels.CreateAsync(vesselTenantAdmin).ConfigureAwait(false);
                await db.Vessels.CreateAsync(vesselTenantUser).ConfigureAwait(false);
                await db.Vessels.CreateAsync(vesselOtherTenant).ConfigureAwait(false);

                Captain captainTenantAdmin = new Captain("captain-tenant-admin")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantAdmin.Id
                };
                Captain captainTenantUser = new Captain("captain-tenant-user")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantUser.Id
                };
                Captain captainOtherTenant = new Captain("captain-other-tenant")
                {
                    TenantId = tenantB.Id,
                    UserId = otherTenantUser.Id
                };
                await db.Captains.CreateAsync(captainTenantAdmin).ConfigureAwait(false);
                await db.Captains.CreateAsync(captainTenantUser).ConfigureAwait(false);
                await db.Captains.CreateAsync(captainOtherTenant).ConfigureAwait(false);

                Voyage voyageTenantAdmin = new Voyage("voyage-tenant-admin")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantAdmin.Id
                };
                Voyage voyageTenantUser = new Voyage("voyage-tenant-user")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantUser.Id
                };
                Voyage voyageOtherTenant = new Voyage("voyage-other-tenant")
                {
                    TenantId = tenantB.Id,
                    UserId = otherTenantUser.Id
                };
                await db.Voyages.CreateAsync(voyageTenantAdmin).ConfigureAwait(false);
                await db.Voyages.CreateAsync(voyageTenantUser).ConfigureAwait(false);
                await db.Voyages.CreateAsync(voyageOtherTenant).ConfigureAwait(false);

                Dock dockTenantAdmin = new Dock(vesselTenantAdmin.Id)
                {
                    TenantId = tenantA.Id,
                    UserId = tenantAdmin.Id,
                    CaptainId = captainTenantAdmin.Id,
                    WorktreePath = Path.Combine(rootPath, "dock-tenant-admin"),
                    BranchName = "tenant-admin"
                };
                Dock dockTenantUser = new Dock(vesselTenantUser.Id)
                {
                    TenantId = tenantA.Id,
                    UserId = tenantUser.Id,
                    CaptainId = captainTenantUser.Id,
                    WorktreePath = Path.Combine(rootPath, "dock-tenant-user"),
                    BranchName = "tenant-user"
                };
                Dock dockOtherTenant = new Dock(vesselOtherTenant.Id)
                {
                    TenantId = tenantB.Id,
                    UserId = otherTenantUser.Id,
                    CaptainId = captainOtherTenant.Id,
                    WorktreePath = Path.Combine(rootPath, "dock-other-tenant"),
                    BranchName = "other-tenant"
                };
                await db.Docks.CreateAsync(dockTenantAdmin).ConfigureAwait(false);
                await db.Docks.CreateAsync(dockTenantUser).ConfigureAwait(false);
                await db.Docks.CreateAsync(dockOtherTenant).ConfigureAwait(false);

                Mission missionTenantAdmin = new Mission("mission-tenant-admin")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantAdmin.Id,
                    VesselId = vesselTenantAdmin.Id,
                    VoyageId = voyageTenantAdmin.Id
                };
                Mission missionTenantUser = new Mission("mission-tenant-user")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantUser.Id,
                    VesselId = vesselTenantUser.Id,
                    VoyageId = voyageTenantUser.Id
                };
                Mission missionOtherTenant = new Mission("mission-other-tenant")
                {
                    TenantId = tenantB.Id,
                    UserId = otherTenantUser.Id,
                    VesselId = vesselOtherTenant.Id,
                    VoyageId = voyageOtherTenant.Id
                };
                await db.Missions.CreateAsync(missionTenantAdmin).ConfigureAwait(false);
                await db.Missions.CreateAsync(missionTenantUser).ConfigureAwait(false);
                await db.Missions.CreateAsync(missionOtherTenant).ConfigureAwait(false);

                ArmadaEvent eventTenantAdmin = new ArmadaEvent("mission.status_changed", "tenant-admin event")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantAdmin.Id,
                    CaptainId = captainTenantAdmin.Id,
                    MissionId = missionTenantAdmin.Id,
                    VesselId = vesselTenantAdmin.Id,
                    VoyageId = voyageTenantAdmin.Id
                };
                ArmadaEvent eventTenantUser = new ArmadaEvent("mission.status_changed", "tenant-user event")
                {
                    TenantId = tenantA.Id,
                    UserId = tenantUser.Id,
                    CaptainId = captainTenantUser.Id,
                    MissionId = missionTenantUser.Id,
                    VesselId = vesselTenantUser.Id,
                    VoyageId = voyageTenantUser.Id
                };
                ArmadaEvent eventOtherTenant = new ArmadaEvent("mission.status_changed", "other-tenant event")
                {
                    TenantId = tenantB.Id,
                    UserId = otherTenantUser.Id,
                    CaptainId = captainOtherTenant.Id,
                    MissionId = missionOtherTenant.Id,
                    VesselId = vesselOtherTenant.Id,
                    VoyageId = voyageOtherTenant.Id
                };
                await db.Events.CreateAsync(eventTenantAdmin).ConfigureAwait(false);
                await db.Events.CreateAsync(eventTenantUser).ConfigureAwait(false);
                await db.Events.CreateAsync(eventOtherTenant).ConfigureAwait(false);

                return new SeedData
                {
                    Captains = new EntityIds(captainTenantAdmin.Id, captainTenantUser.Id, captainOtherTenant.Id),
                    Missions = new EntityIds(missionTenantAdmin.Id, missionTenantUser.Id, missionOtherTenant.Id),
                    Events = new EntityIds(eventTenantAdmin.Id, eventTenantUser.Id, eventOtherTenant.Id),
                    Vessels = new EntityIds(vesselTenantAdmin.Id, vesselTenantUser.Id, vesselOtherTenant.Id),
                    Voyages = new EntityIds(voyageTenantAdmin.Id, voyageTenantUser.Id, voyageOtherTenant.Id),
                    Docks = new EntityIds(dockTenantAdmin.Id, dockTenantUser.Id, dockOtherTenant.Id),
                    EventMissionIds = new EntityIds(missionTenantAdmin.Id, missionTenantUser.Id, missionOtherTenant.Id),
                    AdminAuth = new AuthHeader { ApiKey = settings.ApiKey },
                    TenantAdminAuth = new AuthHeader { BearerToken = tenantAdminCredential.BearerToken },
                    UserAuth = new AuthHeader { BearerToken = tenantUserCredential.BearerToken }
                };
            }
        }
    }
}
