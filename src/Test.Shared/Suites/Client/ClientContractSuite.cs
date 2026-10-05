namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract tests against a live in-process server (<see cref="E2EServerFixture"/>): a representative
    /// subset of calls per API area (auth, tenants, fleets, vessels, captains, missions, voyages, backlog, delivery,
    /// configuration, Ask, activity, health, fleet actions, import, settings), error mapping, and the WebSocket.
    /// </summary>
    public sealed class ClientContractSuite : IArmadaTestSuite
    {
        private const string Suite = "Client.Contract";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("status_and_health", "Status, health, doctor, settings", async c =>
            {
                ArmadaStatus? status = await c.GetStatusAsync();
                AssertNotNull(status, "status");
                HealthResult? health = await c.GetHealthAsync();
                AssertEqual("healthy", health?.Status, "health");
                AssertTrue((health?.Ports?.Admiral ?? 0) > 0, "ports");
                List<DoctorCheck>? doctor = await c.GetDoctorAsync();
                AssertTrue(doctor != null && doctor.Count > 0, "doctor checks");
                SettingsData? settings = await c.GetSettingsAsync();
                AssertTrue((settings?.AdmiralPort ?? 0) > 0, "settings");
                AssertNotNull(settings?.Import, "import settings typed");
            }));

            cases.Add(Case("auth_flows", "Whoami, tenant lookup, authenticate, and session token", async c =>
            {
                WhoAmIResult? me = await c.WhoamiAsync();
                AssertNotNull(me?.User, "whoami with API key");
                TenantLookupResult? tenants = await c.LookupTenantsAsync("admin@armada");
                AssertTrue(tenants != null && tenants.Tenants.Count >= 1, "lookup");
                AssertTrue(tenants!.Tenants.Any(t => t.Id == "default"), "default tenant listed");
                AuthenticateRequest req = new AuthenticateRequest { Email = "admin@armada", Password = "password", TenantId = "default" };
                using (ArmadaClient session = new ArmadaClient(c.BaseUrl))
                {
                    AuthenticateResult? auth = await session.AuthenticateAsync(req);
                    AssertTrue(auth != null && auth.Success && !String.IsNullOrEmpty(auth.Token), "authenticated");
                    session.SetToken(auth!.Token);
                    WhoAmIResult? viaToken = await session.WhoamiAsync();
                    AssertEqual("admin@armada", viaToken?.User?.Email, "session token works as X-Token");
                }
            }));

            cases.Add(Case("admin_lists", "Tenants, users, and credentials", async c =>
            {
                AssertTrue(((await c.ListTenantsAsync())?.Objects.Count ?? 0) >= 1, "tenants");
                AssertTrue(((await c.ListUsersAsync())?.Objects.Count ?? 0) >= 1, "users");
                AssertNotNull(await c.ListCredentialsAsync(), "credentials");
            }));

            cases.Add(Case("fleet_crud", "Fleet create, get, update, list, batch delete", async c =>
            {
                Fleet created = (await c.CreateFleetAsync(new Fleet { Name = "client-contract-" + Guid.NewGuid().ToString("N").Substring(0, 6) }))!;
                AssertStartsWith("flt_", created.Id, "id");
                ArmadaRawJson? rawGet = await c.GetEntityAsync("fleets", created.Id);
                AssertNotNull(rawGet, "generic entity lookup");
                FleetDetail rawDetail = JsonHelper.Deserialize<FleetDetail>(rawGet!.Json);
                AssertEqual(created.Id, rawDetail.Fleet?.Id, "generic entity lookup id");
                AssertEqual(created.Name, rawDetail.Fleet?.Name, "generic entity lookup name");
                FleetDetail? detail = await c.GetFleetAsync(created.Id);
                Fleet? fetched = detail?.Fleet;
                AssertEqual(created.Name, fetched?.Name, "get");
                AssertNotNull(detail?.Vessels, "vessels included");
                fetched!.Description = "updated";
                Fleet? updated = await c.UpdateFleetAsync(created.Id, fetched);
                AssertEqual("updated", updated?.Description, "update");
                AssertEqual(created.Name, updated?.Name, "name kept on update");
                EnumerationResult<Fleet>? page = await c.ListFleetsAsync(new ArmadaPageQuery(1, 5));
                AssertTrue(page != null && page.Objects.Count <= 5, "paged list");
                BatchDeleteResult? deleted = await c.DeleteFleetsBatchAsync(new List<string> { created.Id });
                AssertEqual(1, deleted?.Deleted, "batch delete");
            }));

            cases.Add(Case("vessel_and_errors", "Vessel create, readiness, delete; 404 maps to ArmadaApiException", async c =>
            {
                Fleet fleet = (await c.CreateFleetAsync(new Fleet { Name = "cc-vessels-" + Guid.NewGuid().ToString("N").Substring(0, 6) }))!;
                Vessel vessel = (await c.CreateVesselAsync(new Vessel { Name = "cc-vessel", FleetId = fleet.Id, RepoUrl = "https://github.com/test/client-contract" }))!;
                AssertStartsWith("vsl_", vessel.Id, "vessel id");
                AssertNotNull(await c.GetVesselReadinessAsync(vessel.Id), "readiness");
                await c.DeleteVesselAsync(vessel.Id);
                await c.DeleteFleetAsync(fleet.Id);
                try
                {
                    await c.GetMissionAsync("msn_does_not_exist");
                    throw new AssertionException("expected 404");
                }
                catch (ArmadaApiException ex)
                {
                    AssertEqual(404, ex.StatusCode, "status");
                    AssertStartsWith("req_", ex.RequestId, "request id");
                }
            }));

            cases.Add(Case("work_lists", "Captains, missions, voyages, merge queue, docks, planning, events, signals", async c =>
            {
                AssertNotNull(await c.ListCaptainsAsync(), "captains");
                AssertNotNull(await c.ListMissionSummariesAsync(new ArmadaPageQuery(1, 10)), "mission summaries");
                AssertNotNull(await c.ListMissionsAsync(), "missions");
                AssertNotNull(await c.ListVoyagesAsync(), "voyages");
                AssertNotNull(await c.ListMergeQueueAsync(), "merge queue");
                AssertNotNull(await c.ListDocksAsync(), "docks");
                AssertNotNull(await c.ListPlanningSessionsAsync(), "planning");
                AssertNotNull(await c.ListEventsAsync(new ArmadaPageQuery(1, 10)), "events");
                AssertNotNull(await c.ListSignalsAsync(), "signals");
                AssertNotNull(await c.GetInboxAsync(), "inbox");
                AssertNotNull(await c.ListJobsAsync(), "jobs");
            }));

            cases.Add(Case("backlog_crud", "Backlog item create, get, list, delete", async c =>
            {
                ObjectiveUpsertRequest req = new ObjectiveUpsertRequest();
                req.Title = "Client contract backlog item";
                Objective created = (await c.CreateBacklogItemAsync(req))!;
                AssertEqual("Client contract backlog item", (await c.GetBacklogItemAsync(created.Id))?.Title, "get");
                AssertNotNull(await c.ListBacklogAsync(new ObjectiveQuery { Search = "Client contract" }), "list with a search filter");
                AssertNotNull(await c.EnumerateBacklogAsync(new ObjectiveQuery { PageSize = 5 }), "enumerate");
                await c.DeleteBacklogItemAsync(created.Id);
            }));

            cases.Add(Case("delivery_and_configuration", "Delivery and configuration lists", async c =>
            {
                AssertNotNull(await c.ListEnvironmentsAsync(), "environments");
                AssertNotNull(await c.ListDeploymentsAsync(), "deployments");
                AssertNotNull(await c.ListReleasesAsync(), "releases");
                AssertNotNull(await c.ListIncidentsAsync(), "incidents");
                AssertNotNull(await c.ListRunbooksAsync(), "runbooks");
                AssertNotNull(await c.ListCheckRunsAsync(), "check runs");
                AssertNotNull(await c.ListWorkflowProfilesAsync(), "workflow profiles");
                AssertNotNull(await c.ListProjectProfilesAsync(), "project profiles");
                AssertNotNull(await c.ListSkillsAsync(), "skills");
                AssertTrue(((await c.ListPersonasAsync())?.Objects.Count ?? 0) > 0, "built-in personas");
                AssertTrue(((await c.ListPipelinesAsync())?.Objects.Count ?? 0) > 0, "built-in pipelines");
                AssertTrue(((await c.ListPromptTemplatesAsync())?.Objects.Count ?? 0) > 0, "prompt templates");
                AssertNotNull(await c.ListHarborsAsync(), "harbors");
                AssertNotNull(await c.ListModelEndpointsAsync(), "model endpoints");
                AssertNotNull(await c.ListMemoriesAsync(), "memories");
            }));

            cases.Add(Case("playbook_crud", "Playbook create, update, delete", async c =>
            {
                Playbook created = (await c.CreatePlaybookAsync(new Playbook { FileName = "client-contract-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md", Content = "# Steps" }))!;
                created.Content = "# Steps\n\n1. Test";
                AssertTrue(((await c.UpdatePlaybookAsync(created.Id, created))?.Content ?? "").Contains("1. Test"), "updated");
                await c.DeletePlaybookAsync(created.Id);
            }));

            cases.Add(Case("ask_threads", "Ask thread create, enumerate, update (pin, clear captain), messages, delete", async c =>
            {
                AskThread thread = (await c.CreateAskThreadAsync(new AskThreadCreateRequest { Title = "Client contract" }))!;
                EnumerationResult<AskThread>? threads = await c.EnumerateAskThreadsAsync(new AskThreadEnumerateQuery { Search = "Client contract" });
                AssertTrue(threads != null && threads.Objects.Any(t => t.Id == thread.Id), "enumerate with search");
                AskThreadUpdateRequest update = new AskThreadUpdateRequest { Pinned = true, CaptainIdSpecified = true };
                AskThread? pinned = await c.UpdateAskThreadAsync(thread.Id, update);
                AssertTrue(pinned?.Pinned == true, "pinned");
                AssertNotNull(await c.GetAskThreadAsync(thread.Id), "detail");
                AssertNotNull(await c.EnumerateAskMessagesAsync(thread.Id), "messages");
                List<AskQuickAction> actions = await c.GetAskQuickActionsAsync();
                AssertTrue(actions.Count > 0, "quick actions");
                await c.DeleteAskThreadAsync(thread.Id);
            }));

            cases.Add(Case("activity_and_health", "History, request history (with our request id), token usage, vessel health, fleet actions, import", async c =>
            {
                AssertNotNull(await c.EnumerateHistoryTimelineAsync(new HistoricalTimelineQuery { PageSize = 5 }), "history");
                EnumerationResult<RequestHistoryEntry>? requests = await c.ListRequestHistoryAsync(new RequestHistoryQuery { PageSize = 10 });
                AssertNotNull(requests, "request history");
                AssertNotNull(await c.GetRequestHistorySummaryAsync(), "summary");
                AssertNotNull(await c.GetTokenUsageAsync(), "token usage");
                AssertNotNull(await c.GetMissionHistoryAsync(new MissionHistoryFilter { FromUtc = DateTime.UtcNow.AddDays(-1), ToUtc = DateTime.UtcNow, BucketMinutes = 60 }), "mission history");
                AssertNotNull(await c.GetVesselHealthSummaryAsync(), "health summary");
                AssertNotNull(await c.EnumerateVesselHealthAsync(new VesselHealthEnumerateRequest()), "health enumerate");
                AssertNotNull(await c.EnumerateFleetActionsAsync(), "fleet actions");
                AssertNotNull(await c.EnumerateFleetActionRunsAsync(), "fleet action runs");
                AssertNotNull(await c.BrowseVesselImportAsync(), "import browse roots");
                AssertNotNull(await c.EnumerateVesselImportBatchesAsync(), "import batches");
                AssertNotNull(await c.GetFleetCategorizationDefaultPromptAsync(), "categorization prompt");
            }));

            cases.Add(Case("paging_helper_live", "ReadAllAsync pages through a live list", async c =>
            {
                for (int i = 0; i < 3; i++) await c.CreateFleetAsync(new Fleet { Name = "cc-page-" + i + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) });
                List<Fleet> all = await ArmadaPaging.ReadAllAsync<Fleet>((page, ct) => c.ListFleetsAsync(new ArmadaPageQuery(page, 2), ct));
                AssertTrue(all.Count >= 3, "read across pages");
                AssertEqual(all.Count, all.Select(f => f.Id).Distinct().Count(), "no duplicates");
            }));

            cases.Add(Case("i18n_catalog", "The shared i18n catalog is served and parsed", async c =>
            {
                I18nCatalog? catalog = await c.GetI18nCatalogAsync();
                AssertNotNull(catalog, "catalog");
                AssertTrue(catalog!.SupportedLocales.Any(l => l.Code == "ja"), "japanese");
                AssertTrue(catalog.Locales.ContainsKey("zh-Hans"), "chinese pack");
            }));

            cases.Add(TuiCaseAsync("socket_live", "ArmadaSocket connects with the token query, subscribes, and receives status.snapshot", async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                ArmadaSocketOptions options = new ArmadaSocketOptions(fx.BaseUrl, () => fx.ApiKey);
                using (ArmadaSocket socket = new ArmadaSocket(options))
                {
                    TaskCompletionSource<ArmadaSocketMessage> snapshot = new TaskCompletionSource<ArmadaSocketMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                    socket.MessageReceived += (s, m) => { if (m.Type == ArmadaEventTypes.StatusSnapshot) snapshot.TrySetResult(m); };
                    socket.Start();
                    Task done = await Task.WhenAny(snapshot.Task, Task.Delay(10000));
                    AssertTrue(done == snapshot.Task, "snapshot received");
                    AssertTrue(socket.IsConnected, "connected");
                    AssertNotNull(snapshot.Task.Result.GetTypedData() as ArmadaStatus, "typed status payload");
                    using (ArmadaClient c = Client(fx))
                    {
                        TaskCompletionSource<EntityChangedEvent?> changed = new TaskCompletionSource<EntityChangedEvent?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        socket.On<EntityChangedEvent>("*", (data, m) => { if (m.Type.EndsWith(".changed")) changed.TrySetResult(data); });
                        AssertTrue(socket.IsRunning, "running");
                    }

                    await socket.StopAsync();
                    AssertFalse(socket.IsConnected, "stopped");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract (live server)", cases: cases);
        }

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, Task> body)
        {
            return TuiCaseAsync(id, name, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                using (ArmadaClient client = Client(fx))
                {
                    await body(client);
                }
            });
        }

        private static ArmadaClient Client(E2EServerFixture fx)
        {
            ArmadaClientOptions options = new ArmadaClientOptions(fx.BaseUrl);
            options.ApiKey = fx.ApiKey;
            return new ArmadaClient(options);
        }

        private static TestCaseDescriptor TuiCaseAsync(string id, string name, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: Suite, caseId: id, displayName: name, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { TestTags.EndToEnd });
        }
    }
}
