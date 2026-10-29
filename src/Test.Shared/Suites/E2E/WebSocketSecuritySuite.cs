namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Server;
    using Armada.Server.WebSocket;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Security coverage for the Admiral /ws endpoint: upgrades must be authenticated (query token, Sec-WebSocket-Protocol
    /// token, or the REST credential headers), WebSocket commands must be authorized like their REST equivalents, and
    /// entity change events must only reach sockets of the entity's tenant (global admins may opt in to every tenant).
    /// Mission, voyage, captain, check-run, mission-activity, and approval events must also stay with the sockets that
    /// can read that record. These cases were written first as reproductions of the open /ws defect and fail closed
    /// once it is fixed.
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
                    AssertEqual(Armada.Core.Enums.WebSocketCommandErrorCodeEnum.Forbidden, frame.Code, "the refusal carries code Forbidden");
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

            cases.Add(CaseAsync("record_scope_mission_changed", "Another user does not receive a mission.changed title", TestTags.Negative, async () =>
            {
                await AssertOwnedRecordHiddenAsync(marker =>
                {
                    Mission mission = OwnedMission(marker.Audience, marker.Text);
                    Hub(marker.Fixture).BroadcastMissionChange(mission);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("record_scope_mission_activity", "Another user does not receive mission.activity text", TestTags.Negative, async () =>
            {
                await AssertOwnedRecordHiddenAsync(marker =>
                {
                    RuntimeActivity activity = new RuntimeActivity();
                    activity.Summary = marker.Text;
                    BroadcastMissionActivity(Hub(marker.Fixture), marker.Audience.TenantId, marker.Audience.Owner.UserId, "msn_" + marker.Text, null, activity);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("record_scope_approval_needed", "Another user does not receive an approval-needed title", TestTags.Negative, async () =>
            {
                await AssertOwnedRecordHiddenAsync(marker =>
                {
                    Mission mission = OwnedMission(marker.Audience, marker.Text);
                    Hub(marker.Fixture).BroadcastApprovalNeeded(mission);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("record_scope_voyage_changed", "Another user does not receive a voyage.changed title", TestTags.Negative, async () =>
            {
                await AssertOwnedRecordHiddenAsync(marker =>
                {
                    Voyage voyage = new Voyage();
                    voyage.Id = "vyg_" + marker.Text;
                    voyage.TenantId = marker.Audience.TenantId;
                    voyage.UserId = marker.Audience.Owner.UserId;
                    voyage.Title = marker.Text;
                    Hub(marker.Fixture).BroadcastVoyageChange(voyage);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("record_scope_captain_changed", "Another user does not receive a captain.changed name", TestTags.Negative, async () =>
            {
                await AssertOwnedRecordHiddenAsync(marker =>
                {
                    Captain captain = new Captain();
                    captain.Id = "cpt_" + marker.Text;
                    captain.TenantId = marker.Audience.TenantId;
                    captain.UserId = marker.Audience.Owner.UserId;
                    captain.Name = marker.Text;
                    Hub(marker.Fixture).BroadcastCaptainChange(captain);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("record_scope_check_run_changed", "Another user does not receive a check-run.changed label", TestTags.Negative, async () =>
            {
                await AssertOwnedRecordHiddenAsync(marker =>
                {
                    CheckRun run = new CheckRun();
                    run.Id = "chk_" + marker.Text;
                    run.TenantId = marker.Audience.TenantId;
                    run.UserId = marker.Audience.Owner.UserId;
                    run.Label = marker.Text;
                    run.Command = "echo scope";
                    Hub(marker.Fixture).BroadcastCheckRunChange(run);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("record_scope_empty_user", "A record with no user id stays with tenant administrators", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                using (RecordAudience audience = await RecordAudience.OpenAsync(fx).ConfigureAwait(false))
                {
                    string marker = Marker();
                    Mission mission = new Mission();
                    mission.Id = "msn_" + marker;
                    mission.TenantId = audience.TenantId;
                    mission.UserId = null;
                    mission.Title = marker;
                    Hub(fx).BroadcastMissionChange(mission);
                    await AssertReadersAsync(audience.TenantAdminSocket, audience.AllTenantsAdmin, marker, "empty user id").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.OtherSocket, marker, "another user must not receive a record with no user id").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.OwnerSocket, marker, "a normal user must not receive a record with no user id").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.PlainAdmin, marker, "a global admin that did not opt in must not receive another tenant's record with no user id").ConfigureAwait(false);

                    string blankMarker = Marker();
                    Mission blank = new Mission();
                    blank.Id = "msn_" + blankMarker;
                    blank.TenantId = audience.TenantId;
                    blank.UserId = "";
                    blank.Title = blankMarker;
                    Hub(fx).BroadcastMissionChange(blank);
                    await AssertReadersAsync(audience.TenantAdminSocket, audience.AllTenantsAdmin, blankMarker, "blank user id").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.OtherSocket, blankMarker, "another user must not receive a record with a blank user id").ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("record_scope_empty_tenant", "A record with no tenant id reaches global administrators only", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                using (RecordAudience audience = await RecordAudience.OpenAsync(fx).ConfigureAwait(false))
                {
                    string marker = Marker();
                    Mission mission = new Mission();
                    mission.Id = "msn_" + marker;
                    mission.TenantId = null;
                    mission.UserId = audience.Owner.UserId;
                    mission.Title = marker;
                    Hub(fx).BroadcastMissionChange(mission);
                    AssertNotNull(await audience.PlainAdmin.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "a global admin receives a record with no tenant id");
                    AssertNotNull(await audience.AllTenantsAdmin.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "an all-tenant global admin receives a record with no tenant id");
                    await AssertAbsentAsync(audience.OwnerSocket, marker, "a normal user must not receive a record with no tenant id").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.TenantAdminSocket, marker, "a tenant admin must not receive a record with no tenant id").ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("record_scope_ask_stays_with_user", "An Ask event still reaches only that user", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                using (RecordAudience audience = await RecordAudience.OpenAsync(fx).ConfigureAwait(false))
                using (HttpClient ownerClient = audience.Owner.CreateClient(fx.BaseUrl))
                {
                    string marker = Marker();
                    HttpResponseMessage created = await ownerClient.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { Title = marker })).ConfigureAwait(false);
                    created.EnsureSuccessStatusCode();
                    AssertNotNull(await audience.OwnerSocket.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "ask.thread") && m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "the thread owner receives ask.thread");
                    await AssertAbsentAsync(audience.OtherSocket, marker, "another user must not receive the Ask event").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.TenantAdminSocket, marker, "a tenant admin must not receive another user's Ask event").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.AllTenantsAdmin, marker, "an all-tenant global admin must not receive an Ask event").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.PlainAdmin, marker, "a global admin must not receive an Ask event").ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("record_scope_cli_permission_stays_can_view", "A CLI permission event still follows CliPermissionAccess.CanView", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                using (RecordAudience audience = await RecordAudience.OpenAsync(fx).ConfigureAwait(false))
                {
                    string marker = Marker();
                    CliPermissionRequest request = new CliPermissionRequest();
                    request.Id = "cpr_" + marker;
                    request.TenantId = audience.TenantId;
                    request.UserId = audience.Owner.UserId;
                    request.ToolName = marker;
                    Hub(fx).BroadcastCliPermission("cli_permission.requested", request);
                    AssertNotNull(await audience.OwnerSocket.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "the owner receives the CLI permission event");
                    AssertNotNull(await audience.TenantAdminSocket.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "the tenant admin receives the CLI permission event");
                    AssertNotNull(await audience.AllTenantsAdmin.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "an all-tenant global admin receives the CLI permission event");
                    await AssertAbsentAsync(audience.OtherSocket, marker, "another user must not receive the CLI permission event").ConfigureAwait(false);
                    await AssertAbsentAsync(audience.PlainAdmin, marker, "a global admin that did not opt in must not receive another tenant's CLI permission event").ConfigureAwait(false);
                }
            }));

            cases.Add(CaseAsync("record_scope_objective_stays_tenant", "An objective event still reaches the whole tenant", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                using (RecordAudience audience = await RecordAudience.OpenAsync(fx).ConfigureAwait(false))
                {
                    string marker = Marker();
                    Objective objective = new Objective();
                    objective.Id = "obj_" + marker;
                    objective.TenantId = audience.TenantId;
                    objective.UserId = audience.Owner.UserId;
                    objective.Title = marker;
                    Hub(fx).BroadcastObjectiveChange(objective);
                    AssertNotNull(await audience.OtherSocket.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "another user in the tenant still receives objective.changed");
                    AssertNotNull(await audience.TenantAdminSocket.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "the tenant admin receives objective.changed");
                    AssertNotNull(await audience.AllTenantsAdmin.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "an all-tenant global admin receives objective.changed");
                    await AssertAbsentAsync(audience.PlainAdmin, marker, "a global admin that did not opt in must not receive another tenant's objective").ConfigureAwait(false);
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

        private static string Marker()
        {
            return "wsrec" + Guid.NewGuid().ToString("N").Substring(0, 16);
        }

        private async Task AssertOwnedRecordHiddenAsync(Func<RecordMarker, Task> broadcastAsync)
        {
            E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
            using (RecordAudience audience = await RecordAudience.OpenAsync(fx).ConfigureAwait(false))
            {
                string marker = Marker();
                RecordMarker scope = new RecordMarker();
                scope.Fixture = fx;
                scope.Audience = audience;
                scope.Text = marker;
                await broadcastAsync(scope).ConfigureAwait(false);
                await AssertReadersAsync(audience.OwnerSocket, audience.TenantAdminSocket, audience.AllTenantsAdmin, marker, "owned record").ConfigureAwait(false);
                await AssertAbsentAsync(audience.OtherSocket, marker, "another user must not receive the record").ConfigureAwait(false);
                await AssertAbsentAsync(audience.PlainAdmin, marker, "a global admin that did not opt in must not receive another tenant's record").ConfigureAwait(false);
            }
        }

        private static async Task AssertReadersAsync(WebSocketTestClient owner, WebSocketTestClient tenantAdmin, WebSocketTestClient allTenantsAdmin, string marker, string label)
        {
            AssertNotNull(await owner.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "the owner receives " + label);
            AssertNotNull(await tenantAdmin.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "the tenant admin receives " + label);
            AssertNotNull(await allTenantsAdmin.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "an all-tenant global admin receives " + label);
        }

        private static async Task AssertReadersAsync(WebSocketTestClient tenantAdmin, WebSocketTestClient allTenantsAdmin, string marker, string label)
        {
            AssertNotNull(await tenantAdmin.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "the tenant admin receives " + label);
            AssertNotNull(await allTenantsAdmin.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal)).ConfigureAwait(false), "an all-tenant global admin receives " + label);
        }

        private static async Task AssertAbsentAsync(WebSocketTestClient client, string marker, string message)
        {
            string? leaked = await client.WaitForAsync(m => m.Contains(marker, StringComparison.Ordinal), 2000).ConfigureAwait(false);
            AssertNull(leaked, message);
        }

        private static Mission OwnedMission(RecordAudience audience, string marker)
        {
            Mission mission = new Mission();
            mission.Id = "msn_" + marker;
            mission.TenantId = audience.TenantId;
            mission.UserId = audience.Owner.UserId;
            mission.Title = marker;
            return mission;
        }

        private static ArmadaWebSocketHub Hub(E2EServerFixture fx)
        {
            FieldInfo? field = typeof(ArmadaServer).GetField("_WebSocketHub", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("The WebSocket hub is not available.");
            ArmadaWebSocketHub? hub = field.GetValue(fx.Server) as ArmadaWebSocketHub;
            if (hub == null) throw new InvalidOperationException("The WebSocket hub is not available.");
            return hub;
        }

        private static void BroadcastMissionActivity(ArmadaWebSocketHub hub, string? tenantId, string? userId, string missionId, string? voyageId, RuntimeActivity activity)
        {
            MethodInfo? selected = null;
            foreach (MethodInfo candidate in typeof(ArmadaWebSocketHub).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!String.Equals(candidate.Name, "BroadcastMissionActivity", StringComparison.Ordinal)) continue;
                if (selected == null || candidate.GetParameters().Length > selected.GetParameters().Length)
                    selected = candidate;
            }

            if (selected == null) throw new InvalidOperationException("BroadcastMissionActivity is not available.");
            List<object?> args = new List<object?>();
            foreach (ParameterInfo parameter in selected.GetParameters())
            {
                if (String.Equals(parameter.Name, "tenantId", StringComparison.Ordinal)) args.Add(tenantId);
                else if (String.Equals(parameter.Name, "userId", StringComparison.Ordinal)) args.Add(userId);
                else if (String.Equals(parameter.Name, "missionId", StringComparison.Ordinal)) args.Add(missionId);
                else if (String.Equals(parameter.Name, "voyageId", StringComparison.Ordinal)) args.Add(voyageId);
                else if (String.Equals(parameter.Name, "activity", StringComparison.Ordinal)) args.Add(activity);
                else throw new InvalidOperationException("Unexpected BroadcastMissionActivity parameter " + parameter.Name);
            }

            try
            {
                selected.Invoke(hub, args.ToArray());
            }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException != null) throw ex.InnerException;
                throw;
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

        /// <summary>
        /// One broadcast under test and the sockets that should or should not see it.
        /// </summary>
        private sealed class RecordMarker
        {
            /// <summary>Server the sockets are connected to.</summary>
            public E2EServerFixture Fixture { get; set; } = null!;

            /// <summary>Connected identities.</summary>
            public RecordAudience Audience { get; set; } = null!;

            /// <summary>Text that only the entitled sockets may receive.</summary>
            public string Text { get; set; } = "";
        }

        /// <summary>
        /// Two normal users of one tenant, that tenant's administrator, and two global-admin sockets.
        /// </summary>
        private sealed class RecordAudience : IDisposable
        {
            /// <summary>Tenant shared by the normal users and the tenant administrator.</summary>
            public string TenantId { get; set; } = "";

            /// <summary>Normal user that owns the record.</summary>
            public E2ETenantUser Owner { get; set; } = null!;

            /// <summary>Socket of the owning user.</summary>
            public WebSocketTestClient OwnerSocket { get; set; } = null!;

            /// <summary>Socket of the other normal user.</summary>
            public WebSocketTestClient OtherSocket { get; set; } = null!;

            /// <summary>Socket of the tenant administrator.</summary>
            public WebSocketTestClient TenantAdminSocket { get; set; } = null!;

            /// <summary>Global admin that opted in to every tenant.</summary>
            public WebSocketTestClient AllTenantsAdmin { get; set; } = null!;

            /// <summary>Global admin that did not opt in.</summary>
            public WebSocketTestClient PlainAdmin { get; set; } = null!;

            /// <summary>
            /// Connect the five sockets and subscribe them.
            /// </summary>
            /// <param name="fx">Running server.</param>
            /// <returns>The connected audience.</returns>
            public static async Task<RecordAudience> OpenAsync(E2EServerFixture fx)
            {
                E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "owner", false).ConfigureAwait(false);
                E2ETenantUser other = await E2ETenantUser.CreateAsync(fx.AuthClient, "other", false, owner.TenantId).ConfigureAwait(false);
                E2ETenantUser tenantAdmin = await E2ETenantUser.CreateAsync(fx.AuthClient, "admin", true, owner.TenantId).ConfigureAwait(false);
                Dictionary<string, string> apiKey = new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey };

                WebSocketTestClient? ownerSocket = null;
                WebSocketTestClient? otherSocket = null;
                WebSocketTestClient? tenantAdminSocket = null;
                WebSocketTestClient? allTenantsAdmin = null;
                WebSocketTestClient? plainAdmin = null;
                try
                {
                    ownerSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, owner.BearerToken).ConfigureAwait(false);
                    otherSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, other.BearerToken).ConfigureAwait(false);
                    tenantAdminSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, tenantAdmin.BearerToken).ConfigureAwait(false);
                    allTenantsAdmin = await WebSocketTestClient.ConnectAsync(fx.RestPort, null, apiKey).ConfigureAwait(false);
                    plainAdmin = await WebSocketTestClient.ConnectAsync(fx.RestPort, null, apiKey).ConfigureAwait(false);

                    await SubscribeAsync(ownerSocket).ConfigureAwait(false);
                    await SubscribeAsync(otherSocket).ConfigureAwait(false);
                    await SubscribeAsync(tenantAdminSocket).ConfigureAwait(false);
                    await plainAdmin.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe" }).ConfigureAwait(false);
                    AssertNotNull(await plainAdmin.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false), "snapshot");
                    await allTenantsAdmin.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe", ["AllTenants"] = true }).ConfigureAwait(false);
                    AssertNotNull(await allTenantsAdmin.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false), "snapshot");

                    RecordAudience audience = new RecordAudience();
                    audience.TenantId = owner.TenantId;
                    audience.Owner = owner;
                    audience.OwnerSocket = ownerSocket;
                    audience.OtherSocket = otherSocket;
                    audience.TenantAdminSocket = tenantAdminSocket;
                    audience.AllTenantsAdmin = allTenantsAdmin;
                    audience.PlainAdmin = plainAdmin;
                    ownerSocket = null;
                    otherSocket = null;
                    tenantAdminSocket = null;
                    allTenantsAdmin = null;
                    plainAdmin = null;
                    return audience;
                }
                catch
                {
                    if (ownerSocket != null) ownerSocket.Dispose();
                    if (otherSocket != null) otherSocket.Dispose();
                    if (tenantAdminSocket != null) tenantAdminSocket.Dispose();
                    if (allTenantsAdmin != null) allTenantsAdmin.Dispose();
                    if (plainAdmin != null) plainAdmin.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Close the sockets.
            /// </summary>
            public void Dispose()
            {
                OwnerSocket.Dispose();
                OtherSocket.Dispose();
                TenantAdminSocket.Dispose();
                AllTenantsAdmin.Dispose();
                PlainAdmin.Dispose();
            }
        }

        #endregion
    }
}
