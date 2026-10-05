namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One list screen for the table-driven keyboard-flow test (Tui.KeyboardFlows): where it lives, what the server
    /// returns, the row to find, and where Enter on the row leads.
    /// </summary>
    public sealed class TuiFlowSpec
    {
        #region Public-Members

        /// <summary>
        /// Case id.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Route of the list (for example /configuration?tab=skills).
        /// </summary>
        public string Route { get; set; } = "";

        /// <summary>
        /// List API path the stub serves.
        /// </summary>
        public string ListPath { get; set; } = "";

        /// <summary>
        /// True when the list API returns a bare JSON array instead of an enumeration page.
        /// </summary>
        public bool BareArray { get; set; } = false;

        /// <summary>
        /// One row as JSON.
        /// </summary>
        public string RowJson { get; set; } = "";

        /// <summary>
        /// Text that identifies the row on screen.
        /// </summary>
        public string RowText { get; set; } = "";

        /// <summary>
        /// Detail API path (served with <see cref="RowJson"/>), or null.
        /// </summary>
        public string? DetailPath { get; set; } = null;

        /// <summary>
        /// Route Enter opens (for example /skills/skl_1), or null when Enter opens a modal instead.
        /// </summary>
        public string? DetailRoute { get; set; } = null;

        /// <summary>
        /// Key that opens the create form (default n), or null when the screen has none.
        /// </summary>
        public string? NewKey { get; set; } = "n";

        /// <summary>
        /// Key that opens the row (default Enter; for example j where a row opens as a JSON view).
        /// </summary>
        public string OpenKey { get; set; } = "enter";

        /// <summary>
        /// Text that shows the row opened in place (a drawer) when Enter opens neither a page nor a dialog, or null.
        /// </summary>
        public string? OpenedText { get; set; } = null;

        /// <summary>
        /// Text the detail page shows, or null to skip the check (default: the row text).
        /// </summary>
        public string? DetailText { get; set; } = "";

        /// <summary>
        /// Detail reply when it differs from <see cref="RowJson"/> (for example a wrapped <c>{ Voyage, Missions }</c>).
        /// </summary>
        public string? DetailJson { get; set; } = null;

        /// <summary>
        /// HTTP method of the list call (GET, or POST for enumerate endpoints).
        /// </summary>
        public string ListMethod { get; set; } = "GET";

        /// <summary>
        /// Key that opens the row-action menu (default .), or null when the screen has none.
        /// </summary>
        public string? RowMenuKey { get; set; } = ".";

        /// <summary>
        /// Key that focuses the filter (default /), or null when the screen has no filter.
        /// </summary>
        public string? FilterKey { get; set; } = "/";

        /// <summary>
        /// Extra stub routes the screen needs, as method, path, JSON.
        /// </summary>
        public List<string[]> ExtraRoutes { get; } = new List<string[]>();

        /// <summary>
        /// Route the create key opens when the screen creates on a page instead of in a form dialog, or null.
        /// </summary>
        public string? NewRoute { get; set; } = null;

        /// <summary>
        /// Key that leaves the create page (default Alt+Left; Esc where the page starts in a text field, which keeps
        /// Alt+Left for word movement).
        /// </summary>
        public string NewBackKey { get; set; } = "alt+left";

        /// <summary>
        /// Key that goes back from the detail page (default Alt+Left; Esc where the page opens in an editor field).
        /// </summary>
        public string DetailBackKey { get; set; } = "alt+left";

        /// <summary>
        /// Builds the stub server for the flow, or null for <see cref="TuiEntityFixtures.Server"/> (the BUILD screens use
        /// <c>BuildStubs.Server</c> for their reference lists).
        /// </summary>
        public Func<StubHttpHandler>? ServerFactory { get; set; } = null;

        /// <summary>
        /// Checks the requests the flow made, after the flow (structured: <see cref="StubHttpHandler.RequestsFor"/>,
        /// <see cref="StubHttpHandler.LastBody{T}"/>, <see cref="StubRequest.QueryValue"/>), or null.
        /// </summary>
        public Action<StubHttpHandler>? VerifyRequests { get; set; } = null;

        /// <summary>
        /// For screens that filter on the server (after a debounce): true once the stub has seen the filtered request.
        /// The flow waits for it after typing the filter. Null for screens that filter locally.
        /// </summary>
        public Func<StubHttpHandler, bool>? ServerFilterSeen { get; set; } = null;

        /// <summary>
        /// Terminal width for the flow.
        /// </summary>
        public int Width { get; set; } = 160;

        /// <summary>
        /// Terminal height for the flow.
        /// </summary>
        public int Height { get; set; } = 48;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A spec whose detail API path is the list path plus the key.
        /// </summary>
        /// <param name="id">Case id.</param>
        /// <param name="route">List route.</param>
        /// <param name="listPath">List API path.</param>
        /// <param name="key">Row key (id or name).</param>
        /// <param name="rowText">Row text on screen.</param>
        /// <param name="detailRoute">Route Enter opens, or null for a dialog.</param>
        /// <param name="rowJson">Row JSON.</param>
        /// <returns>Spec.</returns>
        public static TuiFlowSpec Create(string id, string route, string listPath, string key, string rowText, string? detailRoute, string rowJson)
        {
            TuiFlowSpec spec = new TuiFlowSpec();
            spec.Id = id;
            spec.Route = route;
            spec.ListPath = listPath;
            spec.RowJson = rowJson;
            spec.RowText = rowText;
            spec.DetailRoute = detailRoute;
            spec.DetailPath = detailRoute != null ? listPath + "/" + key : null;
            return spec;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Id + " (" + Route + ")";
        }

        #endregion
    }
}
