namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Testing;
    using TUIKit.Widgets;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// ArmadaGrid: sizing, server and local sort with indicators, multi-select, paging bar and page sizes, column
    /// chooser with pinned columns, states, virtualization, and CJK-safe widths.
    /// </summary>
    public sealed class TuiGridSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Grid";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "local_sort_indicator", "Local sort orders rows and shows ^ / v", () =>
            {
                ArmadaGrid<TestGridRow> grid = Grid(Rows(30).AsEnumerable().Reverse().ToList());
                AssertEqual(30, grid.Rows[0].Count, "unsorted order kept");
                grid.SortBy("count");
                string frame = Render(grid, 80, 12);
                TuiCase.Contains(frame, "Count ^", "ascending indicator");
                AssertEqual(1, grid.Rows[0].Count, "smallest first");
                grid.SortBy("count");
                AssertTrue(Render(grid, 80, 12).Contains("Count v"), "descending indicator");
                AssertEqual(30, grid.Rows[0].Count, "largest first");
                grid.MoveCursor(0);
                Press(grid, KeyCode.Right);
                Press(grid, KeyCode.Left);
                grid.HandleKey(KeyEvent.Char('s'));
                AssertEqual("Name", grid.SortKey, "s sorts the focused column");
            }));

            cases.Add(TuiCase.Sync(Suite, "server_sort_and_paging_query", "With a loader, sort and paging go to the server", () =>
            {
                List<GridQuery> queries = new List<GridQuery>();
                ArmadaGrid<TestGridRow> grid = Grid(null);
                grid.Loader = (q, ct) =>
                {
                    lock (queries) queries.Add(q);
                    List<TestGridRow> page = Rows(248).Skip((q.PageNumber - 1) * q.PageSize).Take(q.PageSize).ToList();
                    return Task.FromResult(new GridPage<TestGridRow>(page, 248));
                };
                grid.Reload();
                AssertTrue(SpinUntil(() => grid.Rows.Count == 25), "first page");
                AssertEqual("Showing 1-25 of 248. Page 1 of 10.", grid.PagingText(), "paging text");
                grid.HandleKey(KeyEvent.Char('>'));
                AssertTrue(SpinUntil(() => grid.PageNumber == 2 && grid.Rows.Count == 25 && grid.Rows[0].Id == "r026"), "page 2");
                grid.SortBy("name", true);
                AssertTrue(SpinUntil(() => { lock (queries) return queries.Any(q => q.SortKey == "Name" && q.SortDescending && q.PageNumber == 1); }), "server sort key");
                grid.HandleKey(KeyEvent.Char('z'));
                AssertEqual(50, grid.PageSize, "z cycles page size");
                AssertTrue(SpinUntil(() => grid.Rows.Count == 50), "50 rows");
            }));

            cases.Add(TuiCase.Sync(Suite, "multi_select", "Space, Shift+Down, Ctrl+A, and Esc manage marks", () =>
            {
                ArmadaGrid<TestGridRow> grid = Grid(Rows(10));
                int events = 0;
                grid.SelectionChanged += (s, e) => events++;
                grid.MoveCursor(0);
                grid.HandleKey(KeyEvent.Char(' '));
                AssertTrue(grid.Marked.Contains("r001"), "space marks");
                grid.HandleKey(KeyEvent.Special(KeyCode.Down, KeyModifiers.Shift));
                grid.HandleKey(KeyEvent.Special(KeyCode.Down, KeyModifiers.Shift));
                AssertEqual(3, grid.Marked.Count, "shift extends");
                TuiCase.Contains(Render(grid, 80, 14), "[x]", "checkbox drawn");
                TuiCase.Contains(Render(grid, 80, 14), "3 selected", "count in paging bar");
                grid.HandleKey(KeyEvent.Char('a', KeyModifiers.Ctrl));
                AssertEqual(10, grid.Marked.Count, "ctrl+a selects the page");
                grid.HandleKey(KeyEvent.Char('a', KeyModifiers.Ctrl));
                AssertEqual(0, grid.Marked.Count, "ctrl+a again clears");
                grid.HandleKey(KeyEvent.Char(' '));
                AssertTrue(grid.HandleKey(KeyEvent.Special(KeyCode.Escape)), "esc consumed when marks exist");
                AssertEqual(0, grid.Marked.Count, "esc clears");
                AssertFalse(grid.HandleKey(KeyEvent.Special(KeyCode.Escape)), "esc not consumed without marks");
                AssertTrue(events >= 5, "selection events raised");
            }));

            cases.Add(TuiCase.Sync(Suite, "cursor_survives_reload", "A refresh keeps the cursor on the same row id", () =>
            {
                List<TestGridRow> rows = Rows(10);
                ArmadaGrid<TestGridRow> grid = Grid(rows);
                grid.MoveCursor(4);
                AssertEqual("r005", grid.Current!.Id, "cursor");
                List<TestGridRow> reordered = rows.AsEnumerable().Reverse().ToList();
                grid.SetPage(new GridPage<TestGridRow>(reordered, 10), 1);
                AssertEqual("r005", grid.Current!.Id, "same id after reload");
            }));

            cases.Add(TuiCase.Sync(Suite, "column_chooser_pinned", "The column chooser hides columns but keeps pinned ones", () =>
            {
                ArmadaGrid<TestGridRow> grid = Grid(Rows(5));
                FakeModalHost host = new FakeModalHost();
                grid.ModalHost = host;
                List<string>? persisted = null;
                grid.ColumnsChanged += (s, keys) => persisted = keys;
                grid.HandleKey(KeyEvent.Char('c'));
                MultiPickerModal<string> picker = (MultiPickerModal<string>)host.Shown.Single();
                picker.HandleKey(KeyEvent.Char('a', KeyModifiers.Ctrl));
                picker.HandleKey(KeyEvent.Special(KeyCode.Enter));
                host.Complete();
                AssertNotNull(persisted, "columns persisted");
                List<string> visible = grid.VisibleColumns().Select(c => c.Key).ToList();
                AssertTrue(visible.Contains("name"), "pinned column kept");
                AssertFalse(visible.Contains("status"), "status hidden");
                string frame = Render(grid, 80, 10);
                TuiCase.NotContains(frame, "Status", "hidden column not drawn");
            }));

            cases.Add(TuiCase.Sync(Suite, "states", "Empty, loading, and error states render", () =>
            {
                ArmadaGrid<TestGridRow> grid = Grid(new List<TestGridRow>());
                TuiCase.Contains(Render(grid, 80, 10), "No records found.", "empty");
                grid.SetLoading();
                TuiCase.Contains(Render(grid, 80, 10), "Loading...", "loading");
                grid.SetError("boom");
                TuiCase.Contains(Render(grid, 80, 10), "Failed to load: boom", "error");
                TuiCase.Contains(Render(grid, 80, 10), "F5 Retry", "retry hint");
            }));

            cases.Add(TuiCase.Sync(Suite, "virtualization", "10,000 local rows render quickly (only visible rows are drawn)", () =>
            {
                ArmadaGrid<TestGridRow> grid = Grid(Rows(10000));
                grid.PageSize = 10000;
                grid.Reload();
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < 20; i++)
                {
                    grid.HandleKey(KeyEvent.Special(KeyCode.PageDown));
                    Render(grid, 120, 40);
                }

                AssertTrue(sw.ElapsedMilliseconds < 3000, "20 frames of 10k rows in " + sw.ElapsedMilliseconds + " ms");
                AssertTrue(grid.CursorIndex > 500, "cursor moved through the page");
            }));

            cases.Add(TuiCase.Sync(Suite, "cjk_widths", "CJK cells are padded by display width so columns align", () =>
            {
                List<TestGridRow> rows = new List<TestGridRow>
                {
                    new TestGridRow { Id = "a", Name = "\u4fee\u590d\u8868\u683c", Status = "Complete", Count = 1 },
                    new TestGridRow { Id = "b", Name = "abcd", Status = "Failed", Count = 2 }
                };
                ArmadaGrid<TestGridRow> grid = Grid(rows);
                grid.MultiSelect = false;
                string frame = Render(grid, 60, 6);
                string[] lines = frame.Split('\n');
                int a = TextCells.Width(lines[1].Substring(0, lines[1].IndexOf("Complete", StringComparison.Ordinal)));
                int b = TextCells.Width(lines[2].Substring(0, lines[2].IndexOf("Failed", StringComparison.Ordinal)));
                AssertEqual(a, b, "status column starts at the same cell");
            }));

            cases.Add(TuiCase.Sync(Suite, "activate_and_menu", "Enter activates and . requests the row menu", () =>
            {
                ArmadaGrid<TestGridRow> grid = Grid(Rows(3));
                TestGridRow? activated = null;
                TestGridRow? menu = null;
                grid.Activated += (s, r) => activated = r;
                grid.MenuRequested += (s, r) => menu = r;
                grid.MoveCursor(1);
                grid.HandleKey(KeyEvent.Special(KeyCode.Enter));
                grid.HandleKey(KeyEvent.Char('.'));
                AssertEqual("r002", activated?.Id, "activated");
                AssertEqual("r002", menu?.Id, "menu");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI grid", cases: cases);
        }

        private static ArmadaGrid<TestGridRow> Grid(List<TestGridRow>? rows)
        {
            ArmadaGrid<TestGridRow> grid = new ArmadaGrid<TestGridRow>(r => r.Id);
            grid.AddColumn(new GridColumn<TestGridRow>("name", "Name", r => r.Name) { Weight = 2, Sortable = true, SortKey = "Name", Pinned = true });
            grid.AddColumn(new GridColumn<TestGridRow>("status", "Status", r => r.Status) { Width = 12, Sortable = true, Style = (r, t) => StatusBadge.Style(r.Status, t) });
            grid.AddColumn(new GridColumn<TestGridRow>("count", "Count", r => r.Count.ToString(CultureInfo.InvariantCulture)) { Width = 8, Sortable = true, Align = CellAlignment.Right });
            grid.OnFocusChanged(true);
            if (rows != null) grid.SetLocalRows(rows);
            return grid;
        }

        private static List<TestGridRow> Rows(int count)
        {
            return Enumerable.Range(1, count).Select(i => new TestGridRow
            {
                Id = "r" + i.ToString("000", CultureInfo.InvariantCulture),
                Name = "Row " + i.ToString(CultureInfo.InvariantCulture),
                Status = i % 3 == 0 ? "Failed" : "Complete",
                Count = i
            }).ToList();
        }

        private static string Render(IWidget widget, int width, int height)
        {
            return Snapshot.RenderWidget(widget, width, height);
        }

        private static void Press(ArmadaGrid<TestGridRow> grid, KeyCode code)
        {
            grid.HandleKey(KeyEvent.Special(code));
        }

        private static bool SpinUntil(Func<bool> condition)
        {
            return SpinWait.SpinUntil(condition, 3000);
        }
    }
}
