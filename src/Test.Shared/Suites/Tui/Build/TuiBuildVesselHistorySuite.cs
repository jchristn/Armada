namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Vessel history (<c>/vessels/:id/history</c>) against a stub: the heatmap and totals, a day jump (arrows and Enter)
    /// with the right <c>before</c>, paging through NextCursor (prefetch near the end, no duplicates, stop at the end),
    /// a stale page ignored after a branch change, the date prompt (valid and invalid), the commit detail with files,
    /// year navigation bounded by the first commit, error and empty states, and the H entry points.
    /// </summary>
    public sealed class TuiBuildVesselHistorySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Tui.Build.VesselHistory";
        private const string ActivityPath = "/api/v1/vessels/vsl_demo/history/activity";
        private const string CommitsPath = "/api/v1/vessels/vsl_demo/history/commits";
        private const string Route = "/vessels/vsl_demo/history";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "heatmap_and_list", "The heatmap shows weekdays, months, totals, and the legend; the list shows commits by local day; the requests carry the default branch, the year range, and the local offset", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, stub))
                {
                    VesselHistoryScreen screen = Screen(host);
                    AssertTrue(host.WaitForText("42 commits in the last year"), "totals\n" + host.Screen());
                    AssertTrue(host.WaitForText("Fix the parser"), "commits\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Vessel History", "heading");
                    TuiCase.Contains(frame, "DemoRepo", "vessel name");
                    TuiCase.Contains(frame, "Branch: main (b)", "branch");
                    TuiCase.Contains(frame, "Mon", "weekday label");
                    TuiCase.Contains(frame, "Fri", "weekday label");
                    TuiCase.Contains(frame, "Less", "legend");
                    TuiCase.Contains(frame, "More", "legend");
                    TuiCase.Contains(frame, CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(screen.Today.AddDays(-90).Month), "month label");
                    TuiCase.Contains(frame, CommitHeatmap.Glyph(4, screen.Theme), "the busiest day at the top level");
                    TuiCase.Contains(frame, screen.Today.AddDays(-1).ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture), "day label");
                    TuiCase.Contains(frame, "abc1234", "short sha");
                    TuiCase.Contains(frame, "Ada", "author");
                    TuiCase.Contains(frame, "+12 -3", "lines");
                    TuiCase.Contains(frame, "End of history", "end row");
                    TuiCase.Contains(frame, "Arrows Day", "heatmap hints");
                    TuiCase.Contains(frame, "Enter Show day", "heatmap hints");

                    StubRequest activity = stub.Last("GET", ActivityPath);
                    AssertEqual("main", activity.QueryValue("branch"), "activity branch");
                    AssertEqual(screen.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), activity.QueryValue("to"), "activity to");
                    AssertEqual(screen.Today.AddDays(-364).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), activity.QueryValue("from"), "activity from");
                    AssertEqual(ExpectedOffset().ToString(CultureInfo.InvariantCulture), activity.QueryValue("utcOffsetMinutes"), "local offset");
                    StubRequest commits = stub.Last("GET", CommitsPath);
                    AssertEqual("main", commits.QueryValue("branch"), "commits branch");
                    AssertEqual("50", commits.QueryValue("limit"), "page size");
                    AssertNull(commits.QueryValue("before"), "latest: no before");
                    AssertNull(commits.QueryValue("cursor"), "first page: no cursor");

                    host.Press("tab");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.CommitGrid), "Tab moves to the list");
                    frame = host.Screen();
                    TuiCase.Contains(frame, "Enter Details", "list hints");
                    TuiCase.Contains(frame, "y Copy SHA", "list hints");
                    host.Press("shift+tab");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Heatmap), "Shift+Tab moves back to the heatmap");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "day_jump", "Arrows move the selected day and Enter lists the commits on or before it (before = the next local midnight); l returns to the latest", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, stub))
                {
                    VesselHistoryScreen screen = Screen(host);
                    AssertTrue(host.WaitForText("42 commits in the last year"), "loaded");
                    AssertTrue(host.SettleRequests(), "settled");
                    AssertEqual(screen.Today, screen.Heatmap.Selected, "today selected first");
                    host.Press("left").Press("up");
                    DateTime day = screen.Today.AddDays(-8);
                    AssertEqual(day, screen.Heatmap.Selected, "Left is a week back, Up a day back");
                    TuiCase.Contains(host.Screen(), day.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture) + ": 1 commit", "the footer names the selected day and its count");
                    int before = stub.CountFor("GET", CommitsPath);
                    host.Press("enter");
                    string expected = VesselHistoryScreen.BeforeFor(day, ExpectedOffset());
                    AssertTrue(host.PumpUntil(() => stub.RequestsFor("GET", CommitsPath).Any(r => r.QueryValue("before") == expected)), "before " + expected + ": " + String.Join("\n", stub.Requests));
                    StubRequest jump = stub.RequestsFor("GET", CommitsPath).Last(r => r.QueryValue("before") == expected);
                    AssertEqual("main", jump.QueryValue("branch"), "branch kept");
                    AssertEqual(day, screen.JumpDate, "jump date");
                    AssertTrue(host.WaitForText("on or before " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "caption\n" + host.Screen());
                    AssertEqual(before + 1, stub.CountFor("GET", CommitsPath), "one request for the jump");
                    DateTime expectedUtc = new DateTimeOffset(DateTime.SpecifyKind(day.AddDays(1), DateTimeKind.Unspecified), TimeSpan.FromMinutes(ExpectedOffset())).UtcDateTime;
                    AssertEqual(expectedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), expected, "before is the next local midnight in UTC");

                    host.Press("l");
                    AssertTrue(host.PumpUntil(() => stub.Last("GET", CommitsPath).QueryValue("before") == null && stub.CountFor("GET", CommitsPath) == before + 2), "l reloads the latest");
                    AssertNull(screen.JumpDate, "jump cleared");
                    AssertEqual(screen.Today, screen.Heatmap.Selected, "today selected again");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "paging", "The next page loads only when the cursor nears the end, follows NextCursor alone, drops duplicates, and stops at the last page", () =>
            {
                StubHttpHandler stub = Stub();
                List<VesselCommit> first = new List<VesselCommit>();
                for (int i = 0; i < 20; i++) first.Add(Commit("p1c" + i.ToString("D2", CultureInfo.InvariantCulture), "First page " + i, DateTime.UtcNow.AddHours(-i - 1)));
                stub.Json("GET", CommitsPath + "?branch=main&limit=50", Page(first, "cur2"));
                List<VesselCommit> second = new List<VesselCommit> { first[19], Commit("p2c00", "Second page 0", DateTime.UtcNow.AddHours(-30)), Commit("p2c01", "Second page 1", DateTime.UtcNow.AddHours(-31)) };
                stub.Json("GET", CommitsPath + "?cursor=cur2&limit=50", Page(second, null));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, stub))
                {
                    VesselHistoryScreen screen = Screen(host);
                    AssertTrue(host.WaitForText("First page 0"), "first page\n" + host.Screen());
                    AssertTrue(host.SettleRequests(), "settled");
                    AssertEqual(1, stub.CountFor("GET", CommitsPath), "no prefetch while the cursor is at the top");
                    AssertEqual("cur2", screen.NextCursor, "next cursor kept");
                    TuiCase.NotContains(host.Screen(), "End of history", "not the end yet");

                    host.Press("tab");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.CommitGrid), "list focused");
                    for (int i = 0; i < 14; i++) host.Press("down");
                    AssertTrue(host.SettleRequests(), "settled");
                    AssertEqual(1, stub.CountFor("GET", CommitsPath), "still one page 6 rows from the end");
                    host.Press("down");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", CommitsPath) == 2), "the next page loads 5 rows from the end");
                    StubRequest next = stub.Last("GET", CommitsPath);
                    AssertEqual("cur2", next.QueryValue("cursor"), "cursor sent");
                    AssertNull(next.QueryValue("branch"), "no branch with a cursor");
                    AssertNull(next.QueryValue("before"), "no before with a cursor");
                    AssertTrue(host.WaitForText("Second page 1"), "second page shown\n" + host.Screen());
                    AssertTrue(host.WaitForText("End of history"), "end row");
                    AssertEqual(22, screen.Commits.Count, "duplicate dropped");
                    AssertEqual(22, screen.Commits.Select(c => c.Sha).Distinct().Count(), "no duplicate SHAs");
                    host.Press("end");
                    AssertTrue(host.SettleRequests(), "settled");
                    AssertEqual(2, stub.CountFor("GET", CommitsPath), "nothing after the last page");
                    AssertNull(screen.NextCursor, "no next cursor");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "stale_branch_change", "A page for the previous branch that arrives after a branch change (b) is ignored", () =>
            {
                StubHttpHandler stub = Stub();
                using (ManualResetEventSlim gate = new ManualResetEventSlim(false))
                {
                    stub.On("GET", CommitsPath + "?branch=main&limit=50", body =>
                    {
                        gate.Wait(10000);
                        return StubHttpHandler.Response(HttpStatusCode.OK, Page(new List<VesselCommit> { Commit("aaa0000000", "Main branch commit", DateTime.UtcNow.AddHours(-2)) }, null));
                    });
                    stub.Json("GET", CommitsPath + "?branch=feature%2Fx&limit=50", Page(new List<VesselCommit> { Commit("bbb0000000", "Feature branch commit", DateTime.UtcNow.AddHours(-3)) }, null));
                    try
                    {
                        using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, stub))
                        {
                            VesselHistoryScreen screen = Screen(host);
                            AssertTrue(host.PumpUntil(() => stub.CountFor("GET", CommitsPath) == 1), "main page requested");
                            host.Press("b");
                            AssertTrue(host.PumpUntil(() => host.App.Modals.Top is PickerModal<string>), "branch picker\n" + host.Screen());
                            TuiCase.Contains(host.Screen(), "feature/x", "branches listed");
                            host.Type("feature");
                            host.Press("enter");
                            AssertTrue(host.WaitForText("Feature branch commit"), "feature page shown\n" + host.Screen());
                            AssertEqual("feature/x", screen.Branch, "branch");
                            AssertTrue(stub.RequestsFor("GET", ActivityPath).Any(r => r.QueryValue("branch") == "feature/x"), "activity for the new branch");
                            gate.Set();
                            AssertTrue(host.SettleRequests(), "settled");
                            AssertTrue(host.PumpUntil(() => stub.InFlight == 0), "main page answered");
                            host.Pump();
                            TuiCase.NotContains(host.Screen(), "Main branch commit", "the stale page is ignored");
                            AssertEqual("bbb0000000", String.Join(",", screen.Commits.Select(c => c.Sha)), "only the feature commit");
                            TuiCase.Contains(host.Screen(), "Branch: feature/x (b)", "branch shown");
                        }
                    }
                    finally
                    {
                        gate.Set();
                    }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "jump_to_date", "t asks for a date: invalid input is refused in place, a valid one jumps the list (and moves the heatmap when it is outside the range)", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, stub))
                {
                    VesselHistoryScreen screen = Screen(host);
                    AssertTrue(host.WaitForText("42 commits in the last year"), "loaded");
                    AssertTrue(host.SettleRequests(), "settled");
                    int commits = stub.CountFor("GET", CommitsPath);
                    host.Press("t");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is FormModal), "date prompt");
                    TuiCase.Contains(host.Screen(), "Jump to Date", "prompt title");
                    host.Press("ctrl+u").Type("2026-13-45").Press("enter");
                    AssertTrue(host.WaitForText("Enter a date as yyyy-MM-dd."), "invalid date refused\n" + host.Screen());
                    AssertTrue(host.App.Modals.Top is FormModal, "prompt stays open");
                    AssertEqual(commits, stub.CountFor("GET", CommitsPath), "no request for an invalid date");

                    DateTime inRange = screen.Today.AddDays(-30);
                    host.Press("ctrl+u").Type(inRange.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Press("enter");
                    string before = VesselHistoryScreen.BeforeFor(inRange, ExpectedOffset());
                    AssertTrue(host.PumpUntil(() => stub.RequestsFor("GET", CommitsPath).Any(r => r.QueryValue("before") == before)), "jump request: " + String.Join("\n", stub.Requests));
                    AssertTrue(host.PumpUntil(() => !host.App.Modals.IsActive), "prompt closed");
                    AssertEqual(inRange, screen.Heatmap.Selected, "heatmap selects the day");
                    AssertEqual(screen.Today, screen.RangeTo, "range unchanged for a day inside it");

                    int activity = stub.CountFor("GET", ActivityPath);
                    DateTime old = screen.Today.AddDays(-500);
                    host.Press("t");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.Top is FormModal), "date prompt again");
                    host.Press("ctrl+u").Type(old.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Press("enter");
                    string oldBefore = VesselHistoryScreen.BeforeFor(old, ExpectedOffset());
                    AssertTrue(host.PumpUntil(() => stub.RequestsFor("GET", CommitsPath).Any(r => r.QueryValue("before") == oldBefore)), "old jump request");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", ActivityPath) == activity + 1), "activity reloads for the new range");
                    AssertEqual(old.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), stub.Last("GET", ActivityPath).QueryValue("to"), "range ends on the day");
                    AssertEqual(old, screen.Heatmap.Selected, "heatmap selects the day");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "commit_detail", "Enter on a commit shows the message, author and committer with both dates, parents, and the files (kind, rename, +/-, binary, truncation); y copies the SHA", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, stub))
                {
                    VesselHistoryScreen screen = Screen(host);
                    AssertTrue(host.WaitForText("Fix the parser"), "commits");
                    host.Press("tab");
                    host.Press("home");
                    AssertEqual("abc1234567890", screen.CurrentCommit()?.Sha, "first commit");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => screen.DetailModal != null), "detail opened");
                    AssertTrue(host.WaitForText("Commit abc1234"), "title\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Fix the parser", "subject");
                    TuiCase.Contains(frame, "Handles nested braces.", "body");
                    TuiCase.Contains(frame, "Ada <ada@example.com>", "author");
                    TuiCase.Contains(frame, "Grace <grace@example.com>", "committer");
                    TuiCase.Contains(frame, "Authored", "authored date");
                    TuiCase.Contains(frame, "Committed", "committed date");
                    TuiCase.Contains(frame, "parent111", "parents");
                    TuiCase.Contains(frame, "Merge commit", "merge note");
                    TuiCase.Contains(frame, "src/old.cs -> src/new.cs", "rename source");
                    TuiCase.Contains(frame, "Renamed", "kind");
                    TuiCase.Contains(frame, "binary", "binary file");
                    TuiCase.Contains(frame, "+10", "added lines");
                    TuiCase.Contains(frame, "Showing the first 3 of 250 changed files.", "truncation note");
                    host.Press("y");
                    AssertEqual("abc1234567890", host.Tui.Context.Clipboard.LastCopied, "y in the detail copies the SHA");
                    for (int i = 0; i < 3 && host.App.Modals.IsActive; i++) host.Press("esc");
                    AssertTrue(host.PumpUntil(() => !host.App.Modals.IsActive), "detail closed");
                    host.Press("down");
                    AssertEqual("def4567890123", screen.CurrentCommit()?.Sha, "second commit");
                    host.Press("y");
                    AssertEqual("def4567890123", host.Tui.Context.Clipboard.LastCopied, "y on the row copies its SHA");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "commit_detail_localized", "The commit detail translates each file's change kind through the catalog", () =>
            {
                StubHttpHandler stub = Stub();
                Armada.Client.Models.I18nCatalog catalog = new Armada.Client.Models.I18nCatalog();
                catalog.DefaultLocale = "en";
                catalog.SupportedLocales = Armada.Tui.Services.LocalizationService.DefaultLocales.ToList();
                Armada.Client.Models.I18nLocalePack de = new Armada.Client.Models.I18nLocalePack();
                de.Phrases = new Dictionary<string, string> { ["Renamed"] = "Umbenannt" };
                catalog.Locales["de"] = de;
                stub.Json("GET", "/dashboard/i18n/armada.json", ArmadaJson.Serialize(catalog));
                using (TuiTestHost host = new TuiTestHost(160, 50, stub, "http://127.0.0.1:9", o => { o.Token = "tok_env"; o.StartRoute = Route; }))
                {
                    host.Tui.Context.Prefs.Current.Locale = "de";
                    host.Start();
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Session.IsSignedIn && host.Tui.Shell.Screen != null, 5000), "signed in");
                    host.Tui.Context.Loc.SetLocale("de");
                    VesselHistoryScreen screen = Screen(host);
                    AssertTrue(host.WaitForText("Fix the parser"), "commits");
                    host.Press("tab");
                    host.Press("home");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => screen.DetailModal != null), "detail opened");
                    AssertTrue(host.WaitForText("Umbenannt"), "translated change kind\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "year_navigation", "[ moves the heatmap a year back while the branch has older commits, ] forward to today, never past either end", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, stub))
                {
                    VesselHistoryScreen screen = Screen(host);
                    AssertTrue(host.WaitForText("42 commits in the last year"), "loaded");
                    AssertTrue(host.SettleRequests(), "settled");
                    int count = stub.CountFor("GET", ActivityPath);
                    host.Press("]");
                    AssertTrue(host.SettleRequests(), "settled");
                    AssertEqual(count, stub.CountFor("GET", ActivityPath), "] does nothing at today");
                    host.Press("[");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", ActivityPath) == count + 1), "[ reloads");
                    AssertEqual(screen.Today.AddYears(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), stub.Last("GET", ActivityPath).QueryValue("to"), "a year back");
                    AssertTrue(host.WaitForText("42 commits from "), "range totals\n" + host.Screen());
                    host.Press("[");
                    AssertTrue(host.SettleRequests(), "settled");
                    AssertEqual(count + 1, stub.CountFor("GET", ActivityPath), "[ stops at the first commit (two years back is before it)");
                    host.Press("]");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", ActivityPath) == count + 2), "] reloads");
                    AssertEqual(screen.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), stub.Last("GET", ActivityPath).QueryValue("to"), "back to today");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "errors_and_empty", "A repository error (HTTP 200 with Error) shows in the heatmap and the list; an empty branch says so", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", ActivityPath, "{\"VesselId\":\"vsl_demo\",\"Branch\":\"main\",\"Days\":[],\"Error\":\"Repository not cloned yet\"}");
                stub.Json("GET", CommitsPath, "{\"VesselId\":\"vsl_demo\",\"Branch\":\"main\",\"Commits\":[],\"Error\":\"Repository not cloned yet\"}");
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, stub))
                {
                    AssertTrue(host.WaitForText("! Repository not cloned yet"), "error\n" + host.Screen());
                    VesselHistoryScreen screen = Screen(host);
                    AssertTrue(host.PumpUntil(() => screen.CommitsError != null), "list error");
                    AssertEqual("Repository not cloned yet", screen.ActivityError, "activity error");
                }

                StubHttpHandler empty = Stub();
                empty.Json("GET", CommitsPath, "{\"VesselId\":\"vsl_demo\",\"Branch\":\"main\",\"Commits\":[]}");
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, Route, empty))
                {
                    AssertTrue(host.WaitForText("No commits on this branch."), "empty\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "entry_points", "H on a Vessels row and on the vessel page opens the history; Alt+Left returns; the vessel page lists View History in its action menu", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/vessels", stub))
                {
                    AssertTrue(host.WaitForText("DemoRepo"), "rows");
                    VesselsScreen list = (VesselsScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("home");
                    for (int i = 0; i < 5 && list.Grid.Current?.Id != "vsl_demo"; i++) host.Press("down");
                    AssertEqual("vsl_demo", list.Grid.Current?.Id, "row selected");
                    host.Press(".");
                    AssertTrue(host.WaitForText("View History"), "row menu lists View History");
                    host.Press("esc");
                    AssertTrue(host.PumpUntil(() => !host.App.Modals.IsActive), "menu closed");
                    host.Press("H");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == Route), "H on the row: " + host.Tui.Context.Router.Current!.FullPath);
                    AssertTrue(host.WaitForText("Fix the parser"), "history loaded");
                    host.Press("alt+left");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/vessels"), "Alt+Left returns to Vessels");

                    host.Tui.Context.Navigate("/vessels/vsl_demo");
                    AssertTrue(host.WaitForText("Needs Attention"), "vessel page\n" + host.Screen());
                    VesselScreen page = (VesselScreen)host.Tui.Shell.Screen!;
                    AssertTrue(page.ActionBar.Buttons.Any(b => b.Label == "History" && b.Visible), "header button");
                    host.Press("H");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == Route), "H on the vessel page");
                    AssertTrue(host.WaitForText("42 commits in the last year"), "history loaded");
                    host.Press("alt+left");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/vessels/vsl_demo"), "Alt+Left returns to the vessel page");
                    AssertTrue(host.WaitForText("Needs Attention"), "vessel page again");
                    host.Press(".");
                    AssertTrue(host.WaitForText("History"), "action menu lists History");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "heatmap_levels", "Five levels scaled by the busiest day; high contrast uses a different glyph per level, the color palettes one glyph in a color ramp", () =>
            {
                AssertEqual(0, CommitHeatmap.Level(0, 8), "none");
                AssertEqual(1, CommitHeatmap.Level(1, 8), "low");
                AssertEqual(2, CommitHeatmap.Level(4, 8), "half");
                AssertEqual(4, CommitHeatmap.Level(8, 8), "max");
                AssertEqual(4, CommitHeatmap.Level(9, 8), "clamped");
                AssertEqual(0, CommitHeatmap.Level(3, 0), "no max");
                ArmadaTheme contrast = ThemePalettes.HighContrast();
                HashSet<string> glyphs = new HashSet<string>();
                for (int level = 0; level <= 4; level++) glyphs.Add(CommitHeatmap.Glyph(level, contrast));
                AssertEqual(5, glyphs.Count, "high contrast: a glyph per level");
                ArmadaTheme dark = ThemePalettes.Dark();
                AssertEqual(CommitHeatmap.Glyph(1, dark), CommitHeatmap.Glyph(4, dark), "color: one glyph");
                AssertNotEqual(CommitHeatmap.LevelStyle(1, dark).Foreground, CommitHeatmap.LevelStyle(4, dark).Foreground, "color: the ramp differs");
                contrast.AsciiGlyphs = true;
                glyphs.Clear();
                for (int level = 0; level <= 4; level++)
                {
                    string g = CommitHeatmap.Glyph(level, contrast);
                    AssertTrue(g.All(ch => ch < 128), "ASCII glyph " + g);
                    glyphs.Add(g);
                }

                AssertEqual(5, glyphs.Count, "ASCII high contrast: a glyph per level");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI vessel history", cases: cases);
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// The history stub: the vessels stub plus the vessel, a year of activity (42 commits, first commit 18 months
        /// ago), and one page of commits. Also used by Tui.FocusSweep and Tui.KeyboardFlows.Build.
        /// </summary>
        /// <returns>Stub.</returns>
        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiBuildVesselsSuite.Stub();
            stub.Json("GET", "/api/v1/vessels/vsl_demo", BuildStubs.Vessel("vsl_demo", "DemoRepo", "flt_web", "LocalMerge"));
            DateTime today = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(ExpectedOffset()).Date, DateTimeKind.Unspecified);
            VesselCommitActivity activity = new VesselCommitActivity();
            activity.VesselId = "vsl_demo";
            activity.Branch = "main";
            activity.UtcOffsetMinutes = ExpectedOffset();
            activity.From = today.AddDays(-364).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            activity.To = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (int i = 400; i >= -2; i--)
            {
                DateTime day = today.AddDays(-i);
                int count = i == 1 ? 8 : i == 8 ? 1 : (i % 10 == 3 ? 3 : 0);
                activity.Days.Add(new VesselCommitActivityDay { Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Count = count });
            }

            activity.TotalCommits = 42;
            activity.MaxDayCount = 8;
            activity.FirstCommitUtc = DateTime.UtcNow.AddDays(-548);
            activity.LastCommitUtc = DateTime.UtcNow.AddDays(-1);
            stub.Json("GET", "/api/v1/vessels/vsl_demo/history/activity", ArmadaJson.Serialize(activity));

            DateTime yesterdayNoon = new DateTimeOffset(today.AddDays(-1).AddHours(12), TimeSpan.FromMinutes(ExpectedOffset())).UtcDateTime;
            VesselCommit main = Commit("abc1234567890", "Fix the parser", yesterdayNoon);
            main.Body = "Handles nested braces.";
            main.AuthorName = "Ada";
            main.AuthorEmail = "ada@example.com";
            main.CommitterName = "Grace";
            main.CommitterEmail = "grace@example.com";
            main.ParentShas = new List<string> { "parent111", "parent222" };
            main.IsMerge = true;
            main.AddedLines = 12;
            main.DeletedLines = 3;
            main.FilesChanged = 250;
            main.FilesTruncated = true;
            main.Files = new List<GitChangedFile>
            {
                new GitChangedFile { Kind = GitChangeKindEnum.Modified, Path = "src/parser.cs", AddedLines = 10, DeletedLines = 3 },
                new GitChangedFile { Kind = GitChangeKindEnum.Renamed, Path = "src/new.cs", OldPath = "src/old.cs", AddedLines = 2, DeletedLines = 0 },
                new GitChangedFile { Kind = GitChangeKindEnum.Added, Path = "assets/logo.png", IsBinary = true },
            };
            VesselCommit older = Commit("def4567890123", "Add tests", yesterdayNoon.AddDays(-7));
            stub.Json("GET", "/api/v1/vessels/vsl_demo/history/commits", Page(new List<VesselCommit> { main, older }, null));
            return stub;
        }

        #endregion

        #region Private-Methods

        private static VesselHistoryScreen Screen(TuiTestHost host)
        {
            AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is VesselHistoryScreen), "history screen");
            return (VesselHistoryScreen)host.Tui.Shell.Screen!;
        }

        private static int ExpectedOffset()
        {
            return (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes);
        }

        private static VesselCommit Commit(string sha, string subject, DateTime committedUtc)
        {
            VesselCommit c = new VesselCommit();
            c.Sha = sha;
            c.ShortSha = sha.Substring(0, Math.Min(7, sha.Length));
            c.Subject = subject;
            c.AuthorName = "Lin";
            c.AuthorEmail = "lin@example.com";
            c.CommitterName = "Lin";
            c.CommitterEmail = "lin@example.com";
            c.AuthoredUtc = DateTime.SpecifyKind(committedUtc, DateTimeKind.Utc);
            c.CommittedUtc = DateTime.SpecifyKind(committedUtc, DateTimeKind.Utc);
            c.ParentShas = new List<string> { "0000000" };
            c.FilesChanged = 1;
            c.AddedLines = 1;
            c.Files = new List<GitChangedFile> { new GitChangedFile { Kind = GitChangeKindEnum.Modified, Path = "README.md", AddedLines = 1, DeletedLines = 0 } };
            return c;
        }

        private static string Page(List<VesselCommit> commits, string? next)
        {
            VesselCommitPage page = new VesselCommitPage();
            page.VesselId = "vsl_demo";
            page.Branch = "main";
            page.Commits = commits;
            page.NextCursor = next;
            return ArmadaJson.Serialize(page);
        }

        #endregion
    }
}
