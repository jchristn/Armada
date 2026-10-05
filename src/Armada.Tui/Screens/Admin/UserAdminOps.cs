namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;

    /// <summary>
    /// Shared behavior of the Tenants, Users, and Credentials screens: proxy (remote) mode detection and its banner,
    /// Yes/No cells, and the dashboard's sequential bulk delete with a success/failure summary.
    /// </summary>
    public static class UserAdminOps
    {
        #region Public-Methods

        /// <summary>
        /// The selected Armada.Proxy deployment when connected through the proxy, or null (writes are blocked when set).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <returns>Instance id or null.</returns>
        public static string? RemoteInstance(TuiContext context)
        {
            string? id = context?.Session.Proxy?.SelectedInstanceId;
            return String.IsNullOrEmpty(id) ? null : id;
        }

        /// <summary>
        /// The remote-mode banner text for an entity ("Tenant", "User", "Credential"), or empty outside proxy mode.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="entity">English entity word as the dashboard phrases it.</param>
        /// <returns>Translated banner or empty.</returns>
        public static string RemoteBanner(TuiContext context, string entity)
        {
            string? id = RemoteInstance(context);
            if (id == null) return "";
            return context.Loc.T("This page is connected through Armada.Proxy for {{instanceId}}. " + entity + " create, edit, and delete actions are blocked in remote mode.", LocalizationArgs.Of("instanceId", id));
        }

        /// <summary>
        /// Translated Yes or No.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="value">Value.</param>
        /// <returns>Text.</returns>
        public static string YesNo(TuiContext context, bool value)
        {
            return context.Loc.T(value ? "Yes" : "No");
        }

        /// <summary>
        /// Sortable invariant timestamp for hidden sort columns.
        /// </summary>
        /// <param name="utc">UTC time.</param>
        /// <returns>Text.</returns>
        public static string SortStamp(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Delete ids one at a time (as the dashboard does), then toast "Deleted N {plural}." or "Deleted N {plural}.
        /// M failed." and show the failure summary as an error; finally run <paramref name="after"/> on the UI loop.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="ids">Ids.</param>
        /// <param name="delete">Deletes one id.</param>
        /// <param name="plural">English plural noun as in the dashboard ("tenants", "users", "credentials").</param>
        /// <param name="after">Continuation.</param>
        public static void BulkDelete(TuiContext context, List<string> ids, Func<string, Task> delete, string plural, Action after)
        {
            List<string> copy = new List<string>(ids);
            ScreenOps.Quiet(context, async () =>
            {
                int failed = 0;
                foreach (string id in copy)
                {
                    try
                    {
                        await delete(id).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        failed++;
                    }
                }

                return failed;
            }, failed =>
            {
                int success = copy.Count - failed;
                if (success > 0)
                {
                    if (failed > 0) ScreenOps.Toast(context, NotificationSeverityEnum.Warning, "Deleted {{success}} " + plural + ". {{failed}} failed.", LocalizationArgs.Of("success", success, "failed", failed));
                    else ScreenOps.Toast(context, NotificationSeverityEnum.Success, "Deleted {{success}} " + plural + ".", LocalizationArgs.Of("success", success));
                }

                if (failed > 0) ScreenOps.Toast(context, NotificationSeverityEnum.Error, "Deleted {{success}}, {{failed}} failed.", LocalizationArgs.Of("success", success, "failed", failed));
                after?.Invoke();
            });
        }

        #endregion
    }
}
