namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Metrics;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// How Harbor asks the Admiral for its own charts (<see cref="HarborMetricsFeed"/> and
    /// <see cref="HarborMetricsRefresher"/>) against a stub Admiral: a loaded response, range normalizing, 401, 403, an
    /// unknown Harbor, an Admiral too old to have the endpoint, offline, a server error, no address, no Harbor ID, and
    /// cancellation; and the refresher's timer (driven by a gate, never a sleep), range switches that drop a stale answer,
    /// and stopping.
    /// </summary>
    public sealed class HarborMetricsFeedSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Client.HarborMetricsFeed";
        private const string MetricsPath = "/api/v1/harbors/hbr_1/metrics";
        private const string HarborPath = "/api/v1/harbors/hbr_1";
        private static readonly TimeSpan _Wait = TimeSpan.FromSeconds(10);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("loaded", "A current Admiral answers with the metrics for the Harbor's own ID and range", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler().Json("GET", MetricsPath, HarborMetricsFixture.Json("hbr_1", "7d"));
                HarborMetricsLoadResult result = await Feed(stub).LoadAsync("7D").ConfigureAwait(false);
                AssertEqual(HarborMetricsLoadStateEnum.Loaded, result.State);
                AssertTrue(result.IsLoaded, "loaded");
                AssertEqual("7d", result.Range, "range normalized");
                AssertEqual("hbr_1", result.HarborId);
                AssertEqual(56, result.Metrics!.BucketCount, "7d buckets");
                AssertEqual("", result.Message, "no message");
                AssertEqual("7d", stub.Last("GET", MetricsPath).QueryValue("range"), "range sent");

                HarborMetricsLoadResult fallback = await Feed(stub).LoadAsync("bogus").ConfigureAwait(false);
                AssertEqual("24h", fallback.Range, "an unknown range asks for the default");
                AssertEqual("24h", stub.Last("GET", MetricsPath).QueryValue("range"));
            }));

            cases.Add(Case("unauthorized_and_forbidden", "401 and 403 say what to do about the credential, local and remote", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler().Json("GET", MetricsPath, "{\"Error\":\"Unauthorized\",\"Message\":\"Authentication required\"}", HttpStatusCode.Unauthorized);
                HarborMetricsLoadResult remote = await Feed(stub).LoadAsync("24h").ConfigureAwait(false);
                AssertEqual(HarborMetricsLoadStateEnum.Unauthorized, remote.State);
                AssertEqual(401, remote.StatusCode);
                AssertEqual(HarborMetricsFeed.MessageFor(HarborMetricsLoadStateEnum.Unauthorized, "hbr_1", 401, false), remote.Message);
                HarborMetricsLoadResult local = await Feed(stub, local: true).LoadAsync("24h").ConfigureAwait(false);
                AssertEqual(HarborMetricsFeed.MessageFor(HarborMetricsLoadStateEnum.Unauthorized, "hbr_1", 401, true), local.Message);
                AssertNotEqual(remote.Message, local.Message, "a local Admiral gets the settings.json advice");
                AssertEqual(0, stub.CountFor("GET", HarborPath), "no probe for 401");

                stub.Json("GET", MetricsPath, "{\"Error\":\"Forbidden\",\"Message\":\"Forbidden\"}", HttpStatusCode.Forbidden);
                HarborMetricsLoadResult forbidden = await Feed(stub).LoadAsync("24h").ConfigureAwait(false);
                AssertEqual(HarborMetricsLoadStateEnum.Forbidden, forbidden.State);
                AssertEqual(403, forbidden.StatusCode);
            }));

            cases.Add(Case("older_admiral_and_unknown_harbor", "A 404 from the metrics route is an older Admiral when the Harbor exists, else an unknown Harbor", async () =>
            {
                StubHttpHandler older = new StubHttpHandler()
                    .Json("GET", HarborPath, "{\"Id\":\"hbr_1\",\"Name\":\"build-box\"}");
                HarborMetricsLoadResult outdated = await Feed(older).LoadAsync("24h").ConfigureAwait(false);
                AssertEqual(HarborMetricsLoadStateEnum.AdmiralOutdated, outdated.State, "the stub has no metrics route, like an older Admiral");
                AssertEqual(404, outdated.StatusCode);
                AssertEqual(1, older.CountFor("GET", HarborPath), "probed the Harbor once");
                AssertEqual(HarborMetricsFeed.MessageFor(HarborMetricsLoadStateEnum.AdmiralOutdated, "hbr_1", 404, false), outdated.Message);
                AssertTrue(outdated.Message.StartsWith("The Admiral needs updating", StringComparison.Ordinal), "says the Admiral needs updating");

                StubHttpHandler unknown = new StubHttpHandler()
                    .Json("GET", MetricsPath, "{\"Error\":\"NotFound\",\"Message\":\"Harbor not found\"}", HttpStatusCode.NotFound)
                    .Json("GET", HarborPath, "{\"Error\":\"NotFound\",\"Message\":\"Harbor not found\"}", HttpStatusCode.NotFound);
                HarborMetricsLoadResult notRegistered = await Feed(unknown).LoadAsync("24h").ConfigureAwait(false);
                AssertEqual(HarborMetricsLoadStateEnum.NotRegistered, notRegistered.State);

                StubHttpHandler probeDenied = new StubHttpHandler()
                    .Json("GET", HarborPath, "{\"Error\":\"Forbidden\"}", HttpStatusCode.Forbidden);
                AssertEqual(HarborMetricsLoadStateEnum.Forbidden, (await Feed(probeDenied).LoadAsync("24h").ConfigureAwait(false)).State, "a denied probe reports the credential problem");
            }));

            cases.Add(Case("offline_and_server_error", "A transport failure is offline; a 500 is a failure with its status", async () =>
            {
                StubHttpHandler offline = new StubHttpHandler().On("GET", MetricsPath, body => throw new HttpRequestException("Connection refused"));
                HarborMetricsLoadResult down = await Feed(offline).LoadAsync("1h").ConfigureAwait(false);
                AssertEqual(HarborMetricsLoadStateEnum.Offline, down.State);
                AssertEqual(0, down.StatusCode);
                AssertTrue(down.Detail.Length > 0, "the transport error is kept for details");

                StubHttpHandler broken = new StubHttpHandler().Json("GET", MetricsPath, "{\"Error\":\"InternalError\"}", HttpStatusCode.InternalServerError);
                HarborMetricsLoadResult failed = await Feed(broken).LoadAsync("1h").ConfigureAwait(false);
                AssertEqual(HarborMetricsLoadStateEnum.Failed, failed.State);
                AssertEqual(500, failed.StatusCode);
                AssertEqual("The Admiral could not return the charts (HTTP 500).", failed.Message);

                StubHttpHandler gateway = new StubHttpHandler().Json("GET", MetricsPath, "{}", HttpStatusCode.BadGateway);
                AssertEqual(HarborMetricsLoadStateEnum.Offline, (await Feed(gateway).LoadAsync("1h").ConfigureAwait(false)).State, "a proxy that cannot reach the Admiral is offline");
            }));

            cases.Add(Case("no_address_no_id_cancel", "No address and no Harbor ID make no request; cancelling throws", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler().Json("GET", MetricsPath, HarborMetricsFixture.Json());
                HarborMetricsFeed noAddress = new HarborMetricsFeed(() => null, () => "hbr_1");
                AssertEqual(HarborMetricsLoadStateEnum.NoAddress, (await noAddress.LoadAsync("24h").ConfigureAwait(false)).State);
                HarborMetricsFeed noId = new HarborMetricsFeed(() => Client(stub), () => "  ");
                AssertEqual(HarborMetricsLoadStateEnum.NoHarborId, (await noId.LoadAsync("24h").ConfigureAwait(false)).State);
                AssertEqual(0, stub.Log.Count, "nothing was sent");

                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    bool threw = false;
                    try
                    {
                        await Feed(stub).LoadAsync("24h", cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        threw = true;
                    }

                    AssertTrue(threw, "cancelled");
                }
            }));

            cases.Add(Case("refresher_timer_range_stop", "The refresher loads at once, again each time the timer fires, the new range on a switch, and nothing after Stop", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler().On("GET", MetricsPath, body => StubHttpHandler.Response(HttpStatusCode.OK, HarborMetricsFixture.Json()));
                SemaphoreSlim tick = new SemaphoreSlim(0);
                SemaphoreSlim loaded = new SemaphoreSlim(0);
                List<HarborMetricsLoadResult> results = new List<HarborMetricsLoadResult>();
                using (HarborMetricsRefresher refresher = new HarborMetricsRefresher(Feed(stub), TimeSpan.FromSeconds(30), async (span, ct) =>
                {
                    AssertEqual(TimeSpan.FromSeconds(30), span, "waits the interval");
                    await tick.WaitAsync(ct).ConfigureAwait(false);
                }))
                {
                    refresher.Loaded += (s, r) =>
                    {
                        lock (results) results.Add(r);
                        loaded.Release();
                    };
                    refresher.Start("1h");
                    AssertTrue(refresher.IsRunning, "running");
                    AssertTrue(await loaded.WaitAsync(_Wait).ConfigureAwait(false), "first load at once");
                    AssertEqual("1h", results[0].Range);
                    tick.Release();
                    AssertTrue(await loaded.WaitAsync(_Wait).ConfigureAwait(false), "second load when the timer fires");
                    AssertEqual(2, stub.CountFor("GET", MetricsPath));

                    refresher.SetRange("7d");
                    AssertTrue(await loaded.WaitAsync(_Wait).ConfigureAwait(false), "loads the new range at once");
                    lock (results) AssertEqual("7d", results.Last().Range);
                    AssertEqual("7d", stub.Last("GET", MetricsPath).QueryValue("range"));

                    AssertTrue(SpinWait.SpinUntil(() => refresher.CompletedRuns == 1, _Wait), "the 1h run ended when the range switched");
                    int before = stub.CountFor("GET", MetricsPath);
                    refresher.Stop();
                    AssertFalse(refresher.IsRunning, "stopped");
                    AssertTrue(SpinWait.SpinUntil(() => refresher.CompletedRuns == 2, _Wait), "Stop ended the 7d run (its wait was cancelled)");
                    tick.Release();
                    AssertEqual(before, stub.CountFor("GET", MetricsPath), "no request after Stop");
                    AssertEqual(3, refresher.LoadCount);
                }
            }));

            cases.Add(Case("refresher_drops_stale_answer", "An answer for a range that was switched away from while in flight is dropped", async () =>
            {
                ManualResetEventSlim release = new ManualResetEventSlim(false);
                StubHttpHandler stub = new StubHttpHandler().On("GET", MetricsPath, body =>
                {
                    release.Wait(_Wait);
                    return StubHttpHandler.Response(HttpStatusCode.OK, HarborMetricsFixture.Json());
                });
                SemaphoreSlim loaded = new SemaphoreSlim(0);
                List<string> ranges = new List<string>();
                using (HarborMetricsRefresher refresher = new HarborMetricsRefresher(Feed(stub), TimeSpan.FromSeconds(30), (span, ct) => Task.Delay(Timeout.Infinite, ct)))
                {
                    refresher.Loaded += (s, r) =>
                    {
                        lock (ranges) ranges.Add(r.Range);
                        loaded.Release();
                    };
                    refresher.Start("1h");
                    AssertTrue(SpinWait.SpinUntil(() => stub.InFlight == 1, _Wait), "the 1h request is in flight");
                    refresher.SetRange("24h");
                    release.Set();
                    AssertTrue(await loaded.WaitAsync(_Wait).ConfigureAwait(false), "an answer arrived");
                    AssertTrue(SpinWait.SpinUntil(() => refresher.CompletedRuns == 1, _Wait), "the 1h run ended");
                    lock (ranges) AssertEqual("24h", String.Join(",", ranges), "the 1h answer was dropped");
                    AssertEqual(1, refresher.LoadCount, "one answer raised");
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Harbor Metrics Feed", cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaClient Client(StubHttpHandler stub)
        {
            ArmadaClientOptions options = new ArmadaClientOptions("http://admiral.test");
            options.BearerToken = "harbor-access-key";
            options.TimeoutMs = 5000;
            return new ArmadaClient(options, stub);
        }

        private static HarborMetricsFeed Feed(StubHttpHandler stub, bool local = false)
        {
            return new HarborMetricsFeed(() => Client(stub), () => "hbr_1", () => local);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
