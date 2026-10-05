namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The test-side structured helpers: <see cref="StubHttpHandler"/> records method, path, query, and body together;
    /// <see cref="QueryString"/> compares parameter values exactly; <see cref="JsonShape"/> tells an absent property
    /// from an explicit null.
    /// </summary>
    public sealed class StubRecordingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.StubRecording";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Async(Suite, "concurrent_requests_keep_their_bodies", "Concurrent requests keep method, path, and body together (two queues could pair them wrongly)", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler();
                using (HttpClient client = new HttpClient(stub))
                {
                    List<Task> sends = new List<Task>();
                    for (int i = 0; i < 64; i++)
                    {
                        string path = "/api/v1/items/" + i;
                        string json = "{\"Index\":" + i + "}";
                        sends.Add(client.PostAsync("http://127.0.0.1:9" + path, new StringContent(json, Encoding.UTF8, "application/json")));
                    }

                    await Task.WhenAll(sends);
                }

                AssertEqual(64, stub.Log.Count, "all recorded");
                for (int i = 0; i < 64; i++)
                {
                    StubRequest r = stub.Last("POST", "/api/v1/items/" + i);
                    AssertEqual(i, r.BodyAs<StubRecordingIndexBody>().Index, "body of request " + i);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "route_selection_is_exact", "RequestsFor matches the exact path, not a prefix", () =>
            {
                StubRequest list = new StubRequest { Method = "POST", Path = "/api/v1/fleet-actions" };
                StubRequest run = new StubRequest { Method = "POST", Path = "/api/v1/fleet-actions/fa_1/run" };
                AssertTrue(list.Is("post", "/api/v1/fleet-actions"), "method case-insensitive");
                AssertFalse(run.Is("POST", "/api/v1/fleet-actions"), "a sub-route is a different route");
            }));

            cases.Add(TuiCase.Sync(Suite, "query_values_are_exact", "Query parsing compares values exactly and decodes them", () =>
            {
                StubRequest r = new StubRequest { Method = "GET", Path = "/api/v1/missions/m/log", Query = "?lines=5000&route=%2Fapi%2Fv1%2Ffleets&q=a+b&flag" };
                AssertEqual("5000", r.QueryValue("lines"), "lines");
                AssertNotEqual("500", r.QueryValue("lines"), "5000 is not 500 (a substring check passed here)");
                AssertEqual("/api/v1/fleets", r.QueryValue("route"), "decoded");
                AssertEqual("a b", r.QueryValue("q"), "plus is a space");
                AssertEqual("", r.QueryValue("flag"), "bare name");
                AssertNull(r.QueryValue("Lines"), "names are case-sensitive");
                AssertEqual(0, QueryString.Parse("").Count, "empty");
            }));

            cases.Add(TuiCase.Sync(Suite, "json_shape_null_vs_absent", "JsonShape distinguishes explicit null, absent, and nested properties", () =>
            {
                string json = "{\"CaptainId\":null,\"Count\":5,\"Name\":\"x\",\"Inner\":{\"gitHubTokenOverride\":\"t\"}}";
                AssertEqual(JsonTokenType.Null, JsonShape.TopLevelProperty(json, "captainid")!.ValueToken, "explicit null");
                AssertNull(JsonShape.TopLevelProperty(json, "VesselId"), "absent");
                AssertEqual(JsonTokenType.Number, JsonShape.TopLevelProperty(json, "Count")!.ValueToken, "number");
                AssertEqual("5", JsonShape.TopLevelProperty(json, "Count")!.ScalarText, "number text");
                AssertNull(JsonShape.TopLevelProperty(json, "GitHubTokenOverride"), "nested is not top level");
                AssertTrue(JsonShape.HasPropertyAnywhere(json, "GITHUBTOKENOVERRIDE"), "nested found case-insensitively");
            }));

            cases.Add(TuiCase.Sync(Suite, "missing_route_fails", "Last fails with the requests seen when no request matches", () =>
            {
                StubHttpHandler stub = new StubHttpHandler();
                AssertThrows<AssertionException>(() => stub.Last("GET", "/api/v1/nothing"), "no request");
                AssertThrows<AssertionException>(() => new StubRequest { Method = "POST", Path = "/x", Body = "" }.BodyAs<StubRecordingIndexBody>(), "empty body");
                AssertThrows<AssertionException>(() => new StubRequest { Method = "POST", Path = "/x", Body = "not json" }.BodyAs<StubRecordingIndexBody>(), "bad body");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Stub request recording, query parsing, and JSON shape helpers", cases: cases);
        }

        #endregion
    }
}
