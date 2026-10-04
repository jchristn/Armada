namespace Armada.Tui.Shell
{
    using System;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// The Ask dock (Ctrl+J): a bottom panel that will show the active Ask thread's live tail, the running turn, and
    /// pending approvals while another screen is open. This foundation renders the frame and a placeholder; the live
    /// content arrives with W2.7.
    /// </summary>
    public class AskDockView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Height in rows when visible. Default 7; clamped to 4..20.
        /// </summary>
        public int PreferredHeight
        {
            get { return _Height; }
            set { _Height = Math.Clamp(value, 4, 20); }
        }

        #endregion

        #region Private-Members

        private int _Height = 7;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            SurfaceText.FillRow(surface, 0, 0, width, IsFocused ? Theme.TabActive : Theme.Border);
            SurfaceText.Draw(surface, 0, 0, "-- " + T("Ask Armada") + " " + new string('-', Math.Max(0, width)), IsFocused ? Theme.Accent : Theme.Border, width);
            if (height > 2) SurfaceText.Draw(surface, 1, 2, T("The active conversation, its live reply, and pending approvals appear here (W2.7)."), Theme.Muted, width - 2);
            if (height > 3) SurfaceText.Draw(surface, 1, 3, "Ctrl+J " + T("Hide dock") + "   g a " + T("Open Ask Armada"), Theme.Muted, width - 2);
        }

        #endregion
    }
}
