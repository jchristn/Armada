namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Authorization;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Proves every REST route and MCP tool the Admiral registers has an explicit authorization requirement in
    /// <see cref="RouteAuthorizationRegistry"/> or <see cref="McpToolAuthorizationRegistry"/>, that the registries
    /// declare nothing the server does not register, and that resolving a concrete request path finds the route's own
    /// declaration. A new route or tool without a declaration fails this suite.
    /// </summary>
    public sealed class AuthorizationCoverageSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.AuthorizationCoverage";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("every_registered_route_is_declared", "Every registered REST route has a declared requirement", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<string> registered = fx.Server.GetRegisteredRestRoutes();
                Assert(registered.Count > 300, "expected the full REST surface to be registered, found " + registered.Count);

                List<string> undeclared = new List<string>();
                foreach (string route in registered)
                {
                    int space = route.IndexOf(' ');
                    string method = route.Substring(0, space);
                    string template = route.Substring(space + 1);
                    if (!RouteAuthorizationRegistry.TryGetByTemplate(method, template, out AuthorizationRequirement? _)) undeclared.Add(route);
                }

                Assert(undeclared.Count == 0, "routes without a declared authorization requirement (add them to RouteAuthorizationRegistry): " + String.Join(", ", undeclared));
            }));

            cases.Add(CaseAsync("no_stale_route_declarations", "Every declared route is registered", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HashSet<string> registered = new HashSet<string>(fx.Server.GetRegisteredRestRoutes(), StringComparer.OrdinalIgnoreCase);
                List<string> stale = RouteAuthorizationRegistry.GetDeclaredRoutes().Where(r => !registered.Contains(r)).ToList();
                Assert(stale.Count == 0, "declared routes that the server does not register (remove them): " + String.Join(", ", stale));
            }));

            cases.Add(CaseAsync("every_registered_tool_is_declared", "Every registered MCP tool has a declared requirement", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                IReadOnlyList<string> tools = fx.Server.RegisteredMcpTools;
                Assert(tools.Count > 100, "expected the full MCP surface to be registered, found " + tools.Count);
                List<string> undeclared = tools.Where(t => !McpToolAuthorizationRegistry.TryGet(t, out AuthorizationRequirement? _)).ToList();
                Assert(undeclared.Count == 0, "MCP tools without a declared authorization requirement (add them to McpToolAuthorizationRegistry): " + String.Join(", ", undeclared));
            }));

            cases.Add(CaseAsync("no_stale_tool_declarations", "Every declared MCP tool is registered", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HashSet<string> tools = new HashSet<string>(fx.Server.RegisteredMcpTools, StringComparer.Ordinal);
                List<string> stale = McpToolAuthorizationRegistry.GetDeclaredTools().Where(t => !tools.Contains(t)).ToList();
                Assert(stale.Count == 0, "declared MCP tools that the server does not register (remove them): " + String.Join(", ", stale));
            }));

            cases.Add(CaseAsync("concrete_paths_resolve_to_their_own_template", "A concrete path resolves to the declaration of the route it reaches", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                List<string> mismatches = new List<string>();
                foreach (string route in fx.Server.GetRegisteredRestRoutes())
                {
                    int space = route.IndexOf(' ');
                    string method = route.Substring(0, space);
                    string template = route.Substring(space + 1);
                    string sample = System.Text.RegularExpressions.Regex.Replace(template, "\\{[^}]+\\}", "zz_sample");
                    if (!RouteAuthorizationRegistry.TryResolvePath(method, sample, out AuthorizationRequirement? _, out string? resolved)
                        || !String.Equals(resolved, template, StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches.Add(method + " " + sample + " -> " + (resolved ?? "(none)") + " (expected " + template + ")");
                    }
                }

                Assert(mismatches.Count == 0, "paths that resolve to another route's declaration: " + String.Join("; ", mismatches));
            }));

            cases.Add(Case("undeclared_path_fails_closed", "An undeclared API path requires a global admin", TestTags.Negative, () =>
            {
                AssertEqual(PermissionLevel.AdminOnly, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/not-a-real-route"));
                AssertEqual(PermissionLevel.AdminOnly, McpToolAuthorizationRegistry.GetOrDefault("not_a_real_tool").Level);
            }));

            cases.Add(Case("sensitive_requirements_pinned", "Sensitive routes and tools keep their declared levels", TestTags.Positive, () =>
            {
                AssertEqual(PermissionLevel.AdminOnly, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/server/stop"));
                AssertEqual(PermissionLevel.AdminOnly, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/server/rebuild"));
                AssertEqual(PermissionLevel.AdminOnly, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/restore"));
                AssertEqual(PermissionLevel.TenantAdmin, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/workspace/vessels/vsl_x/exec"));
                AssertEqual(PermissionLevel.TenantAdmin, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/check-runs"));
                AssertEqual(PermissionLevel.TenantAdmin, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/check-runs/chk_x/retry"));
                AssertEqual(PermissionLevel.TenantAdmin, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/harbors/hbr_x/probe"));
                AssertEqual(PermissionLevel.TenantAdmin, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/fleet-actions/run"));
                AssertEqual(PermissionLevel.Authenticated, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/missions/enumerate"));
                AssertEqual(PermissionLevel.Authenticated, AuthorizationConfig.GetPermissionLevel("PUT", "/api/v1/account/password"));
                AssertEqual(PermissionLevel.AdminOnly, McpToolAuthorizationRegistry.GetOrDefault("backup").Level);
                AssertEqual(PermissionLevel.AdminOnly, McpToolAuthorizationRegistry.GetOrDefault("restore").Level);
                AssertEqual(PermissionLevel.AdminOnly, McpToolAuthorizationRegistry.GetOrDefault("stop_server").Level);
                AssertEqual(PermissionLevel.TenantAdmin, McpToolAuthorizationRegistry.GetOrDefault("run_check").Level);
                AssertEqual(PermissionLevel.TenantAdmin, McpToolAuthorizationRegistry.GetOrDefault("run_fleet_action").Level);
                AssertEqual(PermissionLevel.Authenticated, McpToolAuthorizationRegistry.GetOrDefault("enumerate").Level);
            }));

            cases.Add(CaseAsync("server_control_requires_admin", "Server stop/restart/rebuild/rollback refuse unauthenticated callers", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                foreach (string path in new string[] { "/api/v1/server/stop", "/api/v1/server/restart", "/api/v1/server/rebuild", "/api/v1/server/rollback" })
                {
                    using (System.Net.Http.StringContent body = new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json"))
                    {
                        System.Net.Http.HttpResponseMessage response = await fx.UnauthClient.PostAsync(path, body).ConfigureAwait(false);
                        AssertEqual(401, (int)response.StatusCode, path);
                    }
                }

                System.Net.Http.HttpResponseMessage health = await fx.AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(200, (int)health.StatusCode, "server still running");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Authorization Coverage",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
