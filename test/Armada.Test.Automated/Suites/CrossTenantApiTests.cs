namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// REST-level cross-tenant isolation tests. Verifies that entities created by one tenant
    /// are invisible and inaccessible to another tenant via list, read, and delete operations.
    /// </summary>
    public class CrossTenantApiTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Name of this test suite.
        /// </summary>
        public override string Name => "Cross-Tenant Isolation API Tests";

        #endregion

        #region Private-Members

        private HttpClient _AdminClient;
        private HttpClient _UnauthClient;
        private string _BaseUrl;
        private string _ApiKey;

        // Tenant A state
        private string? _TenantAId;
        private string? _UserAId;
        private string? _CredentialAId;
        private string? _BearerTokenA;
        private HttpClient? _ClientA;

        // Tenant B state
        private string? _TenantBId;
        private string? _UserBId;
        private string? _CredentialBId;
        private string? _BearerTokenB;
        private HttpClient? _ClientB;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a new CrossTenantApiTests suite with shared HTTP clients, base URL, and API key.
        /// </summary>
        public CrossTenantApiTests(HttpClient authClient, HttpClient unauthClient, string baseUrl, string apiKey)
        {
            _AdminClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
            _BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
            _ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        #endregion

        #region Private-Methods

        private async Task<(string TenantId, string UserId, string CredentialId, string BearerToken)> CreateTenantWithUserAsync(string label)
        {
            // Create tenant via admin
            string tenantName = "xt-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage tenantResp = await _AdminClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new { Name = tenantName })).ConfigureAwait(false);
            TenantMetadata tenant = await JsonHelper.DeserializeAsync<TenantMetadata>(tenantResp).ConfigureAwait(false);

            // Create user in tenant
            string email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@xt.armada";
            HttpResponseMessage userResp = await _AdminClient.PostAsync("/api/v1/users",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenant.Id,
                    Email = email,
                    PasswordSha256 = UserMaster.ComputePasswordHash("testpass")
                })).ConfigureAwait(false);
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);

            // Create credential (bearer token) for the user
            HttpResponseMessage credResp = await _AdminClient.PostAsync("/api/v1/credentials",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    Name = label + "-cred"
                })).ConfigureAwait(false);
            Credential cred = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);

            return (tenant.Id, user.Id, cred.Id, cred.BearerToken);
        }

        private HttpClient CreateBearerClient(string bearerToken)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(_BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return client;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task RunTestsAsync()
        {
            #region Setup

            await RunTest("Setup_CreateTenantA", async () =>
            {
                var result = await CreateTenantWithUserAsync("tenantA").ConfigureAwait(false);
                _TenantAId = result.TenantId;
                _UserAId = result.UserId;
                _CredentialAId = result.CredentialId;
                _BearerTokenA = result.BearerToken;
                _ClientA = CreateBearerClient(_BearerTokenA);

                AssertNotNull(_TenantAId, "TenantA ID");
                AssertNotNull(_BearerTokenA, "TenantA bearer token");
            }).ConfigureAwait(false);

            await RunTest("Setup_CreateTenantB", async () =>
            {
                var result = await CreateTenantWithUserAsync("tenantB").ConfigureAwait(false);
                _TenantBId = result.TenantId;
                _UserBId = result.UserId;
                _CredentialBId = result.CredentialId;
                _BearerTokenB = result.BearerToken;
                _ClientB = CreateBearerClient(_BearerTokenB);

                AssertNotNull(_TenantBId, "TenantB ID");
                AssertNotNull(_BearerTokenB, "TenantB bearer token");
            }).ConfigureAwait(false);

            await RunTest("Setup_VerifyTenantAIdentity", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/whoami").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                WhoAmIResult whoami = await JsonHelper.DeserializeAsync<WhoAmIResult>(response).ConfigureAwait(false);
                AssertEqual(_TenantAId, whoami.Tenant!.Id);
            }).ConfigureAwait(false);

            await RunTest("Setup_VerifyTenantBIdentity", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/whoami").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                WhoAmIResult whoami = await JsonHelper.DeserializeAsync<WhoAmIResult>(response).ConfigureAwait(false);
                AssertEqual(_TenantBId, whoami.Tenant!.Id);
            }).ConfigureAwait(false);

            #endregion

            #region Fleet-Isolation

            string fleetAId = null!;

            await RunTest("Fleet_CreateInTenantA_Returns201", async () =>
            {
                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/fleets",
                    JsonHelper.ToJsonContent(new { Name = "xt-fleet-A-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(response).ConfigureAwait(false);
                AssertNotNull(fleet.Id, "Fleet ID");
                fleetAId = fleet.Id;
            }).ConfigureAwait(false);

            await RunTest("Fleet_ListFromTenantA_ContainsFleet", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(f => f.Id == fleetAId);
                AssertTrue(found, "Expected fleet " + fleetAId + " to appear in tenant-A list");
            }).ConfigureAwait(false);

            await RunTest("Fleet_ListFromTenantB_DoesNotContainFleet", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(f => f.Id == fleetAId);
                AssertFalse(found, "Expected fleet " + fleetAId + " NOT to appear in tenant-B list");
            }).ConfigureAwait(false);

            await RunTest("Fleet_ReadFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/fleets/" + fleetAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Fleet_DeleteFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/fleets/" + fleetAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Fleet_StillExistsInTenantA_AfterTenantBDeleteAttempt", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/fleets/" + fleetAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(response).ConfigureAwait(false);
                AssertEqual(fleetAId, fleet.Id);
            }).ConfigureAwait(false);

            #endregion

            #region Captain-Isolation

            string captainAId = null!;

            await RunTest("Captain_CreateInTenantA_Returns201", async () =>
            {
                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/captains",
                    JsonHelper.ToJsonContent(new { Name = "xt-captain-A-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Captain captain = await JsonHelper.DeserializeAsync<Captain>(response).ConfigureAwait(false);
                AssertNotNull(captain.Id, "Captain ID");
                captainAId = captain.Id;
            }).ConfigureAwait(false);

            await RunTest("Captain_ListFromTenantA_ContainsCaptain", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(c => c.Id == captainAId);
                AssertTrue(found, "Expected captain " + captainAId + " to appear in tenant-A list");
            }).ConfigureAwait(false);

            await RunTest("Captain_ListFromTenantB_DoesNotContainCaptain", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(c => c.Id == captainAId);
                AssertFalse(found, "Expected captain " + captainAId + " NOT to appear in tenant-B list");
            }).ConfigureAwait(false);

            await RunTest("Captain_ReadFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/captains/" + captainAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Captain_DeleteFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/captains/" + captainAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Captain_StillExistsInTenantA_AfterTenantBDeleteAttempt", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/captains/" + captainAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain captain = await JsonHelper.DeserializeAsync<Captain>(response).ConfigureAwait(false);
                AssertEqual(captainAId, captain.Id);
            }).ConfigureAwait(false);

            #endregion

            #region Vessel-Isolation

            string vesselAId = null!;

            await RunTest("Vessel_CreateInTenantA_Returns201", async () =>
            {
                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/vessels",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "xt-vessel-A-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        FleetId = fleetAId,
                        RepoUrl = TestRepoHelper.GetLocalBareRepoUrl()
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response).ConfigureAwait(false);
                AssertNotNull(vessel.Id, "Vessel ID");
                vesselAId = vessel.Id;
            }).ConfigureAwait(false);

            await RunTest("Vessel_ListFromTenantA_ContainsVessel", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/vessels").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(v => v.Id == vesselAId);
                AssertTrue(found, "Expected vessel " + vesselAId + " to appear in tenant-A list");
            }).ConfigureAwait(false);

            await RunTest("Vessel_ListFromTenantB_DoesNotContainVessel", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/vessels").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(v => v.Id == vesselAId);
                AssertFalse(found, "Expected vessel " + vesselAId + " NOT to appear in tenant-B list");
            }).ConfigureAwait(false);

            await RunTest("Vessel_ReadFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/vessels/" + vesselAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Vessel_DeleteFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/vessels/" + vesselAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Vessel_StillExistsInTenantA_AfterTenantBDeleteAttempt", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/vessels/" + vesselAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response).ConfigureAwait(false);
                AssertEqual(vesselAId, vessel.Id);
            }).ConfigureAwait(false);

            #endregion

            #region Mission-Isolation

            string missionAId = null!;

            await RunTest("Mission_CreateInTenantA_Returns201", async () =>
            {
                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/missions",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "xt-mission-A-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        VesselId = vesselAId
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
                Mission mission;
                if (wrapper.Mission != null)
                    mission = wrapper.Mission;
                else
                    mission = JsonHelper.Deserialize<Mission>(body);

                AssertNotNull(mission.Id, "Mission ID");
                missionAId = mission.Id;
            }).ConfigureAwait(false);

            await RunTest("Mission_ListFromTenantA_ContainsMission", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/missions").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(m => m.Id == missionAId);
                AssertTrue(found, "Expected mission " + missionAId + " to appear in tenant-A list");
            }).ConfigureAwait(false);

            await RunTest("Mission_ListFromTenantB_DoesNotContainMission", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/missions").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(m => m.Id == missionAId);
                AssertFalse(found, "Expected mission " + missionAId + " NOT to appear in tenant-B list");
            }).ConfigureAwait(false);

            await RunTest("Mission_ReadFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/missions/" + missionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Mission_DeleteFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/missions/" + missionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Mission_StillExistsInTenantA_AfterTenantBDeleteAttempt", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/missions/" + missionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Mission mission = JsonHelper.Deserialize<Mission>(body);
                AssertEqual(missionAId, mission.Id);
            }).ConfigureAwait(false);

            #endregion

            #region Voyage-Isolation

            string voyageAId = null!;

            await RunTest("Voyage_CreateInTenantA_Returns201", async () =>
            {
                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/voyages",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "xt-voyage-A-" + Guid.NewGuid().ToString("N").Substring(0, 8)
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(response).ConfigureAwait(false);
                AssertNotNull(voyage.Id, "Voyage ID");
                voyageAId = voyage.Id;
            }).ConfigureAwait(false);

            await RunTest("Voyage_ListFromTenantA_ContainsVoyage", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/voyages").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(v => v.Id == voyageAId);
                AssertTrue(found, "Expected voyage " + voyageAId + " to appear in tenant-A list");
            }).ConfigureAwait(false);

            await RunTest("Voyage_ListFromTenantB_DoesNotContainVoyage", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/voyages").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(v => v.Id == voyageAId);
                AssertFalse(found, "Expected voyage " + voyageAId + " NOT to appear in tenant-B list");
            }).ConfigureAwait(false);

            await RunTest("Voyage_ReadFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/voyages/" + voyageAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Voyage_DeleteFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/voyages/" + voyageAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("Voyage_StillExistsInTenantA_AfterTenantBDeleteAttempt", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/voyages/" + voyageAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(response).ConfigureAwait(false);
                AssertEqual(voyageAId, voyage.Id);
            }).ConfigureAwait(false);

            #endregion

            #region Signal-Isolation

            string signalAId = null!;

            await RunTest("Signal_CreateInTenantA_Returns201", async () =>
            {
                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/signals",
                    JsonHelper.ToJsonContent(new
                    {
                        Type = "Nudge",
                        Payload = "xt-signal-payload",
                        ToCaptainId = captainAId
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Signal signal = await JsonHelper.DeserializeAsync<Signal>(response).ConfigureAwait(false);
                AssertNotNull(signal.Id, "Signal ID");
                signalAId = signal.Id;
            }).ConfigureAwait(false);

            await RunTest("Signal_ListFromTenantA_ContainsSignal", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/signals?toCaptainId=" + captainAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Signal> result = await JsonHelper.DeserializeAsync<EnumerationResult<Signal>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(s => s.Id == signalAId);
                AssertTrue(found, "Expected signal " + signalAId + " to appear in tenant-A list");
            }).ConfigureAwait(false);

            await RunTest("Signal_ListFromTenantB_DoesNotContainSignal", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/signals").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Signal> result = await JsonHelper.DeserializeAsync<EnumerationResult<Signal>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(s => s.Id == signalAId);
                AssertFalse(found, "Expected signal " + signalAId + " NOT to appear in tenant-B list");
            }).ConfigureAwait(false);

            await RunTest("Signal_ReadFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/signals/" + signalAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            #endregion

            #region MergeQueue-Isolation

            string mergeEntryAId = null!;

            await RunTest("MergeQueue_EnqueueInTenantA_Returns201", async () =>
            {
                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/merge-queue",
                    JsonHelper.ToJsonContent(new
                    {
                        MissionId = missionAId,
                        VesselId = vesselAId,
                        BranchName = "feature/xt-merge-test-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        TargetBranch = "main"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                MergeEntry entry = await JsonHelper.DeserializeAsync<MergeEntry>(response).ConfigureAwait(false);
                AssertNotNull(entry.Id, "MergeEntry ID");
                mergeEntryAId = entry.Id;
            }).ConfigureAwait(false);

            await RunTest("MergeQueue_ListFromTenantA_ContainsEntry", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/merge-queue").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(e => e.Id == mergeEntryAId);
                AssertTrue(found, "Expected merge entry " + mergeEntryAId + " to appear in tenant-A list");
            }).ConfigureAwait(false);

            await RunTest("MergeQueue_ListFromTenantB_DoesNotContainEntry", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/merge-queue").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(e => e.Id == mergeEntryAId);
                AssertFalse(found, "Expected merge entry " + mergeEntryAId + " NOT to appear in tenant-B list");
            }).ConfigureAwait(false);

            await RunTest("MergeQueue_ReadFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/merge-queue/" + mergeEntryAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("MergeQueue_DeleteFromTenantB_Returns404", async () =>
            {
                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/merge-queue/" + mergeEntryAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }).ConfigureAwait(false);

            await RunTest("MergeQueue_StillExistsInTenantA_AfterTenantBDeleteAttempt", async () =>
            {
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/merge-queue/" + mergeEntryAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MergeEntry entry = await JsonHelper.DeserializeAsync<MergeEntry>(response).ConfigureAwait(false);
                AssertEqual(mergeEntryAId, entry.Id);
            }).ConfigureAwait(false);

            #endregion

            #region Event-Isolation

            await RunTest("Event_ListFromTenantA_DoesNotContainTenantBEvents", async () =>
            {
                // Creating a fleet in tenant-B generates events scoped to tenant-B
                HttpResponseMessage createResp = await _ClientB!.PostAsync("/api/v1/fleets",
                    JsonHelper.ToJsonContent(new { Name = "xt-event-fleet-B-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);

                // List events from tenant-A and verify none belong to tenant-B
                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/events").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertNotEqual(_TenantBId, evt.TenantId, "Tenant-A event list must not contain tenant-B events");
                }
            }).ConfigureAwait(false);

            await RunTest("Event_ListFromTenantB_DoesNotContainTenantAEvents", async () =>
            {
                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/events").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertNotEqual(_TenantAId, evt.TenantId, "Tenant-B event list must not contain tenant-A events");
                }
            }).ConfigureAwait(false);

            #endregion

            #region Cleanup

            await RunTest("Cleanup_DeleteTenantResources", async () =>
            {
                // Dispose tenant-scoped clients
                _ClientA?.Dispose();
                _ClientB?.Dispose();

                // Delete credentials
                if (_CredentialAId != null)
                    await _AdminClient.DeleteAsync("/api/v1/credentials/" + _CredentialAId).ConfigureAwait(false);
                if (_CredentialBId != null)
                    await _AdminClient.DeleteAsync("/api/v1/credentials/" + _CredentialBId).ConfigureAwait(false);

                // Delete users
                if (_UserAId != null)
                    await _AdminClient.DeleteAsync("/api/v1/users/" + _UserAId).ConfigureAwait(false);
                if (_UserBId != null)
                    await _AdminClient.DeleteAsync("/api/v1/users/" + _UserBId).ConfigureAwait(false);

                // Delete tenants
                if (_TenantAId != null)
                {
                    HttpResponseMessage resp = await _AdminClient.DeleteAsync("/api/v1/tenants/" + _TenantAId).ConfigureAwait(false);
                    Assert(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.NotFound,
                        "Expected OK or NotFound when deleting tenant-A, got " + resp.StatusCode);
                }
                if (_TenantBId != null)
                {
                    HttpResponseMessage resp = await _AdminClient.DeleteAsync("/api/v1/tenants/" + _TenantBId).ConfigureAwait(false);
                    Assert(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.NotFound,
                        "Expected OK or NotFound when deleting tenant-B, got " + resp.StatusCode);
                }
            }).ConfigureAwait(false);

            #endregion
        }

        #endregion
    }
}
