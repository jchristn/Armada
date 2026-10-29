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
        /// CLI requests whose entity segment is a variable, with every request it can expand to (each must be a route).
        /// </summary>
        private static readonly Dictionary<string, string[]> _CliDynamicRequests = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // BaseCommand.GetRemoteLogTailAsync / GetRemoteLogPageAsync: kind is "missions" or "captains".
            ["GET /api/v1/x/x/log"] = new[] { "GET /api/v1/missions/x/log", "GET /api/v1/captains/x/log" }
        };

        /// <summary>
        /// api/client.ts exports whose request is not checked against a single route, with the reason.
        /// </summary>
        private static readonly Dictionary<string, string> _DashboardExportsNotChecked = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["restartCaptain"] = "Composes deleteCaptain and createCaptain, which are checked themselves",
            ["listAllMuxEndpoints"] = "Reads pages through enumerateMuxEndpoints, which is checked itself",
            ["listMuxEndpoints"] = "Calls listAllMuxEndpoints (pages of enumerateMuxEndpoints, which is checked itself)",
            ["getEntity"] = "Generic lookup: the path's entity segment comes from a type map, so it has no single literal route",
            ["getFleet"] = "Unwraps getFleetDetail, which is checked itself",
            ["listWorkspaceDirectory"] = "Walks getWorkspaceTree pages, which is checked itself",
            ["listAllWorkspaceChanges"] = "Walks getWorkspaceChanges pages, which is checked itself",
            ["listAllWorkspaceActiveMissions"] = "Walks listWorkspaceActiveMissions pages, which is checked itself",
            ["browseAllVesselImport"] = "Walks browseVesselImport pages, which is checked itself",
            ["listAllStorageHosts"] = "Walks listStorageHosts pages, which is checked itself"
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
            ["GET /api/v1/storage/hosts"] = "GET twin of the enumerate route; the dashboard and the client use POST /api/v1/storage/hosts/enumerate (ListStorageHostsAsync)",
            ["GET /api/v1/tenants/{id}"] = "The dashboard reads tenants from the list",
            ["GET /api/v1/token-usage"] = "Raw token-usage records; the dashboard uses the summary (GetTokenUsageAsync)",
            ["GET /api/v1/users/{id}"] = "The dashboard reads users from the list",
            ["GET /api/v1/vessels/{id}/cleanup/resources"] = "GET twin of the enumerate route; the dashboard and the client use POST .../cleanup/resources/enumerate (ListVesselCleanupResourcesAsync)",
            ["GET /api/v1/blackbox/recovery-stats"] = "GET twin of the enumerate route; the client uses POST /api/v1/blackbox/recovery-stats/enumerate (GetBlackBoxRecoveryStatsAsync)",
            ["GET /api/v1/blackbox/{id}/nearby-successes"] = "GET twin of the enumerate route; the client uses POST .../nearby-successes/enumerate (ListBlackBoxNearbySuccessesAsync)",
            ["GET /api/v1/blackbox/{id}/timeline"] = "GET twin of the enumerate route; the client uses POST .../timeline/enumerate (GetBlackBoxTimelineAsync)",
            ["GET /api/v1/vessels/{id}/blackbox-doctor"] = "GET twin of the enumerate route; the client uses POST .../blackbox-doctor/enumerate (GetVesselBlackBoxDoctorAsync)",
            ["GET /api/v1/vessels/{id}/reservations"] = "GET twin of the enumerate route; the client uses POST .../reservations/enumerate (ListVesselReservationsAsync)",
            ["GET /api/v1/vessels/{id}/health/dependencies"] = "GET twin of the enumerate route; the dashboard and the client use POST .../health/dependencies/enumerate (ListVesselDependenciesAsync)",
            ["GET /api/v1/workspace/vessels/{vesselId}/active-missions"] = "GET twin of the enumerate route; the dashboard and the client use POST .../active-missions/enumerate (ListWorkspaceActiveMissionsAsync)",
            ["GET /swagger"] = "Swagger UI (HTML); the API Explorer reads /openapi.json (GetOpenApiDocumentAsync)",
            ["PATCH /api/v1/vessels/{id}/context"] = "Context patch (the MCP tool update_vessel_context); the dashboard saves context with PUT /api/v1/vessels/{id}",
            ["POST /api/v1/captains/delete/multiple"] = "Batch delete not offered by the dashboard (captains are deleted one at a time)",
            ["POST /api/v1/captains/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/captains/{id}/tools/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
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
            ["POST /api/v1/missions/delete/multiple"] = "Batch delete not used by the dashboard (bulk delete purges each mission with DELETE /api/v1/missions/{id}/purge)",
            ["POST /api/v1/missions/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/missions/summaries/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/onboarding"] = "Self-registration (sign-up) flow; the dashboard and TUI only sign in",
            ["POST /api/v1/personas/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/pipelines/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/playbooks/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/project-profiles/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/prompt-templates/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/runtimes/mux/endpoints/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/signals/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/skills/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/token-usage/delete/by-filter"] = "Token-usage purge; no dashboard function",
            ["POST /api/v1/vessels/delete/multiple"] = "Batch delete not offered by the dashboard (vessels are deleted one at a time)",
            ["POST /api/v1/vessels/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters",
            ["POST /api/v1/voyages/delete/multiple"] = "Batch delete not offered by the dashboard (voyages are purged one at a time)",
            ["POST /api/v1/voyages/enumerate"] = "Enumerate twin of the GET list; the dashboard and the client use the GET list with query parameters"
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
                    // A deprecated route keeps working for other callers, but first-party clients use its replacement. A retired
                    // route (still in an older baseline until it is regenerated) answers 410, so no client method calls it.
                    string routeMethod = route.Split(' ')[0];
                    string routeTemplate = route.Split(' ')[1];
                    bool listed = _RoutesWithoutClientMethod.ContainsKey(route)
                        || Armada.Core.ApiSurface.DeprecatedSurface.RouteReplacement(routeMethod, routeTemplate) != null
                        || Armada.Core.ApiSurface.RetiredSurface.RouteReplacement(routeMethod, routeTemplate) != null;
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

            cases.Add(TuiCase.Sync(Suite, "dashboard_calls_match_route_surface", "Every api/client.ts request (dashboard and mobile) matches a surface route or a listed non-Admiral path", () =>
            {
                ApiSurfaceDocument surface = ApiSurfaceFiles.LoadBaseline();
                List<string> routes = surface.Rest.Select(r => r.Method.ToUpperInvariant() + " " + r.Route).Distinct().ToList();
                List<DashboardApiCall> calls = DashboardSource.ApiCalls();
                StringBuilder problems = new StringBuilder();

                foreach (string export in DashboardSource.ApiExports())
                {
                    if (_DashboardExportsNotChecked.ContainsKey(export)) continue;
                    List<DashboardApiCall> own = calls.Where(c => c.Export == export).ToList();
                    if (own.Count == 0)
                    {
                        problems.Append("client.ts export with no parseable request: ").Append(export).Append('\n');
                        continue;
                    }

                    foreach (DashboardApiCall call in own)
                    {
                        string request = call.Method + " " + call.Path;
                        if (_NonSurfaceRequests.ContainsKey(request)) continue;
                        if (ClientRouteMap.Match(request, routes) == null)
                            problems.Append("client.ts request without a server route: ").Append(export).Append(" -> ").Append(request).Append('\n');
                    }
                }

                foreach (string listed in _DashboardExportsNotChecked.Keys)
                {
                    if (!DashboardSource.ApiExports().Contains(listed)) problems.Append("stale entry (no such client.ts export): ").Append(listed).Append('\n');
                }

                AssertTrue(calls.Count >= 330, "parsed client.ts requests: " + calls.Count);
                if (problems.Length > 0) throw new AssertionException("api/client.ts and the route surface differ:\n" + problems.ToString());
            }));

            cases.Add(TuiCase.Sync(Suite, "cli_calls_match_route_surface", "Every REST request the Helm CLI and Armada.Core's ArmadaApiClient send matches a surface route", () =>
            {
                ApiSurfaceDocument surface = ApiSurfaceFiles.LoadBaseline();
                List<string> routes = surface.Rest.Select(r => r.Method.ToUpperInvariant() + " " + r.Route).Distinct().ToList();
                string root = DashboardSource.RepoRoot();
                List<string> files = System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "src", "Armada.Helm"), "*.cs", System.IO.SearchOption.AllDirectories)
                    .Where(f => !f.Contains(System.IO.Path.DirectorySeparatorChar + "bin" + System.IO.Path.DirectorySeparatorChar)
                             && !f.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar))
                    .Append(System.IO.Path.Combine(root, "src", "Armada.Core", "Client", "ArmadaApiClient.cs"))
                    .ToList();
                List<DashboardApiCall> calls = CSharpApiCallScanner.Scan(files);
                AssertTrue(calls.Count >= 200, "parsed CLI requests: " + calls.Count);

                StringBuilder problems = new StringBuilder();
                foreach (DashboardApiCall call in calls)
                {
                    string request = call.Method + " " + call.Path;
                    if (_CliDynamicRequests.TryGetValue(request, out string[]? expansions))
                    {
                        foreach (string expansion in expansions)
                        {
                            if (ClientRouteMap.Match(expansion, routes) == null) problems.Append("listed expansion without a route: ").Append(expansion).Append('\n');
                        }

                        continue;
                    }

                    if (ClientRouteMap.Match(request, routes) == null)
                        problems.Append("CLI request without a server route: ").Append(call.Export).Append(" -> ").Append(request).Append('\n');
                }

                if (problems.Length > 0) throw new AssertionException("CLI and route surface differ:\n" + problems.ToString());
            }));

            cases.Add(TuiCase.Async(Suite, "no_first_party_client_calls_a_deprecated_route", "No first-party client (dashboard and mobile client.ts, Armada.Client, the Helm CLI, Armada.Core's ArmadaApiClient) calls a deprecated route or the unpaged jobs list", async () =>
            {
                ApiSurfaceDocument surface = ApiSurfaceFiles.LoadBaseline();
                List<string> routes = surface.Rest.Select(r => r.Method.ToUpperInvariant() + " " + r.Route).Distinct().ToList();
                // Deprecated routes still work; retired routes answer 410 Gone. First-party clients may call neither. Retired
                // routes are matched too, so a call to one is caught even after it leaves the regenerated baseline.
                HashSet<string> deprecated = new HashSet<string>(Armada.Core.ApiSurface.DeprecatedSurface.Routes(), StringComparer.OrdinalIgnoreCase);
                deprecated.UnionWith(Armada.Core.ApiSurface.RetiredSurface.Routes());
                AssertTrue(deprecated.Count > 0, "deprecated or retired routes listed");
                routes = routes.Union(Armada.Core.ApiSurface.RetiredSurface.Routes(), StringComparer.Ordinal).ToList();
                StringBuilder problems = new StringBuilder();

                void Check(string who, string request, bool hasQuery)
                {
                    string? route = ClientRouteMap.Match(request, routes);
                    if (route != null && deprecated.Contains(route)) problems.Append(who).Append(" calls deprecated or retired ").Append(route).Append('\n');
                    // GET /api/v1/jobs without paging returns only the first page; a caller must page explicitly.
                    if (!hasQuery && String.Equals(request, "GET /api/v1/jobs", StringComparison.Ordinal)) problems.Append(who).Append(" lists jobs without paging\n");
                }

                foreach (DashboardApiCall call in DashboardSource.ApiCalls()) Check("client.ts " + call.Export, call.Method + " " + call.Path, call.HasQuery);
                foreach (MethodInfo method in ClientRouteMap.ApiMethods())
                    foreach (string request in await ClientRouteMap.RequestsOfAsync(method, true)) Check("Armada.Client " + method.Name, StripQuery(request), request.Contains('?'));
                string root = DashboardSource.RepoRoot();
                List<string> files = System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "src", "Armada.Helm"), "*.cs", System.IO.SearchOption.AllDirectories)
                    .Where(f => !f.Contains(System.IO.Path.DirectorySeparatorChar + "bin" + System.IO.Path.DirectorySeparatorChar)
                             && !f.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar))
                    .Append(System.IO.Path.Combine(root, "src", "Armada.Core", "Client", "ArmadaApiClient.cs"))
                    .ToList();
                foreach (DashboardApiCall call in CSharpApiCallScanner.Scan(files)) Check(call.Export, call.Method + " " + call.Path, call.HasQuery);

                if (problems.Length > 0) throw new AssertionException("first-party clients still use deprecated APIs (migrate them; see DEPRECATION.md):\n" + problems.ToString());
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client vs. the REST route surface", cases: cases);
        }

        #endregion
    
        private static string StripQuery(string request)
        {
            int query = request.IndexOf('?');
            return query < 0 ? request : request.Substring(0, query);
        }
    }
}
