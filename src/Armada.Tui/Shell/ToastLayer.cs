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
    /// Draws toasts at the top right below the header (TUIKit gap U6: its toasts are one line, 40 columns, with no
    /// actions). Each toast is up to 60 cells wide, wraps to two lines, shows its severity in text, and shows its action
    /// key (<c>Ctrl+O</c>) when it has one. Thread-safe (stateless).
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
                string label = toast.Severity switch
                {
                    NotificationSeverityEnum.Success => "[ok] ",
                    NotificationSeverityEnum.Warning => "[!] ",
                    NotificationSeverityEnum.Error => "[x] ",
                    _ => "[i] "
                };
                List<string> lines = TextCells.Wrap(label + toast.Text, boxWidth - 2).Take(2).ToList();
                if (toast.ActionLabel != null) lines.Add("[" + ActionKey + "] " + loc.T(toast.ActionLabel));
                int x = Math.Max(0, width - boxWidth - 1);
                for (int i = 0; i < lines.Count && y < surface.Size.Height; i++)
                {
                    SurfaceText.FillRow(surface, x, y, boxWidth, style);
                    SurfaceText.Draw(surface, x + 1, y, lines[i], i == lines.Count - 1 && toast.ActionLabel != null ? style.WithForeground(theme.Accent.Foreground) : style, boxWidth - 2);
                    y++;
                }

                y++;
                if (y >= surface.Size.Height - 2) break;
            }
        }

        #endregion
    }
}
