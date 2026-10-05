namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens;

    /// <summary>
    /// Stub-server helpers for the delivery and configuration screen suites: a signed-in stub whose reference lists
    /// (vessels, environments, releases, profiles, personas, ...) answer with empty pages, enumeration JSON builders,
    /// and accessors for the screen under test.
    /// </summary>
    public static class TuiEntityFixtures
    {
        #region Public-Members

        /// <summary>
        /// List endpoints answered with an empty page by <see cref="Server"/>.
        /// </summary>
        public static readonly string[] EmptyLists = new[]
        {
            "/api/v1/vessels", "/api/v1/environments", "/api/v1/releases", "/api/v1/workflow-profiles", "/api/v1/deployments",
            "/api/v1/personas", "/api/v1/prompt-templates", "/api/v1/captains", "/api/v1/fleets", "/api/v1/pipelines",
            "/api/v1/runbook-executions", "/api/v1/check-runs", "/api/v1/incidents", "/api/v1/runbooks", "/api/v1/project-profiles",
            "/api/v1/skills", "/api/v1/playbooks", "/api/v1/memories", "/api/v1/voyages", "/api/v1/missions", "/api/v1/objectives"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// A signed-in stub server (tenant admin) with empty reference lists.
        /// </summary>
        /// <returns>Stub.</returns>
        public static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            foreach (string path in EmptyLists) stub.Json("GET", path, Page());
            stub.Json("GET", "/api/v1/model-endpoints", "[]");
            stub.Json("GET", "/api/v1/harbors", "[]");
            return stub;
        }

        /// <summary>
        /// A signed-in stub server for a regular (non-admin) user.
        /// </summary>
        /// <returns>Stub.</returns>
        public static StubHttpHandler RegularUserServer()
        {
            StubHttpHandler stub = Server();
            stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(System.Net.HttpStatusCode.OK,
                "{\"Tenant\":{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true},\"User\":{\"Id\":\"usr_user\",\"TenantId\":\"ten_default\",\"Email\":\"user@armada\",\"IsAdmin\":false,\"IsTenantAdmin\":false,\"Active\":true}}"));
            return stub;
        }

        /// <summary>
        /// Enumeration JSON for objects (total = count).
        /// </summary>
        /// <param name="objects">Object JSON.</param>
        /// <returns>JSON.</returns>
        public static string Page(params string[] objects)
        {
            return PageWithTotal(objects.Length, objects);
        }

        /// <summary>
        /// Enumeration JSON with an explicit total.
        /// </summary>
        /// <param name="total">Total records.</param>
        /// <param name="objects">Object JSON.</param>
        /// <returns>JSON.</returns>
        public static string PageWithTotal(int total, params string[] objects)
        {
            int pageSize = Math.Max(1, objects.Length);
            int pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            return "{\"Success\":true,\"PageNumber\":1,\"PageSize\":" + pageSize + ",\"TotalPages\":" + pages + ",\"TotalRecords\":" + total + ",\"Objects\":[" + String.Join(",", objects) + "]}";
        }

        /// <summary>
        /// Open the TUI signed in at a route.
        /// </summary>
        /// <param name="stub">Stub.</param>
        /// <param name="route">Route.</param>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        /// <returns>Host.</returns>
        public static TuiTestHost Open(StubHttpHandler stub, string route, int width = 160, int height = 48)
        {
            return Test.Shared.Suites.Tui.TuiCase.SignedIn(width, height, route, stub);
        }

        /// <summary>
        /// The screen in the main region (the active tab's screen inside hubs).
        /// </summary>
        /// <typeparam name="TScreen">Screen type.</typeparam>
        /// <param name="host">Host.</param>
        /// <returns>Screen.</returns>
        /// <exception cref="AssertionException">Thrown when the screen is not of the type.</exception>
        public static TScreen Screen<TScreen>(TuiTestHost host) where TScreen : ScreenBase
        {
            ScreenBase? screen = host.Tui.Shell.Screen;
            if (screen is HubScreen hub) screen = hub.Content;
            if (screen is TScreen typed) return typed;
            throw new AssertionException("Expected screen " + typeof(TScreen).Name + " but found " + (screen?.GetType().Name ?? "null"));
        }

        /// <summary>
        /// Wait until a condition holds (pumping the UI loop) or fail.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="condition">Condition.</param>
        /// <param name="label">Failure label.</param>
        /// <param name="timeoutMs">Timeout.</param>
        public static void WaitFor(TuiTestHost host, Func<bool> condition, string label, int timeoutMs = 5000)
        {
            if (!host.PumpUntil(condition, timeoutMs)) throw new AssertionException("Timed out waiting for: " + label + "\n" + host.Screen());
        }


        /// <summary>
        /// Wait until the stub saw a request with exactly this method and path that satisfies an optional predicate, or fail.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="stub">Stub.</param>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path (no query).</param>
        /// <param name="predicate">Optional predicate on the request (query values, typed body).</param>
        /// <param name="label">Failure label.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>The last matching request.</returns>
        public static StubRequest WaitForRequest(TuiTestHost host, StubHttpHandler stub, string method, string path, Func<StubRequest, bool>? predicate = null, string? label = null, int timeoutMs = 5000)
        {
            if (!host.PumpUntil(() => stub.Saw(method, path, predicate), timeoutMs))
                throw new AssertionException("No " + method + " " + path + " request" + (label != null ? " (" + label + ")" : "") + ". Seen:\n" + String.Join("\n", stub.Requests));
            return stub.Log.Last(r => r.Is(method, path) && (predicate == null || predicate(r)));
        }

        /// <summary>
        /// Wait until the stub saw a request with this method and path whose query has a parameter with exactly this value.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="stub">Stub.</param>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Absolute path (no query).</param>
        /// <param name="name">Query parameter name.</param>
        /// <param name="value">Expected decoded value.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>The last matching request.</returns>
        public static StubRequest WaitForQuery(TuiTestHost host, StubHttpHandler stub, string method, string path, string name, string value, int timeoutMs = 5000)
        {
            return WaitForRequest(host, stub, method, path, r => r.QueryValue(name) == value, name + "=" + value, timeoutMs);
        }


        #endregion
    }
}
