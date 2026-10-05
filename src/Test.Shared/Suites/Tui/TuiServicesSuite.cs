namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Models;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui.Bodies;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// TUI services (preferences, credentials, event pump, refresh, status polling, external files) and client
    /// plumbing (query strings, error mapping, paging, socket backoff and reconnect) without a server.
    /// </summary>
    public sealed class TuiServicesSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Services";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "preferences_roundtrip_and_corrupt", "Preferences round-trip; a corrupt file falls back to defaults", () =>
            {
                string dir = Temp();
                try
                {
                    string file = Path.Combine(dir, "tui.json");
                    PreferencesService prefs = new PreferencesService(file);
                    prefs.Load();
                    prefs.UpsertProfile("work", "http://10.0.0.5:7890/");
                    prefs.Current.Theme = Armada.Tui.Theming.ThemeModeEnum.HighContrast;
                    prefs.Table("missions").PageSize = 50;
                    AssertTrue(prefs.Save(), "saved");
                    PreferencesService again = new PreferencesService(file);
                    again.Load();
                    AssertEqual("http://10.0.0.5:7890", again.FindProfile("WORK")!.Url, "profile normalized and case-insensitive");
                    AssertEqual("work", again.Current.ActiveProfile, "active");
                    AssertEqual(50, again.Table("missions").PageSize, "table prefs");
                    AssertFalse(File.ReadAllText(file).Contains("tok_"), "no secrets in preferences");
                    File.WriteAllText(file, "{ not json");
                    PreferencesService broken = new PreferencesService(file);
                    broken.Load();
                    AssertNotNull(broken.LastError, "error recorded");
                    AssertTrue(File.Exists(file + ".bak"), "backup kept");
                    AssertEqual(0, broken.Current.Profiles.Count, "defaults");
                }
                finally { Cleanup(dir); }
            }));

            cases.Add(TuiCase.Sync(Suite, "file_credentials_0600", "The file credential store keeps secrets with mode 0600", () =>
            {
                string dir = Temp();
                try
                {
                    string file = Path.Combine(dir, "sub", "creds.json");
                    FileCredentialStore store = new FileCredentialStore(file);
                    AssertTrue(store.SetAsync("profile:a", "tok_1").GetAwaiter().GetResult(), "set");
                    AssertEqual("tok_1", store.GetAsync("profile:a").GetAwaiter().GetResult(), "get");
                    if (!OperatingSystem.IsWindows())
                    {
                        UnixFileMode mode = File.GetUnixFileMode(file);
                        AssertEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode, "0600");
                    }

                    store.DeleteAsync("profile:a").GetAwaiter().GetResult();
                    AssertNull(store.GetAsync("profile:a").GetAwaiter().GetResult(), "deleted");
                    AssertEqual("file", CredentialStoreFactory.Create("file", file).Name, "factory honors file");
                    AssertTrue(CredentialStoreFactory.Create("keychain", file).Name.EndsWith("+file"), "keychain falls back to file");
                }
                finally { Cleanup(dir); }
            }));

            cases.Add(TuiCase.Sync(Suite, "fallback_store", "The fallback store uses the file when the keychain fails", () =>
            {
                string dir = Temp();
                try
                {
                    FailingStore primary = new FailingStore();
                    FileCredentialStore file = new FileCredentialStore(Path.Combine(dir, "c.json"));
                    FallbackCredentialStore store = new FallbackCredentialStore(primary, file);
                    AssertTrue(store.SetAsync("k", "v").GetAwaiter().GetResult(), "stored via fallback");
                    AssertEqual("v", store.GetAsync("k").GetAwaiter().GetResult(), "read via fallback");
                }
                finally { Cleanup(dir); }
            }));

            cases.Add(TuiCase.Sync(Suite, "event_pump_coalesces", "The event pump marshals through the dispatcher and coalesces bursts", () =>
            {
                QueueDispatcher dispatcher = new QueueDispatcher();
                EventPump pump = new EventPump(dispatcher);
                pump.CoalesceMs = 100;
                int each = 0;
                int coalesced = 0;
                pump.Subscribe("mission.", m => each++);
                pump.SubscribeCoalesced("*", () => coalesced++);
                for (int i = 0; i < 20; i++) pump.Inject(ArmadaSocketMessage.Parse("{\"type\":\"mission.changed\",\"data\":{\"id\":\"m" + i + "\"}}")!);
                AssertEqual(0, each, "nothing runs off the loop");
                dispatcher.Drain();
                AssertEqual(20, each, "every message delivered on drain");
                Thread.Sleep(300);
                dispatcher.Drain();
                AssertEqual(1, coalesced, "burst coalesced to one refresh");
                pump.Inject(ArmadaSocketMessage.Parse("{\"type\":\"voyage.changed\"}")!);
                Thread.Sleep(300);
                dispatcher.Drain();
                AssertEqual(20, each, "prefix filter");
                AssertEqual(2, coalesced, "next window fires again");
            }));

            cases.Add(TuiCase.Sync(Suite, "refresh_service", "Auto-refresh intervals, pause, and F5", () =>
            {
                string dir = Temp();
                try
                {
                    PreferencesService prefs = new PreferencesService(Path.Combine(dir, "p.json"));
                    ManualClock clock = new ManualClock();
                    bool paused = false;
                    RefreshService refresh = new RefreshService(prefs, new QueueDispatcher(), clock, () => paused);
                    int runs = 0;
                    refresh.Attach("MissionsScreen", () => runs++);
                    AssertEqual("auto 15s", refresh.StatusText, "default 15s");
                    clock.Advance(TimeSpan.FromSeconds(16));
                    refresh.Tick();
                    AssertEqual(1, runs, "due refresh");
                    paused = true;
                    clock.Advance(TimeSpan.FromSeconds(16));
                    refresh.Tick();
                    AssertEqual(1, runs, "paused while a modal is open");
                    AssertEqual("paused", refresh.StatusText, "paused text");
                    paused = false;
                    refresh.SetInterval(40);
                    AssertEqual(30, refresh.IntervalSeconds, "snapped to an allowed interval");
                    AssertEqual(30, prefs.Current.RefreshIntervals["MissionsScreen"], "persisted per screen");
                    refresh.SetInterval(0);
                    clock.Advance(TimeSpan.FromMinutes(10));
                    refresh.Tick();
                    AssertEqual(1, runs, "off");
                    AssertTrue(refresh.RefreshNow(), "f5");
                    AssertEqual(2, runs, "manual refresh");
                }
                finally { Cleanup(dir); }
            }));

            cases.Add(TuiCase.Async(Suite, "status_poller", "The status poller reads health, jobs, and the inbox", async () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/jobs", "{\"Objects\":[{\"Id\":\"job_1\",\"Name\":\"Import\",\"Status\":\"Running\"},{\"Id\":\"job_2\",\"Name\":\"Old\",\"Status\":\"Succeeded\"}],\"TotalRecords\":2}");
                ArmadaClient client = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:9"), stub);
                QueueDispatcher dispatcher = new QueueDispatcher();
                StatusPoller poller = new StatusPoller(() => client, dispatcher);
                await poller.PollAllAsync();
                dispatcher.Drain();
                AssertEqual("healthy", poller.Health?.Status, "health");
                AssertEqual(1, poller.ActiveJobs.Count, "running jobs only");
                AssertEqual(1, poller.Inbox.Count, "inbox");
            }));

            cases.Add(TuiCase.Sync(Suite, "external_files", "External service saves and loads files and expands ~", () =>
            {
                string dir = Temp();
                try
                {
                    ExternalService ext = new ExternalService(null);
                    string path = ext.SaveText(Path.Combine(dir, "a", "export.json"), "{}");
                    AssertEqual("{}", System.Text.Encoding.UTF8.GetString(ext.LoadFile(path)), "round trip");
                    AssertTrue(ExternalService.ExpandPath("~/x").StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "tilde");
                    string? opened = null;
                    ext.UrlOpener = u => { opened = u; return true; };
                    AssertTrue(ext.OpenUrl("https://github.com/jchristn/Armada"), "open");
                    AssertFalse(ext.OpenUrl("file:///etc/passwd"), "non-http refused");
                    AssertEqual("https://github.com/jchristn/Armada", opened, "opener called");
                }
                finally { Cleanup(dir); }
            }));

            cases.Add(TuiCase.Sync(Suite, "clipboard_osc52", "Copy uses OSC 52 when supported", () =>
            {
                RecordingTerminal terminal = new RecordingTerminal(true);
                ClipboardService clipboard = new ClipboardService(terminal, null, null, new LocalizationService(), () => Armada.Tui.Theming.ThemePalettes.Dark());
                AssertTrue(clipboard.Copy("msn_123", "ID"), "osc52");
                AssertTrue(terminal.Written.Contains("\u001b]52;"), "sequence written");
                AssertEqual("msn_123", clipboard.LastCopied, "last copied");
                FakeModalHost modals = new FakeModalHost();
                ClipboardService fallback = new ClipboardService(new RecordingTerminal(false), modals, null, new LocalizationService(), () => Armada.Tui.Theming.ThemePalettes.Dark());
                AssertFalse(fallback.Copy("x"), "fallback");
                AssertEqual(1, modals.Shown.Count, "manual copy dialog");
            }));

            cases.Add(TuiCase.Sync(Suite, "query_strings", "Query helpers match the dashboard's URL building", () =>
            {
                ArmadaPageQuery page = new ArmadaPageQuery(2, 25).With("status", "InProgress").With("empty", "");
                AssertEqual("?pageNumber=2&pageSize=25&status=InProgress", ArmadaQueryString.FromPage(page), "buildQuery");
                HistoricalTimelineQuery history = new HistoricalTimelineQuery();
                history.SourceTypes = new List<string> { "request", "event" };
                history.PageNumber = 1;
                string hq = ArmadaQueryString.FromObject(history);
                AssertStartsWith("?sourceType=request%2Cevent&pageNumber=1", hq, "sourceType first, then paging");
                AssertFalse(QueryString.Parse(hq).ContainsKey("postmortemOnly"), "false booleans skipped");
                RequestHistoryQuery req = new RequestHistoryQuery();
                req.Route = "/api/v1/missions";
                req.IsSuccess = false;
                string q = ArmadaQueryString.FromObject(req);
                AssertEqual("/api/v1/missions", QueryString.Get(q, "route"), "route value");
                AssertTrue(q.TrimStart('?').Split('&').Contains("route=/api/v1/missions"), "route keeps slashes (not percent-encoded): " + q);
                AssertEqual("false", QueryString.Get(q, "isSuccess"), "nullable false sent");
                AssertEqual("L3RtcD_CvA", ArmadaQueryString.Base64Url("/tmp?\u00bc"), "base64url without padding");
            }));

            cases.Add(TuiCase.Async(Suite, "error_mapping_and_401", "Errors map to ArmadaApiException with code and request id; 401 raises Unauthorized", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler();
                stub.Json("POST", "/api/v1/vessels/import/discover", "{\"Error\":\"BadRequest\",\"Message\":\"Path is outside the allowed roots\",\"Data\":{\"Code\":\"path_not_allowed\",\"Path\":\"/etc\"}}", System.Net.HttpStatusCode.BadRequest);
                stub.Json("GET", "/api/v1/whoami", "{\"Message\":\"Authentication required\"}", System.Net.HttpStatusCode.Unauthorized);
                ArmadaClient client = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:9"), stub);
                int unauthorized = 0;
                client.Unauthorized += (s, e) => unauthorized++;
                try
                {
                    await client.DiscoverVesselImportAsync(new VesselDiscoveryRequest());
                    throw new AssertionException("expected an exception");
                }
                catch (ArmadaApiException ex)
                {
                    AssertEqual(400, ex.StatusCode, "status");
                    AssertEqual("path_not_allowed", ex.Code, "code");
                    AssertEqual("Path is outside the allowed roots", ex.Message, "message");
                    AssertStartsWith("req_", ex.RequestId, "request id");
                }

                await AssertThrowsAsync<ArmadaApiException>(() => client.WhoamiAsync());
                AssertEqual(1, unauthorized, "unauthorized hook");
                ArmadaClient unreachable = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:1") { TimeoutMs = 2000 });
                try
                {
                    await unreachable.GetHealthAsync();
                    throw new AssertionException("expected transport error");
                }
                catch (ArmadaApiException ex)
                {
                    AssertTrue(ex.IsTransport, "transport");
                }
            }, TestTags.Negative));

            cases.Add(TuiCase.Async(Suite, "paging_helper", "ReadAllAsync walks pages until the last", async () =>
            {
                int calls = 0;
                List<int> all = await ArmadaPaging.ReadAllAsync<int>((page, ct) =>
                {
                    calls++;
                    EnumerationResult<int> r = new EnumerationResult<int>();
                    r.PageNumber = page;
                    r.PageSize = 10;
                    r.TotalPages = 3;
                    r.TotalRecords = 25;
                    r.Objects = Enumerable.Range((page - 1) * 10, page == 3 ? 5 : 10).ToList();
                    return Task.FromResult<EnumerationResult<int>?>(r);
                });
                AssertEqual(25, all.Count, "all rows");
                AssertEqual(3, calls, "three pages");
                PageWindow w = ArmadaPaging.Window(10, 25, 248);
                AssertEqual(226L, w.First, "first");
                AssertEqual(248L, w.Last, "last");
                AssertFalse(w.HasNext, "last page");
            }));

            cases.Add(TuiCase.Sync(Suite, "socket_backoff_and_url", "Socket backoff is 1-30 s with jitter; the URL carries the token", () =>
            {
                AssertEqual(800, ArmadaSocket.ReconnectDelayMs(0, 0.0), "1s minus 20%");
                AssertEqual(1200, ArmadaSocket.ReconnectDelayMs(0, 1.0), "1s plus 20%");
                AssertEqual(4000, ArmadaSocket.ReconnectDelayMs(2, 0.5), "doubling");
                AssertEqual(30000, ArmadaSocket.ReconnectDelayMs(10, 1.0), "capped");
                AssertEqual(30000, ArmadaSocket.ReconnectDelayMs(99, 0.5), "clamped attempt");
                AssertEqual("ws://127.0.0.1:7890/ws?token=a%2Bb", ArmadaSocket.BuildSocketUri("http://127.0.0.1:7890/", "a+b").ToString(), "ws url");
                AssertEqual("wss://h.example/ws", ArmadaSocket.BuildSocketUri("https://h.example", null).ToString(), "wss url");
                AssertEqual(28, ArmadaEventTypes.Parity.Count + 0, "parity event list");
                foreach (string type in ArmadaEventTypes.Parity) AssertNotNull(ArmadaEventTypes.PayloadTypeFor(type), type);
            }));

            cases.Add(TuiCase.Async(Suite, "socket_reconnects", "The socket subscribes, dispatches typed events, and reconnects with a counter", async () =>
            {
                int attempt = 0;
                ArmadaSocketOptions options = new ArmadaSocketOptions("http://127.0.0.1:7890", () => "tok_1");
                options.Delay = (ms, ct) => Task.CompletedTask;
                options.TransportFactory = () =>
                {
                    int n = Interlocked.Increment(ref attempt);
                    if (n == 2) return new FakeSocketTransport(true);
                    return new FakeSocketTransport(false, "{\"type\":\"ask.chunk\",\"data\":{\"threadId\":\"ath_1\",\"turnId\":\"t\",\"delta\":\"Hi\"}}");
                };
                using (ArmadaSocket socket = new ArmadaSocket(options))
                {
                    List<string> deltas = new List<string>();
                    int reconnected = 0;
                    socket.Reconnected += (s, e) => Interlocked.Increment(ref reconnected);
                    socket.On<AskDeltaEvent>(ArmadaEventTypes.AskChunk, (data, msg) => { lock (deltas) deltas.Add(data?.Delta ?? ""); });
                    socket.Start();
                    AssertTrue(SpinWait.SpinUntil(() => socket.ReconnectCount >= 2, 5000), "reconnected twice");
                    await socket.StopAsync();
                    AssertTrue(reconnected >= 2, "event");
                    lock (deltas) AssertTrue(deltas.Count >= 2 && deltas.All(d => d == "Hi"), "typed payloads");
                    AssertTrue(FakeSocketTransport.Sent.Any(t => JsonHelper.Deserialize<SocketRouteFrame>(t).Route == "subscribe"), "subscribe sent");
                    AssertTrue(FakeSocketTransport.Connected.Any(u => QueryString.Get(u.Query, "token") == "tok_1"), "token in url");
                    AssertFalse(socket.IsConnected, "stopped");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI services and client plumbing", cases: cases);
        }

        private static string Temp()
        {
            string dir = Path.Combine(Path.GetTempPath(), "armada-tui-svc-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }
}
