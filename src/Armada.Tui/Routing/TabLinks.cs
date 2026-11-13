namespace Armada.Tui.Routing
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Links to destinations that became hub tabs in the v1.2.0 navigation consolidation, mirroring the dashboard's
    /// <c>components/LegacyNavRedirects.tsx</c>: Planning is a Dispatch tab, Jobs an Activity tab, the Black Box a
    /// Missions tab, and Storage and CLI Tool Permissions are Settings tabs. The old paths (<c>/planning</c>,
    /// <c>/planning/:id</c>, <c>/jobs</c>, <c>/blackbox</c>, <c>/storage</c>, <c>/cli-permissions</c>) still redirect
    /// here. Thread-safe (stateless).
    /// </summary>
    public static class TabLinks
    {
        #region Public-Members

        /// <summary>
        /// Dispatch, Planning tab.
        /// </summary>
        public const string PlanningPath = "/dispatch?tab=planning";

        /// <summary>
        /// Activity, Jobs tab.
        /// </summary>
        public const string JobsPath = "/activity?source=jobs";

        /// <summary>
        /// Missions, Black Box tab.
        /// </summary>
        public const string BlackBoxPath = "/missions?tab=blackbox";

        /// <summary>
        /// Query parameter of the dashboard Black Box tab's own sections (the Missions hub owns <c>tab</c>).
        /// </summary>
        public const string BlackBoxSectionParam = "bbtab";

        /// <summary>
        /// Settings, Storage tab.
        /// </summary>
        public const string StoragePath = "/server?tab=storage";

        /// <summary>
        /// Settings, CLI Tool Permissions tab.
        /// </summary>
        public const string CliPermissionsPath = "/server?tab=cli-permissions";

        /// <summary>
        /// Query parameter naming the Planning session on the Dispatch Planning tab.
        /// </summary>
        public const string SessionParam = "session";

        /// <summary>
        /// Query parameter naming the CLI Tool Permissions section (<c>requests</c> or <c>rules</c>).
        /// </summary>
        public const string SectionParam = "section";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Link to the Planning tab, optionally opening one session, with extra query parameters.
        /// </summary>
        /// <param name="sessionId">Session id, or null for the session list.</param>
        /// <param name="extra">Extra query parameters, or null.</param>
        /// <returns>Path and query.</returns>
        public static string Planning(string? sessionId = null, IReadOnlyDictionary<string, string>? extra = null)
        {
            Dictionary<string, string> query = new Dictionary<string, string>(StringComparer.Ordinal);
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> kvp in extra) query[kvp.Key] = kvp.Value;
            }

            query["tab"] = "planning";
            if (!String.IsNullOrEmpty(sessionId)) query[SessionParam] = sessionId!;
            else query.Remove(SessionParam);
            return "/dispatch" + RouteMatch.BuildQuery(query);
        }

        /// <summary>
        /// Link to the CLI Tool Permissions tab, optionally on one section or highlighting one request.
        /// </summary>
        /// <param name="requestId">Request id to highlight, or null.</param>
        /// <param name="section">Section (<c>requests</c> or <c>rules</c>), or null for the default.</param>
        /// <returns>Path and query.</returns>
        public static string CliPermissions(string? requestId = null, string? section = null)
        {
            Dictionary<string, string> query = new Dictionary<string, string>(StringComparer.Ordinal) { ["tab"] = "cli-permissions" };
            if (!String.IsNullOrEmpty(section)) query[SectionParam] = section!;
            if (!String.IsNullOrEmpty(requestId)) query["request"] = requestId!;
            return "/server" + RouteMatch.BuildQuery(query);
        }

        /// <summary>
        /// Link to the Black Box tab of the Missions hub, optionally filtered to one failure fingerprint.
        /// </summary>
        /// <param name="fingerprint">Fingerprint, or null for every entry.</param>
        /// <returns>Path and query.</returns>
        public static string BlackBox(string? fingerprint = null)
        {
            Dictionary<string, string> query = new Dictionary<string, string>(StringComparer.Ordinal) { ["tab"] = "blackbox" };
            if (!String.IsNullOrEmpty(fingerprint)) query["fingerprint"] = fingerprint!;
            return "/missions" + RouteMatch.BuildQuery(query);
        }

        #endregion
    }
}
