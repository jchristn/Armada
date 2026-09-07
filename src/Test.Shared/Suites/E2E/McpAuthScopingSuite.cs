namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end coverage for authenticated, per-user MCP. Where <see cref="McpToolSuite"/> drives the MCP
    /// JSON-RPC surface as an anonymous local caller (the additive default-admin fallback), this suite proves
    /// the authentication path itself: a credential presented on the MCP request over the advertised endpoint
    /// resolves to its owning tenant/user, entities created through MCP are attributed to that caller, and a
    /// different user cannot see them via MCP. Cases run sequentially against a fresh per-suite server and
    /// carry the two users' identifiers and clients as suite instance state.
    ///
    /// The requests target the advertised MCP endpoint path (<c>/mcp</c>, per
    /// <c>ArmadaMcpConfigBuilder.GetMcpUrl</c>), so a green run also confirms captains' generated MCP config
    /// reaches a served endpoint.
    /// </summary>
    public sealed class McpAuthScopingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.McpAuthScoping";

        // Armada's MCP server answers legacy JSON-RPC on /rpc (used by the tool suite, unauthenticated), and
        // serves the advertised MCP streamable-HTTP endpoint at /mcp -- the endpoint ArmadaMcpConfigBuilder
        // writes into captains' config, and the one where the credential authentication handler runs. This
        // suite drives /mcp so the per-user auth path is exercised exactly as a real captain hits it.
        private const string AdvertisedMcpPath = "/mcp";

        private string? _TenantAId;
        private string? _UserAId;
        private string? _CredentialAId;
        private HttpClient? _McpA;

        private string? _TenantBId;
        private string? _UserBId;
        private string? _CredentialBId;
        private HttpClient? _McpB;

        private string _FleetAId = null!;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the authenticated MCP scoping suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("advertised_mcp_endpoint_is_served", "The advertised /mcp endpoint is served and enforces the MCP streamable Accept header", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);

                // A POST to the advertised /mcp path without the streamable-HTTP Accept header is rejected
                // with 406 (not 404): proof the endpoint is served and is the MCP streamable transport that
                // captains' generated config targets. A 404 here would mean the advertised path is dead.
                HttpRequestMessage probe = new HttpRequestMessage(HttpMethod.Post, AdvertisedMcpPath);
                probe.Content = JsonHelper.ToJsonContent(new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });

                HttpResponseMessage response = await fx.McpClient.SendAsync(probe).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                AssertFalse(response.StatusCode == System.Net.HttpStatusCode.NotFound,
                    "The advertised /mcp endpoint must be served (got 404).");
                AssertEqual(System.Net.HttpStatusCode.NotAcceptable, response.StatusCode);
                AssertContains("Accept", body);
            }));

            cases.Add(CaseAsync("setup_two_authenticated_users", "Admin mints a bearer credential per user and builds authenticated MCP clients", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);

                TenantUserCredential a = await CreateTenantWithUserAsync(fx.AuthClient, "mcpAuthA").ConfigureAwait(false);
                _TenantAId = a.TenantId;
                _UserAId = a.UserId;
                _CredentialAId = a.CredentialId;
                _McpA = CreateAuthenticatedMcpClient(fx.McpPort, a.BearerToken);

                TenantUserCredential b = await CreateTenantWithUserAsync(fx.AuthClient, "mcpAuthB").ConfigureAwait(false);
                _TenantBId = b.TenantId;
                _UserBId = b.UserId;
                _CredentialBId = b.CredentialId;
                _McpB = CreateAuthenticatedMcpClient(fx.McpPort, b.BearerToken);

                AssertNotNull(_TenantAId, "Tenant A id");
                AssertNotNull(_UserAId, "User A id");
                AssertNotNull(_TenantBId, "Tenant B id");
                AssertNotNull(_UserBId, "User B id");
            }));

            cases.Add(CaseAsync("authenticated_mcp_call_resolves_caller_identity", "A fleet created over authenticated MCP is attributed to the caller's tenant and user", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);

                string sessionId = await InitMcpSessionAsync(_McpA!).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(_McpA!, sessionId, "create_fleet", new
                {
                    name = "mcp-auth-fleet-" + Guid.NewGuid().ToString("N").Substring(0, 8)
                }).ConfigureAwait(false);

                AssertToolResultValid(result);
                Fleet fleet = JsonHelper.Deserialize<Fleet>(GetToolResultText(result));
                AssertNotNull(fleet.Id, "Created fleet id");
                _FleetAId = fleet.Id;

                // The heart of the test: the authenticated MCP call was attributed to user A's identity,
                // not the default admin context, proving the credential resolved end to end through
                // AuthenticateMcpRequestAsync -> RpcCallContext -> the tool handler.
                AssertEqual(_TenantAId, fleet.TenantId);
                AssertEqual(_UserAId, fleet.UserId);
            }));

            cases.Add(CaseAsync("authenticated_read_is_scoped_to_owner", "The owning user sees their own fleet when enumerating over MCP", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);

                string sessionId = await InitMcpSessionAsync(_McpA!).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(_McpA!, sessionId, "enumerate", new
                {
                    entityType = "fleets",
                    pageSize = 100
                }).ConfigureAwait(false);

                AssertToolResultValid(result);
                bool found = EnumerateContainsId(GetToolResultText(result), _FleetAId);
                AssertTrue(found, "Expected user A's fleet " + _FleetAId + " to be visible to user A over MCP.");
            }));

            cases.Add(CaseAsync("mcp_enforces_cross_user_isolation", "A different user cannot see the fleet when enumerating over MCP", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);

                string sessionId = await InitMcpSessionAsync(_McpB!).ConfigureAwait(false);
                JsonElement result = await CallToolAsync(_McpB!, sessionId, "enumerate", new
                {
                    entityType = "fleets",
                    pageSize = 100
                }).ConfigureAwait(false);

                AssertToolResultValid(result);
                bool found = EnumerateContainsId(GetToolResultText(result), _FleetAId);
                AssertFalse(found, "Expected user A's fleet " + _FleetAId + " to be hidden from user B over MCP.");
            }));

            cases.Add(CaseAsync("cleanup_delete_scoping_resources", "Delete the credentials, users, and tenants created by this suite", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpClient admin = fx.AuthClient;

                _McpA?.Dispose();
                _McpB?.Dispose();

                if (_CredentialAId != null) await admin.DeleteAsync("/api/v1/credentials/" + _CredentialAId).ConfigureAwait(false);
                if (_CredentialBId != null) await admin.DeleteAsync("/api/v1/credentials/" + _CredentialBId).ConfigureAwait(false);
                if (_UserAId != null) await admin.DeleteAsync("/api/v1/users/" + _UserAId).ConfigureAwait(false);
                if (_UserBId != null) await admin.DeleteAsync("/api/v1/users/" + _UserBId).ConfigureAwait(false);
                if (_TenantAId != null) await admin.DeleteAsync("/api/v1/tenants/" + _TenantAId).ConfigureAwait(false);
                if (_TenantBId != null) await admin.DeleteAsync("/api/v1/tenants/" + _TenantBId).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "MCP Authenticated Scoping",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<TenantUserCredential> CreateTenantWithUserAsync(HttpClient adminClient, string label)
        {
            string tenantName = "mcpauth-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage tenantResp = await adminClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new { Name = tenantName })).ConfigureAwait(false);
            TenantMetadata tenant = await JsonHelper.DeserializeAsync<TenantMetadata>(tenantResp).ConfigureAwait(false);

            string email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@mcpauth.armada";
            HttpResponseMessage userResp = await adminClient.PostAsync("/api/v1/users",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenant.Id,
                    Email = email,
                    PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                    IsTenantAdmin = true
                })).ConfigureAwait(false);
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);

            HttpResponseMessage credResp = await adminClient.PostAsync("/api/v1/credentials",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    Name = label + "-cred"
                })).ConfigureAwait(false);
            Credential cred = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);

            return new TenantUserCredential
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                CredentialId = cred.Id,
                BearerToken = cred.BearerToken
            };
        }

        private static HttpClient CreateAuthenticatedMcpClient(int mcpPort, string bearerToken)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri("http://127.0.0.1:" + mcpPort);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        /// <summary>
        /// Open an MCP streamable-HTTP session against the advertised /mcp endpoint: send <c>initialize</c>,
        /// capture the server-assigned <c>Mcp-Session-Id</c>, and send the <c>notifications/initialized</c>
        /// acknowledgement. The returned id is threaded onto every later request. The client's Bearer header
        /// (if any) rides on each request, so the server authenticates the caller per request.
        /// </summary>
        private static async Task<string> InitMcpSessionAsync(HttpClient mcpClient)
        {
            StreamableResponse init = await SendStreamableAsync(mcpClient, null, "initialize", 1, new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "auth-scoping-client", version = "1.0" }
            }).ConfigureAwait(false);

            string sessionId = init.SessionId;
            // Acknowledge initialization (a notification carries no id and expects no result body).
            await SendNotificationAsync(mcpClient, sessionId, "notifications/initialized").ConfigureAwait(false);
            return sessionId;
        }

        private static async Task<JsonElement> CallToolAsync(HttpClient mcpClient, string sessionId, string toolName, object arguments)
        {
            StreamableResponse response = await SendStreamableAsync(mcpClient, sessionId, "tools/call", 2, new
            {
                name = toolName,
                arguments = arguments
            }).ConfigureAwait(false);
            return response.Result;
        }

        private static async Task SendNotificationAsync(HttpClient mcpClient, string sessionId, string method)
        {
            HttpRequestMessage httpRequest = BuildStreamableRequest(sessionId, new
            {
                jsonrpc = "2.0",
                method = method,
                @params = new { }
            });
            HttpResponseMessage response = await mcpClient.SendAsync(httpRequest).ConfigureAwait(false);
            response.Dispose();
        }

        /// <summary>
        /// Send one JSON-RPC request over the MCP streamable-HTTP transport and return its result plus the
        /// effective session id. Tolerates both a plain application/json response and a text/event-stream
        /// (SSE) response, extracting the JSON-RPC envelope from the first <c>data:</c> frame in the latter.
        /// </summary>
        private static async Task<StreamableResponse> SendStreamableAsync(HttpClient mcpClient, string? sessionId, string method, int id, object parameters)
        {
            HttpRequestMessage httpRequest = BuildStreamableRequest(sessionId, new
            {
                jsonrpc = "2.0",
                id = id,
                method = method,
                @params = parameters
            });

            HttpResponseMessage response = await mcpClient.SendAsync(httpRequest).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Assert(response.IsSuccessStatusCode,
                "MCP streamable request '" + method + "' to " + AdvertisedMcpPath + " failed with " + response.StatusCode + ": " + body);

            string effectiveSession = sessionId ?? String.Empty;
            if (response.Headers.TryGetValues("Mcp-Session-Id", out System.Collections.Generic.IEnumerable<string>? values))
            {
                foreach (string value in values) { effectiveSession = value; break; }
            }

            JsonElement envelope = ParseEnvelope(body);
            if (envelope.TryGetProperty("error", out JsonElement error))
                throw new Exception("MCP error: " + error.GetProperty("message").GetString());

            JsonElement result = envelope.TryGetProperty("result", out JsonElement resultElement) ? resultElement : default;
            return new StreamableResponse { Result = result, SessionId = effectiveSession };
        }

        private static HttpRequestMessage BuildStreamableRequest(string? sessionId, object payload)
        {
            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, AdvertisedMcpPath);
            httpRequest.Content = JsonHelper.ToJsonContent(payload);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (!String.IsNullOrEmpty(sessionId)) httpRequest.Headers.Add("Mcp-Session-Id", sessionId);
            return httpRequest;
        }

        /// <summary>
        /// Parse a JSON-RPC envelope from either a plain JSON body or an SSE body (event-stream frames).
        /// </summary>
        private static JsonElement ParseEnvelope(string body)
        {
            string trimmed = body.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<JsonElement>(body);

            // SSE: collect the first non-empty data: frame and parse it as the JSON-RPC envelope.
            foreach (string rawLine in body.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    string data = line.Substring(5).Trim();
                    if (!String.IsNullOrEmpty(data) && data.StartsWith("{", StringComparison.Ordinal))
                        return JsonSerializer.Deserialize<JsonElement>(data);
                }
            }

            throw new Exception("Could not parse an MCP JSON-RPC envelope from response body: " + body);
        }

        private static void AssertToolResultValid(JsonElement result)
        {
            Assert(result.TryGetProperty("content", out JsonElement content), "Tool result should have content array");
            Assert(content.GetArrayLength() > 0, "Content array should not be empty");
            AssertEqual("text", content[0].GetProperty("type").GetString());
            AssertFalse(string.IsNullOrEmpty(content[0].GetProperty("text").GetString()), "Tool result text should not be empty");
        }

        private static string GetToolResultText(JsonElement result)
        {
            return result.GetProperty("content")[0].GetProperty("text").GetString()!;
        }

        private static bool EnumerateContainsId(string enumerateText, string id)
        {
            EnumerationResult<JsonElement> data = JsonHelper.Deserialize<EnumerationResult<JsonElement>>(enumerateText);
            if (data.Objects == null) return false;
            foreach (JsonElement obj in data.Objects)
            {
                if (obj.TryGetProperty("Id", out JsonElement idElement) && String.Equals(idElement.GetString(), id, StringComparison.Ordinal))
                    return true;
            }
            return false;
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

        #endregion

        #region Nested-Types

        private sealed class TenantUserCredential
        {
            public string TenantId { get; set; } = String.Empty;

            public string UserId { get; set; } = String.Empty;

            public string CredentialId { get; set; } = String.Empty;

            public string BearerToken { get; set; } = String.Empty;
        }

        private sealed class StreamableResponse
        {
            public JsonElement Result { get; set; }

            public string SessionId { get; set; } = String.Empty;
        }

        #endregion
    }
}
