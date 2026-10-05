namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Security coverage for the Admiral /ws endpoint: upgrades must be authenticated (query token, Sec-WebSocket-Protocol
    /// token, or the REST credential headers), WebSocket commands must be authorized like their REST equivalents, and
    /// entity change events must only reach sockets of the entity's tenant (global admins may opt in to every tenant).
    /// These cases were written first as reproductions of the open /ws defect and fail closed once it is fixed.
    /// </summary>
    public sealed class WebSocketSecuritySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.WebSocketSecurity";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("unauthenticated_upgrade_rejected", "An upgrade without credentials is rejected", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                bool connected = await TryConnectAsync(fx.RestPort, null, null, null).ConfigureAwait(false);
                AssertFalse(connected, "an unauthenticated /ws upgrade must be rejected");
            }));

            cases.Add(CaseAsync("invalid_token_rejected", "An upgrade with an invalid token is rejected", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                bool connected = await TryConnectAsync(fx.RestPort, "not-a-real-token", null, null).ConfigureAwait(false);
                AssertFalse(connected, "an invalid query token must be rejected");
            }));

            cases.Add(CaseAsync("unauthenticated_client_cannot_run_mutating_command", "An unauthenticated client cannot create a fleet over the socket", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string fleetName = "ws-unauth-" + Guid.NewGuid().ToString("N").Substring(0, 8);

                WebSocketTestClient? client = null;
                try
                {
                    client = await WebSocketTestClient.ConnectAsync(fx.RestPort).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                    client = null;
                }

                if (client != null)
                {
                    using (client)
                    {
                        await client.SendAsync(new Dictionary<string, object> { ["Route"] = "command", ["action"] = "create_fleet", ["data"] = new { Name = fleetName } }).ConfigureAwait(false);
                        await client.WaitForAsync(E2eWebSocketFrame.IsCommandReply, 3000).ConfigureAwait(false);
                    }
                }

                bool exists = await FleetExistsAsync(fx.AuthClient, fleetName).ConfigureAwait(false);
                AssertFalse(exists, "an unauthenticated socket must not be able to create a fleet");
            }));

            cases.Add(CaseAsync("query_token_accepted", "A valid token in the query string authenticates the upgrade", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "query").ConfigureAwait(false);
                using (WebSocketTestClient client = await WebSocketTestClient.ConnectAsync(fx.RestPort, user.BearerToken).ConfigureAwait(false))
                {
                    await client.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe" }).ConfigureAwait(false);
                    string? snapshot = await client.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false);
                    AssertNotNull(snapshot, "subscribe should return a status snapshot");
                }
            }));

            cases.Add(CaseAsync("session_token_in_query_accepted", "A dashboard session token (base64, percent-encoded) in the query string authenticates the upgrade", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "session").ConfigureAwait(false);
                string sessionToken = new Armada.Core.Services.SessionTokenService(fx.SessionTokenEncryptionKey).CreateToken(user.TenantId, user.UserId).Token!;
                bool connected = await TryConnectAsync(fx.RestPort, sessionToken, null, null).ConfigureAwait(false);
                AssertTrue(connected, "a percent-encoded session token should authenticate the upgrade");

                string threadToken = new Armada.Core.Services.SessionTokenService(fx.SessionTokenEncryptionKey).CreateThreadScopedToken(user.TenantId, user.UserId, "ath_x", TimeSpan.FromMinutes(5)).Token!;
                AssertFalse(await TryConnectAsync(fx.RestPort, threadToken, null, null).ConfigureAwait(false), "a thread-scoped token must be refused");
            }));

            cases.Add(CaseAsync("subprotocol_token_accepted", "A token carried in Sec-WebSocket-Protocol authenticates the upgrade", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "proto").ConfigureAwait(false);
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(user.BearerToken)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                bool connected = await TryConnectAsync(fx.RestPort, null, null, new List<string> { "armada", "armada-token." + encoded }).ConfigureAwait(false);
                AssertTrue(connected, "a Sec-WebSocket-Protocol token should authenticate the upgrade");
            }));

            cases.Add(CaseAsync("header_api_key_accepted", "The REST X-Api-Key header authenticates the upgrade", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                bool connected = await TryConnectAsync(fx.RestPort, null, ApiKeyHeaders(fx), null).ConfigureAwait(false);
                AssertTrue(connected, "X-Api-Key should authenticate the upgrade");
            }));

            cases.Add(CaseAsync("non_admin_command_forbidden", "A tenant admin cannot run WebSocket commands", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser user = await E2ETenantUser.CreateAsync(fx.AuthClient, "cmd").ConfigureAwait(false);
                string fleetName = "ws-tenant-cmd-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                using (WebSocketTestClient client = await WebSocketTestClient.ConnectAsync(fx.RestPort, user.BearerToken).ConfigureAwait(false))
                {
                    await client.SendAsync(new Dictionary<string, object> { ["Route"] = "command", ["action"] = "create_fleet", ["data"] = new { Name = fleetName } }).ConfigureAwait(false);
                    string? reply = await client.WaitForAsync(E2eWebSocketFrame.IsCommandReply).ConfigureAwait(false);
                    AssertNotNull(reply, "the command should be answered");
                    E2eWebSocketFrame frame = E2eWebSocketFrame.Parse(reply)!;
                    AssertEqual("command.error", frame.Type, "the command should be refused");
                    AssertEqual("create_fleet", frame.Action, "the refusal answers the create_fleet command");
                }

                bool exists = await FleetExistsAsync(fx.AuthClient, fleetName).ConfigureAwait(false);
                AssertFalse(exists, "a refused command must not create a fleet");
            }));

            cases.Add(CaseAsync("admin_command_allowed", "A global admin can still run WebSocket commands", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                string fleetName = "ws-admin-cmd-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                using (WebSocketTestClient client = await WebSocketTestClient.ConnectAsync(fx.RestPort, null, ApiKeyHeaders(fx)).ConfigureAwait(false))
                {
                    await client.SendAsync(new Dictionary<string, object> { ["Route"] = "command", ["action"] = "create_fleet", ["data"] = new { Name = fleetName } }).ConfigureAwait(false);
                    string? reply = await client.WaitForAsync(E2eWebSocketFrame.IsCommandReply).ConfigureAwait(false);
                    AssertNotNull(reply, "the command should be answered");
                    E2eWebSocketFrame frame = E2eWebSocketFrame.Parse(reply)!;
                    AssertEqual("command.result", frame.Type, "the command should succeed: " + reply);
                    AssertEqual("create_fleet", frame.Action, "the result answers the create_fleet command");
                }

                bool exists = await FleetExistsAsync(fx.AuthClient, fleetName).ConfigureAwait(false);
                AssertTrue(exists, "the admin command should create the fleet");
            }));

            cases.Add(CaseAsync("other_tenant_does_not_receive_entity_events", "Entity change events stay inside the entity's tenant", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "owner").ConfigureAwait(false);
                E2ETenantUser other = await E2ETenantUser.CreateAsync(fx.AuthClient, "other").ConfigureAwait(false);

                using (HttpClient ownerClient = owner.CreateClient(fx.BaseUrl))
                using (WebSocketTestClient ownerSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, owner.BearerToken).ConfigureAwait(false))
                using (WebSocketTestClient otherSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, other.BearerToken).ConfigureAwait(false))
                {
                    await SubscribeAsync(ownerSocket).ConfigureAwait(false);
                    await SubscribeAsync(otherSocket).ConfigureAwait(false);

                    string voyageId = await CreateAndCancelVoyageAsync(ownerClient).ConfigureAwait(false);

                    string? ownerEvent = await ownerSocket.WaitForAsync(m => E2eWebSocketEventFrame.ParseOfType(m, "voyage.changed")?.Data?.Id == voyageId).ConfigureAwait(false);
                    AssertNotNull(ownerEvent, "the owning tenant should receive voyage.changed");

                    // Deliberately broad: a frame of any type that carries the id anywhere is a leak.
                    string? leaked = await otherSocket.WaitForAsync(m => m.Contains(voyageId, StringComparison.Ordinal), 2000).ConfigureAwait(false);
                    AssertNull(leaked, "another tenant must not receive the voyage's events");
                }
            }));

            cases.Add(CaseAsync("admin_all_tenants_opt_in", "A global admin receives other tenants' events only after opting in", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "optin").ConfigureAwait(false);

                using (HttpClient ownerClient = owner.CreateClient(fx.BaseUrl))
                using (WebSocketTestClient plainAdmin = await WebSocketTestClient.ConnectAsync(fx.RestPort, null, ApiKeyHeaders(fx)).ConfigureAwait(false))
                using (WebSocketTestClient allTenantsAdmin = await WebSocketTestClient.ConnectAsync(fx.RestPort, null, ApiKeyHeaders(fx)).ConfigureAwait(false))
                {
                    await SubscribeAsync(plainAdmin).ConfigureAwait(false);
                    await allTenantsAdmin.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe", ["AllTenants"] = true }).ConfigureAwait(false);
                    AssertNotNull(await allTenantsAdmin.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false), "snapshot");

                    string voyageId = await CreateAndCancelVoyageAsync(ownerClient).ConfigureAwait(false);

                    string? seen = await allTenantsAdmin.WaitForAsync(m => E2eWebSocketEventFrame.ParseOfType(m, "voyage.changed")?.Data?.Id == voyageId).ConfigureAwait(false);
                    AssertNotNull(seen, "an admin that opted in to all tenants should receive the event");

                    // Deliberately broad: a frame of any type that carries the id anywhere counts as delivered.
                    string? notSeen = await plainAdmin.WaitForAsync(m => m.Contains(voyageId, StringComparison.Ordinal), 1500).ConfigureAwait(false);
                    AssertNull(notSeen, "an admin that did not opt in should only receive its own tenant's events");
                }
            }));

            cases.Add(CaseAsync("non_admin_all_tenants_flag_ignored", "A non-admin cannot opt in to other tenants' events", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "flagowner").ConfigureAwait(false);
                E2ETenantUser snoop = await E2ETenantUser.CreateAsync(fx.AuthClient, "snoop").ConfigureAwait(false);

                using (HttpClient ownerClient = owner.CreateClient(fx.BaseUrl))
                using (WebSocketTestClient snoopSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, snoop.BearerToken).ConfigureAwait(false))
                {
                    await snoopSocket.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe", ["AllTenants"] = true }).ConfigureAwait(false);
                    AssertNotNull(await snoopSocket.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false), "snapshot");

                    string voyageId = await CreateAndCancelVoyageAsync(ownerClient).ConfigureAwait(false);
                    // Deliberately broad: a frame of any type that carries the id anywhere is a leak.
                    string? leaked = await snoopSocket.WaitForAsync(m => m.Contains(voyageId, StringComparison.Ordinal), 2000).ConfigureAwait(false);
                    AssertNull(leaked, "the AllTenants flag must be ignored for non-admins");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "WebSocket Security",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string> ApiKeyHeaders(E2EServerFixture fx)
        {
            return new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey };
        }

        private static async Task<bool> TryConnectAsync(int restPort, string? queryToken, Dictionary<string, string>? headers, List<string>? subprotocols)
        {
            try
            {
                using (WebSocketTestClient client = await WebSocketTestClient.ConnectAsync(restPort, queryToken, headers, subprotocols).ConfigureAwait(false))
                {
                    await client.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe" }).ConfigureAwait(false);
                    string? snapshot = await client.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot"), 3000).ConfigureAwait(false);
                    return snapshot != null;
                }
            }
            catch (WebSocketException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static async Task SubscribeAsync(WebSocketTestClient client)
        {
            await client.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe" }).ConfigureAwait(false);
            string? snapshot = await client.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false);
            AssertNotNull(snapshot, "subscribe should return a status snapshot");
        }

        private static async Task<string> CreateAndCancelVoyageAsync(HttpClient client)
        {
            HttpResponseMessage create = await client.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new { Title = "ws-security-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
            create.EnsureSuccessStatusCode();
            Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(create).ConfigureAwait(false);

            HttpResponseMessage cancel = await client.DeleteAsync("/api/v1/voyages/" + voyage.Id).ConfigureAwait(false);
            cancel.EnsureSuccessStatusCode();
            return voyage.Id;
        }

        private static async Task<bool> FleetExistsAsync(HttpClient adminClient, string name)
        {
            // A failed listing must fail the test, never read as "the fleet does not exist".
            int page = 1;
            while (true)
            {
                HttpResponseMessage response = await adminClient.GetAsync("/api/v1/fleets?pageSize=1000&pageNumber=" + page).ConfigureAwait(false);
                AssertStatusCode(System.Net.HttpStatusCode.OK, response, "admin fleet listing");
                EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(response).ConfigureAwait(false);
                AssertNotNull(result, "fleet listing body");
                if (result.Objects.Any(f => String.Equals(f.Name, name, StringComparison.Ordinal))) return true;
                if (page >= result.TotalPages || result.Objects.Count == 0) return false;
                page++;
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

        #endregion
    }
}
