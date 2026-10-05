namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Services;
    using Armada.Tui.Shell;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Testing;
    using TUIKit.Widgets;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// W8.5 performance harness (headless, repeatable): 10,000-row grids (local and server paged), a 5,000-message Ask
    /// transcript (open, idle frames, streaming chunks, navigation), and idle rendering cost and frame counts under the
    /// frame governor. Thresholds are deliberately generous (several times the measured numbers on a laptop) so the
    /// suite catches regressions of an order of magnitude without being flaky; the measured numbers are printed with a
    /// <c>[tui-perf]</c> prefix (and appended to the file named by <c>ARMADA_TUI_PERF_REPORT</c>) and recorded in
    /// docs/TUI_PERFORMANCE.md.
    /// </summary>
    public sealed class TuiPerformanceSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Performance";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "grid_10k_local", "A 10,000-row local grid loads, sorts, renders, and navigates quickly", () =>
            {
                List<TestGridRow> rows = Rows(10000);
                ArmadaGrid<TestGridRow> grid = Grid();
                double load = TuiPerfProbe.Once(() => grid.SetLocalRows(rows));
                TuiPerfProbe.Report("grid.local10k.load", load, "ms");
                AssertEqual(10000L, grid.TotalRecords, "all rows counted");
                AssertEqual(25, grid.Rows.Count, "one page materialized");
                double sort = TuiPerfProbe.Once(() => grid.SortBy("name"));
                TuiPerfProbe.Report("grid.local10k.sort", sort, "ms");
                AssertTrue(sort < 3000, "sort under 3 s (measured " + sort + " ms)");

                grid.PageSize = 250;
                grid.Reload();
                TuiPerfSample frame = TuiPerfProbe.Measure(50, () => RenderWidget(grid, 120, 40));
                TuiPerfProbe.Report("grid.local10k.frame.page250", frame);
                AssertTrue(frame.MeanMs < 25, "frame under 25 ms (measured " + frame.MeanMs + " ms)");

                grid.PageSize = 10000;
                grid.Reload();
                AssertEqual(10000, grid.Rows.Count, "whole set on one page");
                TuiPerfSample big = TuiPerfProbe.Measure(50, () => RenderWidget(grid, 120, 40));
                TuiPerfProbe.Report("grid.local10k.frame.page10000", big);
                AssertTrue(big.MeanMs < 25, "frame with 10,000 rows on the page under 25 ms (measured " + big.MeanMs + " ms)");
                TuiPerfSample nav = TuiPerfProbe.Measure(200, () =>
                {
                    grid.HandleKey(KeyEvent.Special(KeyCode.PageDown));
                    RenderWidget(grid, 120, 40);
                });
                TuiPerfProbe.Report("grid.local10k.pagedown_and_frame", nav);
                AssertTrue(nav.MeanMs < 25, "PgDn plus frame under 25 ms (measured " + nav.MeanMs + " ms)");
                TuiPerfSample mark = TuiPerfProbe.Measure(20, () =>
                {
                    grid.HandleKey(KeyEvent.Char(' '));
                    RenderWidget(grid, 120, 40);
                });
                TuiPerfProbe.Report("grid.local10k.mark_and_frame", mark);
                AssertTrue(mark.MeanMs < 25, "mark plus frame under 25 ms (measured " + mark.MeanMs + " ms)");
            }));

            cases.Add(TuiCase.Sync(Suite, "grid_10k_server_paged", "A 10,000-row server-paged grid only asks for one page at a time", () =>
            {
                List<TestGridRow> all = Rows(10000);
                List<GridQuery> queries = new List<GridQuery>();
                ArmadaGrid<TestGridRow> grid = Grid();
                grid.Loader = (q, ct) =>
                {
                    lock (queries) queries.Add(q);
                    List<TestGridRow> page = all.Skip((q.PageNumber - 1) * q.PageSize).Take(q.PageSize).ToList();
                    return System.Threading.Tasks.Task.FromResult(new GridPage<TestGridRow>(page, all.Count));
                };
                grid.PageSize = 100;
                double first = TuiPerfProbe.Once(() =>
                {
                    grid.Reload();
                    SpinWait.SpinUntil(() => grid.State == GridStateEnum.Ready && grid.Rows.Count == 100, 5000);
                });
                TuiPerfProbe.Report("grid.server10k.first_page", first, "ms");
                AssertEqual(10000L, grid.TotalRecords, "total from the server");
                double last = TuiPerfProbe.Once(() =>
                {
                    grid.GoToPage(100);
                    SpinWait.SpinUntil(() => grid.State == GridStateEnum.Ready && grid.PageNumber == 100 && grid.Rows.Count == 100 && grid.Rows[0].Count == 9901, 5000);
                });
                TuiPerfProbe.Report("grid.server10k.jump_to_last_page", last, "ms");
                AssertEqual(9901, grid.Rows[0].Count, "last page rows");
                lock (queries)
                {
                    AssertTrue(queries.All(q => q.PageSize == 100), "never more than one page requested");
                }

                TuiPerfSample frame = TuiPerfProbe.Measure(50, () => RenderWidget(grid, 120, 40));
                TuiPerfProbe.Report("grid.server10k.frame", frame);
                AssertTrue(frame.MeanMs < 25, "frame under 25 ms (measured " + frame.MeanMs + " ms)");
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_5000_messages", "A 5,000-message Ask transcript opens, renders, streams, and navigates quickly", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "Long thread"), Messages("ath_1", 5000));
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/ask/ath_1", fx.Stub))
                {
                    AskController ask = host.Tui.Ask;
                    AssertTrue(host.PumpUntil(() => ask.Conversation.Messages.Count == 5000, 20000), "5,000 messages loaded");
                    double open = TuiPerfProbe.Once(() => RenderShell(host));
                    TuiPerfProbe.Report("ask.5000.first_frame", open, "ms");
                    AssertTrue(open < 10000, "first frame under 10 s (measured " + open + " ms)");

                    TuiPerfSample idle = TuiPerfProbe.Measure(30, () => RenderShell(host));
                    TuiPerfProbe.Report("ask.5000.idle_frame", idle);
                    AssertTrue(idle.MeanMs < 50, "idle frame under 50 ms (measured " + idle.MeanMs + " ms)");

                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    TuiPerfSample rebuild = TuiPerfProbe.Measure(30, () =>
                    {
                        screen.Transcript.ViewState.Toggle(screen.Transcript.ViewState.ExpandedTools, "none");
                        RenderShell(host);
                    });
                    TuiPerfProbe.Report("ask.5000.relayout_and_frame", rebuild);
                    TuiPerfProbe.Report("ask.5000.relayout.blocks_reused", screen.Transcript.Builder.LastReused, "blocks");
                    AssertTrue(screen.Transcript.Builder.LastReused >= 4900, "unchanged messages reuse their layout (reused " + screen.Transcript.Builder.LastReused + ")");
                    AssertTrue(rebuild.MeanMs < 100, "relayout plus frame under 100 ms (measured " + rebuild.MeanMs + " ms)");

                    EventPump events = host.Tui.Context.Events;
                    events.Inject(AskFixtures.EventJson("ask.turn", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"state\":\"started\"}"));
                    host.Pump();
                    int chunk = 0;
                    TuiPerfSample stream = TuiPerfProbe.Measure(30, () =>
                    {
                        chunk++;
                        events.Inject(AskFixtures.EventJson("ask.chunk", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"delta\":\"word " + chunk + " \"}"));
                        host.Pump();
                        RenderShell(host);
                    });
                    TuiPerfProbe.Report("ask.5000.stream_chunk_and_frame", stream);
                    AssertTrue(stream.MeanMs < 100, "chunk plus frame under 100 ms (measured " + stream.MeanMs + " ms)");

                    TuiPerfSample nav = TuiPerfProbe.Measure(30, () =>
                    {
                        host.Tui.Shell.HandleKey(KeyEvent.Special(KeyCode.Up));
                        RenderShell(host);
                    });
                    TuiPerfProbe.Report("ask.5000.up_and_frame", nav);
                    AssertTrue(nav.MeanMs < 100, "Up plus frame under 100 ms (measured " + nav.MeanMs + " ms)");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "idle_shell_frame_cost", "An idle shell frame is cheap on a list screen", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    TuiPerfSample frame = TuiPerfProbe.Measure(100, () => RenderShell(host));
                    TuiPerfProbe.Report("shell.idle_frame.jobs", frame);
                    AssertTrue(frame.MeanMs < 20, "idle frame under 20 ms (measured " + frame.MeanMs + " ms)");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "idle_cpu", "The running loop idles near zero CPU with the frame governor (and far below the ungoverned loop)", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "Long thread"), Messages("ath_1", 5000));
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/ask/ath_1", fx.Stub))
                {
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count == 5000, 20000), "loaded");
                    double off = IdleCpu(host, false, 2000);
                    TuiPerfProbe.Report("idle.cpu.ask5000.ungoverned", off, "percent of one core");
                    long before = host.Tui.Frames.Composed;
                    double on = IdleCpu(host, true, 2000);
                    long redraws = host.Tui.Frames.Composed - before;
                    TuiPerfProbe.Report("idle.cpu.ask5000.governed", on, "percent of one core");
                    TuiPerfProbe.Report("idle.redraws.ask5000.governed_per_2_5s", redraws, "redraws");
                    AssertTrue(redraws <= 40, "idle redraws limited to the idle tick (measured " + redraws + " in 2.5 s)");
                    // CPU percentages depend on the machine and its load (a shared CI runner measured 10 percent where a
                    // workstation measures 1), so compare against the ungoverned loop measured here, in this run. The
                    // redraw count above is the deterministic check.
                    AssertTrue(on <= off * 0.5 + 1.0, "governed idle CPU at most half the ungoverned loop (governed " + on + ", ungoverned " + off + ")");
                }

                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    double off = IdleCpu(host, false, 2000);
                    TuiPerfProbe.Report("idle.cpu.jobs.ungoverned", off, "percent of one core");
                    long before = host.Tui.Frames.Composed;
                    double on = IdleCpu(host, true, 2000);
                    long redraws = host.Tui.Frames.Composed - before;
                    TuiPerfProbe.Report("idle.cpu.jobs.governed", on, "percent of one core");
                    AssertTrue(redraws <= 40, "idle redraws limited to the idle tick (measured " + redraws + " in 2.5 s)");
                    AssertTrue(on <= off * 0.5 + 1.0, "governed idle CPU at most half the ungoverned loop (governed " + on + ", ungoverned " + off + ")");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "frame_governor_rules", "The governor composes on change, input, resize, and the idle tick, and sleeps longer when idle", () =>
            {
                long now = 0;
                FrameGovernor g = new FrameGovernor();
                g.Clock = () => now;
                Size size = new Size(100, 30);
                AssertTrue(g.ShouldRender(size), "disabled always composes");
                AssertEqual(g.ActiveFrameMs, g.NextDelayMs(), "disabled ticks at the frame rate");
                g.Enabled = true;
                now = 10000;
                AssertTrue(g.ShouldRender(size), "first idle tick composes");
                now += 30;
                AssertFalse(g.ShouldRender(size), "nothing changed");
                AssertEqual(g.IdlePollMs, g.NextDelayMs(), "idle polling interval");
                g.Invalidate();
                AssertEqual(g.ActiveFrameMs, g.NextDelayMs(), "pending change ticks fast");
                AssertTrue(g.ShouldRender(size), "invalidate composes");
                now += 100;
                AssertTrue(g.ShouldRender(size), "hot window after a change");
                now += 200;
                AssertFalse(g.ShouldRender(size), "hot window over");
                now += 30;
                g.NoteInput();
                AssertTrue(g.ShouldRender(size), "input composes");
                now += 200;
                AssertFalse(g.ShouldRender(size), "quiet again");
                AssertTrue(g.ShouldRender(new Size(90, 30)), "resize composes");
                now += 600;
                AssertTrue(g.ShouldRender(new Size(90, 30)), "idle tick");
                g.TerminalFocused = false;
                now += 600;
                AssertFalse(g.ShouldRender(new Size(90, 30)), "unfocused uses the slower tick");
                now += 500;
                AssertTrue(g.ShouldRender(new Size(90, 30)), "unfocused tick");
            }));

            cases.Add(TuiCase.Sync(Suite, "posted_work_and_quit", "Posted work wakes the governor and Ctrl+Q ends the run loop", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(100, 30, "/jobs"))
                {
                    FrameGovernor g = host.Tui.Frames;
                    g.Enabled = true;
                    g.ShouldRender(new Size(100, 30));
                    g.ShouldRender(new Size(100, 30));
                    host.Tui.Context.Dispatcher.Post(() => { });
                    host.Pump();
                    AssertEqual(g.ActiveFrameMs, g.NextDelayMs(), "posted work marks the frame dirty");
                    TuiRunLoop loop = new TuiRunLoop(host.App, host.Adapter, g);
                    host.Tui.Context.QuitHandler = loop.RequestStop;
                    try
                    {
                        System.Threading.Tasks.Task run = loop.RunAsync();
                        Thread.Sleep(150);
                        long composed = g.Composed;
                        host.Backend.FeedInput("\u0011");
                        AssertTrue(run.Wait(3000), "Ctrl+Q ended the loop");
                        AssertTrue(loop.StopRequested, "stop requested");
                        AssertTrue(composed > 0, "the loop composed frames");
                    }
                    finally
                    {
                        host.App.Stop();
                        g.Enabled = false;
                    }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_memory_cap", "Live messages past the cap drop the oldest and offer them as earlier messages", () =>
            {
                AskConversation conv = new AskConversation("ath_1");
                conv.MaxMessages = 100;
                conv.Loaded("ath_1", null, Messages("ath_1", 100), false, DateTime.UtcNow);
                AssertFalse(conv.HasMore, "full history loaded");
                AskMessage live = AskFixtures.Message("amg_live", "ath_1", 1000, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "new");
                conv.Apply(AskEventParser.Parse(AskFixtures.Event("ask.message", new AskMessageEvent { ThreadId = "ath_1", Message = live }))!, DateTime.UtcNow);
                AssertEqual(100, conv.Messages.Count, "capped");
                AssertEqual("amg_live", conv.Messages[conv.Messages.Count - 1].Id, "newest kept");
                AssertEqual("amg_00001", conv.Messages[0].Id, "oldest dropped");
                AssertTrue(conv.HasMore, "earlier messages can be loaded again");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI performance", cases: cases);
        }

        internal static ArmadaGrid<TestGridRow> Grid()
        {
            ArmadaGrid<TestGridRow> grid = new ArmadaGrid<TestGridRow>(r => r.Id);
            grid.AddColumn(new GridColumn<TestGridRow>("name", "Name", r => r.Name) { Weight = 2, Sortable = true, SortKey = "Name", Pinned = true });
            grid.AddColumn(new GridColumn<TestGridRow>("status", "Status", r => StatusBadge.Label(r.Status)) { Width = 12, Sortable = true, Style = (r, t) => StatusBadge.Style(r.Status, t) });
            grid.AddColumn(new GridColumn<TestGridRow>("count", "Count", r => r.Count.ToString(CultureInfo.InvariantCulture)) { Width = 8, Sortable = true, Align = CellAlignment.Right });
            grid.OnFocusChanged(true);
            return grid;
        }

        internal static List<TestGridRow> Rows(int count)
        {
            return Enumerable.Range(1, count).Select(i => new TestGridRow
            {
                Id = "r" + i.ToString("00000", CultureInfo.InvariantCulture),
                Name = "Row " + i.ToString(CultureInfo.InvariantCulture),
                Status = i % 3 == 0 ? "Failed" : "Complete",
                Count = i
            }).ToList();
        }

        internal static AskMessage[] Messages(string threadId, int count)
        {
            AskMessage[] messages = new AskMessage[count];
            for (int i = 0; i < count; i++)
            {
                bool user = i % 2 == 0;
                string text = user
                    ? "Question " + i + ": what is running on the fleet right now?"
                    : "Answer " + i + ":\n\n- voyage **A** is landing\n- voyage `B` is queued\n\nSee [the dashboard](http://localhost/x) for more detail about mission " + i + ".";
                AskMessage m = AskFixtures.Message("amg_" + i.ToString("00000", CultureInfo.InvariantCulture), threadId, i + 1, user ? AskMessageRoleEnum.User : AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, text);
                m.CreatedUtc = DateTime.UtcNow.AddMinutes(-count + i);
                messages[i] = m;
            }

            return messages;
        }

        internal static double IdleCpu(TuiTestHost host, bool governed, int ms)
        {
            host.Tui.Frames.Enabled = governed;
            host.App.Start();
            try
            {
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    TuiRunLoop runLoop = new TuiRunLoop(host.App, host.Adapter, host.Tui.Frames);
                    System.Threading.Tasks.Task loop = governed ? runLoop.RunAsync(cts.Token) : host.App.RunAsync(cts.Token);
                    Thread.Sleep(500);
                    System.Diagnostics.Process proc = System.Diagnostics.Process.GetCurrentProcess();
                    proc.Refresh();
                    TimeSpan cpu0 = proc.TotalProcessorTime;
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    Thread.Sleep(ms);
                    proc.Refresh();
                    TimeSpan cpu1 = proc.TotalProcessorTime;
                    sw.Stop();
                    cts.Cancel();
                    try { loop.Wait(2000); } catch (AggregateException) { }
                    return 100.0 * (cpu1 - cpu0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds;
                }
            }
            finally
            {
                host.App.Stop();
                host.Tui.Frames.Enabled = false;
            }
        }

        internal static void RenderShell(TuiTestHost host)
        {
            CellBuffer buffer = new CellBuffer(host.Width, host.Height);
            host.Tui.Shell.Render(new BufferSurface(buffer));
        }

        private static void RenderWidget(IWidget widget, int width, int height)
        {
            CellBuffer buffer = new CellBuffer(width, height);
            widget.Render(new BufferSurface(buffer));
        }
    }
}
