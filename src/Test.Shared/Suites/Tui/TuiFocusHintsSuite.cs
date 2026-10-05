namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Services;
    using Armada.Tui.Shell;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Widgets;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Focus-aware status bar hints and the Ask approval affordances: the hints follow the focused control (the Ask
    /// composer says how to leave it and what that unlocks, the transcript and a focused proposal card show their own
    /// keys, a list's filter row says how to get back to the list, the dock says how to leave it), <c>F1</c> replaces
    /// <c>?</c> for help while typing, the pending-approval strip above the composer (worded for focus, pluralized,
    /// gone after the decision), <c>Alt+Down</c> to the oldest pending card, a mouse click on a card's [Approve]
    /// button, the "[typing]" focus marker, and the transcript's follow-the-tail rules.
    /// </summary>
    public sealed class TuiFocusHintsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.FocusHints";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "ask_hints_follow_focus", "Ask hints change between the composer, the transcript, and a focused proposal card", () =>
            {
                AskFixtures fx = PendingThread(1);
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = Loaded(host, 1);
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Composer), "composer focused on open");
                    string status = Status(host);
                    AssertTrue(status.StartsWith(" Esc Leave the message box (then a approve, r reject)", StringComparison.Ordinal), "composer: the first hint is how to leave it and what that unlocks: " + status);
                    TuiCase.Contains(status, "Alt+Down Go to approval", "composer: jump to the card");
                    TuiCase.Contains(status, "Enter Send", "composer: send");
                    TuiCase.Contains(status, "F1 Help", "help while typing is F1 (? would type)");
                    TuiCase.NotContains(status, "? Help", "no ? while typing");
                    TuiCase.NotContains(status, "a Approve", "no single-key decision while typing");
                    TuiCase.Contains(Composer(host, screen), "[typing]", "the composer shows the typing marker");

                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Transcript), "Esc moves to the transcript");
                    AssertEqual("amg_p1", screen.Transcript.SelectedKey, "pending card selected");
                    status = Status(host);
                    AssertTrue(status.StartsWith(" a Approve  r Reject  x Arguments", StringComparison.Ordinal), "card: its keys first: " + status);
                    TuiCase.Contains(status, "Esc Back to the message box", "card: the way back");
                    TuiCase.Contains(status, "? Help", "? help outside text");
                    TuiCase.NotContains(Composer(host, screen), "[typing]", "no typing marker once focus left the composer");

                    host.Press("up");
                    AssertEqual("amg_1", screen.Transcript.SelectedKey, "a plain message selected");
                    status = Status(host);
                    TuiCase.NotContains(status, "a Approve", "a plain message has no decision keys");
                    TuiCase.Contains(status, "Alt+Down Go to approval", "transcript: jump to the card");
                    TuiCase.Contains(status, "Up/Down Select", "transcript: move");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "pending_strip", "The pending-approval strip sits above the composer, adapts to focus, pluralizes, and disappears after the decisions", () =>
            {
                AskFixtures fx = PendingThread(2);
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = Loaded(host, 2);
                    string frame = host.Screen();
                    string strip = "! 2 actions waiting for approval: Esc, then a to approve or r to reject";
                    TuiCase.Contains(frame, strip, "plural strip from the composer");
                    string[] lines = frame.Split('\n');
                    int stripRow = Array.FindIndex(lines, l => l.Contains(strip, StringComparison.Ordinal));
                    int composerTop = host.Tui.Shell.LastLayout!.MainInner.Y + screen.Scope.RectOf(screen.Composer).Y;
                    // The strip wraps to a second row when the composer is narrow; either way its last row sits on the
                    // top line of the composer's box (see RegionFrames) and nothing is cut off.
                    int column = lines[stripRow].IndexOf(strip, StringComparison.Ordinal);
                    string stripText = lines[stripRow].Substring(column).TrimEnd(' ', '\u2502', '\u2503');
                    if (stripRow == composerTop - 3) stripText += " " + lines[stripRow + 1].Substring(column).Trim(' ', '\u2502', '\u2503');
                    else AssertEqual(composerTop - 2, stripRow, "strip directly above the composer's box");
                    AssertEqual(strip + " (Ctrl+A for all)", stripText, "the whole strip is shown");

                    host.Press("esc");
                    TuiCase.Contains(host.Screen(), "! 2 actions waiting for approval: a to approve or r to reject (Ctrl+A for all)", "on a pending card: no \"Esc, then\"");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_2/approve") == 1), "newest approved");
                    AssertTrue(host.WaitForText("! 1 action waiting for approval:"), "singular after one decision:\n" + host.Screen());
                    TuiCase.NotContains(host.Screen(), "1 actions", "pluralized correctly");

                    host.Press("esc").Press("alt+down");
                    AssertEqual("amg_p1", screen.Transcript.SelectedKey, "Alt+Down reaches the remaining card");
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_1/reject") == 1), "oldest rejected");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("waiting for approval", StringComparison.Ordinal)), "strip gone after the last decision:\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "alt_down_to_oldest", "Alt+Down from the composer focuses the oldest pending card, and a then approves it", () =>
            {
                AskFixtures fx = PendingThread(2);
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = Loaded(host, 2);
                    host.Press("alt+down");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Transcript), "transcript focused");
                    AssertEqual("amg_p1", screen.Transcript.SelectedKey, "oldest pending card selected");
                    AssertEqual("", screen.Composer.Text, "nothing typed into the composer");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_1/approve") == 1), "approve call for the oldest");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "mouse_approve_on_card", "A click on a card's [Approve] sends the approve call while the composer has focus", () =>
            {
                AskFixtures fx = PendingThread(1);
                using (TuiTestHost host = TuiCase.SignedIn(140, 50, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = Loaded(host, 1);
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Composer), "composer focused");
                    host.Screen();
                    AskBlock block = screen.Transcript.Blocks.First(b => b.Proposal != null && b.Proposal.Id == "aap_1");
                    AskCardButton approve = block.Buttons.First(b => b.Action == AskCardActionEnum.Approve);
                    Rect main = host.Tui.Shell.LastLayout!.MainInner;
                    Rect transcript = screen.Scope.RectOf(screen.Transcript);
                    int y = main.Y + transcript.Y + block.Top + approve.Line - screen.Transcript.ScrollOffset;
                    int x = main.X + transcript.X + 2 + approve.X;
                    string row = host.Screen().Split('\n')[y];
                    AssertEqual("[Approve]", row.Substring(x, approve.Width), "the button is drawn where it is hit-tested: " + row);
                    AssertEqual(0, fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_1/approve"), "nothing sent yet");
                    host.Click(x + 3, y);
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_1/approve") == 1), "approve call");
                    AssertEqual(0, fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/proposals/aap_1/reject"), "no reject");
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Proposals["aap_1"].Status == AskProposalStatusEnum.Executed), "executed");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filter_row_hints", "In a list's filter row the hints say Esc goes back to the list; on the grid they show the list keys", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/missions"))
                {
                    host.WaitForText("Missions");
                    string status = Status(host);
                    TuiCase.Contains(status, "Enter Open", "grid: list keys");
                    TuiCase.Contains(status, "? Help", "grid: ? help");
                    host.Press("/");
                    AssertTrue(host.Tui.Shell.FocusedLeaf() is SelectField<string>, "the Status select filter has focus first");
                    status = Status(host);
                    AssertTrue(status.StartsWith(" Enter Choose  Esc Back to the list  Tab Next filter", StringComparison.Ordinal), "select filter hints: " + status);
                    TuiCase.Contains(status, "? Help", "a select does not take text, so ? still opens help");
                    for (int i = 0; i < 4 && !(host.Tui.Shell.FocusedLeaf() is TextInput); i++) host.Press("tab");
                    IWidget? leaf = host.Tui.Shell.FocusedLeaf();
                    AssertTrue(leaf is TextInput, "a text filter has focus (" + (leaf?.GetType().Name ?? "none") + ")");
                    status = Status(host);
                    AssertTrue(status.StartsWith(" Esc Back to the list  Tab Next filter", StringComparison.Ordinal), "filter: how to leave first: " + status);
                    TuiCase.Contains(status, "F1 Help", "filter: F1 help");
                    TuiCase.NotContains(status, "Enter Open", "filter: no list keys while typing");
                    host.Press("esc");
                    status = Status(host);
                    TuiCase.Contains(status, "Enter Open", "back on the grid");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "dock_hints_and_marker", "The focused Ask dock says how to leave it and shows the typing marker", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"));
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/ask/ath_1", fx.Stub))
                {
                    host.PumpUntil(() => host.Tui.Ask.Conversation.Thread != null);
                    host.Tui.Context.Navigate("/missions");
                    host.Press("ctrl+j");
                    host.Screen();
                    TuiCase.NotContains(DockTitle(host), "[typing]", "no marker while the dock is not focused");
                    host.Tui.Shell.FocusPane("dock");
                    string status = Status(host);
                    AssertTrue(status.StartsWith(" Tab Leave the dock  Enter Send", StringComparison.Ordinal), "dock hints: " + status);
                    TuiCase.Contains(status, "F1 Help", "dock: F1 help");
                    TuiCase.Contains(DockTitle(host), "[typing]", "the focused dock shows the typing marker");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "follow_after_decision", "After selecting a proposal and approving it, new messages keep the transcript at the live tail", () =>
            {
                AskFixtures fx = LongThread(1);
                using (TuiTestHost host = TuiCase.SignedIn(120, 30, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = Loaded(host, 1);
                    host.Press("esc");
                    AssertEqual("amg_p1", screen.Transcript.SelectedKey, "proposal selected");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Proposals["aap_1"].Status == AskProposalStatusEnum.Executed), "approved");
                    Inject(host, fx, 100, 12);
                    AssertTrue(screen.Transcript.Following, "following the tail after the decision");
                    AssertEqual(0, screen.Transcript.NewBelow, "nothing hidden below");
                    AssertTrue(host.WaitForText("reply number 111"), "the newest message is on screen:\n" + host.Screen());
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "follow_with_selection_at_tail", "With a block selected at the tail, new messages keep following; PgUp detaches and counts new lines; End re-attaches", () =>
            {
                AskFixtures fx = LongThread(0);
                using (TuiTestHost host = TuiCase.SignedIn(120, 30, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = Loaded(host, 0);
                    screen.Scope.Focus(screen.Transcript);
                    host.Press("up");
                    AssertEqual("amg_40", screen.Transcript.SelectedKey, "the newest message selected");
                    AssertTrue(screen.Transcript.Following, "selecting a block on screen at the tail does not detach");
                    Inject(host, fx, 100, 6);
                    AssertTrue(screen.Transcript.Following, "still following with a block selected");
                    AssertTrue(host.WaitForText("reply number 105"), "the newest message is on screen");

                    host.Press("pgup");
                    AssertFalse(screen.Transcript.Following, "PgUp detaches");
                    Inject(host, fx, 200, 3);
                    AssertTrue(screen.Transcript.NewBelow > 0, "new lines counted while detached");
                    AssertTrue(host.WaitForText("new below"), "the indicator shows:\n" + host.Screen());
                    host.Press("end");
                    AssertTrue(screen.Transcript.Following, "End re-attaches");
                    AssertTrue(host.WaitForText("reply number 202"), "back at the tail");

                    host.Press("pgup");
                    AssertFalse(screen.Transcript.Following, "detached again");
                    screen.Scope.Focus(screen.Composer);
                    host.Type("hello").Press("enter");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_1/messages") == 1), "sent");
                    AssertTrue(screen.Transcript.Following, "a send re-attaches");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "strip_localized", "The pending-approval strip and the new hints are translated in every catalog locale, with the count and keys kept", () =>
            {
                string path = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "src", "Armada.Server", "wwwroot", "i18n", "armada.json");
                I18nCatalog catalog = ArmadaJson.Deserialize<I18nCatalog>(File.ReadAllText(path)) ?? throw new AssertionException("catalog");
                AssertTrue(catalog.Locales.Count >= 8, "every maintained locale is present");
                string[] strips = new string[]
                {
                    "{count, plural, one {# action waiting for approval: Esc, then a to approve or r to reject (Ctrl+A for all)} other {# actions waiting for approval: Esc, then a to approve or r to reject (Ctrl+A for all)}}",
                    "{count, plural, one {# action waiting for approval: a to approve or r to reject (Ctrl+A for all)} other {# actions waiting for approval: a to approve or r to reject (Ctrl+A for all)}}",
                    "{count, plural, one {# action waiting for approval: Alt+Down to review it (Ctrl+A for all)} other {# actions waiting for approval: Alt+Down to review the oldest (Ctrl+A for all)}}"
                };
                string[] phrases = new string[] { "typing", "Leave the message box (then a approve, r reject)", "Leave the message box", "Back to the message box", "Go to approval", "Back to the list", "Next filter", "Leave the dock" };
                LocalizationService loc = new LocalizationService();
                loc.SetCatalog(catalog);
                AssertEqual("1 action waiting for approval: Esc, then a to approve or r to reject (Ctrl+A for all)", loc.T(strips[0], LocalizationArgs.Of("count", 1)), "English singular");
                AssertEqual("3 actions waiting for approval: Esc, then a to approve or r to reject (Ctrl+A for all)", loc.T(strips[0], LocalizationArgs.Of("count", 3)), "English plural");
                foreach (string locale in catalog.Locales.Keys)
                {
                    loc.SetLocale(locale);
                    foreach (string key in strips)
                    {
                        string english = new LocalizationService().T(key, LocalizationArgs.Of("count", 3));
                        string text = loc.T(key, LocalizationArgs.Of("count", 3));
                        AssertTrue(text != english, locale + " translates the strip: " + text);
                        AssertTrue(text.Contains("3", StringComparison.Ordinal) && text.Contains("Ctrl+A", StringComparison.Ordinal), locale + " keeps the count and Ctrl+A: " + text);
                    }

                    foreach (string phrase in phrases) AssertTrue(loc.T(phrase) != phrase, locale + " translates \"" + phrase + "\"");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI focus-aware hints", cases: cases);
        }

        /// <summary>
        /// A thread with one user message and <paramref name="count"/> pending proposal cards (amg_p1, amg_p2, ...).
        /// </summary>
        private static AskFixtures PendingThread(int count)
        {
            AskFixtures fx = new AskFixtures();
            List<AskMessage> messages = new List<AskMessage> { AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "dispatch please") };
            List<AskActionProposal> pending = new List<AskActionProposal>();
            for (int i = 1; i <= count; i++)
            {
                AskActionProposal p = AskFixtures.Proposal("aap_" + i, "ath_1", "dispatch", AskProposalStatusEnum.Pending);
                p.CreatedUtc = DateTime.UtcNow.AddMinutes(-10 + i);
                fx.Decisions(p);
                pending.Add(p);
                AskMessage m = AskFixtures.Message("amg_p" + i, "ath_1", 1 + i, AskMessageRoleEnum.Assistant, AskMessageKindEnum.ActionProposal, "");
                m.ProposalId = p.Id;
                m.Proposal = p;
                messages.Add(m);
            }

            fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), messages.ToArray());
            fx.Pending["ath_1"] = pending;
            return fx;
        }

        /// <summary>
        /// A thread long enough to scroll (40 messages), optionally followed by pending proposal cards.
        /// </summary>
        private static AskFixtures LongThread(int proposals)
        {
            AskFixtures fx = new AskFixtures();
            List<AskMessage> messages = new List<AskMessage>();
            for (int i = 1; i <= 40; i++)
            {
                AskMessageRoleEnum role = i % 2 == 1 ? AskMessageRoleEnum.User : AskMessageRoleEnum.Assistant;
                messages.Add(AskFixtures.Message("amg_" + i, "ath_1", i, role, AskMessageKindEnum.Text, "message number " + i));
            }

            List<AskActionProposal> pending = new List<AskActionProposal>();
            for (int i = 1; i <= proposals; i++)
            {
                AskActionProposal p = AskFixtures.Proposal("aap_" + i, "ath_1", "dispatch", AskProposalStatusEnum.Pending);
                fx.Decisions(p);
                pending.Add(p);
                AskMessage m = AskFixtures.Message("amg_p" + i, "ath_1", 40 + i, AskMessageRoleEnum.Assistant, AskMessageKindEnum.ActionProposal, "");
                m.ProposalId = p.Id;
                m.Proposal = p;
                messages.Add(m);
            }

            fx.AddThread(AskFixtures.Thread("ath_1", "TUIKit fixes"), messages.ToArray());
            fx.Pending["ath_1"] = pending;
            return fx;
        }

        private static AskScreen Loaded(TuiTestHost host, int pending)
        {
            AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
            AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count > 0 && host.Tui.Ask.Conversation.PendingProposals().Count == pending && host.Tui.Ask.Captains.Count > 0), "conversation loaded");
            host.Screen();
            return screen;
        }

        /// <summary>
        /// Inject assistant replies "reply number N" as live events (and serve them on refetch).
        /// </summary>
        private static void Inject(TuiTestHost host, AskFixtures fx, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int n = start + i;
                AskMessage m = AskFixtures.Message("amg_r" + n, "ath_1", 1000 + n, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "reply number " + n);
                m.CreatedUtc = DateTime.UtcNow;
                fx.Messages["ath_1"].Add(m);
                host.Tui.Context.Events.Inject(AskFixtures.Event("ask.message", new AskMessageEvent { ThreadId = "ath_1", Message = m }));
            }

            host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Any(x => x.Id == "amg_r" + (start + count - 1)));
            host.Screen();
        }

        private static string Status(TuiTestHost host)
        {
            string[] lines = host.Screen().Split('\n');
            return lines[host.Height - 1];
        }

        private static string Composer(TuiTestHost host, AskScreen screen)
        {
            string[] lines = host.Screen().Split('\n');
            Rect main = host.Tui.Shell.LastLayout!.MainInner;
            Rect composer = screen.Scope.RectOf(screen.Composer);
            return String.Join("\n", lines.Skip(main.Y + composer.Y).Take(composer.Height));
        }

        private static string DockTitle(TuiTestHost host)
        {
            string[] lines = host.Screen().Split('\n');
            ShellLayout layout = host.Tui.Shell.LastLayout!;
            return lines[layout.DockInner.Y];
        }
    }
}
