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
    using Armada.Core.Storage;
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
                ArmadaStatus? status = await c.Status.GetAsync();
                AssertNotNull(status, "status");
                HealthResult? health = await c.Status.GetHealthAsync();
                AssertEqual("healthy", health?.Status, "health");
                AssertTrue((health?.Ports?.Admiral ?? 0) > 0, "ports");
                List<DoctorCheck>? doctor = await c.Status.GetDoctorAsync();
                AssertTrue(doctor != null && doctor.Count > 0, "doctor checks");
                SettingsData? settings = await c.Server.GetSettingsAsync();
                AssertTrue((settings?.AdmiralPort ?? 0) > 0, "settings");
                AssertNotNull(settings?.Import, "import settings typed");
            }));

            cases.Add(Case("storage", "Cached storage and asynchronous refresh", async c =>
            {
                StorageSnapshot? snapshot = await c.Server.GetStorageAsync().ConfigureAwait(false);
                AssertNotNull(snapshot);
                AssertTrue(snapshot!.IntervalSeconds >= 30);
                StorageSnapshot? requested = await c.Server.RefreshStorageAsync(new StorageRefreshRequest()).ConfigureAwait(false);
                AssertNotNull(requested);
                EnumerationResult<StorageHostReport>? hosts = await c.Server.ListStorageHostsAsync(new EnumerationQuery { PageSize = 1 }).ConfigureAwait(false);
                AssertNotNull(hosts, "host page");
                AssertTrue(hosts!.Objects.Count <= 1, "page of one");
                List<StorageHostReport> allHosts = await c.Server.ListAllStorageHostsAsync().ConfigureAwait(false);
                AssertEqual((int)hosts.TotalRecords, allHosts.Count, "every host across pages");
            }));

            cases.Add(Case("auth_flows", "Whoami, tenant lookup, authenticate, and session token", async c =>
            {
                WhoAmIResult? me = await c.Auth.WhoamiAsync();
                AssertNotNull(me?.User, "whoami with API key");
                TenantLookupResult? tenants = await c.Auth.LookupTenantsAsync("admin@armada");
                AssertTrue(tenants != null && tenants.Tenants.Count >= 1, "lookup");
                AssertTrue(tenants!.Tenants.Any(t => t.Id == "default"), "default tenant listed");
                AuthenticateRequest req = new AuthenticateRequest { Email = "admin@armada", Password = "password", TenantId = "default" };
                using (ArmadaClient session = new ArmadaClient(c.BaseUrl))
                {
                    AuthenticateResult? auth = await session.Auth.AuthenticateAsync(req);
                    AssertTrue(auth != null && auth.Success && !String.IsNullOrEmpty(auth.Token), "authenticated");
                    session.SetToken(auth!.Token);
                    WhoAmIResult? viaToken = await session.Auth.WhoamiAsync();
                    AssertEqual("admin@armada", viaToken?.User?.Email, "session token works as X-Token");
                }
            }));

            cases.Add(Case("admin_lists", "Tenants, users, and credentials", async c =>
            {
                AssertTrue(((await c.Tenants.ListAsync())?.Objects.Count ?? 0) >= 1, "tenants");
                AssertTrue(((await c.Users.ListAsync())?.Objects.Count ?? 0) >= 1, "users");
                AssertNotNull(await c.Credentials.ListAsync(), "credentials");
            }));

            cases.Add(Case("fleet_crud", "Fleet create, get, update, list, batch delete", async c =>
            {
                Fleet created = (await c.Fleets.CreateAsync(new Fleet { Name = "client-contract-" + Guid.NewGuid().ToString("N").Substring(0, 6) }))!;
                AssertStartsWith("flt_", created.Id, "id");
                ArmadaRawJson? rawGet = await c.Entities.GetAsync("fleets", created.Id);
                AssertNotNull(rawGet, "generic entity lookup");
                FleetDetail rawDetail = JsonHelper.Deserialize<FleetDetail>(rawGet!.Json);
                AssertEqual(created.Id, rawDetail.Fleet?.Id, "generic entity lookup id");
                AssertEqual(created.Name, rawDetail.Fleet?.Name, "generic entity lookup name");
                FleetDetail? detail = await c.Fleets.GetAsync(created.Id);
                AssertEqual(created.Id, (await c.Fleets.GetDetailAsync(created.Id))?.Fleet?.Id, "fleet detail");
                Fleet? fetched = detail?.Fleet;
                AssertEqual(created.Name, fetched?.Name, "get");
                AssertEqual(0L, detail?.VesselCount, "vessel count (vessels are not embedded)");
                AssertFalse(rawGet.Json.Contains("\"vessels\"", StringComparison.OrdinalIgnoreCase), "no embedded vessel list");
                fetched!.Description = "updated";
                Fleet? updated = await c.Fleets.UpdateAsync(created.Id, fetched);
                AssertEqual("updated", updated?.Description, "update");
                AssertEqual(created.Name, updated?.Name, "name kept on update");
                EnumerationResult<Fleet>? page = await c.Fleets.ListAsync(new ArmadaPageQuery(1, 5));
                AssertTrue(page != null && page.Objects.Count <= 5, "paged list");
                BatchDeleteResult? deleted = await c.Fleets.DeleteBatchAsync(new List<string> { created.Id });
                AssertEqual(1, deleted?.Deleted, "batch delete");
            }));

            cases.Add(Case("vessel_and_errors", "Vessel create, readiness, delete; 404 maps to ArmadaApiException", async c =>
            {
                Fleet fleet = (await c.Fleets.CreateAsync(new Fleet { Name = "cc-vessels-" + Guid.NewGuid().ToString("N").Substring(0, 6) }))!;
                Vessel vessel = (await c.Vessels.CreateAsync(new Vessel { Name = "cc-vessel", FleetId = fleet.Id, RepoUrl = "https://github.com/test/client-contract" }))!;
                AssertStartsWith("vsl_", vessel.Id, "vessel id");
                AssertNotNull(await c.Vessels.GetReadinessAsync(vessel.Id), "readiness");
                await c.Vessels.DeleteAsync(vessel.Id);
                await c.Fleets.DeleteAsync(fleet.Id);
                try
                {
                    await c.Missions.GetAsync("msn_does_not_exist");
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
                AssertNotNull(await c.Captains.ListAsync(), "captains");
                AssertNotNull(await c.Missions.ListSummariesAsync(new ArmadaPageQuery(1, 10)), "mission summaries");
                AssertNotNull(await c.Missions.ListAsync(), "missions");
                AssertNotNull(await c.Voyages.ListAsync(), "voyages");
                AssertNotNull(await c.MergeQueue.ListAsync(), "merge queue");
                AssertNotNull(await c.Docks.ListAsync(), "docks");
                AssertNotNull(await c.PlanningSessions.ListAsync(), "planning");
                AssertNotNull(await c.Events.ListAsync(new ArmadaPageQuery(1, 10)), "events");
                AssertNotNull(await c.Signals.ListAsync(), "signals");
                AssertNotNull(await c.Inbox.GetAsync(), "inbox");
                AssertNotNull(await c.Jobs.ListAsync(), "jobs");
            }));

            cases.Add(Case("backlog_crud", "Backlog item create, get, list, delete", async c =>
            {
                ObjectiveUpsertRequest req = new ObjectiveUpsertRequest();
                req.Title = "Client contract backlog item";
                Objective created = (await c.Objectives.CreateBacklogItemAsync(req))!;
                AssertEqual("Client contract backlog item", (await c.Objectives.GetBacklogItemAsync(created.Id))?.Title, "get");
                AssertNotNull(await c.Objectives.ListBacklogAsync(new ObjectiveQuery { Search = "Client contract" }), "list with a search filter");
                AssertNotNull(await c.Objectives.EnumerateBacklogAsync(new ObjectiveQuery { PageSize = 5 }), "enumerate");
                await c.Objectives.DeleteBacklogItemAsync(created.Id);
            }));

            cases.Add(Case("delivery_and_configuration", "Delivery and configuration lists", async c =>
            {
                AssertNotNull(await c.Environments.ListAsync(), "environments");
                AssertNotNull(await c.Deployments.ListAsync(), "deployments");
                AssertNotNull(await c.Releases.ListAsync(), "releases");
                AssertNotNull(await c.Incidents.ListAsync(), "incidents");
                AssertNotNull(await c.Runbooks.ListAsync(), "runbooks");
                AssertNotNull(await c.CheckRuns.ListAsync(), "check runs");
                AssertNotNull(await c.WorkflowProfiles.ListAsync(), "workflow profiles");
                AssertNotNull(await c.ProjectProfiles.ListAsync(), "project profiles");
                AssertNotNull(await c.Skills.ListAsync(), "skills");
                AssertTrue(((await c.Personas.ListAsync())?.Objects.Count ?? 0) > 0, "built-in personas");
                AssertTrue(((await c.Pipelines.ListAsync())?.Objects.Count ?? 0) > 0, "built-in pipelines");
                AssertTrue(((await c.PromptTemplates.ListAsync())?.Objects.Count ?? 0) > 0, "prompt templates");
                AssertNotNull(await c.Harbors.ListAsync(), "harbors");
                AssertNotNull(await c.ModelEndpoints.ListAsync(), "model endpoints");
                EnumerationResult<Harbor>? harborPage = await c.Harbors.EnumerateAsync(new EnumerationQuery { PageSize = 1 });
                AssertNotNull(harborPage, "harbor page");
                AssertTrue(harborPage!.Objects.Count <= 1, "harbor page bounded");
                EnumerationResult<ModelEndpoint>? endpointPage = await c.ModelEndpoints.EnumerateAsync(new EnumerationQuery { PageSize = 1 });
                AssertNotNull(endpointPage, "endpoint page");
                AssertTrue(endpointPage!.Objects.Count <= 1, "endpoint page bounded");
                AssertNotNull(await c.Memories.ListAsync(), "memories");
            }));

            cases.Add(Case("playbook_crud", "Playbook create, update, delete", async c =>
            {
                Playbook created = (await c.Playbooks.CreateAsync(new Playbook { FileName = "client-contract-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md", Content = "# Steps" }))!;
                created.Content = "# Steps\n\n1. Test";
                AssertTrue(((await c.Playbooks.UpdateAsync(created.Id, created))?.Content ?? "").Contains("1. Test"), "updated");
                await c.Playbooks.DeleteAsync(created.Id);
            }));

            cases.Add(Case("ask_threads", "Ask thread create, enumerate, update (pin, clear captain), messages, delete, bulk delete", async c =>
            {
                AskThread thread = (await c.Ask.CreateThreadAsync(new AskThreadCreateRequest { Title = "Client contract" }))!;
                EnumerationResult<AskThread>? threads = await c.Ask.EnumerateThreadsAsync(new AskThreadEnumerateQuery { Search = "Client contract" });
                AssertTrue(threads != null && threads.Objects.Any(t => t.Id == thread.Id), "enumerate with search");
                AskThreadUpdateRequest update = new AskThreadUpdateRequest { Pinned = true, CaptainIdSpecified = true };
                AskThread? pinned = await c.Ask.UpdateThreadAsync(thread.Id, update);
                AssertTrue(pinned?.Pinned == true, "pinned");
                AskThread? located = await c.Ask.UpdateThreadAsync(thread.Id, new AskThreadUpdateRequest { WorkingDirectory = "~/client-contract" });
                AssertEqual("~/client-contract", located?.WorkingDirectory, "working directory sent");
                AskThread? unlocated = await c.Ask.UpdateThreadAsync(thread.Id, new AskThreadUpdateRequest { WorkingDirectory = "" });
                AssertNull(unlocated?.WorkingDirectory, "empty working directory clears it");
                AssertNotNull(await c.Ask.GetThreadAsync(thread.Id), "detail");
                AssertNotNull(await c.Ask.EnumerateMessagesAsync(thread.Id), "messages");
                List<AskQuickAction> actions = await c.Ask.GetQuickActionsAsync();
                AssertTrue(actions.Count > 0, "quick actions");
                await c.Ask.DeleteThreadAsync(thread.Id);

                AskThread bulk = (await c.Ask.CreateThreadAsync(new AskThreadCreateRequest { Title = "Client contract bulk" }))!;
                AskThreadDeleteMultipleResult? deleted = await c.Ask.DeleteThreadsAsync(new List<string> { bulk.Id, "ath_missing" });
                AssertTrue(deleted != null && deleted.DeletedIds.SequenceEqual(new[] { bulk.Id }), "bulk delete names the deleted thread");
                AssertTrue(deleted!.Skipped.Any(s => s.Id == "ath_missing"), "bulk delete skips an unknown id");
            }));

            cases.Add(Case("activity_and_health", "History, request history (with our request id), token usage, vessel health, fleet actions, import", async c =>
            {
                AssertNotNull(await c.History.EnumerateTimelineAsync(new HistoricalTimelineQuery { PageSize = 5 }), "history");
                EnumerationResult<RequestHistoryEntry>? requests = await c.RequestHistory.ListAsync(new RequestHistoryQuery { PageSize = 10 });
                AssertNotNull(requests, "request history");
                AssertNotNull(await c.RequestHistory.GetSummaryAsync(), "summary");
                AssertNotNull(await c.TokenUsage.GetSummaryAsync(), "token usage");
                AssertNotNull(await c.Missions.GetHistoryAsync(new MissionHistoryFilter { FromUtc = DateTime.UtcNow.AddDays(-1), ToUtc = DateTime.UtcNow, BucketMinutes = 60 }), "mission history");
                AssertNotNull(await c.VesselHealth.GetSummaryAsync(), "health summary");
                AssertNotNull(await c.VesselHealth.EnumerateAsync(new VesselHealthEnumerateRequest()), "health enumerate");
                AssertNotNull(await c.FleetActions.EnumerateAsync(), "fleet actions");
                AssertNotNull(await c.FleetActions.EnumerateRunsAsync(), "fleet action runs");
                AssertNotNull(await c.VesselImport.BrowseAsync(), "import browse roots");
                VesselBrowseResult? allRoots = await c.VesselImport.BrowseAllAsync();
                AssertEqual((int)allRoots!.TotalRecords, allRoots.Entries.Count, "every allowed root across pages");
                AssertNotNull(await c.VesselImport.EnumerateBatchesAsync(), "import batches");
                AssertNotNull(await c.VesselImport.GetFleetCategorizationDefaultPromptAsync(), "categorization prompt");
            }));

            cases.Add(Case("paging_helper_live", "ReadAllAsync pages through a live list", async c =>
            {
                for (int i = 0; i < 3; i++) await c.Fleets.CreateAsync(new Fleet { Name = "cc-page-" + i + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) });
                List<Fleet> all = await ArmadaPaging.ReadAllAsync<Fleet>((page, ct) => c.Fleets.ListAsync(new ArmadaPageQuery(page, 2), ct));
                AssertTrue(all.Count >= 3, "read across pages");
                AssertEqual(all.Count, all.Select(f => f.Id).Distinct().Count(), "no duplicates");
            }));

            cases.Add(Case("i18n_catalog", "The shared i18n catalog is served and parsed", async c =>
            {
                I18nCatalog? catalog = await c.Support.GetI18nCatalogAsync();
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
