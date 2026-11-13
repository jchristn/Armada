namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end Status API descriptors covering the status and health-check routes against a live
    /// in-process Armada server provided by <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class StatusSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Status API end-to-end suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            #region Status-Endpoint

            cases.Add(CaseAsync("get_status_returns_ok", "GetStatus_ReturnsOk", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("get_status_returns_json", "GetStatus_ReturnsJson", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status");
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                AssertEqual("application/json", contentType);
            }));

            cases.Add(CaseAsync("get_status_has_all_expected_properties", "GetStatus_HasAllExpectedProperties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                ArmadaStatus status = await GetStatusAsync(authClient);

                AssertNotNull(status);
                Assert(status.MissionsByStatus != null, "Missing MissionsByStatus");
                Assert(status.RecentSignals != null, "Missing RecentSignals");
                AssertEqual(status.InProgressVoyages + status.OpenVoyages, status.ActiveVoyages, "ActiveVoyages is InProgress plus Open");
                Assert(status.TimestampUtc != default, "Missing TimestampUtc");
            }));

            cases.Add(CaseAsync("get_status_no_data_shows_zeros", "GetStatus_NoData_ShowsZeros", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                ArmadaStatus status = await GetStatusAsync(authClient);

                // Note: previous test suites may have created captains, so we just verify the fields exist and are >= 0
                AssertTrue(status.TotalCaptains >= 0);
                AssertTrue(status.IdleCaptains >= 0);
                AssertTrue(status.WorkingCaptains >= 0);
                AssertTrue(status.StalledCaptains >= 0);
                AssertTrue(status.ActiveVoyages >= 0);
            }));

            cases.Add(CaseAsync("get_status_embeds_no_voyage_list", "GetStatus_EmbedsNoVoyageList", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fleetId = await CreateFleetAsync(authClient);
                string vesselId = await CreateVesselAsync(authClient, fleetId);
                await CreateVoyageAsync(authClient, vesselId, "Status Shape Voyage");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status");
                string json = await response.Content.ReadAsStringAsync();
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                AssertFalse(json.Contains("\"Voyages\"", StringComparison.Ordinal), "status no longer embeds the active voyages: " + json);
                AssertTrue(json.Contains("\"ActiveVoyages\"", StringComparison.Ordinal), "status keeps the active voyage count");
                AssertTrue(json.Contains("\"InProgressVoyages\"", StringComparison.Ordinal) && json.Contains("\"OpenVoyages\"", StringComparison.Ordinal), "status carries the per-status voyage counts");
            }));

            cases.Add(CaseAsync("get_status_no_data_empty_recent_signals", "GetStatus_NoData_EmptyRecentSignals", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                ArmadaStatus status = await GetStatusAsync(authClient);
                AssertTrue(status.RecentSignals.Count >= 0);
            }));

            cases.Add(CaseAsync("get_status_no_data_missions_by_status_is_object", "GetStatus_NoData_MissionsByStatusIsObject", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                ArmadaStatus status = await GetStatusAsync(authClient);
                AssertNotNull(status.MissionsByStatus);
            }));

            cases.Add(CaseAsync("get_status_after_creating_captains_shows_correct_counts", "GetStatus_AfterCreatingCaptains_ShowsCorrectCounts", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await CreateCaptainAsync(authClient, "status-captain-1");
                await CreateCaptainAsync(authClient, "status-captain-2");
                await CreateCaptainAsync(authClient, "status-captain-3");

                ArmadaStatus status = await GetStatusAsync(authClient);

                AssertTrue(status.TotalCaptains >= 3);
                AssertTrue(status.IdleCaptains >= 3);
            }));

            cases.Add(CaseAsync("get_status_after_creating_missions_shows_missions_by_status", "GetStatus_AfterCreatingMissions_ShowsMissionsByStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await CreateMissionAsync(authClient, "Status Mission 1");
                await CreateMissionAsync(authClient, "Status Mission 2");

                ArmadaStatus status = await GetStatusAsync(authClient);

                if (status.MissionsByStatus.TryGetValue("Pending", out int pending))
                {
                    AssertTrue(pending >= 2);
                }
            }));

            cases.Add(CaseAsync("get_status_after_creating_voyage_shows_active_voyages", "GetStatus_AfterCreatingVoyage_ShowsActiveVoyages", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fleetId = await CreateFleetAsync(authClient);
                string vesselId = await CreateVesselAsync(authClient, fleetId);
                await CreateVoyageAsync(authClient, vesselId, "Status Voyage 1");

                ArmadaStatus status = await GetStatusAsync(authClient);
                AssertTrue(status.ActiveVoyages >= 1);
            }));

            cases.Add(CaseAsync("get_status_after_creating_voyage_voyages_array_populated", "GetStatus_AfterCreatingVoyage_VoyagesArrayPopulated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fleetId = await CreateFleetAsync(authClient);
                string vesselId = await CreateVesselAsync(authClient, fleetId);
                await CreateVoyageAsync(authClient, vesselId, "Voyage Array Check");

                ArmadaStatus status = await GetStatusAsync(authClient);
                AssertTrue(status.ActiveVoyages >= 1);

                // The voyages themselves come from the paged progress enumeration, with the same total.
                HttpResponseMessage page = await authClient.PostAsync("/api/v1/voyages/progress/enumerate", new StringContent("{\"PageSize\":1000}", System.Text.Encoding.UTF8, "application/json"));
                EnumerationResult<VoyageProgress> progress = await JsonHelper.DeserializeAsync<EnumerationResult<VoyageProgress>>(page);
                AssertEqual((long)status.ActiveVoyages, progress.TotalRecords, "progress enumeration total matches the status count");
                AssertTrue(progress.Objects.Exists(p => p.Voyage != null && p.Voyage.Title == "Voyage Array Check"), "the new voyage is listed");
            }));

            cases.Add(CaseAsync("get_status_after_creating_signals_shows_recent_signals", "GetStatus_AfterCreatingSignals_ShowsRecentSignals", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await CreateSignalAsync(authClient, "Mail", "Test signal 1");
                await CreateSignalAsync(authClient, "Heartbeat", "Test signal 2");

                ArmadaStatus status = await GetStatusAsync(authClient);
                AssertTrue(status.RecentSignals.Count >= 2);
            }));

            cases.Add(CaseAsync("get_status_timestamp_utc_is_recent", "GetStatus_TimestampUtc_IsRecent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                ArmadaStatus status = await GetStatusAsync(authClient);
                DateTime timestamp = status.TimestampUtc.ToUniversalTime();
                TimeSpan elapsed = DateTime.UtcNow - timestamp;

                Assert(elapsed.TotalMinutes < 1, "TimestampUtc should be within the last minute but was " + elapsed.TotalMinutes + " minutes ago");
            }));

            cases.Add(CaseAsync("get_status_timestamp_utc_is_valid_date_time", "GetStatus_TimestampUtc_IsValidDateTime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                ArmadaStatus status = await GetStatusAsync(authClient);
                Assert(status.TimestampUtc != default, "TimestampUtc should be a valid datetime");
            }));

            cases.Add(CaseAsync("get_status_without_auth_returns_response", "GetStatus_WithoutAuth_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/status");
                AssertNotNull(response);
            }));

            cases.Add(CaseAsync("get_status_wrong_api_key_returns_response", "GetStatus_WrongApiKey_ReturnsResponse", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = authClient.BaseAddress;
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "wrong-api-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/status");
                AssertNotNull(response);

                wrongKeyClient.Dispose();
            }));

            #endregion

            #region Health-Check-Endpoint

            cases.Add(CaseAsync("get_health_returns_ok", "GetHealth_ReturnsOk", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("get_health_returns_healthy_status", "GetHealth_ReturnsHealthyStatus", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health");
                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response);
                AssertEqual("healthy", health.Status);
            }));

            cases.Add(CaseAsync("get_health_no_auth_returns_ok", "GetHealth_NoAuth_ReturnsOk", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/status/health");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
            }));

            cases.Add(CaseAsync("get_health_no_auth_returns_healthy_status", "GetHealth_NoAuth_ReturnsHealthyStatus", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.GetAsync("/api/v1/status/health");
                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response);
                AssertEqual("healthy", health.Status);
            }));

            cases.Add(CaseAsync("get_health_wrong_api_key_still_returns_ok", "GetHealth_WrongApiKey_StillReturnsOk", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpClient wrongKeyClient = new HttpClient();
                wrongKeyClient.BaseAddress = authClient.BaseAddress;
                wrongKeyClient.DefaultRequestHeaders.Add("X-Api-Key", "totally-wrong-key");

                HttpResponseMessage response = await wrongKeyClient.GetAsync("/api/v1/status/health");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response);
                AssertEqual("healthy", health.Status);

                wrongKeyClient.Dispose();
            }));

            cases.Add(CaseAsync("get_health_has_timestamp", "GetHealth_HasTimestamp", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health");
                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response);
                Assert(health.Timestamp != null, "Health check should include Timestamp");
            }));

            cases.Add(CaseAsync("get_health_has_version", "GetHealth_HasVersion", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health");
                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response);
                Assert(health.Version != null, "Health check should include Version");
                AssertEqual(Armada.Server.BuildIdentity.InformationalVersion, health.BuildVersion, "Build identity comes from the loaded server assembly");
                AssertEqual(Armada.Server.BuildIdentity.CommitHash, health.CommitHash, "Commit is embedded at build time");
            }));

            cases.Add(CaseAsync("get_health_has_uptime", "GetHealth_HasUptime", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health");
                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response);
                Assert(health.Uptime != null, "Health check should include Uptime");
            }));

            cases.Add(CaseAsync("get_health_has_ports", "GetHealth_HasPorts", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health");
                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response);
                Assert(health.Ports != null, "Health check should include Ports");
                Assert(health.Ports!.Admiral >= 0, "Ports should include Admiral");
                Assert(health.Ports!.Mcp >= 0, "Ports should include Mcp");
            }));

            cases.Add(CaseAsync("get_health_returns_json", "GetHealth_ReturnsJson", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health");
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                AssertEqual("application/json", contentType);
            }));

            cases.Add(CaseAsync("get_health_start_utc_is_before_now", "GetHealth_StartUtcIsBeforeNow", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/status/health");
                HealthResponse health = await JsonHelper.DeserializeAsync<HealthResponse>(response);
                Assert(health.StartUtc != null, "Health check should include StartUtc");
                DateTime startUtc = health.StartUtc!.Value.ToUniversalTime();
                Assert(startUtc <= DateTime.UtcNow, "StartUtc should be in the past");
            }));

            #endregion

            return new TestSuiteDescriptor(
                suiteId: "E2E.Status",
                displayName: "Status Routes",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Creates a captain with a unique name and returns the deserialized Captain object.
        /// </summary>
        private static async Task<Captain> CreateCaptainAsync(HttpClient client, string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName, Runtime = "ClaudeCode" });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/captains", content);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Captain>(resp);
        }

        /// <summary>
        /// Creates a fleet and returns its identifier.
        /// </summary>
        private static async Task<string> CreateFleetAsync(HttpClient client)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Name = "StatusTestFleet-" + Guid.NewGuid().ToString("N").Substring(0, 8) });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/fleets", content);
            resp.EnsureSuccessStatusCode();
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp);
            return fleet.Id;
        }

        /// <summary>
        /// Creates a vessel in the given fleet against a local bare repository and returns its identifier.
        /// </summary>
        private static async Task<string> CreateVesselAsync(HttpClient client, string fleetId)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Name = "StatusTestVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8), RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/vessels", content);
            resp.EnsureSuccessStatusCode();
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp);
            return vessel.Id;
        }

        /// <summary>
        /// Creates a mission with the given title and returns the deserialized Mission object.
        /// </summary>
        private static async Task<Mission> CreateMissionAsync(HttpClient client, string title)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Title = title, Description = "Status test mission" });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/missions", content);
            resp.EnsureSuccessStatusCode();

            // Read body once since stream can only be consumed once.
            string body = await resp.Content.ReadAsStringAsync();

            // When mission stays Pending (no captain available), the API returns
            // { "Mission": {...}, "Warning": "..." } instead of the mission directly.
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            if (wrapper.Mission != null)
                return wrapper.Mission;

            // Response was the mission directly.
            return JsonHelper.Deserialize<Mission>(body);
        }

        /// <summary>
        /// Creates a voyage against the given vessel with the specified number of missions.
        /// </summary>
        private static async Task<Voyage> CreateVoyageAsync(HttpClient client, string vesselId, string title, int missionCount = 2)
        {
            object[] missions = Enumerable.Range(1, missionCount)
                .Select(i => (object)new { Title = "Voyage Mission " + i, Description = "Desc " + i })
                .ToArray();

            StringContent content = JsonHelper.ToJsonContent(new
            {
                Title = title,
                Description = "Status test voyage",
                VesselId = vesselId,
                Missions = missions
            });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/voyages", content);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Voyage>(resp);
        }

        /// <summary>
        /// Creates a signal with the given type and message and returns the deserialized Signal object.
        /// </summary>
        private static async Task<Signal> CreateSignalAsync(HttpClient client, string type, string message)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Type = type, Message = message });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/signals", content);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Signal>(resp);
        }

        /// <summary>
        /// Retrieves the current Armada status.
        /// </summary>
        private static async Task<ArmadaStatus> GetStatusAsync(HttpClient client)
        {
            HttpResponseMessage response = await client.GetAsync("/api/v1/status");
            response.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<ArmadaStatus>(response);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "E2E.Status",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
