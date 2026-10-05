namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Test.Shared.Suites.Tui;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client against the server's route surface (docs/api-surface-1.0.json): every client method sends only
    /// requests that match a surface route (or a listed non-Admiral path), and every surface route is called by a client
    /// method or listed with the reason it has none. A new route without a client method, a client method without a route,
    /// or a stale list entry fails.
    /// </summary>
    public sealed class ClientRouteSurfaceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.RouteSurface";

        /// <summary>
        /// Requests the client sends to paths outside the Admiral's route surface, with the reason.
        /// </summary>
        private static readonly Dictionary<string, string> _NonSurfaceRequests = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GET /dashboard/i18n/armada.json"] = "Static i18n catalog served with the dashboard, not a REST route",
            ["GET /proxy-api/v1/session/context"] = "Armada.Proxy route; against an Admiral the client reads 404 as not behind the proxy",
            ["POST /proxy-api/v1/session/logout-instance"] = "Armada.Proxy route (Switch Deployment)",
            ["POST /proxy-api/v1/auth/logout"] = "Armada.Proxy route; a 404 from an Admiral is ignored"
        };

        /// <summary>
        /// Surface routes no client method calls, with the reason.
        /// </summary>
        private static readonly Dictionary<string, string> _RoutesWithoutClientMethod = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DELETE /api/v1/docks/{id}/purge"] = "Operator recovery, also the MCP tool purge_dock; no dashboard function",
            ["DELETE /api/v1/events/{id}"] = "Single delete; the dashboard deletes through POST /api/v1/events/delete/multiple (DeleteEventsBatchAsync)",
            ["DELETE /api/v1/merge-queue/{id}/purge"] = "Operator recovery, also the MCP tool purge_merge_entry; no dashboard function",
            ["DELETE /api/v1/signals/{id}"] = "Single delete; the dashboard deletes through POST /api/v1/signals/delete/multiple (DeleteSignalsBatchAsync)",
            ["GET /api/v1/credentials/{id}"] = "The dashboard reads credentials from the list",
            ["GET /api/v1/missions/{id}/evaluate-autoland"] = "Auto-land evaluation, also the MCP tool evaluate_autoland; no dashboard function",
            ["GET /api/v1/signals/recent"] = "Captain-facing feed; the dashboard uses the signal list",
            ["GET /api/v1/signals/recipient/{captainId}"] = "Captain-facing feed; the dashboard uses the signal list",
            ["GET /api/v1/tenants/{id}"] = "The dashboard reads tenants from the list",
            ["GET /api/v1/token-usage"] = "Raw token-usage records; the dashboard uses the summary (GetTokenUsageAsync)",
            ["GET /api/v1/users/{id}"] = "The dashboard reads users from the list",
            ["GET /swagger"] = "Swagger UI (HTML); the API Explorer reads /openapi.json (GetOpenApiDocumentAsync)",
            ["PATCH /api/v1/vessels/{id}/context"] = "Context patch (the MCP tool update_vessel_context); the dashboard saves context with PUT /api/v1/vessels/{id}",
            ["POST /api/v1/captains/delete/multiple"] = "Batch delete not offered by the dashboard (captains are deleted one at a time)",
            ["POST /api/v1/captains/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/check-runs/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/docks/delete/multiple"] = "Batch delete not offered by the dashboard (docks are deleted one at a time)",
            ["POST /api/v1/docks/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/docks/{id}/repair"] = "Operator recovery, also the MCP tool repair_dock; no dashboard function",
            ["POST /api/v1/docks/{id}/unstick"] = "Operator recovery, also the MCP tool unstick_dock; no dashboard function",
            ["POST /api/v1/events/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/fleets/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/harbors/{id}/probe"] = "Harbor probe; no dashboard function",
            ["POST /api/v1/merge-queue/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/merge-queue/purge"] = "Batch purge, also the MCP tool purge_merge_queue; no dashboard function",
            ["POST /api/v1/missions/delete/multiple"] = "Batch delete not used by the dashboard (bulk delete calls DELETE per mission)",
            ["POST /api/v1/missions/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/missions/summaries/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/onboarding"] = "Self-registration (sign-up) flow; the dashboard and TUI only sign in",
            ["POST /api/v1/personas/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/pipelines/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/playbooks/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/project-profiles/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/prompt-templates/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/signals/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/skills/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/token-usage/delete/by-filter"] = "Token-usage purge; no dashboard function",
            ["POST /api/v1/vessels/delete/multiple"] = "Batch delete not offered by the dashboard (vessels are deleted one at a time)",
            ["POST /api/v1/vessels/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/voyages/delete/multiple"] = "Batch delete not offered by the dashboard (voyages are purged one at a time)",
            ["POST /api/v1/voyages/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/workflow-profiles/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters"
        };

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Async(Suite, "client_matches_route_surface", "Every client method maps to a surface route and every surface route has a client method or a listed reason", async () =>
            {
                ApiSurfaceDocument surface = ApiSurfaceFiles.LoadBaseline();
                List<string> routes = surface.Rest.Select(r => r.Method.ToUpperInvariant() + " " + r.Route).Distinct().ToList();
                HashSet<string> called = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> nonSurfaceSeen = new HashSet<string>(StringComparer.Ordinal);
                StringBuilder problems = new StringBuilder();

                List<MethodInfo> methods = ClientRouteMap.ApiMethods();
                AssertTrue(methods.Count >= 300, "client methods found: " + methods.Count);
                foreach (MethodInfo method in methods)
                {
                    List<string> requests = await ClientRouteMap.RequestsOfAsync(method);
                    if (requests.Count == 0)
                    {
                        problems.Append("client method sends no request: ").Append(method.Name).Append('\n');
                        continue;
                    }

                    foreach (string request in requests)
                    {
                        if (_NonSurfaceRequests.ContainsKey(request))
                        {
                            nonSurfaceSeen.Add(request);
                            continue;
                        }

                        string? route = ClientRouteMap.Match(request, routes);
                        if (route == null) problems.Append("client method without a route: ").Append(method.Name).Append(" -> ").Append(request).Append('\n');
                        else called.Add(route);
                    }
                }

                foreach (string route in routes.OrderBy(r => r, StringComparer.Ordinal))
                {
                    bool listed = _RoutesWithoutClientMethod.ContainsKey(route);
                    if (!called.Contains(route) && !listed) problems.Append("route without a client method: ").Append(route).Append('\n');
                    if (called.Contains(route) && listed) problems.Append("stale entry (a client method calls it now): ").Append(route).Append('\n');
                }

                foreach (string listed in _RoutesWithoutClientMethod.Keys)
                {
                    if (!routes.Contains(listed)) problems.Append("stale entry (not in the surface): ").Append(listed).Append('\n');
                }

                foreach (string listed in _NonSurfaceRequests.Keys)
                {
                    if (!nonSurfaceSeen.Contains(listed)) problems.Append("stale non-surface entry (no client method sends it): ").Append(listed).Append('\n');
                }

                if (problems.Length > 0) throw new AssertionException("Client and route surface differ:\n" + problems.ToString());
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client vs. the REST route surface", cases: cases);
        }

        #endregion
    }
}
