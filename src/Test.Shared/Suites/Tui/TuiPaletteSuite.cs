namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Input;
    using Armada.Tui.Modals;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using TUIKit.Input;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Command palette (routes, hub tabs, commands, entity jump) and the help overlay; command dispatch and rebinding.
    /// </summary>
    public sealed class TuiPaletteSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Palette";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "palette_navigates_hub_tab", "Ctrl+K, type, Enter opens a hub tab", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    host.Press("ctrl+k");
                    AssertTrue(host.App.Modals.IsActive, "palette open");
                    host.Type("merge queue");
                    TuiCase.Contains(host.Screen(), "Missions: Merge Queue", "hub tab entry");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/missions?tab=merge-queue"), "navigated");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "palette_runs_command", "The palette runs commands, not only navigation", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    host.Press("ctrl+k").Type("theme light").Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Theme.EffectiveMode == Armada.Tui.Theming.ThemeModeEnum.Light), "theme changed");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "palette_entity_jump", "Typing an id offers Open and jumps to the entity", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    host.Press("ctrl+k").Paste("vsl_abc123");
                    TuiCase.Contains(host.Screen(), "Open vsl_abc123", "entity entry");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/vessels/vsl_abc123"), "jumped");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "palette_tab_fills_and_esc", "Tab fills the query and Esc closes", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    host.Press("ctrl+k").Type("capt").Press("tab");
                    CommandPaletteModal palette = (CommandPaletteModal)host.App.Modals.Top!;
                    AssertEqual("Captains", palette.List.Query, "filled");
                    host.Press("esc");
                    AssertTrue(host.PumpUntil(() => !host.App.Modals.IsActive), "closed");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "help_overlay", "? opens the help overlay listing global and list bindings", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    host.Press("?");
                    HelpOverlayModal help = (HelpOverlayModal)host.App.Modals.Top!;
                    string all = String.Join("\n", help.Lines);
                    TuiCase.Contains(all, "Ctrl+K = Command palette", "palette binding");
                    TuiCase.Contains(all, "g m = Missions", "go-to binding");
                    TuiCase.Contains(all, "Alt+Left = Back", "back binding");
                    TuiCase.Contains(all, "Space, Shift+Up/Down, Ctrl+A, Esc", "list keys");
                    TuiCase.Contains(host.Screen(), "Keyboard shortcuts", "title drawn");
                    host.Press("esc");
                    AssertFalse(host.App.Modals.IsActive, "closed");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "commands_rebinding_and_scope", "Screen commands shadow globals; user rebinding applies", () =>
            {
                CommandService commands = new CommandService();
                int global = 0;
                int screen = 0;
                commands.Register(new ArmadaCommand("x.global", "Global", CommandMenuEnum.View, () => global++, "ctrl+e"));
                commands.SetScreenCommands("s1", new[] { new ArmadaCommand("x.screen", "Screen", CommandMenuEnum.None, () => screen++, "ctrl+e") });
                commands.TryHandleKey(KeyEvent.Char('e', KeyModifiers.Ctrl), DateTime.UtcNow);
                AssertEqual(1, screen, "screen wins");
                AssertEqual(0, global, "global shadowed");
                AssertEqual(CommandMenuEnum.Actions, commands.Find("x.screen")!.Menu, "screen commands land in Actions");
                Dictionary<string, string> overrides = new Dictionary<string, string> { ["x.global"] = "f9" };
                commands.SetOverrides(overrides);
                commands.TryHandleKey(KeyEvent.Special(KeyCode.F9), DateTime.UtcNow);
                AssertEqual(1, global, "rebound key runs the command");
                DateTime now = DateTime.UtcNow;
                commands.Register(new ArmadaCommand("x.seq", "Seq", CommandMenuEnum.Go, () => global += 10, "g z"));
                AssertTrue(commands.TryHandleKey(KeyEvent.Char('g'), now), "prefix consumed");
                AssertEqual("g", commands.PendingPrefix, "pending");
                commands.TryHandleKey(KeyEvent.Char('z'), now.AddMilliseconds(5000));
                AssertEqual(1, global, "timed-out sequence does not fire");
                commands.TryHandleKey(KeyEvent.Char('g'), now);
                commands.TryHandleKey(KeyEvent.Char('z'), now.AddMilliseconds(100));
                AssertEqual(11, global, "sequence fires");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI palette, help, and commands", cases: cases);
        }
    }
}
