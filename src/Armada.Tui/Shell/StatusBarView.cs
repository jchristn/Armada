namespace Armada.Tui.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// The status bar: key hints for the current context, a pending go-to prefix, the refresh state ("auto 15s",
    /// "paused"), and the approvals count. Not focusable.
    /// </summary>
    public class StatusBarView : ArmadaWidget
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        /// <summary>
        /// Hints (key label, English description) for the current context.
        /// </summary>
        public List<KeyValuePair<string, string>> Hints { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Transient message (English) shown instead of the hints, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Context.</param>
        public StatusBarView(TuiContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            SurfaceText.FillRow(surface, 0, 0, width, Theme.StatusBar);
            string rightText = "";
            string? prefix = _Context.Commands.PendingPrefix;
            if (prefix != null) rightText += prefix + " ...  ";
            string refresh = _Context.Refresh.StatusText;
            if (refresh.Length > 0) rightText += T(refresh) + "  ";
            int approvals = _Context.Approvals.Count;
            if (approvals > 0) rightText += "[" + T("approvals") + ": " + approvals.ToString(CultureInfo.InvariantCulture) + "]";
            rightText = rightText.TrimEnd();
            int rw = TextCells.Width(rightText);
            int limit = Math.Max(0, width - rw - 2);
            int x = 1;
            if (!String.IsNullOrEmpty(Message))
            {
                SurfaceText.Draw(surface, x, 0, T(Message!), Theme.StatusBar, limit);
            }
            else
            {
                foreach (KeyValuePair<string, string> hint in Hints)
                {
                    string label = T(hint.Value);
                    int need = TextCells.Width(hint.Key) + 1 + TextCells.Width(label) + 2;
                    if (x + need > limit) break;
                    x += SurfaceText.Draw(surface, x, 0, hint.Key, Theme.StatusKey, limit - x);
                    x += 1;
                    x += SurfaceText.Draw(surface, x, 0, label, Theme.StatusBar, limit - x);
                    x += 2;
                }
            }

            if (rw > 0 && rw < width) SurfaceText.Draw(surface, width - rw - 1, 0, rightText, Theme.StatusBar.WithForeground(Theme.Accent.Foreground), rw);
        }

        #endregion
    }
}
