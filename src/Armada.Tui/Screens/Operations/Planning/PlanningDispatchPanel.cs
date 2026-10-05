namespace Armada.Tui.Screens.Operations
{
    using System;
    using Armada.Core.Models;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Dispatch From Session panel of Planning (the dashboard's PlanningDispatchCard): the selected reply id,
    /// voyage title and mission description (seeded from the selected assistant reply or a summary), and Summarize
    /// Draft, Open In Dispatch, and Dispatch (<c>Ctrl+S</c>). Not thread-safe.
    /// </summary>
    public class PlanningDispatchPanel : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Form.
        /// </summary>
        public FormView Form { get; } = new FormView();

        /// <summary>
        /// Summarize Draft.
        /// </summary>
        public Button SummarizeButton { get; }

        /// <summary>
        /// Open In Dispatch.
        /// </summary>
        public Button OpenButton { get; }

        #endregion

        #region Private-Members

        private readonly PlanningScreen _Owner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="owner">Planning screen.</param>
        public PlanningDispatchPanel(PlanningScreen owner)
        {
            _Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            SummarizeButton = new Button("Summarize Draft", owner.Summarize);
            OpenButton = new Button("Open In Dispatch", owner.OpenDraftInDispatch);
            ButtonRow extra = new ButtonRow();
            extra.Add(SummarizeButton);
            extra.Add(OpenButton);
            Form.SaveButton.Label = "Dispatch";
            Form.SaveButton.Hint = "Ctrl+S";
            Form.DiscardButton.Visible = false;
            Form.SaveRequested += (s, e) => owner.DispatchFromSession();
            Form.AddField("Voyage Title", owner.DispatchTitle);
            Form.AddField("Mission Description", owner.DispatchDescription, null, 10);
            Form.AddField("Draft", extra);
            AddChild(Form);
            Scope.Focus(Form);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 3) return;
            OpsDocument doc = new OpsDocument(Theme, Localizer);
            PlanningSessionMessage? selected = _Owner.SelectedMessage();
            StyledText head = StyledText.From(T("Dispatch From Session"), Theme.Accent.WithAttribute(CellAttributes.Bold, true));
            if (selected != null) head = head.Append(StyledText.From("   " + selected.Id, Theme.Muted));
            doc.Add(head);
            doc.Note("Select an assistant response, optionally summarize it into a cleaner draft, then dispatch or open the draft in the main dispatch page.");
            if (!_Owner.CurrentDetail().HasSession) doc.Note("Choose an existing planning session from the table above, or start a new one to begin chatting with a captain.", Theme.Warning);
            int y = OpsDraw.Lines(surface, 0, 0, doc.Lines, width, Math.Max(1, height / 3), Theme.Text) + 1;
            SummarizeButton.Label = _Owner.Summarizing ? "Summarizing..." : "Summarize Draft";
            SummarizeButton.Enabled = _Owner.CanSummarize();
            OpenButton.Enabled = _Owner.CanOpenInDispatch();
            Form.SaveButton.Label = _Owner.DispatchingDraft ? "Dispatching..." : "Dispatch";
            Form.SaveButton.Enabled = _Owner.CanDispatch();
            Form.MarkClean();
            if (y < height) Scope.RenderChild(surface, Form, new Rect(0, y, width, height - y));
        }

        #endregion
    }
}
