namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Current Session panel of Planning (the dashboard's PlanningTranscriptCard): the session header (title,
    /// status, captain, runtime, vessel, branch, pipeline, playbooks, updated, message count, failure reason), the
    /// transcript, the composer, and a footer with the stream and show-thinking toggles. <c>Esc</c> in the composer
    /// moves to the transcript; <c>Tab</c> moves between them. Not thread-safe.
    /// </summary>
    public class PlanningTranscriptPanel : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Transcript.
        /// </summary>
        public PlanningTranscriptView Transcript { get; }

        /// <summary>
        /// Composer.
        /// </summary>
        public PlanningComposer Composer { get; }

        #endregion

        #region Private-Members

        private readonly PlanningScreen _Owner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="owner">Planning screen.</param>
        public PlanningTranscriptPanel(PlanningScreen owner)
        {
            _Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Transcript = new PlanningTranscriptView(owner);
            Composer = owner.Composer;
            AddChild(Transcript);
            AddChild(Composer);
            Scope.Wrap = true;
            // The transcript and the composer are separate focus regions, each in its own box.
            Scope.RegionHost = true;
            Scope.Focus(Composer);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape && ReferenceEquals(Scope.Focused, Composer))
            {
                Scope.Focus(Transcript);
                return true;
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && key.Rune == 'i' && ReferenceEquals(Scope.Focused, Transcript))
            {
                Scope.Focus(Composer);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 4) return;
            PlanningSessionDetailView detail = _Owner.CurrentDetail();
            Transcript.Visible = detail.HasSession;
            if (!detail.HasSession)
            {
                // No session yet: the composer's box holds the explanation (the transcript is not a Tab stop).
                SurfaceText.Draw(surface, 0, 0, T("Current Session"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
                string text = _Owner.SessionId == null
                    ? T("Choose an existing planning session from the table above, or start a new one to begin chatting with a captain.")
                    : _Owner.LoadingDetail ? T("Loading planning session...") : T("Planning session not found.");
                int y0 = 2;
                foreach (string line in TextCells.Wrap(text, width)) SurfaceText.Draw(surface, 0, y0++, line, Theme.Muted, width);
                Scope.Place(Composer, new Rect(0, 2, width, Math.Max(1, height - 2)));
                return;
            }

            OpsDocument doc = new OpsDocument(Theme, Localizer);
            doc.Add(StyledText.From(detail.Title, Theme.Accent.WithAttribute(CellAttributes.Bold, true)));
            doc.Text(Localizer.T("Chat with {{captain}} against {{vessel}}, keep the transcript intact, and promote the right reply into dispatch.", Services.LocalizationArgs.Of("captain", detail.CaptainName, "vessel", detail.VesselName)), Theme.Muted);
            string summary = T("Captain") + ": " + detail.CaptainName + "  " + T("Runtime") + ": " + detail.Runtime + "  " + T("Vessel") + ": " + detail.VesselName
                + "  " + T("Branch") + ": " + detail.Branch + "  " + T("Pipeline") + ": " + detail.Pipeline + "  " + T("Playbooks") + ": " + detail.PlaybookCount
                + "  " + T("Updated") + ": " + detail.Updated + "  " + T("Messages") + ": " + detail.MessageCount;
            doc.Text(summary, Theme.Text);
            if (!String.IsNullOrEmpty(detail.FailureReason)) doc.Text("! " + detail.FailureReason, Theme.Error);
            int headerRows = OpsDraw.Lines(surface, 0, 0, doc.Lines, width, Math.Max(1, height / 3), Theme.Text);

            // Rows: header, line, transcript, line, composer, line, footer (the lines are box edges, see RegionFrames).
            int composerRows = Math.Clamp(Composer.Editor.VisualLineCount(Math.Max(10, width)), 2, 6);
            int footerY = height - 1;
            int composerY = footerY - 1 - composerRows;
            int transcriptTop = headerRows + 1;
            if (composerY - 1 - transcriptTop >= 1)
            {
                Scope.RenderChild(surface, Transcript, new Rect(0, transcriptTop, width, composerY - 1 - transcriptTop));
            }

            Composer.Placeholder = _Owner.ComposerEnabled ? "Describe the problem, ask for a plan, or negotiate the next steps with the captain." : "Message the captain...";
            if (composerY >= 0) Scope.RenderChild(surface, Composer, new Rect(0, composerY, width, composerRows));
            string toggles = (_Owner.Streaming ? "[x] " : "[ ] ") + T("Stream responses") + " (Alt+S)  " + (_Owner.ShowThinking ? "[x] " : "[ ] ") + T("Show thinking") + " (Alt+T)";
            string right = _Owner.Busy ? "Ctrl+C " + T("Stop") : "Enter " + T("Send") + "  Ctrl+J " + T("newline") + "  Ctrl+E " + T("Editor");
            int x = SurfaceText.Draw(surface, 0, footerY, toggles + "   ", Theme.Muted, width);
            x += SurfaceText.Draw(surface, x, footerY, T("AI can make mistakes. Check answers."), Theme.Muted, Math.Max(0, width - x - TextCells.Width(right) - 1));
            SurfaceText.Draw(surface, Math.Max(x, width - TextCells.Width(right)), footerY, right, _Owner.Busy ? Theme.Warning : Theme.Accent, width);
        }

        #endregion
    }
}
