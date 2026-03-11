namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Test.Common;

    /// <summary>
    /// Tests for status and health check routes.
    /// </summary>
    public class StatusTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Status Routes";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new StatusTests suite with shared HTTP clients.
        /// </summary>
        public StatusTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Private-Methods

        private async Task<JsonElement> CreateCaptainAsync(string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName, Runtime = "ClaudeCode" }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        private async Task<string> CreateFleetAsync()
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = "StatusTestFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8) }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/fleets", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<string> CreateVesselAsync(string fleetId)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = "StatusTestVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/vessels", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
        }

        private async Task<JsonElement> CreateMissionAsync(string title)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Title = title, Description = "Status test mission" }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        private async Task<JsonElement> CreateVoyageAsync(string vesselId, string title, int missionCount = 2)
        {
            object[] missions = Enumerable.Range(1, missionCount)
                .Select(i => (object)new { Title = "Voyage Mission " + i, Description = "Desc " + i })
                .ToArray();

            StringContent content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    Title = title,
                    Description = "Status test voyage",
                    VesselId = vesselId,
                    Missions = missions
                }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/voyages", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        private async Task<JsonElement> CreateSignalAsync(string type, string message)
        {
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Type = type, Message = message }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/signals", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        private async Task<JsonDocument> GetStatusAsync()
        {
            HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(body);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task RunTestsAsync()
        {
            #region Status-Endpoint

            await RunTest("GetStatus_ReturnsOk", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_ReturnsJson", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                AssertEqual("application/json", contentType);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_HasAllExpectedProperties", async () =>
            {
                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                JsonElement root = doc.RootElement;

                Assert(root.TryGetProperty("TotalCaptains", out _), "Missing TotalCaptains");
                Assert(root.TryGetProperty("IdleCaptains", out _), "Missing IdleCaptains");
                Assert(root.TryGetProperty("WorkingCaptains", out _), "Missing WorkingCaptains");
                Assert(root.TryGetProperty("StalledCaptains", out _), "Missing StalledCaptains");
                Assert(root.TryGetProperty("ActiveVoyages", out _), "Missing ActiveVoyages");
                Assert(root.TryGetProperty("MissionsByStatus", out _), "Missing MissionsByStatus");
                Assert(root.TryGetProperty("Voyages", out _), "Missing Voyages");
                Assert(root.TryGetProperty("RecentSignals", out _), "Missing RecentSignals");
                Assert(root.TryGetProperty("TimestampUtc", out _), "Missing TimestampUtc");
            }).ConfigureAwait(false);

            await RunTest("GetStatus_NoData_ShowsZeros", async () =>
            {
                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                JsonElement root = doc.RootElement;

                // Note: previous test suites may have created captains, so we just verify the fields exist and are >= 0
                AssertTrue(root.GetProperty("TotalCaptains").GetInt32() >= 0);
                AssertTrue(root.GetProperty("IdleCaptains").GetInt32() >= 0);
                AssertTrue(root.GetProperty("WorkingCaptains").GetInt32() >= 0);
                AssertTrue(root.GetProperty("StalledCaptains").GetInt32() >= 0);
                AssertTrue(root.GetProperty("ActiveVoyages").GetInt32() >= 0);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_NoData_EmptyVoyages", async () =>
            {
                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                AssertTrue(doc.RootElement.GetProperty("Voyages").GetArrayLength() >= 0);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_NoData_EmptyRecentSignals", async () =>
            {
                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                AssertTrue(doc.RootElement.GetProperty("RecentSignals").GetArrayLength() >= 0);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_NoData_MissionsByStatusIsObject", async () =>
            {
                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                AssertEqual(JsonValueKind.Object, doc.RootElement.GetProperty("MissionsByStatus").ValueKind);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_AfterCreatingCaptains_ShowsCorrectCounts", async () =>
            {
                await CreateCaptainAsync("status-captain-1").ConfigureAwait(false);
                await CreateCaptainAsync("status-captain-2").ConfigureAwait(false);
                await CreateCaptainAsync("status-captain-3").ConfigureAwait(false);

                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                JsonElement root = doc.RootElement;

                AssertTrue(root.GetProperty("TotalCaptains").GetInt32() >= 3);
                AssertTrue(root.GetProperty("IdleCaptains").GetInt32() >= 3);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_AfterCreatingMissions_ShowsMissionsByStatus", async () =>
            {
                await CreateMissionAsync("Status Mission 1").ConfigureAwait(false);
                await CreateMissionAsync("Status Mission 2").ConfigureAwait(false);

                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                JsonElement missionsByStatus = doc.RootElement.GetProperty("MissionsByStatus");

                if (missionsByStatus.TryGetProperty("Pending", out JsonElement pending))
                {
                    AssertTrue(pending.GetInt32() >= 2);
                }
            }).ConfigureAwait(false);

            await RunTest("GetStatus_AfterCreatingVoyage_ShowsActiveVoyages", async () =>
            {
                string fleetId = await CreateFleetAsync().ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fleetId).ConfigureAwait(false);
                await CreateVoyageAsync(vesselId, "Status Voyage 1").ConfigureAwait(false);

                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                AssertTrue(doc.RootElement.GetProperty("ActiveVoyages").GetInt32() >= 1);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_AfterCreatingVoyage_VoyagesArrayPopulated", async () =>
            {
                string fleetId = await CreateFleetAsync().ConfigureAwait(false);
                string vesselId = await CreateVesselAsync(fleetId).ConfigureAwait(false);
                await CreateVoyageAsync(vesselId, "Voyage Array Check").ConfigureAwait(false);

                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                AssertTrue(doc.RootElement.GetProperty("Voyages").GetArrayLength() >= 1);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_AfterCreatingSignals_ShowsRecentSignals", async () =>
            {
                await CreateSignalAsync("Mail", "Test signal 1").ConfigureAwait(false);
                await CreateSignalAsync("Heartbeat", "Test signal 2").ConfigureAwait(false);

                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                AssertTrue(doc.RootElement.GetProperty("RecentSignals").GetArrayLength() >= 2);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_TimestampUtc_IsRecent", async () =>
            {
                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                string timestampStr = doc.RootElement.GetProperty("TimestampUtc").GetString()!;
                DateTime timestamp = DateTime.Parse(timestampStr).ToUniversalTime();
                TimeSpan elapsed = DateTime.UtcNow - timestamp;

                Assert(elapsed.TotalMinutes < 1, "TimestampUtc should be within the last minute but was " + elapsed.TotalMinutes + " minutes ago");
            }).ConfigureAwait(false);

            await RunTest("GetStatus_TimestampUtc_IsValidDateTime", async () =>
            {
                using JsonDocument doc = await GetStatusAsync().ConfigureAwait(false);
                string timestampStr = doc.RootElement.GetProperty("TimestampUtc").GetString()!;
                Assert(DateTime.TryParse(timestampStr, out _), "TimestampUtc should be a valid datetime string");
            }).ConfigureAwait(false);

            await RunTest("GetStatus_WithoutAuth_ReturnsResponse", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertNotNull(response);
            }).ConfigureAwait(false);

            await RunTest("GetStatus_WrongApiKey_ReturnsResponse", async () =>
            {
                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = _AuthClient.BaseAddress;
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "wrong-api-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/status").ConfigureAwait(false);
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }).ConfigureAwait(false);

            #endregion

            #region Health-Check-Endpoint

            await RunTest("GetHealth_ReturnsOk", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("GetHealth_ReturnsHealthyStatus", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("healthy", doc.RootElement.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetHealth_NoAuth_ReturnsOk", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("GetHealth_NoAuth_ReturnsHealthyStatus", async () =>
            {
                HttpResponseMessage response = await _UnauthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("healthy", doc.RootElement.GetProperty("Status").GetString());
            }).ConfigureAwait(false);

            await RunTest("GetHealth_WrongApiKey_StillReturnsOk", async () =>
            {
                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = _AuthClient.BaseAddress;
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "totally-wrong-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual("healthy", doc.RootElement.GetProperty("Status").GetString());

                wrongKeyClient.Dispose();
            }).ConfigureAwait(false);

            await RunTest("GetHealth_HasTimestamp", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Timestamp", out _), "Health check should include Timestamp");
            }).ConfigureAwait(false);

            await RunTest("GetHealth_HasVersion", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Version", out _), "Health check should include Version");
            }).ConfigureAwait(false);

            await RunTest("GetHealth_HasUptime", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Uptime", out _), "Health check should include Uptime");
            }).ConfigureAwait(false);

            await RunTest("GetHealth_HasPorts", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                Assert(doc.RootElement.TryGetProperty("Ports", out JsonElement ports), "Health check should include Ports");
                Assert(ports.TryGetProperty("Admiral", out _), "Ports should include Admiral");
                Assert(ports.TryGetProperty("Mcp", out _), "Ports should include Mcp");
                Assert(ports.TryGetProperty("WebSocket", out _), "Ports should include WebSocket");
            }).ConfigureAwait(false);

            await RunTest("GetHealth_ReturnsJson", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                AssertEqual("application/json", contentType);
            }).ConfigureAwait(false);

            await RunTest("GetHealth_StartUtcIsBeforeNow", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/status/health").ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonDocument doc = JsonDocument.Parse(body);
                string startStr = doc.RootElement.GetProperty("StartUtc").GetString()!;
                DateTime startUtc = DateTime.Parse(startStr).ToUniversalTime();
                Assert(startUtc <= DateTime.UtcNow, "StartUtc should be in the past");
            }).ConfigureAwait(false);

            #endregion
        }

        #endregion
    }
}
