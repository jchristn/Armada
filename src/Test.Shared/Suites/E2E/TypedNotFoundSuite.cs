namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Server.Mcp;
    using Armada.Server.Routes;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using ApiResultEnum = WatsonWebserver.Core.ApiResultEnum;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// A missing or invisible entity is signaled by type: services throw <see cref="KeyNotFoundException"/>, REST routes
    /// map it to 404 through <see cref="RouteErrorMapper"/>, and MCP tools return ErrorCode NotFound through
    /// <see cref="McpToolRegistrar.MapToolExceptions"/>. Before this, the routes below chose 404 by searching the
    /// exception message for "not found" (or answered 404 for every state error), and the MCP tools returned untyped
    /// isError text.
    /// </summary>
    public sealed class TypedNotFoundSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.TypedNotFound";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("route_mapper_maps_by_type", "RouteErrorMapper maps exception types to status codes", TestTags.Positive, () =>
            {
                AssertEqual(404, RouteErrorMapper.StatusCodeFor(new KeyNotFoundException("x")), "KeyNotFoundException");
                AssertEqual(400, RouteErrorMapper.StatusCodeFor(new ArgumentException("x")), "ArgumentException");
                AssertEqual(400, RouteErrorMapper.StatusCodeFor(new ArgumentNullException("x")), "ArgumentNullException");
                AssertEqual(403, RouteErrorMapper.StatusCodeFor(new UnauthorizedAccessException("x")), "UnauthorizedAccessException");
                AssertEqual(400, RouteErrorMapper.StatusCodeFor(new InvalidOperationException("x")), "InvalidOperationException default");
                AssertEqual(409, RouteErrorMapper.StatusCodeFor(new InvalidOperationException("x"), 409), "InvalidOperationException as conflict");
                // The message no longer matters: a state error that mentions "not found" is not a 404.
                AssertEqual(400, RouteErrorMapper.StatusCodeFor(new InvalidOperationException("Linked check run not found in pipeline state")), "message text ignored");
                AssertFalse(RouteErrorMapper.IsMapped(new Exception("not found")), "a plain Exception stays a server error");
                AssertFalse(RouteErrorMapper.IsMapped(new TimeoutException("not found")), "a TimeoutException stays unmapped");
                AssertEqual(ApiResultEnum.NotFound, RouteErrorMapper.ResultFor(404), "404 result");
                AssertEqual(ApiResultEnum.Conflict, RouteErrorMapper.ResultFor(409), "409 result");
            }));

            cases.Add(CaseAsync("mcp_wrapper_maps_by_type", "MapToolExceptions returns typed errors and lets unmapped exceptions through", TestTags.Positive, async () =>
            {
                Dictionary<string, Func<JsonElement?, Task<object>>> handlers = new Dictionary<string, Func<JsonElement?, Task<object>>>();
                RegisterToolDelegate wrapped = McpToolRegistrar.MapToolExceptions((name, description, schema, handler) => handlers[name] = handler);
                wrapped("missing", "d", new { }, _ => throw new KeyNotFoundException("Thing not found."));
                wrapped("bad", "d", new { }, _ => throw new ArgumentException("bad input"));
                wrapped("state", "d", new { }, _ => throw new InvalidOperationException("Thing not found in a state message"));
                wrapped("crash", "d", new { }, _ => throw new FormatException("boom"));
                wrapped("ok", "d", new { }, _ => Task.FromResult<object>("fine"));

                McpToolError missing = AssertToolError(await handlers["missing"](null).ConfigureAwait(false));
                AssertEqual(McpToolErrorCodeEnum.NotFound, missing.ErrorCode, "KeyNotFoundException");
                AssertEqual("Thing not found.", missing.Error, "message carried");
                AssertEqual(McpToolErrorCodeEnum.InvalidArgument, AssertToolError(await handlers["bad"](null).ConfigureAwait(false)).ErrorCode, "ArgumentException");
                AssertEqual(McpToolErrorCodeEnum.Conflict, AssertToolError(await handlers["state"](null).ConfigureAwait(false)).ErrorCode, "InvalidOperationException (message ignored)");
                AssertEqual("fine", (string)await handlers["ok"](null).ConfigureAwait(false), "success passes through");

                bool threw = false;
                try { await handlers["crash"](null).ConfigureAwait(false); }
                catch (FormatException) { threw = true; }
                AssertTrue(threw, "an unmapped exception must propagate (reported by the transport as isError)");
            }));

            cases.Add(CaseAsync("rest_missing_entities_return_404", "REST routes answer 404 NotFound for missing entities", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpClient client = fx.AuthClient;

                // Routes that used to pick 404 by searching the exception message.
                await AssertStatusAsync(client, HttpMethod.Put, "/api/v1/deployments/dpl_missing", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/deployments/dpl_missing/approve", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/deployments/dpl_missing/deny", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/deployments/dpl_missing/verify", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/deployments/dpl_missing/rollback", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Put, "/api/v1/objectives/obj_missing", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Put, "/api/v1/backlog/obj_missing", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Put, "/api/v1/runbooks/pbk_missing", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/runbooks/pbk_missing/executions", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Put, "/api/v1/runbook-executions/rbx_missing", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Put, "/api/v1/releases/rel_missing", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/releases/rel_missing/refresh", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Put, "/api/v1/incidents/inc_missing", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Put, "/api/v1/environments/env_missing", "{}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);

                // Deletes that used to answer 404 for every InvalidOperationException.
                await AssertStatusAsync(client, HttpMethod.Delete, "/api/v1/deployments/dpl_missing", null, 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Delete, "/api/v1/objectives/obj_missing", null, 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Delete, "/api/v1/runbooks/pbk_missing", null, 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Delete, "/api/v1/runbook-executions/rbx_missing", null, 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Delete, "/api/v1/releases/rel_missing", null, 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Delete, "/api/v1/incidents/inc_missing", null, 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Delete, "/api/v1/environments/env_missing", null, 404, ApiResultEnum.NotFound).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("rest_referenced_missing_entity_is_404_and_bad_input_stays_400", "A missing referenced entity is 404; an invalid request stays 400", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpClient client = fx.AuthClient;

                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/deployments", "{\"vesselId\":\"vsl_missing\"}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/deployments", "{\"vesselId\":\"vsl_missing\",\"objectiveIds\":[\"obj_missing\"]}", 404, ApiResultEnum.NotFound).ConfigureAwait(false);
                // No vessel at all is an invalid request (InvalidOperationException), not a missing entity.
                await AssertStatusAsync(client, HttpMethod.Post, "/api/v1/deployments", "{}", 400, ApiResultEnum.BadRequest).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Typed not-found (REST and MCP)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static McpToolError AssertToolError(object result)
        {
            AssertTrue(result is McpToolError, "expected an McpToolError, got " + (result?.GetType().Name ?? "null"));
            return (McpToolError)result;
        }

        private static async Task AssertStatusAsync(HttpClient client, HttpMethod method, string path, string? body, int expectedStatus, ApiResultEnum expectedResult)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(expectedStatus, (int)response.StatusCode, method + " " + path + " status (body: " + text + ")");
                    ApiErrorProbe probe = ApiErrorProbe.From(text);
                    AssertEqual(expectedResult, probe.Error, method + " " + path + " Error code");
                }
            }
        }

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
