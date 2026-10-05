namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless flows for Token Usage: range and metric switching, CSV export, and copying the chart as a table.
    /// </summary>
    public sealed class TuiTokenUsageSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Activity.Tokens";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "range_and_metric", "Range keys reload with the right bucket size; metric and shape re-render", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 44, "/activity?source=tokens", stub))
                {
                    AssertTrue(host.WaitForText("Usage by model"), "renders");
                    AssertTrue(host.WaitForText("claude-opus"), "model row");
                    string frame = host.Screen();
                    TuiScreenDump.Write("token-usage", frame);
                    TuiCase.Contains(frame, "1.5K", "total formatted");
                    TuiCase.Contains(frame, "2 of 10 records estimated", "estimated note");
                    AssertTrue(stub.Requests.Any(r => r.StartsWith("GET /api/v1/token-usage/summary") && r.Contains("bucketMinutes=15")), "day query: " + String.Join("\n", stub.Requests));
                    host.Press("h");
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.StartsWith("GET /api/v1/token-usage/summary") && r.Contains("bucketMinutes=0.5"))), "hour query");
                    host.Press("m");
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.StartsWith("GET /api/v1/token-usage/summary") && r.Contains("bucketMinutes=360"))), "month query");
                    TokenUsageScreen screen = Current<TokenUsageScreen>(host);
                    host.PumpUntil(() => !screen.Loading);
                    AssertEqual("claude-opus", screen.TimeChart.Series[0].Name, "series by model");
                    host.Press("t");
                    AssertEqual("byType", screen.MetricField.Value, "metric toggled");
                    AssertEqual(3, screen.TimeChart.Series.Count, "series by type");
                    AssertEqual("Input", screen.TimeChart.Series[0].Name, "input series");
                    host.Press("b");
                    AssertEqual(ChartKindEnum.Line, screen.TimeChart.Kind, "lines");
                    TuiScreenDump.Write("token-usage-lines", host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "csv_and_copy", "Export CSV writes the buckets; Copy chart copies a text table", () =>
            {
                StubHttpHandler stub = Stub();
                string dir = Path.Combine(Path.GetTempPath(), "armada-tui-tokens-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string? previous = Environment.GetEnvironmentVariable("ARMADA_TUI_SAVE_DIR");
                Environment.SetEnvironmentVariable("ARMADA_TUI_SAVE_DIR", dir);
                try
                {
                    using (TuiTestHost host = TuiCase.SignedIn(160, 44, "/activity?source=tokens", stub))
                    {
                        AssertTrue(host.WaitForText("claude-opus"), "model row");
                        host.Press("e");
                        AssertTrue(host.WaitForText("File path"), "path prompt");
                        host.Press("ctrl+s");
                        AssertTrue(host.PumpUntil(() => Directory.Exists(dir) && Directory.GetFiles(dir, "*.csv").Length == 1), "csv written");
                        string csv = File.ReadAllText(Directory.GetFiles(dir, "*.csv")[0]);
                        AssertTrue(csv.StartsWith("bucketStartUtc,bucketEndUtc,inputTokens,outputTokens,cachedTokens,totalTokens,claude-opus", StringComparison.Ordinal), "header: " + csv);
                        TuiCase.Contains(csv, "1000,400,100,1500,1500", "bucket row");
                        host.Press("y");
                        string? copied = host.Tui.Context.Clipboard.LastCopied;
                        AssertNotNull(copied, "copied");
                        TuiCase.Contains(copied!, "Time\tclaude-opus", "table header");
                        TuiCase.Contains(copied!, "1500", "table value");
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("ARMADA_TUI_SAVE_DIR", previous);
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI Token Usage", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/token-usage/summary", "{\"RecordCount\":10,\"EstimatedCount\":2,\"InputTokens\":1000,\"OutputTokens\":400,\"CachedTokens\":100,\"TotalTokens\":1500,"
                + "\"Buckets\":[{\"BucketStartUtc\":\"2026-10-04T10:00:00Z\",\"BucketEndUtc\":\"2026-10-04T10:15:00Z\",\"InputTokens\":1000,\"OutputTokens\":400,\"CachedTokens\":100,\"TotalTokens\":1500,"
                + "\"Models\":[{\"Model\":\"claude-opus\",\"InputTokens\":1000,\"OutputTokens\":400,\"CachedTokens\":100,\"TotalTokens\":1500}]}],"
                + "\"ByModel\":[{\"Model\":\"claude-opus\",\"InputTokens\":1000,\"OutputTokens\":400,\"CachedTokens\":100,\"TotalTokens\":1500}]}");
            return stub;
        }

        private static T Current<T>(TuiTestHost host) where T : ScreenBase
        {
            ScreenBase? screen = host.Tui.Shell.Screen;
            if (screen is HubScreen hub) screen = hub.Content;
            if (screen is T typed) return typed;
            throw new AssertionException("current screen is " + (screen?.GetType().Name ?? "null") + ", expected " + typeof(T).Name);
        }
    }
}
