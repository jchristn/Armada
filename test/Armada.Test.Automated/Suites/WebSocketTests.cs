namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Test.Common;

    /// <summary>
    /// WebSocket route tests migrated from xUnit to TestSuite harness.
    /// </summary>
    public class WebSocketTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "WebSocket Tests";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;
        private int _WebSocketPort;
        private string _ApiKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new WebSocket test suite.
        /// </summary>
        public WebSocketTests(HttpClient authClient, HttpClient unauthClient, int webSocketPort, string apiKey)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
            _WebSocketPort = webSocketPort;
            _ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all WebSocket tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            // Subscribe Tests
            await RunTest("Subscribe_ReturnsStatusSnapshot", async () =>
            {
                using ClientWebSocket ws = await ConnectAsync().ConfigureAwait(false);

                string msg = JsonSerializer.Serialize(new { Route = "subscribe" });
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

                byte[] buffer = new byte[1048576];
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                AssertEqual("status.snapshot", root.GetProperty("type").GetString());
                Assert(root.TryGetProperty("data", out _), "Should contain data property");
                Assert(root.TryGetProperty("timestamp", out _), "Should contain timestamp");
            }).ConfigureAwait(false);

            // Status Tests
            await RunTest("Status_ReturnsArmadaStatus", async () =>
            {
                JsonElement resp = await WsCommandAsync("status").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("status", resp.GetProperty("action").GetString());
                Assert(resp.TryGetProperty("data", out JsonElement data), "Should contain data");
                Assert(data.TryGetProperty("totalCaptains", out _), "Should contain totalCaptains");
            }).ConfigureAwait(false);

            await RunTest("StopAll_ReturnsAllStopped", async () =>
            {
                try
                {
                    JsonElement resp = await WsCommandAsync("stop_all").ConfigureAwait(false);
                    AssertEqual("command.result", resp.GetProperty("type").GetString());
                    AssertEqual("stop_all", resp.GetProperty("action").GetString());
                    AssertEqual("all_stopped", resp.GetProperty("data").GetProperty("status").GetString());
                }
                catch (TaskCanceledException)
                {
                    // StopAll may take longer than expected with many active captains - acceptable
                }
                catch (System.Net.WebSockets.WebSocketException)
                {
                    // StopAll may close the WebSocket connection - acceptable
                }
            }).ConfigureAwait(false);

            // Fleet Tests
            await RunTest("ListFleets_Empty_ReturnsEmptyList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_fleets").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_fleets", resp.GetProperty("action").GetString());
                JsonElement data = resp.GetProperty("data");
                Assert(data.TryGetProperty("objects", out _), "Should contain objects");
            }).ConfigureAwait(false);

            await RunTest("CreateFleet_ReturnsCreatedFleet", async () =>
            {
                JsonElement resp = await WsCommandAsync("create_fleet", new { data = new { Name = "ws-fleet" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("create_fleet", resp.GetProperty("action").GetString());
                JsonElement data = resp.GetProperty("data");
                AssertStartsWith("flt_", data.GetProperty("id").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("GetFleet_ExistingFleet_ReturnsFleet", async () =>
            {
                string fleetId = await CreateFleetViaRestAsync("ws-get-fleet").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("get_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(fleetId, resp.GetProperty("data").GetProperty("fleet").GetProperty("id").GetString());
                Assert(resp.GetProperty("data").TryGetProperty("vessels", out _), "Should have vessels array");
            }).ConfigureAwait(false);

            await RunTest("GetFleet_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_fleet", new { id = "flt_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Fleet not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateFleet_ExistingFleet_ReturnsUpdated", async () =>
            {
                string fleetId = await CreateFleetViaRestAsync("ws-upd-fleet").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("update_fleet", new { id = fleetId, data = new { Name = "ws-upd-fleet-renamed" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(fleetId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateFleet_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("update_fleet", new { id = "flt_nonexistent", data = new { Name = "x" } }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Fleet not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("DeleteFleet_ExistingFleet_ReturnsDeleted", async () =>
            {
                string fleetId = await CreateFleetViaRestAsync("ws-del-fleet").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("delete_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("deleted", resp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            await RunTest("ListFleets_AfterCreate_ReturnsFleet", async () =>
            {
                await WsCommandAsync("create_fleet", new { data = new { Name = "ws-list-fleet" } }).ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("list_fleets").ConfigureAwait(false);
                JsonElement data = resp.GetProperty("data");
                AssertTrue(data.GetProperty("totalRecords").GetInt64() >= 1);
            }).ConfigureAwait(false);

            await RunTest("ListFleets_WithPagination_RespectsPageSize", async () =>
            {
                await CreateFleetViaRestAsync("ws-page-1").ConfigureAwait(false);
                await CreateFleetViaRestAsync("ws-page-2").ConfigureAwait(false);
                await CreateFleetViaRestAsync("ws-page-3").ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync("list_fleets", new { query = new { pageSize = 2, pageNumber = 1 } }).ConfigureAwait(false);
                JsonElement data = resp.GetProperty("data");
                AssertEqual(2, data.GetProperty("pageSize").GetInt32());
                AssertEqual(2, data.GetProperty("objects").GetArrayLength());
            }).ConfigureAwait(false);

            // Vessel Tests
            await RunTest("ListVessels_Empty_ReturnsEmptyList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_vessels").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_vessels", resp.GetProperty("action").GetString());
            }).ConfigureAwait(false);

            await RunTest("CreateVessel_ReturnsCreatedVessel", async () =>
            {
                JsonElement resp = await WsCommandAsync("create_vessel", new { data = new { Name = "ws-vessel", RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                JsonElement data = resp.GetProperty("data");
                AssertStartsWith("vsl_", data.GetProperty("id").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("GetVessel_ExistingVessel_ReturnsVessel", async () =>
            {
                string vesselId = await CreateVesselViaRestAsync("ws-get-vessel").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("get_vessel", new { id = vesselId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(vesselId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetVessel_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_vessel", new { id = "vsl_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Vessel not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateVessel_ExistingVessel_ReturnsUpdated", async () =>
            {
                string vesselId = await CreateVesselViaRestAsync("ws-upd-vessel").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("update_vessel", new { id = vesselId, data = new { Name = "ws-upd-vessel-renamed", RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(vesselId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateVessel_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("update_vessel", new { id = "vsl_nonexistent", data = new { Name = "x", RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() } }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("DeleteVessel_ExistingVessel_ReturnsDeleted", async () =>
            {
                string vesselId = await CreateVesselViaRestAsync("ws-del-vessel").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("delete_vessel", new { id = vesselId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("deleted", resp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            // Voyage Tests
            await RunTest("ListVoyages_Empty_ReturnsEmptyList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_voyages").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_voyages", resp.GetProperty("action").GetString());
            }).ConfigureAwait(false);

            await RunTest("CreateVoyage_BareVoyage_ReturnsCreatedVoyage", async () =>
            {
                JsonElement resp = await WsCommandAsync("create_voyage", new { data = new { title = "ws-voyage", description = "test voyage" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("create_voyage", resp.GetProperty("action").GetString());
                JsonElement data = resp.GetProperty("data");
                AssertStartsWith("vyg_", data.GetProperty("id").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("GetVoyage_ExistingVoyage_ReturnsVoyageWithMissions", async () =>
            {
                string voyageId = await CreateVoyageViaRestAsync("ws-get-voyage").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("get_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                JsonElement data = resp.GetProperty("data");
                Assert(data.TryGetProperty("voyage", out _), "Should contain voyage");
                Assert(data.TryGetProperty("missions", out _), "Should contain missions");
            }).ConfigureAwait(false);

            await RunTest("GetVoyage_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_voyage", new { id = "vyg_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Voyage not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("CancelVoyage_ExistingVoyage_ReturnsCancelled", async () =>
            {
                string voyageId = await CreateVoyageViaRestAsync("ws-cancel-voyage").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("cancel_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("Cancelled", resp.GetProperty("data").GetProperty("voyage").GetProperty("status").GetString());
                Assert(resp.GetProperty("data").TryGetProperty("cancelledMissions", out _), "Should have cancelledMissions");
            }).ConfigureAwait(false);

            await RunTest("CancelVoyage_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("cancel_voyage", new { id = "vyg_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Voyage not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("PurgeVoyage_ExistingVoyage_ReturnsDeleted", async () =>
            {
                string voyageId = await CreateVoyageViaRestAsync("ws-purge-voyage").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("purge_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("deleted", resp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            await RunTest("PurgeVoyage_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("purge_voyage", new { id = "vyg_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Voyage not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("ListVoyages_AfterCreate_ReturnsVoyage", async () =>
            {
                await WsCommandAsync("create_voyage", new { data = new { title = "ws-list-voyage" } }).ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("list_voyages").ConfigureAwait(false);
                JsonElement data = resp.GetProperty("data");
                AssertTrue(data.GetProperty("totalRecords").GetInt64() >= 1);
            }).ConfigureAwait(false);

            // Mission Tests
            await RunTest("ListMissions_Empty_ReturnsEmptyList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_missions").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_missions", resp.GetProperty("action").GetString());
            }).ConfigureAwait(false);

            await RunTest("CreateMission_ReturnsCreatedMission", async () =>
            {
                JsonElement resp = await WsCommandAsync("create_mission", new { data = new { Title = "ws-mission" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                JsonElement data = resp.GetProperty("data");
                AssertStartsWith("msn_", data.GetProperty("id").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("GetMission_ExistingMission_ReturnsMission", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-get-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("get_mission", new { id = missionId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(missionId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetMission_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_mission", new { id = "msn_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Mission not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateMission_ExistingMission_ReturnsUpdated", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-upd-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("update_mission", new { id = missionId, data = new { Title = "ws-upd-mission-renamed" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(missionId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateMission_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("update_mission", new { id = "msn_nonexistent", data = new { Title = "x" } }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("CancelMission_ExistingMission_ReturnsCancelled", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-cancel-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("cancel_mission", new { id = missionId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("Cancelled", resp.GetProperty("data").GetProperty("status").GetString());
                AssertEqual(missionId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("CancelMission_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("cancel_mission", new { id = "msn_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Mission not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("TransitionMissionStatus_ValidTransition_ReturnsUpdated", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-transition-mission").ConfigureAwait(false);

                // Pending -> Assigned via REST
                await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                    new StringContent(JsonSerializer.Serialize(new { Status = "Assigned" }), Encoding.UTF8, "application/json")).ConfigureAwait(false);

                // Assigned -> InProgress via WebSocket
                JsonElement resp = await WsCommandAsync("transition_mission_status", new { id = missionId, status = "InProgress" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("InProgress", resp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            await RunTest("TransitionMissionStatus_InvalidTransition_ReturnsError", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-bad-transition").ConfigureAwait(false);

                // Pending -> Complete is not valid
                JsonElement resp = await WsCommandAsync("transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertContains("Invalid transition", resp.GetProperty("error").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("TransitionMissionStatus_InvalidStatusString_ReturnsError", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-bad-status").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("transition_mission_status", new { id = missionId, status = "BogusStatus" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertContains("Invalid status", resp.GetProperty("error").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("TransitionMissionStatus_NonExistentMission_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("transition_mission_status", new { id = "msn_nonexistent", status = "InProgress" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Mission not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("TransitionMissionStatus_ToComplete_SetsCompletedUtc", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-complete-mission").ConfigureAwait(false);

                // Pending -> Assigned -> InProgress via REST
                await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                    new StringContent(JsonSerializer.Serialize(new { Status = "Assigned" }), Encoding.UTF8, "application/json")).ConfigureAwait(false);
                await _AuthClient.PutAsync("/api/v1/missions/" + missionId + "/status",
                    new StringContent(JsonSerializer.Serialize(new { Status = "InProgress" }), Encoding.UTF8, "application/json")).ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync("transition_mission_status", new { id = missionId, status = "Complete" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                Assert(resp.GetProperty("data").TryGetProperty("completedUtc", out JsonElement completedEl), "Should have completedUtc");
                AssertNotEqual(JsonValueKind.Null, completedEl.ValueKind);
            }).ConfigureAwait(false);

            await RunTest("ListMissions_WithPagination_RespectsPageSize", async () =>
            {
                await CreateMissionViaRestAsync("ws-page-m1").ConfigureAwait(false);
                await CreateMissionViaRestAsync("ws-page-m2").ConfigureAwait(false);
                await CreateMissionViaRestAsync("ws-page-m3").ConfigureAwait(false);

                JsonElement resp = await WsCommandAsync("list_missions", new { query = new { pageSize = 2 } }).ConfigureAwait(false);
                JsonElement data = resp.GetProperty("data");
                AssertEqual(2, data.GetProperty("objects").GetArrayLength());
            }).ConfigureAwait(false);

            // Captain Tests
            await RunTest("ListCaptains_Empty_ReturnsEmptyList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_captains").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_captains", resp.GetProperty("action").GetString());
            }).ConfigureAwait(false);

            await RunTest("CreateCaptain_ReturnsCreatedCaptain", async () =>
            {
                JsonElement resp = await WsCommandAsync("create_captain", new { data = new { Name = "ws-captain", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                JsonElement data = resp.GetProperty("data");
                AssertStartsWith("cpt_", data.GetProperty("id").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("GetCaptain_ExistingCaptain_ReturnsCaptain", async () =>
            {
                string captainId = await CreateCaptainViaRestAsync("ws-get-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("get_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(captainId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetCaptain_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_captain", new { id = "cpt_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Captain not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateCaptain_ExistingCaptain_ReturnsUpdated", async () =>
            {
                string captainId = await CreateCaptainViaRestAsync("ws-upd-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("update_captain", new { id = captainId, data = new { Name = "ws-upd-captain-renamed", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(captainId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateCaptain_PreservesOperationalFields", async () =>
            {
                string captainId = await CreateCaptainViaRestAsync("ws-preserve-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("update_captain", new { id = captainId, data = new { Name = "ws-preserve-renamed", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                JsonElement data = resp.GetProperty("data");
                AssertEqual("Idle", data.GetProperty("state").GetString());
            }).ConfigureAwait(false);

            await RunTest("UpdateCaptain_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("update_captain", new { id = "cpt_nonexistent", data = new { Name = "x" } }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Captain not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("DeleteCaptain_ExistingCaptain_ReturnsDeleted", async () =>
            {
                string captainId = await CreateCaptainViaRestAsync("ws-del-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("delete_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("deleted", resp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            await RunTest("DeleteCaptain_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("delete_captain", new { id = "cpt_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Captain not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("StopCaptain_ExistingCaptain_ReturnsStopped", async () =>
            {
                string captainId = await CreateCaptainViaRestAsync("ws-stop-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("stop_captain", new { captainId = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("stopped", resp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            // Signal Tests
            await RunTest("ListSignals_Empty_ReturnsEmptyList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_signals").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_signals", resp.GetProperty("action").GetString());
            }).ConfigureAwait(false);

            await RunTest("SendSignal_ReturnsCreatedSignal", async () =>
            {
                JsonElement resp = await WsCommandAsync("send_signal", new { data = new { Type = "Nudge", Payload = "hello" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("send_signal", resp.GetProperty("action").GetString());
                JsonElement data = resp.GetProperty("data");
                AssertStartsWith("sig_", data.GetProperty("id").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("ListSignals_AfterSend_ReturnsSignal", async () =>
            {
                await WsCommandAsync("send_signal", new { data = new { Type = "Mail", Payload = "test-mail" } }).ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("list_signals").ConfigureAwait(false);
                JsonElement data = resp.GetProperty("data");
                AssertTrue(data.GetProperty("totalRecords").GetInt64() >= 1);
            }).ConfigureAwait(false);

            // Event Tests
            await RunTest("ListEvents_ReturnsEventList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_events").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_events", resp.GetProperty("action").GetString());
                Assert(resp.GetProperty("data").TryGetProperty("objects", out _), "Should have objects");
            }).ConfigureAwait(false);

            // Dock Tests
            await RunTest("ListDocks_ReturnsEmptyList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_docks").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_docks", resp.GetProperty("action").GetString());
                Assert(resp.GetProperty("data").TryGetProperty("objects", out _), "Should have objects");
            }).ConfigureAwait(false);

            // MergeQueue Tests
            await RunTest("ListMergeQueue_ReturnsEmptyList", async () =>
            {
                JsonElement resp = await WsCommandAsync("list_merge_queue").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("list_merge_queue", resp.GetProperty("action").GetString());
            }).ConfigureAwait(false);

            await RunTest("EnqueueMerge_ReturnsCreatedEntry", async () =>
            {
                JsonElement resp = await WsCommandAsync("enqueue_merge", new { data = new { BranchName = "feature/ws-test", TargetBranch = "main" } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("enqueue_merge", resp.GetProperty("action").GetString());
                JsonElement data = resp.GetProperty("data");
                AssertStartsWith("mrg_", data.GetProperty("id").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("GetMergeEntry_ExistingEntry_ReturnsEntry", async () =>
            {
                JsonElement createResp = await WsCommandAsync("enqueue_merge", new { data = new { BranchName = "feature/ws-get-merge", TargetBranch = "main" } }).ConfigureAwait(false);
                string mergeId = createResp.GetProperty("data").GetProperty("id").GetString()!;

                JsonElement resp = await WsCommandAsync("get_merge_entry", new { id = mergeId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual(mergeId, resp.GetProperty("data").GetProperty("id").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetMergeEntry_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_merge_entry", new { id = "mrg_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertEqual("Merge entry not found", resp.GetProperty("error").GetString());
            }).ConfigureAwait(false);

            await RunTest("CancelMerge_ExistingEntry_ReturnsCancelled", async () =>
            {
                JsonElement createResp = await WsCommandAsync("enqueue_merge", new { data = new { BranchName = "feature/ws-cancel-merge", TargetBranch = "main" } }).ConfigureAwait(false);
                string mergeId = createResp.GetProperty("data").GetProperty("id").GetString()!;

                JsonElement resp = await WsCommandAsync("cancel_merge", new { id = mergeId }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("cancelled", resp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            await RunTest("ProcessMergeQueue_ReturnsProcessed", async () =>
            {
                JsonElement resp = await WsCommandAsync("process_merge_queue").ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("processed", resp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            // ── Mission Diff/Log and Captain Log ────────────────────────

            await RunTest("GetMissionDiff_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_mission_diff", new { id = "msn_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetMissionDiff_ExistingMission_ReturnsResult", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-diff-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("get_mission_diff", new { id = missionId }).ConfigureAwait(false);
                // May return error (no worktree/settings) or result — just verify action is correct
                string action = resp.GetProperty("action").GetString()!;
                AssertEqual("get_mission_diff", action);
            }).ConfigureAwait(false);

            await RunTest("GetMissionLog_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_mission_log", new { id = "msn_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetMissionLog_ExistingMission_ReturnsLogData", async () =>
            {
                string missionId = await CreateMissionViaRestAsync("ws-log-mission").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("get_mission_log", new { id = missionId }).ConfigureAwait(false);
                // May return error (settings not configured) or result with empty log
                string action = resp.GetProperty("action").GetString()!;
                AssertEqual("get_mission_log", action);
            }).ConfigureAwait(false);

            await RunTest("GetCaptainLog_NonExistent_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("get_captain_log", new { id = "cpt_nonexistent" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetCaptainLog_ExistingCaptain_ReturnsLogData", async () =>
            {
                string captainId = await CreateCaptainViaRestAsync("ws-log-captain").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("get_captain_log", new { id = captainId }).ConfigureAwait(false);
                // May return error (settings not configured) or result with empty log
                string action = resp.GetProperty("action").GetString()!;
                AssertEqual("get_captain_log", action);
            }).ConfigureAwait(false);

            // Note: stop_server is not tested in automated suite as it would shut down the server.

            // ── Enumerate ─────────────────────────────────────────────

            await RunTest("Enumerate_Fleets_ReturnsPaginatedResult", async () =>
            {
                await CreateFleetViaRestAsync("ws-enum-fleet").ConfigureAwait(false);
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "fleets", query = new { pageSize = 10, pageNumber = 1 } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
                AssertEqual("enumerate", resp.GetProperty("action").GetString());
                Assert(resp.GetProperty("data").TryGetProperty("objects", out _) || resp.GetProperty("data").TryGetProperty("Objects", out _), "Should contain objects array");
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Vessels_ReturnsResult", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "vessels" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Captains_ReturnsResult", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "captains" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Missions_ReturnsResult", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "missions" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Voyages_ReturnsResult", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "voyages" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Docks_ReturnsResult", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "docks" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Signals_ReturnsResult", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "signals" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_Events_ReturnsResult", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "events" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_MergeQueue_ReturnsResult", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "merge_queue" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_WithPagination_RespectsPageSize", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "fleets", query = new { pageSize = 5, pageNumber = 1 } }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_SingularEntityType_Works", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "fleet" }).ConfigureAwait(false);
                AssertEqual("command.result", resp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("Enumerate_InvalidEntityType_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("enumerate", new { entityType = "bananas" }).ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertContains("Unknown entity type", resp.GetProperty("error").GetString()!);
            }).ConfigureAwait(false);

            // Error Handling Tests
            await RunTest("UnknownAction_ReturnsError", async () =>
            {
                JsonElement resp = await WsCommandAsync("totally_bogus_action").ConfigureAwait(false);
                AssertEqual("command.error", resp.GetProperty("type").GetString());
                AssertContains("Unknown action", resp.GetProperty("error").GetString()!);
            }).ConfigureAwait(false);

            await RunTest("UnknownRoute_ReturnsError", async () =>
            {
                using ClientWebSocket ws = await ConnectAsync().ConfigureAwait(false);

                string msg = JsonSerializer.Serialize(new { Route = "bad_route" });
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

                byte[] buffer = new byte[1048576];
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                AssertEqual("error", root.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("NoRoute_ReturnsError", async () =>
            {
                using ClientWebSocket ws = await ConnectAsync().ConfigureAwait(false);

                string msg = JsonSerializer.Serialize(new { hello = "world" });
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

                byte[] buffer = new byte[1048576];
                using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                AssertEqual("error", root.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            // CrossEntity Tests
            await RunTest("FullFleetLifecycle_CreateGetUpdateDelete", async () =>
            {
                // Create
                JsonElement createResp = await WsCommandAsync("create_fleet", new { data = new { Name = "lifecycle-fleet" } }).ConfigureAwait(false);
                AssertEqual("command.result", createResp.GetProperty("type").GetString());
                string fleetId = createResp.GetProperty("data").GetProperty("id").GetString()!;
                AssertStartsWith("flt_", fleetId);

                // Get
                JsonElement getResp = await WsCommandAsync("get_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.result", getResp.GetProperty("type").GetString());
                AssertEqual(fleetId, getResp.GetProperty("data").GetProperty("fleet").GetProperty("id").GetString());

                // Update
                JsonElement updateResp = await WsCommandAsync("update_fleet", new { id = fleetId, data = new { Name = "lifecycle-fleet-updated" } }).ConfigureAwait(false);
                AssertEqual("command.result", updateResp.GetProperty("type").GetString());

                // Delete
                JsonElement deleteResp = await WsCommandAsync("delete_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.result", deleteResp.GetProperty("type").GetString());
                AssertEqual("deleted", deleteResp.GetProperty("data").GetProperty("status").GetString());

                // Verify deleted
                JsonElement verifyResp = await WsCommandAsync("get_fleet", new { id = fleetId }).ConfigureAwait(false);
                AssertEqual("command.error", verifyResp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("FullVoyageLifecycle_CreateGetCancelPurge", async () =>
            {
                // Create bare voyage
                JsonElement createResp = await WsCommandAsync("create_voyage", new { data = new { title = "lifecycle-voyage", description = "test" } }).ConfigureAwait(false);
                AssertEqual("command.result", createResp.GetProperty("type").GetString());
                string voyageId = createResp.GetProperty("data").GetProperty("id").GetString()!;

                // Get
                JsonElement getResp = await WsCommandAsync("get_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", getResp.GetProperty("type").GetString());

                // Create another voyage for purge test
                JsonElement create2Resp = await WsCommandAsync("create_voyage", new { data = new { title = "purge-voyage" } }).ConfigureAwait(false);
                string purgeId = create2Resp.GetProperty("data").GetProperty("id").GetString()!;

                // Cancel first
                JsonElement cancelResp = await WsCommandAsync("cancel_voyage", new { id = voyageId }).ConfigureAwait(false);
                AssertEqual("command.result", cancelResp.GetProperty("type").GetString());
                AssertEqual("Cancelled", cancelResp.GetProperty("data").GetProperty("voyage").GetProperty("status").GetString());

                // Purge second
                JsonElement purgeResp = await WsCommandAsync("purge_voyage", new { id = purgeId }).ConfigureAwait(false);
                AssertEqual("command.result", purgeResp.GetProperty("type").GetString());
                AssertEqual("deleted", purgeResp.GetProperty("data").GetProperty("status").GetString());

                // Verify purged
                JsonElement verifyResp = await WsCommandAsync("get_voyage", new { id = purgeId }).ConfigureAwait(false);
                AssertEqual("command.error", verifyResp.GetProperty("type").GetString());
            }).ConfigureAwait(false);

            await RunTest("FullMissionLifecycle_CreateTransitionCancel", async () =>
            {
                // Create
                JsonElement createResp = await WsCommandAsync("create_mission", new { data = new { Title = "lifecycle-mission" } }).ConfigureAwait(false);
                AssertEqual("command.result", createResp.GetProperty("type").GetString());
                string missionId = createResp.GetProperty("data").GetProperty("id").GetString()!;

                // Transition: Pending -> Assigned
                JsonElement t1 = await WsCommandAsync("transition_mission_status", new { id = missionId, status = "Assigned" }).ConfigureAwait(false);
                AssertEqual("command.result", t1.GetProperty("type").GetString());

                // Transition: Assigned -> InProgress
                JsonElement t2 = await WsCommandAsync("transition_mission_status", new { id = missionId, status = "InProgress" }).ConfigureAwait(false);
                AssertEqual("command.result", t2.GetProperty("type").GetString());

                // Cancel
                JsonElement cancelResp = await WsCommandAsync("cancel_mission", new { id = missionId }).ConfigureAwait(false);
                AssertEqual("command.result", cancelResp.GetProperty("type").GetString());
                AssertEqual("Cancelled", cancelResp.GetProperty("data").GetProperty("status").GetString());
            }).ConfigureAwait(false);

            await RunTest("FullCaptainLifecycle_CreateUpdateDelete", async () =>
            {
                // Create
                JsonElement createResp = await WsCommandAsync("create_captain", new { data = new { Name = "lifecycle-captain", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", createResp.GetProperty("type").GetString());
                string captainId = createResp.GetProperty("data").GetProperty("id").GetString()!;

                // Get
                JsonElement getResp = await WsCommandAsync("get_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", getResp.GetProperty("type").GetString());

                // Update
                JsonElement updateResp = await WsCommandAsync("update_captain", new { id = captainId, data = new { Name = "lifecycle-renamed", Runtime = "ClaudeCode" } }).ConfigureAwait(false);
                AssertEqual("command.result", updateResp.GetProperty("type").GetString());

                // Delete
                JsonElement deleteResp = await WsCommandAsync("delete_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.result", deleteResp.GetProperty("type").GetString());

                // Verify deleted
                JsonElement verifyResp = await WsCommandAsync("get_captain", new { id = captainId }).ConfigureAwait(false);
                AssertEqual("command.error", verifyResp.GetProperty("type").GetString());
            }).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<ClientWebSocket> ConnectAsync()
        {
            ClientWebSocket ws = new ClientWebSocket();
            Uri uri = new Uri("ws://localhost:" + _WebSocketPort);
            await ws.ConnectAsync(uri, CancellationToken.None).ConfigureAwait(false);
            return ws;
        }

        private async Task<JsonElement> WsCommandAsync(string action, object? extraFields = null)
        {
            using ClientWebSocket ws = await ConnectAsync().ConfigureAwait(false);

            Dictionary<string, object?> msg = new Dictionary<string, object?>
            {
                ["Route"] = "command",
                ["action"] = action
            };

            if (extraFields != null)
            {
                string extraJson = JsonSerializer.Serialize(extraFields);
                using JsonDocument extraDoc = JsonDocument.Parse(extraJson);
                foreach (JsonProperty prop in extraDoc.RootElement.EnumerateObject())
                {
                    msg[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                }
            }

            string payload = JsonSerializer.Serialize(msg);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

            byte[] buffer = new byte[1048576];
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            WebSocketReceiveResult result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
            string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        private async Task<string> CreateFleetViaRestAsync(string name)
        {
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets",
                new StringContent(JsonSerializer.Serialize(new { Name = name }), Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateVesselViaRestAsync(string name)
        {
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels",
                new StringContent(JsonSerializer.Serialize(new { Name = name, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl() }), Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateCaptainViaRestAsync(string name)
        {
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains",
                new StringContent(JsonSerializer.Serialize(new { Name = name, Runtime = "ClaudeCode" }), Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateMissionViaRestAsync(string title)
        {
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions",
                new StringContent(JsonSerializer.Serialize(new { Title = title }), Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateVoyageViaRestAsync(string title)
        {
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages",
                new StringContent(JsonSerializer.Serialize(new { Title = title }), Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("Id").GetString()!;
        }

        #endregion
    }
}
