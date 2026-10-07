namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end cross-tenant isolation descriptors, ported 1:1 from the retired automated
    /// CrossTenantApiTests suite. Verifies that entities created by one tenant are invisible and
    /// inaccessible to another via list, read, and delete operations. Cases run sequentially against
    /// the shared e2e server fixture and carry tenant/user/credential/client and per-entity
    /// identifiers across cases as suite instance state, mirroring the legacy suite's shared fields.
    /// </summary>
    public sealed class CrossTenantApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.CrossTenantApi";

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

        // Per-entity identifiers carried across isolation cases
        private string _FleetAId = null!;
        private string _CaptainAId = null!;
        private string _ObjectiveAId = null!;
        private string _RefinementSessionAId = null!;
        private string _VesselAId = null!;
        private string _MissionAId = null!;
        private string _VoyageAId = null!;
        private string _SignalAId = null!;
        private string _MergeEntryAId = null!;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Cross-Tenant Isolation API Tests suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup_create_tenant_a", "Setup_CreateTenantA", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;
                string baseUrl = fx.BaseUrl;

                TenantUserCredentialResult result = await CreateTenantWithUserAsync(adminClient, "tenantA").ConfigureAwait(false);
                _TenantAId = result.TenantId;
                _UserAId = result.UserId;
                _CredentialAId = result.CredentialId;
                _BearerTokenA = result.BearerToken;
                _ClientA = CreateBearerClient(baseUrl, _BearerTokenA);

                AssertNotNull(_TenantAId, "TenantA ID");
                AssertNotNull(_BearerTokenA, "TenantA bearer token");
            }));

            cases.Add(CaseAsync("setup_create_tenant_b", "Setup_CreateTenantB", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;
                string baseUrl = fx.BaseUrl;

                TenantUserCredentialResult result = await CreateTenantWithUserAsync(adminClient, "tenantB").ConfigureAwait(false);
                _TenantBId = result.TenantId;
                _UserBId = result.UserId;
                _CredentialBId = result.CredentialId;
                _BearerTokenB = result.BearerToken;
                _ClientB = CreateBearerClient(baseUrl, _BearerTokenB);

                AssertNotNull(_TenantBId, "TenantB ID");
                AssertNotNull(_BearerTokenB, "TenantB bearer token");
            }));

            cases.Add(CaseAsync("setup_verify_tenant_a_identity", "Setup_VerifyTenantAIdentity", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/whoami").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                WhoAmIResult whoami = await JsonHelper.DeserializeAsync<WhoAmIResult>(response).ConfigureAwait(false);
                AssertEqual(_TenantAId, whoami.Tenant!.Id);
            }));

            cases.Add(CaseAsync("setup_verify_tenant_b_identity", "Setup_VerifyTenantBIdentity", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/whoami").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                WhoAmIResult whoami = await JsonHelper.DeserializeAsync<WhoAmIResult>(response).ConfigureAwait(false);
                AssertEqual(_TenantBId, whoami.Tenant!.Id);
            }));

            cases.Add(CaseAsync("fleet_create_in_tenant_a_returns_201", "Fleet_CreateInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/fleets",
                    JsonHelper.ToJsonContent(new { Name = "xt-fleet-A-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(response).ConfigureAwait(false);
                AssertNotNull(fleet.Id, "Fleet ID");
                _FleetAId = fleet.Id;
            }));

            cases.Add(CaseAsync("fleet_list_from_tenant_a_contains_fleet", "Fleet_ListFromTenantA_ContainsFleet", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(f => f.Id == _FleetAId);
                AssertTrue(found, "Expected fleet " + _FleetAId + " to appear in tenant-A list");
            }));

            cases.Add(CaseAsync("fleet_list_from_tenant_b_does_not_contain_fleet", "Fleet_ListFromTenantB_DoesNotContainFleet", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Fleet> result = await JsonHelper.DeserializeAsync<EnumerationResult<Fleet>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(f => f.Id == _FleetAId);
                AssertFalse(found, "Expected fleet " + _FleetAId + " NOT to appear in tenant-B list");
            }));

            cases.Add(CaseAsync("fleet_read_from_tenant_b_returns_404", "Fleet_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/fleets/" + _FleetAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("fleet_delete_from_tenant_b_returns_404", "Fleet_DeleteFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/fleets/" + _FleetAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("fleet_still_exists_in_tenant_a_after_tenant_b_delete_attempt", "Fleet_StillExistsInTenantA_AfterTenantBDeleteAttempt", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/fleets/" + _FleetAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                FleetDetailResponse detail = await JsonHelper.DeserializeAsync<FleetDetailResponse>(response).ConfigureAwait(false);
                AssertEqual(_FleetAId, detail.Fleet!.Id);
            }));

            cases.Add(CaseAsync("captain_create_in_tenant_a_returns_201", "Captain_CreateInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/captains",
                    JsonHelper.ToJsonContent(new { Name = "xt-captain-A-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Captain captain = await JsonHelper.DeserializeAsync<Captain>(response).ConfigureAwait(false);
                AssertNotNull(captain.Id, "Captain ID");
                _CaptainAId = captain.Id;
            }));

            cases.Add(CaseAsync("captain_list_from_tenant_a_contains_captain", "Captain_ListFromTenantA_ContainsCaptain", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(c => c.Id == _CaptainAId);
                AssertTrue(found, "Expected captain " + _CaptainAId + " to appear in tenant-A list");
            }));

            cases.Add(CaseAsync("captain_list_from_tenant_b_does_not_contain_captain", "Captain_ListFromTenantB_DoesNotContainCaptain", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/captains").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Captain> result = await JsonHelper.DeserializeAsync<EnumerationResult<Captain>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(c => c.Id == _CaptainAId);
                AssertFalse(found, "Expected captain " + _CaptainAId + " NOT to appear in tenant-B list");
            }));

            cases.Add(CaseAsync("captain_read_from_tenant_b_returns_404", "Captain_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/captains/" + _CaptainAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("captain_delete_from_tenant_b_returns_404", "Captain_DeleteFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/captains/" + _CaptainAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("captain_still_exists_in_tenant_a_after_tenant_b_delete_attempt", "Captain_StillExistsInTenantA_AfterTenantBDeleteAttempt", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/captains/" + _CaptainAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Captain captain = await JsonHelper.DeserializeAsync<Captain>(response).ConfigureAwait(false);
                AssertEqual(_CaptainAId, captain.Id);
            }));

            cases.Add(CaseAsync("backlog_create_in_tenant_a_returns_201", "Backlog_CreateInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/backlog",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "xt-backlog-A-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Description = "Cross-tenant backlog isolation coverage.",
                        Status = "Scoped",
                        Kind = "Feature",
                        Priority = "P1",
                        Rank = 10,
                        BacklogState = "Inbox",
                        Effort = "M"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Objective objective = await JsonHelper.DeserializeAsync<Objective>(response).ConfigureAwait(false);
                AssertNotNull(objective.Id, "Backlog objective ID");
                _ObjectiveAId = objective.Id;
            }));

            cases.Add(CaseAsync("backlog_list_from_tenant_a_contains_objective", "Backlog_ListFromTenantA_ContainsObjective", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/backlog?pageSize=100").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Objective> result = await JsonHelper.DeserializeAsync<EnumerationResult<Objective>>(response).ConfigureAwait(false);
                AssertTrue(result.Objects.Any(objective => objective.Id == _ObjectiveAId), "Expected backlog item in tenant-A list");
            }));

            cases.Add(CaseAsync("backlog_list_from_tenant_b_does_not_contain_objective", "Backlog_ListFromTenantB_DoesNotContainObjective", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/backlog?pageSize=100").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Objective> result = await JsonHelper.DeserializeAsync<EnumerationResult<Objective>>(response).ConfigureAwait(false);
                AssertFalse(result.Objects.Any(objective => objective.Id == _ObjectiveAId), "Expected backlog item to be hidden from tenant-B list");
            }));

            cases.Add(CaseAsync("backlog_read_from_tenant_b_returns_404", "Backlog_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/backlog/" + _ObjectiveAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("backlog_delete_from_tenant_b_returns_404", "Backlog_DeleteFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/backlog/" + _ObjectiveAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("backlog_still_exists_in_tenant_a_after_tenant_b_delete_attempt", "Backlog_StillExistsInTenantA_AfterTenantBDeleteAttempt", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/backlog/" + _ObjectiveAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Objective objective = await JsonHelper.DeserializeAsync<Objective>(response).ConfigureAwait(false);
                AssertEqual(_ObjectiveAId, objective.Id);
            }));

            cases.Add(CaseAsync("backlog_refinement_create_in_tenant_a_returns_201", "BacklogRefinement_CreateInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/backlog/" + _ObjectiveAId + "/refinement-sessions",
                    JsonHelper.ToJsonContent(new
                    {
                        CaptainId = _CaptainAId,
                        Title = "xt-refinement-A-" + Guid.NewGuid().ToString("N").Substring(0, 8)
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                ObjectiveRefinementSessionDetail detail = await JsonHelper.DeserializeAsync<ObjectiveRefinementSessionDetail>(response).ConfigureAwait(false);
                AssertNotNull(detail.Session.Id, "Refinement session ID");
                _RefinementSessionAId = detail.Session.Id;
                AssertEqual(_ObjectiveAId, detail.Session.ObjectiveId);
            }));

            cases.Add(CaseAsync("backlog_refinement_list_from_tenant_a_contains_session", "BacklogRefinement_ListFromTenantA_ContainsSession", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/backlog/" + _ObjectiveAId + "/refinement-sessions").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                List<ObjectiveRefinementSession> sessions = await JsonHelper.DeserializeAsync<List<ObjectiveRefinementSession>>(response).ConfigureAwait(false);
                AssertTrue(sessions.Any(session => session.Id == _RefinementSessionAId), "Expected refinement session in tenant-A list");
            }));

            cases.Add(CaseAsync("backlog_refinement_list_from_tenant_b_returns_404", "BacklogRefinement_ListFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/backlog/" + _ObjectiveAId + "/refinement-sessions").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("backlog_refinement_read_from_tenant_b_returns_404", "BacklogRefinement_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/objective-refinement-sessions/" + _RefinementSessionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("backlog_refinement_delete_from_tenant_b_returns_404", "BacklogRefinement_DeleteFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/objective-refinement-sessions/" + _RefinementSessionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("backlog_refinement_still_exists_in_tenant_a_after_tenant_b_delete_attempt", "BacklogRefinement_StillExistsInTenantA_AfterTenantBDeleteAttempt", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/objective-refinement-sessions/" + _RefinementSessionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                ObjectiveRefinementSessionDetail detail = await JsonHelper.DeserializeAsync<ObjectiveRefinementSessionDetail>(response).ConfigureAwait(false);
                AssertEqual(_RefinementSessionAId, detail.Session.Id);
            }));

            cases.Add(CaseAsync("backlog_refinement_delete_from_tenant_a_returns_204", "BacklogRefinement_DeleteFromTenantA_Returns204", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.DeleteAsync("/api/v1/objective-refinement-sessions/" + _RefinementSessionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
            }));

            cases.Add(CaseAsync("backlog_delete_from_tenant_a_returns_204", "Backlog_DeleteFromTenantA_Returns204", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.DeleteAsync("/api/v1/backlog/" + _ObjectiveAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
            }));

            cases.Add(CaseAsync("vessel_create_in_tenant_a_returns_201", "Vessel_CreateInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/vessels",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "xt-vessel-A-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        FleetId = _FleetAId,
                        RepoUrl = TestRepoHelper.GetLocalBareRepoUrl()
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response).ConfigureAwait(false);
                AssertNotNull(vessel.Id, "Vessel ID");
                _VesselAId = vessel.Id;
            }));

            cases.Add(CaseAsync("vessel_list_from_tenant_a_contains_vessel", "Vessel_ListFromTenantA_ContainsVessel", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/vessels").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(v => v.Id == _VesselAId);
                AssertTrue(found, "Expected vessel " + _VesselAId + " to appear in tenant-A list");
            }));

            cases.Add(CaseAsync("vessel_list_from_tenant_b_does_not_contain_vessel", "Vessel_ListFromTenantB_DoesNotContainVessel", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/vessels").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(v => v.Id == _VesselAId);
                AssertFalse(found, "Expected vessel " + _VesselAId + " NOT to appear in tenant-B list");
            }));

            cases.Add(CaseAsync("vessel_read_from_tenant_b_returns_404", "Vessel_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/vessels/" + _VesselAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("vessel_history_from_tenant_b_returns_404", "Vessel history (activity and commits) from tenant B returns 404; tenant A reads it", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                foreach (string route in new[] { "/history/activity", "/history/commits" })
                {
                    HttpResponseMessage denied = await _ClientB!.GetAsync("/api/v1/vessels/" + _VesselAId + route).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.NotFound, denied.StatusCode, "tenant B " + route);
                    HttpResponseMessage allowed = await _ClientA!.GetAsync("/api/v1/vessels/" + _VesselAId + route).ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.OK, allowed.StatusCode, "tenant A " + route);
                }
            }));

            cases.Add(CaseAsync("vessel_delete_from_tenant_b_returns_404", "Vessel_DeleteFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/vessels/" + _VesselAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("vessel_still_exists_in_tenant_a_after_tenant_b_delete_attempt", "Vessel_StillExistsInTenantA_AfterTenantBDeleteAttempt", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/vessels/" + _VesselAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response).ConfigureAwait(false);
                AssertEqual(_VesselAId, vessel.Id);
            }));

            cases.Add(CaseAsync("mission_create_in_tenant_a_returns_201", "Mission_CreateInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/missions",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "xt-mission-A-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        VesselId = _VesselAId
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
                _MissionAId = mission.Id;
            }));

            cases.Add(CaseAsync("mission_list_from_tenant_a_contains_mission", "Mission_ListFromTenantA_ContainsMission", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/missions").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(m => m.Id == _MissionAId);
                AssertTrue(found, "Expected mission " + _MissionAId + " to appear in tenant-A list");
            }));

            cases.Add(CaseAsync("mission_list_from_tenant_b_does_not_contain_mission", "Mission_ListFromTenantB_DoesNotContainMission", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/missions").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Mission> result = await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(m => m.Id == _MissionAId);
                AssertFalse(found, "Expected mission " + _MissionAId + " NOT to appear in tenant-B list");
            }));

            cases.Add(CaseAsync("mission_read_from_tenant_b_returns_404", "Mission_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/missions/" + _MissionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("mission_delete_from_tenant_b_returns_404", "Mission_DeleteFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/missions/" + _MissionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("mission_still_exists_in_tenant_a_after_tenant_b_delete_attempt", "Mission_StillExistsInTenantA_AfterTenantBDeleteAttempt", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/missions/" + _MissionAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Mission mission = JsonHelper.Deserialize<Mission>(body);
                AssertEqual(_MissionAId, mission.Id);
            }));

            cases.Add(CaseAsync("voyage_create_in_tenant_a_returns_201", "Voyage_CreateInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/voyages",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "xt-voyage-A-" + Guid.NewGuid().ToString("N").Substring(0, 8)
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(response).ConfigureAwait(false);
                AssertNotNull(voyage.Id, "Voyage ID");
                _VoyageAId = voyage.Id;
            }));

            cases.Add(CaseAsync("voyage_list_from_tenant_a_contains_voyage", "Voyage_ListFromTenantA_ContainsVoyage", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/voyages").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(v => v.Id == _VoyageAId);
                AssertTrue(found, "Expected voyage " + _VoyageAId + " to appear in tenant-A list");
            }));

            cases.Add(CaseAsync("voyage_list_from_tenant_b_does_not_contain_voyage", "Voyage_ListFromTenantB_DoesNotContainVoyage", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/voyages").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(v => v.Id == _VoyageAId);
                AssertFalse(found, "Expected voyage " + _VoyageAId + " NOT to appear in tenant-B list");
            }));

            cases.Add(CaseAsync("voyage_read_from_tenant_b_returns_404", "Voyage_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/voyages/" + _VoyageAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("voyage_delete_from_tenant_b_returns_404", "Voyage_DeleteFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/voyages/" + _VoyageAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("voyage_still_exists_in_tenant_a_after_tenant_b_delete_attempt", "Voyage_StillExistsInTenantA_AfterTenantBDeleteAttempt", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/voyages/" + _VoyageAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response).ConfigureAwait(false);
                AssertEqual(_VoyageAId, detail.Voyage!.Id);
            }));

            cases.Add(CaseAsync("signal_create_in_tenant_a_returns_201", "Signal_CreateInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/signals",
                    JsonHelper.ToJsonContent(new
                    {
                        Type = "Nudge",
                        Payload = "xt-signal-payload",
                        ToCaptainId = _CaptainAId
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Signal signal = await JsonHelper.DeserializeAsync<Signal>(response).ConfigureAwait(false);
                AssertNotNull(signal.Id, "Signal ID");
                _SignalAId = signal.Id;
            }));

            cases.Add(CaseAsync("signal_list_from_tenant_a_contains_signal", "Signal_ListFromTenantA_ContainsSignal", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/signals?toCaptainId=" + _CaptainAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Signal> result = await JsonHelper.DeserializeAsync<EnumerationResult<Signal>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(s => s.Id == _SignalAId);
                AssertTrue(found, "Expected signal " + _SignalAId + " to appear in tenant-A list");
            }));

            cases.Add(CaseAsync("signal_list_from_tenant_b_does_not_contain_signal", "Signal_ListFromTenantB_DoesNotContainSignal", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/signals").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Signal> result = await JsonHelper.DeserializeAsync<EnumerationResult<Signal>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(s => s.Id == _SignalAId);
                AssertFalse(found, "Expected signal " + _SignalAId + " NOT to appear in tenant-B list");
            }));

            cases.Add(CaseAsync("signal_read_from_tenant_b_returns_404", "Signal_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/signals/" + _SignalAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("merge_queue_enqueue_in_tenant_a_returns_201", "MergeQueue_EnqueueInTenantA_Returns201", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.PostAsync("/api/v1/merge-queue",
                    JsonHelper.ToJsonContent(new
                    {
                        MissionId = _MissionAId,
                        VesselId = _VesselAId,
                        BranchName = "feature/xt-merge-test-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        TargetBranch = "main"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                MergeEntry entry = await JsonHelper.DeserializeAsync<MergeEntry>(response).ConfigureAwait(false);
                AssertNotNull(entry.Id, "MergeEntry ID");
                _MergeEntryAId = entry.Id;
            }));

            cases.Add(CaseAsync("merge_queue_list_from_tenant_a_contains_entry", "MergeQueue_ListFromTenantA_ContainsEntry", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/merge-queue").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(e => e.Id == _MergeEntryAId);
                AssertTrue(found, "Expected merge entry " + _MergeEntryAId + " to appear in tenant-A list");
            }));

            cases.Add(CaseAsync("merge_queue_list_from_tenant_b_does_not_contain_entry", "MergeQueue_ListFromTenantB_DoesNotContainEntry", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/merge-queue").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<MergeEntry> result = await JsonHelper.DeserializeAsync<EnumerationResult<MergeEntry>>(response).ConfigureAwait(false);
                bool found = result.Objects.Any(e => e.Id == _MergeEntryAId);
                AssertFalse(found, "Expected merge entry " + _MergeEntryAId + " NOT to appear in tenant-B list");
            }));

            cases.Add(CaseAsync("merge_queue_read_from_tenant_b_returns_404", "MergeQueue_ReadFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/merge-queue/" + _MergeEntryAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("merge_queue_delete_from_tenant_b_returns_404", "MergeQueue_DeleteFromTenantB_Returns404", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientB!.DeleteAsync("/api/v1/merge-queue/" + _MergeEntryAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, response.StatusCode);
            }));

            cases.Add(CaseAsync("merge_queue_still_exists_in_tenant_a_after_tenant_b_delete_attempt", "MergeQueue_StillExistsInTenantA_AfterTenantBDeleteAttempt", TestTags.Positive, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/merge-queue/" + _MergeEntryAId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                MergeEntry entry = await JsonHelper.DeserializeAsync<MergeEntry>(response).ConfigureAwait(false);
                AssertEqual(_MergeEntryAId, entry.Id);
            }));

            cases.Add(CaseAsync("event_list_from_tenant_a_does_not_contain_tenant_b_events", "Event_ListFromTenantA_DoesNotContainTenantBEvents", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage response = await _ClientA!.GetAsync("/api/v1/events").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertNotEqual(_TenantBId, evt.TenantId, "Tenant-A event list must not contain tenant-B events");
                }
            }));

            cases.Add(CaseAsync("event_list_from_tenant_b_does_not_contain_tenant_a_events", "Event_ListFromTenantB_DoesNotContainTenantAEvents", TestTags.Negative, async () =>
            {
                await E2EServerFixture.AcquireAsync(this);

                HttpResponseMessage createResp = await _ClientB!.PostAsync("/api/v1/fleets",
                    JsonHelper.ToJsonContent(new { Name = "xt-event-fleet-B-" + Guid.NewGuid().ToString("N").Substring(0, 8) })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);

                HttpResponseMessage response = await _ClientB!.GetAsync("/api/v1/events").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<ArmadaEvent> result = await JsonHelper.DeserializeAsync<EnumerationResult<ArmadaEvent>>(response).ConfigureAwait(false);
                foreach (ArmadaEvent evt in result.Objects)
                {
                    AssertNotEqual(_TenantAId, evt.TenantId, "Tenant-B event list must not contain tenant-A events");
                }
            }));

            cases.Add(CaseAsync("cleanup_delete_tenant_resources", "Cleanup_DeleteTenantResources", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient adminClient = fx.AuthClient;

                // Dispose tenant-scoped clients
                _ClientA?.Dispose();
                _ClientB?.Dispose();

                // Delete credentials
                if (_CredentialAId != null)
                    await adminClient.DeleteAsync("/api/v1/credentials/" + _CredentialAId).ConfigureAwait(false);
                if (_CredentialBId != null)
                    await adminClient.DeleteAsync("/api/v1/credentials/" + _CredentialBId).ConfigureAwait(false);

                // Delete users
                if (_UserAId != null)
                    await adminClient.DeleteAsync("/api/v1/users/" + _UserAId).ConfigureAwait(false);
                if (_UserBId != null)
                    await adminClient.DeleteAsync("/api/v1/users/" + _UserBId).ConfigureAwait(false);

                // Delete tenants
                if (_TenantAId != null)
                {
                    HttpResponseMessage resp = await adminClient.DeleteAsync("/api/v1/tenants/" + _TenantAId).ConfigureAwait(false);
                    Assert(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.NotFound,
                        "Expected OK or NotFound when deleting tenant-A, got " + resp.StatusCode);
                }
                if (_TenantBId != null)
                {
                    HttpResponseMessage resp = await adminClient.DeleteAsync("/api/v1/tenants/" + _TenantBId).ConfigureAwait(false);
                    Assert(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.NotFound,
                        "Expected OK or NotFound when deleting tenant-B, got " + resp.StatusCode);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Cross-Tenant Isolation API Tests",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<TenantUserCredentialResult> CreateTenantWithUserAsync(HttpClient adminClient, string label)
        {
            // Create tenant via admin
            string tenantName = "xt-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage tenantResp = await adminClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new { Name = tenantName })).ConfigureAwait(false);
            TenantMetadata tenant = await JsonHelper.DeserializeAsync<TenantMetadata>(tenantResp).ConfigureAwait(false);

            // Create user in tenant
            string email = label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@xt.armada";
            HttpResponseMessage userResp = await adminClient.PostAsync("/api/v1/users",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenant.Id,
                    Email = email,
                    PasswordSha256 = UserMaster.ComputePasswordHash("testpass"),
                    IsTenantAdmin = true
                })).ConfigureAwait(false);
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResp).ConfigureAwait(false);

            // Create credential (bearer token) for the user
            HttpResponseMessage credResp = await adminClient.PostAsync("/api/v1/credentials",
                JsonHelper.ToJsonContent(new
                {
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    Name = label + "-cred"
                })).ConfigureAwait(false);
            Credential cred = await JsonHelper.DeserializeAsync<Credential>(credResp).ConfigureAwait(false);

            return new TenantUserCredentialResult
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                CredentialId = cred.Id,
                BearerToken = cred.BearerToken
            };
        }

        private static HttpClient CreateBearerClient(string baseUrl, string bearerToken)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return client;
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

        private sealed class TenantUserCredentialResult
        {
            public string TenantId { get; set; } = String.Empty;

            public string UserId { get; set; } = String.Empty;

            public string CredentialId { get; set; } = String.Empty;

            public string BearerToken { get; set; } = String.Empty;
        }

        #endregion
    }
}
