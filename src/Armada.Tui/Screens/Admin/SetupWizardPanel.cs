namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The content of one setup wizard step: a heading, explanatory paragraphs, an optional context strip ("Fleet:
    /// Starter"), an optional Use Existing / Create New toggle, the step body (a form or text), and the step's
    /// action buttons. Focus order: toggle, body, actions. Not thread-safe.
    /// </summary>
    public class SetupWizardPanel : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// English heading.
        /// </summary>
        public string Heading { get; set; } = "";

        /// <summary>
        /// Paragraphs under the heading.
        /// </summary>
        public List<SetupWizardLine> Intro { get; } = new List<SetupWizardLine>();

        /// <summary>
        /// English context label, or empty for none.
        /// </summary>
        public string ContextLabel { get; set; } = "";

        /// <summary>
        /// Context value (already translated or data).
        /// </summary>
        public string ContextValue { get; set; } = "";

        /// <summary>
        /// Mode toggle, or null.
        /// </summary>
        public SetupWizardActionBar? ModeBar { get; private set; } = null;

        /// <summary>
        /// Body widget, or null.
        /// </summary>
        public IWidget? Body { get; private set; } = null;

        /// <summary>
        /// Step actions.
        /// </summary>
        public SetupWizardActionBar Actions { get; } = new SetupWizardActionBar();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="heading">English heading.</param>
        public SetupWizardPanel(string heading)
        {
            Heading = heading ?? "";
            // The mode toggle, the body, and the step's buttons are separate focus regions, each in its own box.
            Scope.RegionHost = true;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the mode toggle (call once, before <see cref="SetBody"/>).
        /// </summary>
        /// <param name="bar">Toggle bar.</param>
        public void SetModeBar(SetupWizardActionBar bar)
        {
            ModeBar = bar ?? throw new ArgumentNullException(nameof(bar));
            Rebuild();
        }

        /// <summary>
        /// Replace the body.
        /// </summary>
        /// <param name="body">Body, or null.</param>
        public void SetBody(IWidget? body)
        {
            Body = body;
            Rebuild();
        }

        /// <summary>
        /// Focus the body (or the actions when there is no focusable body).
        /// </summary>
        public void FocusBody()
        {
            if (Body == null || !Scope.Focus(Body)) Scope.Focus(Actions);
        }

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
            if (width < 4 || height < 1) return;
            int y = 0;
            SurfaceText.Draw(surface, 0, y++, T(Heading), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            foreach (SetupWizardLine line in Intro)
            {
                foreach (string wrapped in TextCells.Wrap(line.Text, Math.Max(1, width - line.Indent)))
                {
                    if (y >= height) break;
                    SurfaceText.Draw(surface, line.Indent, y++, wrapped, line.Style != null ? line.Style(Theme) : Theme.Muted, width - line.Indent);
                }
            }

            if (ContextLabel.Length > 0 && y < height)
            {
                int x = SurfaceText.Draw(surface, 0, y, T(ContextLabel) + ": ", Theme.Muted, width);
                SurfaceText.Draw(surface, x, y, ContextValue, Theme.Text.WithAttribute(CellAttributes.Bold, true), width - x);
                y++;
            }

            if (ModeBar != null && y < height)
            {
                y++;
                int mh = Math.Max(1, ModeBar.HeightFor(width));
                Scope.RenderChild(surface, ModeBar, new Rect(0, y, width, mh));
                y += mh;
            }

            int actionsHeight = Actions.HeightFor(width);
            int bodyTop = y + 1;
            int bodyHeight = Math.Max(1, height - bodyTop - (actionsHeight > 0 ? actionsHeight + 1 : 0));
            if (Body != null && bodyTop < height) Scope.RenderChild(surface, Body, new Rect(0, bodyTop, width, Math.Min(bodyHeight, height - bodyTop)));
            int actionsTop = Math.Min(height - actionsHeight, bodyTop + bodyHeight + 1);
            if (actionsHeight > 0 && actionsTop >= 0 && actionsTop < height) Scope.RenderChild(surface, Actions, new Rect(0, actionsTop, width, actionsHeight));
        }

        #endregion

        #region Private-Methods

        private void Rebuild()
        {
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            Scope.Clear();
            if (ModeBar != null) AddChild(ModeBar);
            if (Body != null) AddChild(Body);
            AddChild(Actions);
            if (Body == null || !Scope.Focus(Body)) Scope.Focus(Actions);
            if (active) Scope.SetActive(true);
        }

        #endregion
    }
}
