namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Modals;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Testing;
    using TUIKit.Widgets;
    using Button = Armada.Tui.Widgets.Button;
    using ButtonRow = Armada.Tui.Widgets.ButtonRow;
    using ChartSeries = Armada.Tui.Widgets.ChartSeries;
    using FocusScope = Armada.Tui.Widgets.FocusScope;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Shared widgets: text input, select and multi-select fields, tri-state, date, form view (validation, dirty,
    /// Ctrl+S), detail view copy, wizard, drawer, focus scopes, the binding adapter, viewers, and dialogs.
    /// </summary>
    public sealed class TuiWidgetSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Widgets";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "text_input_editing", "TextInput edits by grapheme, raises changes, and masks", () =>
            {
                TextInput input = new TextInput();
                List<string> changes = new List<string>();
                input.ValueChanged += (s, e) => changes.Add(e.NewValue);
                input.OnFocusChanged(true);
                foreach (char c in "hello") input.HandleKey(KeyEvent.Char(c));
                input.HandleKey(KeyEvent.Special(KeyCode.Left));
                input.HandleKey(KeyEvent.Special(KeyCode.Backspace));
                AssertEqual("helo", input.Value, "backspace before caret");
                input.Insert("\u4fee\u590d");
                AssertEqual("hel\u4fee\u590do", input.Value, "insert at caret");
                input.HandleKey(KeyEvent.Char('w', KeyModifiers.Ctrl));
                AssertEqual("o", input.Value, "ctrl+w deletes a word");
                input.HandleKey(KeyEvent.Char('u', KeyModifiers.Ctrl));
                AssertEqual("", input.Value, "ctrl+u clears");
                AssertTrue(changes.Count >= 5, "change events");
                input.Masked = true;
                input.Value = "secret";
                string frame = Snapshot.RenderWidget(input, 20, 1);
                AssertFalse(frame.Contains("secret"), "masked");
                input.HandleKey(KeyEvent.Char('r', KeyModifiers.Ctrl));
                AssertTrue(Snapshot.RenderWidget(input, 20, 1).Contains("secret"), "revealed");
                input.Validator = v => v.Length < 3 ? "Too short." : null;
                input.Value = "ab";
                AssertFalse(input.Validate(), "invalid");
                AssertEqual("Too short.", input.Error, "error");
            }));

            cases.Add(TuiCase.Sync(Suite, "select_fields", "Select, multi-select, tri-state, and date fields", () =>
            {
                FakeModalHost host = new FakeModalHost();
                SelectField<string> select = new SelectField<string>();
                select.ModalHost = host;
                select.Options = new List<SelectOption<string>> { new SelectOption<string>("a", "Alpha"), new SelectOption<string>("b", "Beta") };
                string? changed = null;
                select.ValueChanged += (s, e) => changed = e.NewValue;
                select.HandleKey(KeyEvent.Special(KeyCode.Enter));
                PickerModal<string> picker = (PickerModal<string>)host.Shown.Last();
                picker.HandleKey(KeyEvent.Char('b'));
                picker.HandleKey(KeyEvent.Char('e'));
                picker.HandleKey(KeyEvent.Special(KeyCode.Enter));
                host.Complete();
                AssertEqual("b", select.Value, "picked by filter");
                AssertEqual("b", changed, "event");
                select.HandleKey(KeyEvent.Special(KeyCode.Right));
                AssertEqual("a", select.Value, "right cycles");
                select.Required = true;
                select.Choose(null);
                AssertFalse(select.ValidateField(), "required");

                MultiSelectField<string> multi = new MultiSelectField<string>();
                multi.ModalHost = host;
                multi.Options = new List<SelectOption<string>> { new SelectOption<string>("x", "X"), new SelectOption<string>("y", "Y"), new SelectOption<string>("z", "Z") };
                multi.HandleKey(KeyEvent.Char(' '));
                MultiPickerModal<string> mp = (MultiPickerModal<string>)host.Shown.Last();
                mp.HandleKey(KeyEvent.Char(' '));
                mp.HandleKey(KeyEvent.Special(KeyCode.Down));
                mp.HandleKey(KeyEvent.Special(KeyCode.Down));
                mp.HandleKey(KeyEvent.Char(' '));
                mp.HandleKey(KeyEvent.Special(KeyCode.Enter));
                host.Complete();
                AssertEqual("x,z", String.Join(",", multi.Values), "multi values in option order");
                AssertTrue(Snapshot.RenderWidget(multi, 30, 1).Contains("X, Z"), "labels shown");

                TriStateField tri = new TriStateField();
                tri.HandleKey(KeyEvent.Char(' '));
                AssertEqual(true, tri.AsBoolean, "yes");
                tri.HandleKey(KeyEvent.Char(' '));
                AssertEqual(false, tri.AsBoolean, "no");
                tri.HandleKey(KeyEvent.Special(KeyCode.Left));
                AssertEqual(true, tri.AsBoolean, "left goes back");

                DateTime now = new DateTime(2026, 10, 4, 15, 30, 0);
                AssertEqual(new DateTime(2026, 9, 27, 15, 30, 0), DateField.Parse("-7d", now), "relative");
                AssertEqual(new DateTime(2026, 10, 1), DateField.Parse("2026-10-01", now), "iso");
                AssertNull(DateField.Parse("garbage", now), "invalid");
                DateField date = new DateField(() => now);
                date.Value = "today";
                date.HandleKey(KeyEvent.Special(KeyCode.Up));
                AssertEqual("2026-10-05", date.Value, "up adds a day");
                date.Value = "bad";
                AssertFalse(date.ValidateField(), "validation message");
            }));

            cases.Add(TuiCase.Sync(Suite, "form_view", "FormView validates, tracks dirty state, saves with Ctrl+S, and scrolls", () =>
            {
                FormView form = new FormView();
                InputField name = form.AddField("Name", new InputField());
                name.Validator = v => String.IsNullOrWhiteSpace(v) ? "Name is required." : null;
                form.AddSection("Advanced");
                InputField branch = form.AddField("Default branch", new InputField(), "Used for landing.");
                for (int i = 0; i < 20; i++) form.AddField("Extra " + i, new InputField());
                form.MarkClean();
                int saves = 0;
                form.SaveRequested += (s, e) => saves++;
                form.OnFocusChanged(true);
                form.HandleKey(KeyEvent.Char('s', KeyModifiers.Ctrl));
                AssertEqual(0, saves, "blocked by validation");
                string frame = Snapshot.RenderWidget(form, 60, 12);
                AssertTrue(frame.Contains("Name is required."), "inline error");
                foreach (char c in "fleet") form.HandleKey(KeyEvent.Char(c));
                AssertTrue(form.IsDirty, "dirty");
                form.HandleKey(KeyEvent.Char('s', KeyModifiers.Ctrl));
                AssertEqual(1, saves, "saved");
                form.MarkClean();
                AssertFalse(form.IsDirty, "clean after save");
                for (int i = 0; i < 15; i++) form.HandleKey(KeyEvent.Special(KeyCode.Tab));
                frame = Snapshot.RenderWidget(form, 60, 12);
                AssertTrue(frame.Contains("Extra 1"), "scrolled to focused field");
                AssertFalse(frame.Contains("Advanced"), "scrolled past the top");
            }));

            cases.Add(TuiCase.Sync(Suite, "detail_wizard_drawer", "DetailView copies, Wizard steps and validates, Drawer closes on Esc", () =>
            {
                DetailView detail = new DetailView();
                detail.AddSection("Identifiers");
                detail.Add("ID", "msn_123");
                detail.Add("Branch", "armada/fix", t => t.Code);
                string? copied = null;
                detail.CopyRequested += (s, v) => copied = v;
                detail.OnFocusChanged(true);
                detail.HandleKey(KeyEvent.Special(KeyCode.Down));
                detail.HandleKey(KeyEvent.Char('y'));
                AssertEqual("armada/fix", copied, "copied value");
                AssertTrue(Snapshot.RenderWidget(detail, 40, 5).Contains("msn_123"), "rendered");

                bool block = true;
                WizardStep one = new WizardStep("Fleet", new TextBlock("fleet step"));
                one.Validate = () => block ? "Pick a fleet." : null;
                WizardStep two = new WizardStep("Vessel", new TextBlock("vessel step"));
                two.Optional = true;
                WizardStep three = new WizardStep("Captain", new TextBlock("captain step"));
                Wizard wizard = new Wizard(new[] { one, two, three });
                bool finished = false;
                wizard.Finished += (s, e) => finished = true;
                wizard.Next();
                AssertEqual(0, wizard.Index, "blocked");
                AssertTrue(Snapshot.RenderWidget(wizard, 60, 10).Contains("Pick a fleet."), "error shown");
                block = false;
                wizard.Next();
                AssertEqual(1, wizard.Index, "advanced");
                AssertTrue(Snapshot.RenderWidget(wizard, 60, 10).Contains("Step 2 / 3"), "header");
                wizard.HandleKey(KeyEvent.Special(KeyCode.Right, KeyModifiers.Alt));
                wizard.Next();
                AssertTrue(finished, "finished");

                Drawer drawer = new Drawer();
                drawer.Open("Target", new TextBlock("detail"));
                bool closed = false;
                drawer.Closed += (s, e) => closed = true;
                drawer.HandleKey(KeyEvent.Special(KeyCode.Escape));
                AssertTrue(closed && !drawer.IsOpen, "closed");
            }));

            cases.Add(TuiCase.Sync(Suite, "focus_scope_nested", "Focus scopes route keys to the focused child and bubble Tab at the ends", () =>
            {
                FormView inner = new FormView();
                inner.ShowButtons = false;
                InputField a = inner.AddField("A", new InputField());
                InputField b = inner.AddField("B", new InputField());
                FocusScope outer = new FocusScope();
                Button before = outer.Add(new Button("Before"));
                outer.Add(inner);
                Button after = outer.Add(new Button("After"));
                outer.SetActive(true);
                AssertTrue(before.IsFocused, "first focused");
                outer.HandleKey(KeyEvent.Special(KeyCode.Tab));
                AssertTrue(inner.IsFocused && a.IsFocused, "entered inner scope at its first field");
                outer.HandleKey(KeyEvent.Special(KeyCode.Tab));
                AssertTrue(b.IsFocused, "moved within inner");
                outer.HandleKey(KeyEvent.Special(KeyCode.Tab));
                AssertTrue(after.IsFocused && !b.IsFocused, "bubbled out at the end");
                outer.HandleKey(KeyEvent.Special(KeyCode.Tab, KeyModifiers.Shift));
                AssertTrue(b.IsFocused, "shift+tab enters inner at its last field");
                outer.HandleKey(KeyEvent.Char('q'));
                AssertEqual("q", b.Value, "keys reach the focused leaf");
                AssertTrue(ReferenceEquals(b, outer.FocusedLeaf()), "leaf");
            }));

            cases.Add(TuiCase.Sync(Suite, "focus_path_hideable", "TUIKit's FocusPath follows Armada scopes; hidden widgets and emptied containers are not stops (IHideable)", () =>
            {
                FormView form = new FormView();
                form.ShowButtons = false;
                InputField a = form.AddField("A", new InputField());
                InputField b = form.AddField("B", new InputField());
                ButtonRow row = new ButtonRow();
                Button only = row.Add(new Button("Only"));
                FocusScope outer = new FocusScope();
                outer.Add(form);
                outer.Add(row);
                outer.SetActive(true);
                FocusPath path = FocusPath.Build("r", form);
                AssertEqual(2, path.Depth, "form then its focused field");
                AssertTrue(ReferenceEquals(a, path.Leaf) && path.Contains(form), "leaf is the first field");
                form.HandleKey(KeyEvent.Special(KeyCode.Tab));
                AssertTrue(ReferenceEquals(b, FocusPath.Build("r", form).Leaf), "the path follows Tab inside the scope");

                AssertTrue(row.IsVisible && FocusScope.IsFocusStop(row), "a row with a visible button is a stop");
                only.Visible = false;
                AssertFalse(((IHideable)row).IsVisible, "a row whose buttons are all hidden reports hidden");
                AssertFalse(FocusScope.IsFocusStop(row) || TUIKit.Widgets.FocusScope.IsFocusable(row), "and is not a stop for Armada or TUIKit");
                b.Visible = false;
                AssertFalse(TUIKit.Widgets.FocusScope.IsFocusable(b), "a hidden field is not focusable by TUIKit's rule");
                AssertTrue(form.IsVisible, "the form still has a field to focus");
            }));

            cases.Add(TuiCase.Sync(Suite, "binding_adapter", "ObservedWidget raises change events for TUIKit widgets", () =>
            {
                TUIKit.Widgets.Checkbox box = new TUIKit.Widgets.Checkbox("Active");
                ObservedWidget<bool> observed = new ObservedWidget<bool>(box, () => box.Checked);
                int changes = 0;
                observed.Changed += (s, e) => changes++;
                observed.HandleKey(KeyEvent.Char(' '));
                AssertTrue(box.Checked, "toggled");
                AssertEqual(1, changes, "change raised");
                observed.HandleKey(KeyEvent.Special(KeyCode.Up));
                AssertEqual(1, changes, "no change no event");
            }));

            cases.Add(TuiCase.Sync(Suite, "viewers", "JSON, diff, log, Markdown, and chart viewers", () =>
            {
                JsonViewer json = new JsonViewer("{\"Id\":\"msn_1\",\"Status\":\"Complete\"}");
                AssertTrue(json.PlainText.Contains("\n"), "pretty printed");
                AssertTrue(Snapshot.RenderWidget(json, 40, 6).Contains("\"Status\""), "rendered");
                json.Search("Complete");
                AssertTrue(json.FindNext(), "search");

                DiffViewer diff = new DiffViewer("diff --git a/x.cs b/x.cs\n--- a/x.cs\n+++ b/x.cs\n@@ -1 +1 @@\n-old\n+new\n");
                AssertEqual("x.cs", diff.Files.Single(), "file list");
                string d = Snapshot.RenderWidget(diff, 40, 8);
                AssertTrue(d.Contains("-old") && d.Contains("+new"), "markers kept");

                LogViewer log = new LogViewer();
                for (int i = 0; i < 50; i++) log.Append("line " + i + "\u001b[31m red\u001b[0m");
                string l = Snapshot.RenderWidget(log, 40, 5);
                AssertTrue(l.Contains("line 49"), "follows the tail");
                AssertFalse(l.Contains("\u001b"), "ansi stripped");
                log.HandleKey(KeyEvent.Special(KeyCode.Home));
                AssertFalse(log.Follow, "home stops following");
                AssertTrue(Snapshot.RenderWidget(log, 40, 5).Contains("line 0"), "top");

                MarkdownView md = new MarkdownView("# Title\n\n- item one\n- item two");
                AssertTrue(Snapshot.RenderWidget(md, 40, 6).Contains("item two"), "markdown");

                ChartView chart = new ChartView();
                chart.Title = "Missions";
                chart.Series.Add(new ChartSeries("Complete", new double[] { 1, 3, 2, 5 }));
                chart.Labels.AddRange(new[] { "Mon", "Tue", "Wed", "Thu" });
                AssertTrue(Snapshot.RenderWidget(chart, 40, 8).Contains("Complete: 11"), "legend total");
                AssertTrue(chart.ToTextTable().Contains("Thu\t5"), "text table");
            }));

            cases.Add(TuiCase.Sync(Suite, "dialogs", "Confirm (typed delete) and error dialogs", () =>
            {
                ConfirmDialog confirm = new ConfirmDialog("Delete vessel", "Delete vessel \"TUIKit\"? This cannot be undone.", "Delete", "Cancel", "delete");
                AssertFalse(confirm.CanConfirm, "needs typing");
                confirm.HandleKey(KeyEvent.Special(KeyCode.Enter));
                AssertFalse(confirm.IsClosed, "enter ignored until typed");
                foreach (char c in "delete") confirm.HandleKey(KeyEvent.Char(c));
                AssertTrue(confirm.CanConfirm, "typed");
                confirm.HandleKey(KeyEvent.Special(KeyCode.Enter));
                AssertEqual(true, confirm.Completion.Result, "confirmed");

                ConfirmDialog simple = new ConfirmDialog("Stop", "Stop all captains?");
                simple.HandleKey(KeyEvent.Char('n'));
                AssertEqual(false, simple.Completion.Result, "n cancels");

                ErrorDialog error = new ErrorDialog("Request failed", "Vessel not found", "not_found", "req_abc", 404, true);
                AssertTrue(error.Details().Contains("req_abc"), "request id in details");
                error.HandleKey(KeyEvent.Special(KeyCode.Left));
                error.HandleKey(KeyEvent.Special(KeyCode.Left));
                error.HandleKey(KeyEvent.Special(KeyCode.Enter));
                AssertEqual("retry", error.Completion.Result, "retry");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI widgets", cases: cases);
        }
    }
}
