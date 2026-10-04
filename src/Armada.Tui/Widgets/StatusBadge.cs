namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// Maps a status name to a severity style and an ASCII marker so status is never conveyed by color alone (the
    /// dashboard's StatusBadge and its severity mapping). Thread-safe (stateless).
    /// </summary>
    public static class StatusBadge
    {
        #region Public-Methods

        /// <summary>
        /// Severity for a status: success, error, warning, or info (the dashboard's <c>statusToSeverity</c>, extended
        /// with running states).
        /// </summary>
        /// <param name="status">Status name.</param>
        /// <returns>success, error, warning, running, or info.</returns>
        public static string Severity(string? status)
        {
            if (String.IsNullOrEmpty(status)) return "info";
            string s = status!.ToLowerInvariant();
            if (s == "completed" || s == "complete" || s == "landed" || s == "passed" || s == "pass" || s.Contains("succeeded") || s == "healthy" || s == "idle" || s == "shipped") return "success";
            if (s == "failed" || s == "fail" || s == "error" || s.Contains("failed") || s == "unhealthy") return "error";
            if (s == "cancelled" || s == "stalled" || s == "stopping" || s.Contains("rolledback") || s.Contains("denied") || s == "warn" || s == "warning" || s == "review" || s.Contains("pending")) return "warning";
            if (s == "inprogress" || s == "running" || s == "working" || s == "active" || s == "assigned" || s == "testing" || s == "queued") return "running";
            return "info";
        }

        /// <summary>
        /// Style for a status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>Style.</returns>
        public static CellStyle Style(string? status, ArmadaTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            switch (Severity(status))
            {
                case "success": return theme.Success;
                case "error": return theme.Error;
                case "warning": return theme.Warning;
                case "running": return theme.Info;
                default: return theme.Text;
            }
        }

        /// <summary>
        /// ASCII marker for a status: <c>+</c> success, <c>x</c> error, <c>!</c> warning, <c>~</c> running, <c>-</c> other.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Marker.</returns>
        public static string Marker(string? status)
        {
            switch (Severity(status))
            {
                case "success": return "+";
                case "error": return "x";
                case "warning": return "!";
                case "running": return "~";
                default: return "-";
            }
        }

        /// <summary>
        /// Marker plus status text, for example <c>+ Complete</c>.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string Label(string? status)
        {
            return Marker(status) + " " + (status ?? "");
        }

        #endregion
    }
}
