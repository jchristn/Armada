namespace Armada.Tui.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// Draws the active toasts of <see cref="NotificationService.Toasts"/> (a TUIKit <c>NotificationCenter</c>, which
    /// keeps, expires, and coalesces them, and spells each severity as a label such as <c>[!]</c>) at the top right below
    /// the menu bar. Armada draws them rather than TUIKit's renderer because part of its look has no TUIKit equivalent:
    /// newest first, half the screen up to 60 cells, two wrapped lines, the newest four, and the action as a key line
    /// (<c>[Ctrl+O] Open</c>, the key bound to <c>NotificationCenter.InvokeLatestAction</c>) rather than a clickable
    /// button. Thread-safe (stateless).
    /// </summary>
    public static class ToastLayer
    {
        #region Public-Members

        /// <summary>
        /// Key that runs the newest toast's action.
        /// </summary>
        public const string ActionKey = "Ctrl+O";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Draw active toasts.
        /// </summary>
        /// <param name="surface">Full-screen surface.</param>
        /// <param name="top">First row below the header.</param>
        /// <param name="toasts">Toasts, oldest first.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="loc">Localizer.</param>
        public static void Render(ISurface surface, int top, IReadOnlyList<ToastEntry> toasts, ArmadaTheme theme, ITextLocalizer loc)
        {
            if (toasts == null || toasts.Count == 0) return;
            int width = surface.Size.Width;
            int boxWidth = Math.Min(60, Math.Max(24, width / 2));
            int y = top;
            foreach (ToastEntry toast in toasts.Reverse().Take(4))
            {
                CellStyle style = toast.Severity switch
                {
                    NotificationSeverityEnum.Success => theme.ToastSuccess,
                    NotificationSeverityEnum.Warning => theme.ToastWarning,
                    NotificationSeverityEnum.Error => theme.ToastError,
                    _ => theme.ToastInfo
                };
                string label = toast.SeverityLabel.Length > 0 ? toast.SeverityLabel + " " : "";
                List<string> lines = TextCells.Wrap(label + toast.Text.Trim() + toast.RepeatSuffix, boxWidth - 2).Take(2).ToList();
                if (toast.ActionLabel != null) lines.Add("[" + ActionKey + "] " + loc.T(toast.ActionLabel));
                int x = Math.Max(0, width - boxWidth - 1);
                for (int i = 0; i < lines.Count && y < surface.Size.Height; i++)
                {
                    SurfaceText.FillRow(surface, x, y, boxWidth, style);
                    SurfaceText.Draw(surface, x + 1, y, lines[i], i == lines.Count - 1 && toast.ActionLabel != null ? style.WithForeground(theme.Accent.Foreground) : style, boxWidth - 2);
                    y++;
                }

                if (y >= surface.Size.Height - 2) break;
            }
        }

        #endregion
    }
}
