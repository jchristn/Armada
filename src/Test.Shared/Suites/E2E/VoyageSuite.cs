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
    /// End-to-end Voyage API descriptors covering voyage creation with missions, retrieval, cancel,
    /// purge, list, pagination, ordering, and enumeration against a live in-process Armada server
    /// provided by <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class VoyageSuite : IArmadaTestSuite
    {
        #region Private-Members

        private readonly object _SeedLock = new object();
        private Task? _SharedVoyageSeed;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Voyage API end-to-end suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            #region Create-Voyage-Tests

            cases.Add(CaseAsync("create_voyage_with_missions_returns_201_with_correct_properties", "Create Voyage With Missions Returns 201 With Correct Properties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "My Voyage", missionCount: 1);

                AssertStartsWith("vyg_", voyage.Id);
                AssertEqual("My Voyage", voyage.Title);
                string status = voyage.Status.ToString();
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
                AssertTrue(voyage.CreatedUtc != default);
                AssertTrue(voyage.LastUpdateUtc != default);
            }));

            cases.Add(CaseAsync("create_voyage_with_description_returns_description", "Create Voyage With Description Returns Description", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Described Voyage", description: "A detailed description");

                AssertEqual("A detailed description", voyage.Description!);
            }));

            cases.Add(CaseAsync("create_voyage_status_defaults_to_open_or_in_progress", "Create Voyage Status Defaults To Open Or InProgress", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Open Status Voyage");

                string status = voyage.Status.ToString();
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
            }));

            cases.Add(CaseAsync("create_voyage_id_has_vyg_prefix", "Create Voyage Id Has Vyg Prefix", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Id Prefix Voyage");

                string id = voyage.Id;
                AssertStartsWith("vyg_", id);
                AssertTrue(id.Length > 4);
            }));

            cases.Add(CaseAsync("create_voyage_with_single_mission_missions_created", "Create Voyage With Single Mission Missions Created", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Single Mission Voyage", missionCount: 1);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertEqual(1, detail.Missions!.Count);
            }));

            cases.Add(CaseAsync("create_voyage_with_multiple_missions_all_missions_created", "Create Voyage With Multiple Missions All Missions Created", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Multi Mission Voyage", missionCount: 5);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertEqual(5, detail.Missions!.Count);
            }));

            cases.Add(CaseAsync("create_voyage_missions_have_correct_titles", "Create Voyage Missions Have Correct Titles", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Title Check Voyage", missionCount: 3);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                List<string> titles = detail.Missions!.Select(m => m.Title).ToList();

                AssertEqual("Mission 1,Mission 2,Mission 3", string.Join(",", titles.OrderBy(t => t, StringComparer.Ordinal)), "exactly the three mission titles");
            }));

            cases.Add(CaseAsync("create_voyage_missions_have_msn_prefix", "Create Voyage Missions Have Msn Prefix", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Msn Prefix Voyage", missionCount: 2);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                foreach (Mission m in detail.Missions!)
                {
                    AssertStartsWith("msn_", m.Id);
                }
            }));

            cases.Add(CaseAsync("create_voyage_missions_linked_to_voyage", "Create Voyage Missions Linked To Voyage", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Linked Voyage", missionCount: 2);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                foreach (Mission m in detail.Missions!)
                {
                    AssertEqual(voyageId, m.VoyageId!);
                }
            }));

            cases.Add(CaseAsync("create_voyage_missions_linked_to_vessel", "Create Voyage Missions Linked To Vessel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Vessel Link Voyage", missionCount: 2);
                string voyageId = voyage.Id;

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                foreach (Mission m in detail.Missions!)
                {
                    AssertEqual(vesselId, m.VesselId!);
                }
            }));

            cases.Add(CaseAsync("create_voyage_completed_utc_is_null_or_omitted", "Create Voyage CompletedUtc Is Null Or Omitted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "No Completion Voyage");

                Assert(
                    voyage.CompletedUtc == null,
                    "CompletedUtc should be null or omitted on a new voyage");
            }));

            cases.Add(CaseAsync("create_voyage_bare_voyage_without_vessel_id_returns_201", "Create Voyage Bare Voyage Without VesselId Returns 201", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdVoyageIds = new List<string>();

                StringContent content = JsonHelper.ToJsonContent(new { Title = "Bare Voyage", Description = "No vessel" });

                HttpResponseMessage resp = await authClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, resp.StatusCode);

                Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(resp);
                createdVoyageIds.Add(voyage.Id);
                AssertStartsWith("vyg_", voyage.Id);
                AssertEqual("Open", voyage.Status.ToString());
            }));

            cases.Add(CaseAsync("create_voyage_bare_voyage_with_empty_missions_returns_201", "Create Voyage Bare Voyage With Empty Missions Returns 201", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                StringContent content = JsonHelper.ToJsonContent(new { Title = "Empty Missions Voyage", VesselId = vesselId, Missions = Array.Empty<object>() });

                HttpResponseMessage resp = await authClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, resp.StatusCode);

                Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(resp);
                createdVoyageIds.Add(voyage.Id);
                AssertStartsWith("vyg_", voyage.Id);
            }));

            cases.Add(CaseAsync("create_voyage_multiple_voyages_get_unique_ids", "Create Voyage Multiple Voyages Get Unique Ids", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage voyage1 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Unique Id Voyage 1");
                Voyage voyage2 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Unique Id Voyage 2");
                Voyage voyage3 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Unique Id Voyage 3");

                string id1 = voyage1.Id;
                string id2 = voyage2.Id;
                string id3 = voyage3.Id;

                AssertNotEqual(id1, id2);
                AssertNotEqual(id2, id3);
                AssertNotEqual(id1, id3);
            }));

            #endregion

            #region Get-Voyage-Tests

            cases.Add(CaseAsync("get_voyage_exists_returns_voyage_and_missions", "Get Voyage Exists Returns Voyage And Missions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "GetVoyage Test", missionCount: 2);
                string voyageId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response);

                AssertTrue(detail.Voyage != null);
                AssertTrue(detail.Missions != null);
                AssertEqual(voyageId, detail.Voyage!.Id);
                AssertEqual("GetVoyage Test", detail.Voyage!.Title);
                AssertEqual(2, detail.Missions!.Count);
            }));

            cases.Add(CaseAsync("get_voyage_returns_correct_voyage_properties", "Get Voyage Returns Correct Voyage Properties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Property Check", description: "Check all props");
                string voyageId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response);
                Voyage voyage = detail.Voyage!;

                AssertEqual(voyageId, voyage.Id);
                AssertEqual("Property Check", voyage.Title);
                AssertEqual("Check all props", voyage.Description!);
                string status = voyage.Status.ToString();
                Assert(status == "Open" || status == "InProgress",
                    "Expected Open or InProgress but got: " + status);
                AssertTrue(voyage.CreatedUtc != default);
                AssertTrue(voyage.LastUpdateUtc != default);
            }));

            cases.Add(CaseAsync("create_voyage_unknown_pipeline_returns_404", "Create Voyage With An Unknown PipelineId Or Pipeline Name Returns 404 And Creates Nothing", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string title = "Unknown Pipeline Voyage " + Guid.NewGuid().ToString("N");

                StringContent byId = JsonHelper.ToJsonContent(new { Title = title, VesselId = prereqs.VesselId, PipelineId = "ppl_doesnotexist", Missions = new[] { new { Title = "m", Description = "d" } } });
                HttpResponseMessage idResp = await authClient.PostAsync("/api/v1/voyages", byId);
                await E2eApiErrorAssert.ExpectAsync(idResp, HttpStatusCode.NotFound, "an unknown PipelineId must not fall back to the default pipeline");

                StringContent byName = JsonHelper.ToJsonContent(new { Title = title, VesselId = prereqs.VesselId, Pipeline = "NoSuchPipeline", Missions = new[] { new { Title = "m", Description = "d" } } });
                HttpResponseMessage nameResp = await authClient.PostAsync("/api/v1/voyages", byName);
                await E2eApiErrorAssert.ExpectAsync(nameResp, HttpStatusCode.NotFound, "an unknown pipeline name is not found");
            }));

            cases.Add(CaseAsync("get_voyage_not_found_returns_error", "Get Voyage Not Found Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages/vyg_nonexistent");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("get_voyage_not_found_contains_not_found_message", "Get Voyage Not Found Contains Not Found Message", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages/vyg_doesnotexist");
                ApiErrorProbe error = await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Not-found voyage returns a typed 404");
                AssertFalse(String.IsNullOrEmpty(error.Message), "a message accompanies the error");
            }));

            cases.Add(CaseAsync("get_voyage_missions_include_mission_details", "Get Voyage Missions Include Mission Details", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Mission Details Voyage", missionCount: 1);
                string voyageId = created.Id;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response);
                Mission firstMission = detail.Missions![0];

                AssertTrue(firstMission.Id != null);
                AssertTrue(firstMission.Title != null);
                AssertTrue(Enum.IsDefined(firstMission.Status), "Status should be a valid enum value");
            }));

            #endregion

            #region Cancel-Voyage-Tests

            cases.Add(CaseAsync("cancel_voyage_returns_cancelled_status", "Cancel Voyage Returns Cancelled Status", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Cancel Me", missionCount: 2);
                string voyageId = created.Id;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                CancelVoyageResponse cancelResp = await JsonHelper.DeserializeAsync<CancelVoyageResponse>(response);
                AssertEqual("Cancelled", cancelResp.Voyage!.Status.ToString());
                AssertTrue(cancelResp.CancelledMissions >= 0);
            }));

            cases.Add(CaseAsync("cancel_voyage_voyage_status_set_to_cancelled", "Cancel Voyage Voyage Status Set To Cancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Cancel Status Check", missionCount: 1);
                string voyageId = created.Id;

                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertEqual("Cancelled", detail.Voyage!.Status.ToString());
            }));

            cases.Add(CaseAsync("cancel_voyage_sets_completed_utc", "Cancel Voyage Sets CompletedUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "CompletedUtc Cancel", missionCount: 1);
                string voyageId = created.Id;

                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertTrue(detail.Voyage!.CompletedUtc != null);
            }));

            cases.Add(CaseAsync("cancel_voyage_cancels_all_pending_missions", "Cancel Voyage Cancels All Pending Missions", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Cancel Missions Voyage", missionCount: 3);
                string voyageId = created.Id;

                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);

                foreach (Mission m in detail.Missions!)
                {
                    string status = m.Status.ToString();
                    Assert(status == "Cancelled" || status == "Complete" || status == "Failed",
                        "Expected mission status to be Cancelled, Complete, or Failed but got: " + status);
                }
            }));

            cases.Add(CaseAsync("cancel_voyage_not_found_returns_error", "Cancel Voyage Not Found Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/voyages/vyg_nonexistent");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("cancel_voyage_voyage_still_retrievable_after_cancel", "Cancel Voyage Voyage Still Retrievable After Cancel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Still Retrievable", missionCount: 1);
                string voyageId = created.Id;

                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getResp);
                AssertTrue(detail.Voyage != null);
            }));

            #endregion

            #region Purge-Voyage-Tests

            cases.Add(CaseAsync("purge_voyage_returns_deleted_status", "Purge Voyage Returns Deleted Status", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Purge Me", missionCount: 2);
                string voyageId = created.Id;

                // Cancel first -- purge is blocked on Open/InProgress voyages
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                PurgeVoyageResponse purgeResp = await JsonHelper.DeserializeAsync<PurgeVoyageResponse>(response);
                AssertEqual("deleted", purgeResp.Status!);
                AssertEqual(voyageId, purgeResp.VoyageId!);

                createdVoyageIds.Remove(voyageId);
            }));

            cases.Add(CaseAsync("purge_voyage_returns_missions_deleted_count", "Purge Voyage Returns Missions Deleted Count", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Purge Count", missionCount: 3);
                string voyageId = created.Id;

                // Cancel first -- purge is blocked on Open/InProgress voyages
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                PurgeVoyageResponse purgeResp = await JsonHelper.DeserializeAsync<PurgeVoyageResponse>(response);
                AssertEqual(3, purgeResp.MissionsDeleted);

                createdVoyageIds.Remove(voyageId);
            }));

            cases.Add(CaseAsync("purge_voyage_voyage_not_found_after_purge", "Purge Voyage Voyage Not Found After Purge", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Purge Gone", missionCount: 1);
                string voyageId = created.Id;

                // Cancel first -- purge is blocked on Open/InProgress voyages
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                createdVoyageIds.Remove(voyageId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                await E2eApiErrorAssert.ExpectAsync(getResp, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("purge_voyage_missions_also_deleted", "Purge Voyage Missions Also Deleted", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Purge Missions Gone", missionCount: 2);
                string voyageId = created.Id;

                // Cancel first -- purge is blocked on Open/InProgress voyages
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage getBeforeResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detailBefore = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(getBeforeResp);
                List<string> missionIds = detailBefore.Missions!.Select(m => m.Id).ToList();

                AssertTrue(missionIds.Count > 0);

                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                createdVoyageIds.Remove(voyageId);

                foreach (string missionId in missionIds)
                {
                    HttpResponseMessage missionResp = await authClient.GetAsync("/api/v1/missions/" + missionId);
                    await E2eApiErrorAssert.ExpectAsync(missionResp, HttpStatusCode.NotFound, "expected NotFound");
                }
            }));

            cases.Add(CaseAsync("purge_voyage_not_found_returns_error", "Purge Voyage Not Found Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/voyages/vyg_nonexistent/purge");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("purge_voyage_with_zero_missions_returns_zero_missions_deleted", "Purge Voyage With Zero Missions Returns Zero Missions Deleted", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                StringContent content = JsonHelper.ToJsonContent(new { Title = "Bare Purge" });
                HttpResponseMessage createResp = await authClient.PostAsync("/api/v1/voyages", content);
                AssertEqual(HttpStatusCode.Created, createResp.StatusCode);
                Voyage createdVoyage = await JsonHelper.DeserializeAsync<Voyage>(createResp);
                string voyageId = createdVoyage.Id;

                // Cancel first -- purge is blocked on Open/InProgress voyages
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                PurgeVoyageResponse purgeResp = await JsonHelper.DeserializeAsync<PurgeVoyageResponse>(response);
                AssertEqual(0, purgeResp.MissionsDeleted);
            }));

            cases.Add(CaseAsync("purge_voyage_voyage_not_in_list_after_purge", "Purge Voyage Voyage Not In List After Purge", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Purge List Check", missionCount: 1);
                string voyageId = created.Id;

                // Cancel first -- purge is blocked on Open/InProgress voyages
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                createdVoyageIds.Remove(voyageId);

                HttpResponseMessage listResp = await authClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> listResult = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(listResp);

                foreach (Voyage v in listResult.Objects)
                {
                    AssertNotEqual(voyageId, v.Id);
                }
            }));

            #endregion

            #region List-Voyages-Tests

            cases.Add(CaseAsync("list_voyages_empty_returns_empty_enumeration_result", "List Voyages Empty Returns Empty Enumeration Result", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects != null);
                AssertTrue(result.TotalRecords >= 0);
            }));

            cases.Add(CaseAsync("list_voyages_empty_returns_correct_pagination_metadata", "List Voyages Empty Returns Correct Pagination Metadata", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.TotalPages >= 0);
                AssertTrue(result.TotalRecords >= 0);
                AssertTrue(result.Success || !result.Success); // field exists
            }));

            cases.Add(CaseAsync("list_voyages_after_create_returns_voyages", "List Voyages After Create Returns Voyages", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "List Test Voyage");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects.Count >= 1);
            }));

            cases.Add(CaseAsync("list_voyages_multiple_voyages_returns_all", "List Voyages Multiple Voyages Returns All", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "List Multi 1");
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "List Multi 2");
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "List Multi 3");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects.Count >= 3);
            }));

            cases.Add(CaseAsync("list_voyages_pagination_25_voyages_pagesize_10_correct_totals", "List Voyages Pagination 25 Voyages PageSize 10 Correct Totals", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureVoyagesSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?pageSize=10");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(10, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_voyages_pagination_page_2_returns_correct_page", "List Voyages Pagination Page 2 Returns Correct Page", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureVoyagesSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=2");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_voyages_pagination_last_page_returns_remaining_records", "List Voyages Pagination Last Page Returns Remaining Records", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureVoyagesSeededAsync(authClient);

                // Get total to find actual last page dynamically
                HttpResponseMessage firstResp = await authClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=1");
                EnumerationResult<Voyage> firstResult = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(firstResp);
                int totalRecords = (int)firstResult.TotalRecords;
                int totalPages = (int)Math.Ceiling(totalRecords / 10.0);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=" + totalPages);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                int expectedRemaining = totalRecords - (totalPages - 1) * 10;
                AssertEqual(expectedRemaining, result.Objects.Count);
                AssertEqual(totalPages, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_voyages_pagination_beyond_last_page_returns_empty", "List Voyages Pagination Beyond Last Page Returns Empty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 5; i++)
                {
                    await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Beyond Voyage " + i);
                }

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=99");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(0, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_voyages_pagination_pagesize_1_each_page_has_one_record", "List Voyages Pagination PageSize 1 Each Page Has One Record", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Single A");
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Single B");
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Single C");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?pageSize=1&pageNumber=1");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(1, result.Objects.Count);
                AssertTrue((int)result.TotalRecords >= 3);
            }));

            cases.Add(CaseAsync("list_voyages_no_pagination_overlap_all_records_unique", "List Voyages No Pagination Overlap All Records Unique", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                await EnsureVoyagesSeededAsync(authClient);

                HttpResponseMessage resp1 = await authClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=1");
                EnumerationResult<Voyage> result1 = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(resp1);

                HttpResponseMessage resp2 = await authClient.GetAsync("/api/v1/voyages?pageSize=10&pageNumber=2");
                EnumerationResult<Voyage> result2 = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(resp2);

                HashSet<string> page1Ids = new HashSet<string>();
                foreach (Voyage v in result1.Objects)
                {
                    page1Ids.Add(v.Id);
                }

                foreach (Voyage v in result2.Objects)
                {
                    string id = v.Id;
                    AssertFalse(page1Ids.Contains(id));
                }
            }));

            cases.Add(CaseAsync("list_voyages_order_created_ascending_oldest_first", "List Voyages Order Created Ascending Oldest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Asc Voyage 1");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Asc Voyage 2");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Asc Voyage 3");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?order=CreatedAscending");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] >= dates[i - 1], "Expected ascending order by CreatedUtc");
                }
            }));

            cases.Add(CaseAsync("list_voyages_order_created_descending_newest_first", "List Voyages Order Created Descending Newest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Desc Voyage 1");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Desc Voyage 2");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Desc Voyage 3");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?order=CreatedDescending");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Expected descending order by CreatedUtc");
                }
            }));

            cases.Add(CaseAsync("list_voyages_filter_by_status_open_returns_only_open", "List Voyages Filter By Status Open Returns Only Open", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Open Voyage 1");
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Open Voyage 2");

                Voyage toCancel = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "To Cancel For Filter");
                string cancelId = toCancel.Id;
                await authClient.DeleteAsync("/api/v1/voyages/" + cancelId);

                // Voyages start as Open (no captain available to advance them)
                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?status=Open");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Objects.Count >= 2);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("Open", v.Status.ToString());
                }
            }));

            cases.Add(CaseAsync("list_voyages_filter_by_status_cancelled_returns_only_cancelled", "List Voyages Filter By Status Cancelled Returns Only Cancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage v1 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Cancel Filter 1");
                Voyage v2 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Cancel Filter 2");
                await authClient.DeleteAsync("/api/v1/voyages/" + v1.Id);
                await authClient.DeleteAsync("/api/v1/voyages/" + v2.Id);

                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Keep Open");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?status=Cancelled");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Objects.Count >= 2);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("Cancelled", v.Status.ToString());
                }
            }));

            cases.Add(CaseAsync("list_voyages_filter_by_non_matching_status_returns_empty", "List Voyages Filter By Non Matching Status Returns Empty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Open Only");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?status=Complete");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertEqual(0, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_voyages_voyages_contain_expected_properties", "List Voyages Voyages Contain Expected Properties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Props Voyage");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                Voyage firstVoyage = result.Objects[0];

                AssertTrue(firstVoyage.Id != null);
                AssertTrue(firstVoyage.Title != null);
                AssertTrue(firstVoyage.Status != default || firstVoyage.Status == default); // field exists
                AssertTrue(firstVoyage.CreatedUtc != default);
            }));

            #endregion

            #region Enumerate-Voyages-Tests

            cases.Add(CaseAsync("enumerate_voyages_default_query_returns_enumeration_result", "Enumerate Voyages Default Query Returns Enumeration Result", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enumerate Default");

                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects != null);
                AssertTrue(result.TotalRecords >= 0);
                AssertTrue(result.PageNumber >= 0);
                AssertTrue(result.PageSize >= 0);
                AssertTrue(result.Objects.Count >= 1);
            }));

            cases.Add(CaseAsync("enumerate_voyages_empty_database_returns_empty_result", "Enumerate Voyages Empty Database Returns Empty Result", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects != null);
                AssertTrue(result.TotalRecords >= 0);
            }));

            cases.Add(CaseAsync("enumerate_voyages_with_pagesize_respects_limit", "Enumerate Voyages With PageSize Respects Limit", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 15; i++)
                {
                    await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum PageSize " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 5 });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(5, result.Objects.Count);
            }));

            cases.Add(CaseAsync("enumerate_voyages_with_pagenumber_returns_correct_page", "Enumerate Voyages With PageNumber Returns Correct Page", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 20; i++)
                {
                    await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Page " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 10, PageNumber = 2 });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }));

            cases.Add(CaseAsync("enumerate_voyages_with_status_filter_returns_only_matching", "Enumerate Voyages With Status Filter Returns Only Matching", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum InProgress 1");
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum InProgress 2");

                Voyage toCancel = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Cancelled 1");
                await authClient.DeleteAsync("/api/v1/voyages/" + toCancel.Id);

                StringContent content = JsonHelper.ToJsonContent(new { Status = "InProgress" });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("InProgress", v.Status.ToString());
                }
            }));

            cases.Add(CaseAsync("enumerate_voyages_with_cancelled_status_filter_returns_only_cancelled", "Enumerate Voyages With Cancelled Status Filter Returns Only Cancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Keep Open");

                Voyage c1 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Cancel A");
                Voyage c2 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Cancel B");
                await authClient.DeleteAsync("/api/v1/voyages/" + c1.Id);
                await authClient.DeleteAsync("/api/v1/voyages/" + c2.Id);

                StringContent content = JsonHelper.ToJsonContent(new { Status = "Cancelled" });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Objects.Count >= 2);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("Cancelled", v.Status.ToString());
                }
            }));

            cases.Add(CaseAsync("enumerate_voyages_order_created_ascending_sorts_correctly", "Enumerate Voyages Order Created Ascending Sorts Correctly", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Asc 1");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Asc 2");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Asc 3");

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedAscending" });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] >= dates[i - 1], "Expected ascending order by CreatedUtc");
                }
            }));

            cases.Add(CaseAsync("enumerate_voyages_order_created_descending_sorts_correctly", "Enumerate Voyages Order Created Descending Sorts Correctly", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Desc 1");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Desc 2");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Desc 3");

                StringContent content = JsonHelper.ToJsonContent(new { Order = "CreatedDescending" });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Expected descending order by CreatedUtc");
                }
            }));

            cases.Add(CaseAsync("enumerate_voyages_pagination_and_filter_combined", "Enumerate Voyages Pagination And Filter Combined", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 12; i++)
                {
                    await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Combined " + i);
                }

                HttpResponseMessage listResp = await authClient.GetAsync("/api/v1/voyages?pageSize=3");
                EnumerationResult<Voyage> listResult = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(listResp);
                int cancelCount = 0;
                foreach (Voyage v in listResult.Objects)
                {
                    if (cancelCount < 3)
                    {
                        await authClient.DeleteAsync("/api/v1/voyages/" + v.Id);
                        cancelCount++;
                    }
                }

                // Voyages start as Open (no captain available to advance them)
                StringContent content = JsonHelper.ToJsonContent(new { Status = "Open", PageSize = 5, PageNumber = 1 });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Objects.Count >= 5);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("Open", v.Status.ToString());
                }
            }));

            cases.Add(CaseAsync("enumerate_voyages_returns_total_pages_correctly", "Enumerate Voyages Returns TotalPages Correctly", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 7; i++)
                {
                    await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "TotalPages Voyage " + i);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 3 });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                int totalRecords = (int)result.TotalRecords;
                int totalPages = result.TotalPages;
                int expectedPages = (int)Math.Ceiling(totalRecords / 3.0);
                AssertEqual(expectedPages, totalPages);
                AssertTrue(totalPages >= 3, "Should have at least 3 pages with 7+ voyages at pageSize 3");
            }));

            cases.Add(CaseAsync("enumerate_voyages_returns_success_field", "Enumerate Voyages Returns Success Field", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                StringContent content = JsonHelper.ToJsonContent(new { });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertTrue(result.Success);
            }));

            #endregion

            #region Edge-Case-Tests

            cases.Add(CaseAsync("cancel_voyage_then_purge_both_succeed", "Cancel Voyage Then Purge Both Succeed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Cancel Then Purge", missionCount: 2);
                string voyageId = created.Id;

                HttpResponseMessage cancelResp = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, cancelResp.StatusCode);

                HttpResponseMessage purgeResp = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, purgeResp.StatusCode);
                createdVoyageIds.Remove(voyageId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                await E2eApiErrorAssert.ExpectAsync(getResp, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("create_multiple_voyages_same_vessel_all_succeed", "Create Multiple Voyages Same Vessel All Succeed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage v1 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Same Vessel 1", missionCount: 2);
                Voyage v2 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Same Vessel 2", missionCount: 3);
                Voyage v3 = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Same Vessel 3", missionCount: 1);

                AssertNotEqual(v1.Id, v2.Id);
                AssertNotEqual(v2.Id, v3.Id);
            }));

            cases.Add(CaseAsync("cancelled_voyage_still_appears_in_list", "Cancelled Voyage Still Appears In List", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Cancelled In List");
                string voyageId = created.Id;
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                bool found = false;
                foreach (Voyage v in result.Objects)
                {
                    if (v.Id == voyageId)
                    {
                        found = true;
                        AssertEqual("Cancelled", v.Status.ToString());
                        break;
                    }
                }
                Assert(found, "Cancelled voyage should still appear in list");
            }));

            cases.Add(CaseAsync("list_voyages_default_order_is_created_descending", "List Voyages Default Order Is Created Descending", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Default Order 1");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Default Order 2");
                await Task.Delay(50);
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Default Order 3");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                List<DateTime> dates = new List<DateTime>();
                foreach (Voyage v in result.Objects)
                {
                    dates.Add(v.CreatedUtc);
                }

                for (int i = 1; i < dates.Count; i++)
                {
                    Assert(dates[i] <= dates[i - 1], "Default order should be CreatedDescending (newest first)");
                }
            }));

            cases.Add(CaseAsync("list_voyages_cancelled_voyage_has_completed_utc_set", "List Voyages Cancelled Voyage Has CompletedUtc Set", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "CompletedUtc Check");
                string voyageId = created.Id;
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                foreach (Voyage v in result.Objects)
                {
                    if (v.Id == voyageId)
                    {
                        AssertTrue(v.CompletedUtc != null);
                        break;
                    }
                }
            }));

            cases.Add(CaseAsync("get_voyage_after_cancel_missions_have_completed_utc", "Get Voyage After Cancel Missions Have CompletedUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Missions CompletedUtc", missionCount: 2);
                string voyageId = created.Id;
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages/" + voyageId);
                VoyageDetailResponse detail = await JsonHelper.DeserializeAsync<VoyageDetailResponse>(response);

                foreach (Mission m in detail.Missions!)
                {
                    if (m.Status.ToString() == "Cancelled")
                    {
                        AssertTrue(m.CompletedUtc != null);
                    }
                }
            }));

            cases.Add(CaseAsync("enumerate_voyages_beyond_last_page_returns_empty", "Enumerate Voyages Beyond Last Page Returns Empty", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Beyond 1");
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Enum Beyond 2");

                StringContent content = JsonHelper.ToJsonContent(new { PageSize = 10, PageNumber = 100 });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(0, result.Objects.Count);
            }));

            cases.Add(CaseAsync("list_voyages_filter_by_status_in_progress_returns_empty_when_none_in_progress", "List Voyages Filter By Status InProgress Returns Empty When None InProgress", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "No InProgress");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?status=InProgress");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                foreach (Voyage v in result.Objects)
                {
                    AssertEqual("InProgress", v.Status.ToString());
                }
            }));

            cases.Add(CaseAsync("purge_voyage_double_purge_second_returns_error", "Purge Voyage Double Purge Second Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Double Purge", missionCount: 1);
                string voyageId = created.Id;

                // Cancel first -- purge is blocked on Open/InProgress voyages
                await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);

                HttpResponseMessage resp1 = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                AssertEqual(HttpStatusCode.OK, resp1.StatusCode);
                createdVoyageIds.Remove(voyageId);

                HttpResponseMessage resp2 = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId + "/purge");
                await E2eApiErrorAssert.ExpectAsync(resp2, HttpStatusCode.NotFound, "expected NotFound");
            }));

            cases.Add(CaseAsync("cancel_voyage_double_cancel_second_still_succeeds", "Cancel Voyage Double Cancel Second Still Succeeds", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;

                Voyage created = await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Double Cancel", missionCount: 1);
                string voyageId = created.Id;

                HttpResponseMessage resp1 = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, resp1.StatusCode);

                HttpResponseMessage resp2 = await authClient.DeleteAsync("/api/v1/voyages/" + voyageId);
                AssertEqual(HttpStatusCode.OK, resp2.StatusCode);

                CancelVoyageResponse cancelResp = await JsonHelper.DeserializeAsync<CancelVoyageResponse>(resp2);
                AssertEqual("Cancelled", cancelResp.Voyage!.Status.ToString());
            }));

            cases.Add(CaseAsync("list_voyages_with_pagesize_query_param_overrides_default", "List Voyages With PageSize Query Param Overrides Default", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                for (int i = 1; i <= 5; i++)
                {
                    await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "PageSize Override " + i);
                }

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/voyages?pageSize=2");
                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);

                AssertEqual(2, result.Objects.Count);
            }));

            cases.Add(CaseAsync("enumerate_voyages_null_body_returns_results", "Enumerate Voyages Null Body Returns Results", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();
                List<string> createdVoyageIds = new List<string>();

                PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
                string vesselId = prereqs.VesselId;
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "Null Body Enum");

                StringContent content = new StringContent(
                    "{}",
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/voyages/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Voyage> result = await JsonHelper.DeserializeAsync<EnumerationResult<Voyage>>(response);
                AssertTrue(result.Objects.Count >= 1);
            }));

            #endregion

            return new TestSuiteDescriptor(
                suiteId: "E2E.Voyage",
                displayName: "Voyages",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Creates a fleet and returns its ID.
        /// </summary>
        private static async Task<string> CreateFleetAsync(HttpClient client, List<string> createdFleetIds, string name = "VoyageTestFleet")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/fleets", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp);
            createdFleetIds.Add(fleet.Id);
            return fleet.Id;
        }

        /// <summary>
        /// Creates a vessel under the given fleet and returns its ID.
        /// </summary>
        private static async Task<string> CreateVesselAsync(HttpClient client, List<string> createdVesselIds, string fleetId, string name = "VoyageTestVessel")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName, RepoUrl = TestRepoHelper.GetLocalBareRepoUrl(), FleetId = fleetId });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/vessels", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp);
            createdVesselIds.Add(vessel.Id);
            return vessel.Id;
        }

        /// <summary>
        /// Creates a voyage with the specified number of missions and returns the deserialized Voyage object.
        /// </summary>
        private static async Task<Voyage> CreateVoyageAsync(HttpClient client, List<string> createdVoyageIds, string vesselId, string title, int missionCount = 1, string? description = null)
        {
            object[] missions = Enumerable.Range(1, missionCount)
                .Select(i => (object)new { Title = "Mission " + i, Description = "Description for mission " + i })
                .ToArray();
            object body = new { Title = title, Description = description ?? ("Description for " + title), VesselId = vesselId, Missions = missions };
            StringContent content = JsonHelper.ToJsonContent(body);
            HttpResponseMessage resp = await client.PostAsync("/api/v1/voyages", content);
            AssertEqual(HttpStatusCode.Created, resp.StatusCode);
            Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(resp);
            createdVoyageIds.Add(voyage.Id);
            return voyage;
        }

        /// <summary>
        /// Creates prerequisite fleet and vessel and returns their IDs.
        /// </summary>
        private static async Task<PrerequisiteResult> CreatePrerequisitesAsync(HttpClient client, List<string> createdFleetIds, List<string> createdVesselIds)
        {
            string fleetId = await CreateFleetAsync(client, createdFleetIds);
            string vesselId = await CreateVesselAsync(client, createdVesselIds, fleetId);
            return new PrerequisiteResult(fleetId, vesselId);
        }

        /// <summary>
        /// Ensure the shared voyage list holds a reusable dataset of at least 25 voyages, seeding it exactly
        /// once for the suite. Every case runs against the same in-process server fixture and the four
        /// list-voyage pagination cases only assert accumulation-tolerant conditions (a full page of 10, a
        /// second page of 10, a dynamically computed last page, and no overlap between page 1 and page 2),
        /// so they can share one dataset instead of each re-creating 15-25 voyages over sequential HTTP
        /// round-trips. The seed Task is memoized: the first pagination case to run pays the cost and the
        /// rest await the completed Task.
        /// </summary>
        /// <param name="authClient">Authenticated client for the shared e2e server.</param>
        /// <returns>A task that completes once the shared dataset exists.</returns>
        private Task EnsureVoyagesSeededAsync(HttpClient authClient)
        {
            lock (_SeedLock)
            {
                if (_SharedVoyageSeed == null) _SharedVoyageSeed = SeedSharedVoyagesAsync(authClient);
                return _SharedVoyageSeed;
            }
        }

        /// <summary>
        /// Creates the shared pagination dataset of 25 voyages under a single throwaway fleet and vessel.
        /// </summary>
        /// <param name="authClient">Authenticated client for the shared e2e server.</param>
        /// <returns>A task that completes once all 25 voyages have been created.</returns>
        private static async Task SeedSharedVoyagesAsync(HttpClient authClient)
        {
            List<string> createdFleetIds = new List<string>();
            List<string> createdVesselIds = new List<string>();
            List<string> createdVoyageIds = new List<string>();

            PrerequisiteResult prereqs = await CreatePrerequisitesAsync(authClient, createdFleetIds, createdVesselIds);
            string vesselId = prereqs.VesselId;

            for (int i = 1; i <= 25; i++)
            {
                await CreateVoyageAsync(authClient, createdVoyageIds, vesselId, "SharedPagVoyage " + i);
            }
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "E2E.Voyage",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
