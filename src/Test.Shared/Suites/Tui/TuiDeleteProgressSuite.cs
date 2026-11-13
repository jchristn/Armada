namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Configuration;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The delete progress notice (DeleteProgress and DeleteProgressDialog): it opens while a delete is pending,
    /// ignores Escape, closes on success, turns a failure into an error with Retry, and every title it shows is
    /// translated in every catalog locale.
    /// </summary>
    public sealed class TuiDeleteProgressSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.DeleteProgress";

        private static readonly Regex _RunTitle = new Regex("DeleteProgress\\.(?:Run|RunVoid)(?:<[^>]+>)?\\(\\s*[\\w.]+,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly Regex _OverrideTitle = new Regex("Deleting(?:Many)?Title\\s*\\{\\s*get\\s*\\{\\s*return\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly Regex _BulkTitle = new Regex("UserAdminOps\\.BulkDelete\\(.*,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\);", RegexOptions.Compiled);

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "pending_notice", "A slow delete shows the notice, Esc does not close it, and success closes it and reloads", () =>
            {
                StubHttpHandler stub = TuiMemorySuite.Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "DELETE", "/api/v1/memories/mem_1", "{}", box);
                stub.Delay("DELETE", "/api/v1/memories/mem_1", 1500);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=memory"))
                {
                    MemoryScreen screen = TuiEntityFixtures.Screen<MemoryScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    int listsBefore = stub.CountFor("GET", "/api/v1/memories");
                    host.Press("del");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is DeleteProgressDialog, "progress notice");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Deleting memory", "title");
                    TuiCase.Contains(frame, "This may take some time", "message");
                    host.Press("esc");
                    host.Pump();
                    AssertTrue(host.App.Modals.Top is DeleteProgressDialog, "Esc does not close the pending notice");
                    TuiCase.Contains(host.Screen(), "Deleting memory", "still shown after Esc");
                    TuiEntityFixtures.WaitFor(host, () => box.Count == 1 && !(host.App.Modals.Top is DeleteProgressDialog), "delete finished", 8000);
                    TuiEntityFixtures.WaitFor(host, () => stub.CountFor("GET", "/api/v1/memories") > listsBefore, "list reloaded");
                    TuiCase.NotContains(host.Screen(), "This may take some time", "notice closed");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "failure_retry", "A failed delete shows an error with Retry, and Retry deletes again", () =>
            {
                StubHttpHandler stub = TuiMemorySuite.Server();
                int calls = 0;
                stub.On("DELETE", "/api/v1/memories/mem_1", body =>
                {
                    int n = Interlocked.Increment(ref calls);
                    return n == 1
                        ? StubHttpHandler.Response(HttpStatusCode.InternalServerError, "{\"Error\":\"InternalError\",\"Message\":\"Database is locked\"}")
                        : StubHttpHandler.Response(HttpStatusCode.OK, "{}");
                });
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=memory"))
                {
                    MemoryScreen screen = TuiEntityFixtures.Screen<MemoryScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("del");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is ErrorDialog, "error dialog");
                    ErrorDialog error = (ErrorDialog)host.App.Modals.Top!;
                    AssertTrue(error.CanRetry, "Retry is offered");
                    TuiCase.Contains(host.Screen(), "Retry", "Retry button");
                    AssertTrue(DeleteProgress.IsBusy(host.Tui.Context), "a second delete is blocked while the failure is shown");
                    // Buttons are Retry, Copy details, Close (Close selected); Tab wraps to Retry.
                    host.Press("tab");
                    AssertEqual(0, error.SelectedButton, "Retry selected");
                    host.Press("enter");
                    TuiEntityFixtures.WaitFor(host, () => Volatile.Read(ref calls) == 2, "second DELETE");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top == null || !(host.App.Modals.Top is DeleteProgressDialog || host.App.Modals.Top is ErrorDialog), "dialogs closed");
                    AssertEqual(2, stub.CountFor("DELETE", "/api/v1/memories/mem_1"), "exactly two DELETE requests");
                    AssertFalse(DeleteProgress.IsBusy(host.Tui.Context), "not busy after success");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "dismiss_failure", "Closing a failed delete without retrying clears the busy state", () =>
            {
                StubHttpHandler stub = TuiMemorySuite.Server();
                stub.On("DELETE", "/api/v1/memories/mem_1", body => StubHttpHandler.Response(HttpStatusCode.InternalServerError, "{\"Error\":\"InternalError\",\"Message\":\"Still locked\"}"));
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=memory"))
                {
                    MemoryScreen screen = TuiEntityFixtures.Screen<MemoryScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("del");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => host.App.Modals.Top is ErrorDialog, "error dialog");
                    host.Press("esc");
                    TuiEntityFixtures.WaitFor(host, () => !DeleteProgress.IsBusy(host.Tui.Context), "busy cleared");
                    AssertEqual(1, stub.CountFor("DELETE", "/api/v1/memories/mem_1"), "no retry");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "titles_translated", "Every delete progress title and the message are translated in every locale", () =>
            {
                string root = ApiSurfaceFiles.FindRepositoryRoot();
                string path = Path.Combine(root, "src", "Armada.Server", "wwwroot", "i18n", "armada.json");
                I18nCatalog catalog = ArmadaJson.Deserialize<I18nCatalog>(File.ReadAllText(path)) ?? throw new AssertionException("catalog");
                AssertTrue(catalog.Locales.Count >= 8, "every maintained locale is present");
                HashSet<string> titles = new HashSet<string>(StringComparer.Ordinal);
                foreach (string file in Directory.GetFiles(Path.Combine(root, "src", "Armada.Tui"), "*.cs", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    foreach (Match m in _RunTitle.Matches(text)) titles.Add(Regex.Unescape(m.Groups[1].Value));
                    foreach (Match m in _OverrideTitle.Matches(text)) titles.Add(Regex.Unescape(m.Groups[1].Value));
                    foreach (Match m in _BulkTitle.Matches(text)) titles.Add(Regex.Unescape(m.Groups[1].Value));
                }

                AssertTrue(titles.Count >= 30, "found the delete titles (" + titles.Count + ")");
                AssertTrue(titles.Contains("Deleting vessel"), "vessel title");
                AssertTrue(titles.Contains("{count, plural, one {Deleting # fleet} other {Deleting # fleets}}"), "fleet bulk title");
                AssertTrue(titles.Contains("Purging mission"), "purge title");
                List<string> phrases = titles.ToList();
                phrases.Add(DeleteProgress.Message);
                LocalizationService english = new LocalizationService();
                english.SetCatalog(catalog);
                AssertEqual("Deleting 3 fleets", english.T("{count, plural, one {Deleting # fleet} other {Deleting # fleets}}", LocalizationArgs.Of("count", 3)), "English plural");
                AssertEqual("Deleting 1 fleet", english.T("{count, plural, one {Deleting # fleet} other {Deleting # fleets}}", LocalizationArgs.Of("count", 1)), "English singular");
                List<string> missing = new List<string>();
                foreach (string locale in catalog.Locales.Keys)
                {
                    LocalizationService loc = new LocalizationService();
                    loc.SetCatalog(catalog);
                    loc.SetLocale(locale);
                    foreach (string phrase in phrases)
                    {
                        if (phrase.StartsWith("{count, plural,", StringComparison.Ordinal))
                        {
                            string rendered = loc.T(phrase, LocalizationArgs.Of("count", 2));
                            if (rendered.Contains("plural") || !rendered.Contains("2") || rendered == english.T(phrase, LocalizationArgs.Of("count", 2))) missing.Add(locale + ": " + phrase);
                        }
                        else if (loc.T(phrase) == phrase)
                        {
                            missing.Add(locale + ": " + phrase);
                        }
                    }
                }

                AssertEqual("", String.Join("\n", missing), "untranslated delete progress phrases");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI delete progress", cases: cases);
        }
    }
}
