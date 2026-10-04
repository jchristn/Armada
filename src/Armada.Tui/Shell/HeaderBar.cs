namespace Armada.Tui.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// The header: product name, tenant and user, role badge, server health, WebSocket Live/Offline, background job
    /// count, pending approvals, and the notification bell with its unread count. Every state is spelled out in text,
    /// never color alone. A second row shows the proxy strip when connected through Armada.Proxy. Not focusable.
    /// </summary>
    public class HeaderBar : ArmadaWidget
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        /// <summary>
        /// Rows needed (2 with the proxy strip).
        /// </summary>
        public int Rows
        {
            get { return 1 + (_Context.Session.Proxy != null ? 1 : 0) + (_Context.Session.DefaultCredentialsInUse ? 1 : 0); }
        }

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Context.</param>
        public HeaderBar(TuiContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            SurfaceText.FillRow(surface, 0, 0, width, Theme.Header);
            int x = SurfaceText.Draw(surface, 0, 0, " Armada ", Theme.HeaderAccent, width);
            if (_Context.Session.IsSignedIn)
            {
                x += SurfaceText.Draw(surface, x, 0, " [" + _Context.Session.TenantName + "] " + _Context.Session.UserEmail, Theme.Header, width - x);
                string role = _Context.Session.IsGlobalAdmin ? "Global Admin" : _Context.Session.IsTenantAdmin ? "Tenant Admin" : "";
                if (role.Length > 0) x += SurfaceText.Draw(surface, x, 0, "  [" + T(role) + "]", Theme.Header.WithForeground(Theme.Accent.Foreground), width - x);
            }

            List<KeyValuePair<string, CellStyle>> right = new List<KeyValuePair<string, CellStyle>>();
            if (_Context.Status.HealthChecked)
            {
                bool healthy = _Context.Status.Health != null;
                right.Add(Pair((healthy ? "* " + T("Healthy") : "x " + T("Unreachable")), healthy ? Theme.Success : Theme.Error));
            }

            bool live = _Context.Events.IsLive;
            right.Add(Pair(live ? "* " + T("Live") : "o " + T("Offline"), live ? Theme.Success : Theme.Muted));
            int jobs = _Context.Status.ActiveJobs.Count;
            if (jobs > 0) right.Add(Pair("(" + _Context.Loc.T("{count, plural, one {# running} other {# running}}", Services.LocalizationArgs.Of("count", jobs)) + ")", Theme.Info));
            int approvals = _Context.Approvals.Count;
            if (approvals > 0) right.Add(Pair("[!" + approvals.ToString(CultureInfo.InvariantCulture) + " " + T("approvals") + "]", Theme.Warning));
            int unread = _Context.Notifications.UnreadCount;
            right.Add(Pair("[" + T("bell") + " " + unread.ToString(CultureInfo.InvariantCulture) + "]", unread > 0 ? Theme.Warning : Theme.Muted));

            int totalWidth = 0;
            foreach (KeyValuePair<string, CellStyle> p in right) totalWidth += TextCells.Width(p.Key) + 2;
            int rx = Math.Max(x + 2, width - totalWidth);
            foreach (KeyValuePair<string, CellStyle> p in right)
            {
                if (rx >= width) break;
                rx += SurfaceText.Draw(surface, rx, 0, p.Key, p.Value.WithBackground(Theme.Header.Background), width - rx) + 2;
            }

            int row = 1;
            if (_Context.Session.DefaultCredentialsInUse && surface.Size.Height > row)
            {
                CellStyle warn = Theme.Warning.WithAttribute(CellAttributes.Reverse, true);
                SurfaceText.FillRow(surface, 0, row, width, warn);
                string text = " ! " + T("Default credentials are in use.") + " " + T("An admin@armada account still has the default password, or the default bearer token is active. Change the password (each tenant's admin@armada signs in and is prompted) before exposing this server beyond localhost.");
                SurfaceText.Draw(surface, 0, row, text, warn, width);
                row++;
            }

            if (_Context.Session.Proxy != null && surface.Size.Height > row)
            {
                Armada.Client.Models.ProxySessionContext proxy = _Context.Session.Proxy;
                string strip = " " + T("Proxy") + ": " + (proxy.SelectedInstanceId ?? proxy.SelectedInstance?.InstanceId ?? "-")
                    + "  " + (proxy.SelectedInstance?.State ?? "") + "  v" + (proxy.SelectedInstance?.ArmadaVersion ?? "?")
                    + "   [" + T("Switch Deployment") + "] [" + T("Proxy Logout") + "] (File menu)";
                SurfaceText.FillRow(surface, 0, row, width, Theme.Header.WithForeground(Theme.Warning.Foreground));
                SurfaceText.Draw(surface, 0, row, strip, Theme.Header.WithForeground(Theme.Warning.Foreground), width);
            }
        }

        #endregion

        #region Private-Methods

        private static KeyValuePair<string, CellStyle> Pair(string text, CellStyle style)
        {
            return new KeyValuePair<string, CellStyle>(text, style);
        }

        #endregion
    }
}
