namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
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
    /// End-to-end Vessel API descriptors covering CRUD, list, pagination, ordering, fleet filtering,
    /// and enumeration against a live in-process Armada server provided by <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class VesselSuite : IArmadaTestSuite
    {
        #region Private-Members

        private readonly object _SeedLock = new object();
        private Task<string>? _SharedVesselFleet;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Vessel API end-to-end suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            #region CRUD - Create

            cases.Add(CaseAsync("create_vessel_with_all_fields_returns_201_with_correct_properties", "Create Vessel With All Fields Returns 201 With Correct Properties", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "CreateAllFieldsFleet");

                StringContent content = JsonHelper.ToJsonContent(new
                {
                    Name = "FullVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/full",
                    LocalPath = "/home/user/repos/full",
                    WorkingDirectory = "/home/user/repos/full/src",
                    DefaultBranch = "develop",
                    Active = true
                });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                createdVesselIds.Add(vessel.Id);

                AssertStartsWith("vsl_", vessel.Id);
                AssertStartsWith("FullVessel", vessel.Name);
                AssertEqual(fleetId, vessel.FleetId);
                AssertEqual("https://github.com/test/full", vessel.RepoUrl);
                AssertEqual("/home/user/repos/full", vessel.LocalPath);
                AssertEqual("/home/user/repos/full/src", vessel.WorkingDirectory);
                AssertEqual("develop", vessel.DefaultBranch);
                AssertTrue(vessel.Active);
                Assert(vessel.CreatedUtc != default, "CreatedUtc should be set");
                Assert(vessel.LastUpdateUtc != default, "LastUpdateUtc should be set");
            }));

            cases.Add(CaseAsync("create_vessel_with_minimal_fields_returns_201", "Create Vessel With Minimal Fields Returns 201", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "MinimalFleet");

                StringContent content = JsonHelper.ToJsonContent(new { Name = "MinimalVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/minimal" });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                createdVesselIds.Add(vessel.Id);

                AssertStartsWith("vsl_", vessel.Id);
                AssertStartsWith("MinimalVessel", vessel.Name);
                AssertEqual(fleetId, vessel.FleetId);
                AssertEqual("main", vessel.DefaultBranch);
                AssertTrue(vessel.Active);
            }));

            cases.Add(CaseAsync("create_vessel_id_has_vsl_prefix", "Create Vessel Id Has Vsl Prefix", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                Vessel vessel = await CreateVesselAsync(authClient, createdVesselIds, "PrefixTest", fleetId: fleetId);

                AssertStartsWith("vsl_", vessel.Id);
            }));

            cases.Add(CaseAsync("create_vessel_generates_unique_ids", "Create Vessel Generates Unique Ids", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string id1 = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Vessel1", fleetId: fleetId);
                string id2 = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Vessel2", fleetId: fleetId);

                AssertNotEqual(id1, id2);
            }));

            cases.Add(CaseAsync("create_vessel_sets_created_utc_and_last_update_utc", "Create Vessel Sets CreatedUtc And LastUpdateUtc", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                DateTime beforeCreate = DateTime.UtcNow.AddSeconds(-1);

                Vessel vessel = await CreateVesselAsync(authClient, createdVesselIds, "TimestampVessel", fleetId: fleetId);

                DateTime createdUtc = vessel.CreatedUtc;
                DateTime lastUpdateUtc = vessel.LastUpdateUtc;

                Assert(createdUtc.ToUniversalTime() >= beforeCreate, "CreatedUtc " + createdUtc + " should be >= " + beforeCreate);
                Assert(lastUpdateUtc.ToUniversalTime() >= beforeCreate, "LastUpdateUtc " + lastUpdateUtc + " should be >= " + beforeCreate);
            }));

            cases.Add(CaseAsync("create_vessel_default_branch_defaults_to_main", "Create Vessel DefaultBranch Defaults To Main", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);

                StringContent content = JsonHelper.ToJsonContent(new { Name = "DefaultBranchVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/default-branch" });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels", content);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                createdVesselIds.Add(vessel.Id);

                AssertEqual("main", vessel.DefaultBranch);
            }));

            cases.Add(CaseAsync("create_vessel_active_defaults_to_true", "Create Vessel Active Defaults To True", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);

                StringContent content = JsonHelper.ToJsonContent(new { Name = "ActiveDefaultVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/active-default" });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels", content);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                createdVesselIds.Add(vessel.Id);

                AssertTrue(vessel.Active);
            }));

            cases.Add(CaseAsync("create_vessel_github_token_override_does_not_leak_and_sets_has_override", "Create Vessel GitHubTokenOverride DoesNotLeakAndSetsHasOverride", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "GitHubOverrideFleet");
                string token = "ghp_create_override_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                StringContent content = JsonHelper.ToJsonContent(new
                {
                    Name = "GitHubOverrideVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/github-override",
                    GitHubTokenOverride = token
                });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string responseText = await response.Content.ReadAsStringAsync();
                AssertFalse(responseText.Contains(token, StringComparison.Ordinal));
                AssertFalse(JsonShape.HasPropertyAnywhere(responseText, "GitHubTokenOverride"), "no GitHubTokenOverride property in any casing");

                Vessel vessel = JsonHelper.Deserialize<Vessel>(responseText);
                createdVesselIds.Add(vessel.Id);
                AssertTrue(vessel.HasGitHubTokenOverride);

                HttpResponseMessage getResponse = await authClient.GetAsync("/api/v1/vessels/" + vessel.Id);
                string getText = await getResponse.Content.ReadAsStringAsync();
                AssertFalse(getText.Contains(token, StringComparison.Ordinal));
                AssertFalse(JsonShape.HasPropertyAnywhere(getText, "GitHubTokenOverride"), "no GitHubTokenOverride property in any casing");
                Vessel fetched = JsonHelper.Deserialize<Vessel>(getText);
                AssertTrue(fetched.HasGitHubTokenOverride);
            }));

            #endregion

            #region CRUD - Read

            cases.Add(CaseAsync("get_vessel_exists_returns_correct_data", "Get Vessel Exists Returns Correct Data", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "GetFleet");

                StringContent content = JsonHelper.ToJsonContent(new
                {
                    Name = "GetVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/get",
                    DefaultBranch = "develop"
                });

                HttpResponseMessage createResp = await authClient.PostAsync("/api/v1/vessels", content);
                Vessel created = await JsonHelper.DeserializeAsync<Vessel>(createResp);
                string vesselId = created.Id;
                createdVesselIds.Add(vesselId);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels/" + vesselId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                AssertEqual(vesselId, vessel.Id);
                AssertStartsWith("GetVessel", vessel.Name);
                AssertEqual(fleetId, vessel.FleetId);
                AssertEqual("https://github.com/test/get", vessel.RepoUrl);
                AssertEqual("develop", vessel.DefaultBranch);
            }));

            cases.Add(CaseAsync("get_vessel_not_found_returns_error", "Get Vessel Not Found Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels/vsl_nonexistent");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Should have Error or Message property");
            }));

            cases.Add(CaseAsync("get_vessel_invalid_id_returns_error", "Get Vessel Invalid Id Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels/invalid_id_format");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Should have Error or Message property");
            }));

            #endregion

            #region CRUD - Update

            cases.Add(CaseAsync("update_vessel_name_returns_updated_name", "Update Vessel Name Returns Updated Name", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "OriginalName", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "UpdatedName", FleetId = fleetId, RepoUrl = "https://github.com/test/originalname" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                AssertEqual("UpdatedName", vessel.Name);
            }));

            cases.Add(CaseAsync("update_vessel_repo_url_returns_updated_repo_url", "Update Vessel RepoUrl Returns Updated RepoUrl", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "RepoUrlVessel", fleetId: fleetId, repoUrl: "https://github.com/test/old");

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "RepoUrlVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/new" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                AssertEqual("https://github.com/test/new", vessel.RepoUrl);
            }));

            cases.Add(CaseAsync("update_vessel_default_branch_returns_updated_branch", "Update Vessel DefaultBranch Returns Updated Branch", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "BranchVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "BranchVessel", FleetId = fleetId, RepoUrl = "https://github.com/test/branchvessel", DefaultBranch = "release" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                AssertEqual("release", vessel.DefaultBranch);
            }));

            cases.Add(CaseAsync("update_vessel_multiple_fields_all_updated", "Update Vessel Multiple Fields All Updated", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "MultiUpdateVessel", fleetId: fleetId, repoUrl: "https://github.com/test/orig");

                string renamedName = "RenamedVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string renamedUrl = "https://github.com/test/renamed-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                StringContent updateContent = JsonHelper.ToJsonContent(new
                {
                    Name = renamedName,
                    FleetId = fleetId,
                    RepoUrl = renamedUrl,
                    DefaultBranch = "staging"
                });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                AssertEqual(renamedName, vessel.Name);
                AssertEqual(renamedUrl, vessel.RepoUrl);
                AssertEqual("staging", vessel.DefaultBranch);
            }));

            cases.Add(CaseAsync("update_vessel_preserves_id_and_fleet_id", "Update Vessel Preserves Id And FleetId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "PreserveIdVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "StillSameId", FleetId = fleetId, RepoUrl = "https://github.com/test/preserveidvessel" });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                AssertEqual(vesselId, vessel.Id);
                AssertEqual(fleetId, vessel.FleetId);
            }));

            cases.Add(CaseAsync("update_vessel_verify_via_get", "Update Vessel Verify Via Get", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "VerifyUpdateVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new { Name = "VerifiedUpdate", FleetId = fleetId, RepoUrl = "https://github.com/test/verifyupdatevessel", DefaultBranch = "feature" });
                await authClient.PutAsync("/api/v1/vessels/" + vesselId, updateContent);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/vessels/" + vesselId);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(getResp);

                AssertEqual("VerifiedUpdate", vessel.Name);
                AssertEqual("feature", vessel.DefaultBranch);
            }));

            cases.Add(CaseAsync("update_vessel_omitting_github_token_override_preserves_existing_override", "Update Vessel OmittingGitHubTokenOverride PreservesExistingOverride", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "PreserveGitHubOverrideFleet");
                string token = "ghp_preserve_" + Guid.NewGuid().ToString("N").Substring(0, 10);

                HttpResponseMessage createResponse = await authClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new
                {
                    Name = "PreserveGitHubOverride",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/preserve-override",
                    GitHubTokenOverride = token
                }));
                Vessel created = await JsonHelper.DeserializeAsync<Vessel>(createResponse);
                createdVesselIds.Add(created.Id);
                AssertTrue(created.HasGitHubTokenOverride);

                HttpResponseMessage updateResponse = await authClient.PutAsync("/api/v1/vessels/" + created.Id, JsonHelper.ToJsonContent(new
                {
                    Name = "PreserveGitHubOverrideUpdated",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/preserve-override"
                }));
                AssertEqual(HttpStatusCode.OK, updateResponse.StatusCode);

                Vessel updated = await JsonHelper.DeserializeAsync<Vessel>(updateResponse);
                AssertTrue(updated.HasGitHubTokenOverride);

                HttpResponseMessage getResponse = await authClient.GetAsync("/api/v1/vessels/" + created.Id);
                Vessel fetched = await JsonHelper.DeserializeAsync<Vessel>(getResponse);
                AssertTrue(fetched.HasGitHubTokenOverride);
            }));

            cases.Add(CaseAsync("update_vessel_empty_github_token_override_clears_override", "Update Vessel EmptyGitHubTokenOverride ClearsOverride", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "ClearGitHubOverrideFleet");
                string token = "ghp_clear_" + Guid.NewGuid().ToString("N").Substring(0, 10);

                HttpResponseMessage createResponse = await authClient.PostAsync("/api/v1/vessels", JsonHelper.ToJsonContent(new
                {
                    Name = "ClearGitHubOverride",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/clear-override",
                    GitHubTokenOverride = token
                }));
                Vessel created = await JsonHelper.DeserializeAsync<Vessel>(createResponse);
                createdVesselIds.Add(created.Id);

                HttpResponseMessage updateResponse = await authClient.PutAsync("/api/v1/vessels/" + created.Id, JsonHelper.ToJsonContent(new
                {
                    Name = "ClearGitHubOverride",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/clear-override",
                    GitHubTokenOverride = ""
                }));
                AssertEqual(HttpStatusCode.OK, updateResponse.StatusCode);
                Vessel updated = await JsonHelper.DeserializeAsync<Vessel>(updateResponse);
                AssertFalse(updated.HasGitHubTokenOverride);

                HttpResponseMessage getResponse = await authClient.GetAsync("/api/v1/vessels/" + created.Id);
                Vessel fetched = await JsonHelper.DeserializeAsync<Vessel>(getResponse);
                AssertFalse(fetched.HasGitHubTokenOverride);
            }));

            #endregion

            #region CRUD - Delete

            cases.Add(CaseAsync("delete_vessel_exists_returns_204", "Delete Vessel Exists Returns 204", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "ToDelete", fleetId: fleetId);

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/vessels/" + vesselId);
                AssertEqual(HttpStatusCode.NoContent, response.StatusCode);
                createdVesselIds.Remove(vesselId);
            }));

            cases.Add(CaseAsync("delete_vessel_not_found_returns_error", "Delete Vessel Not Found Returns Error", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.DeleteAsync("/api/v1/vessels/vsl_nonexistent");
                await E2eApiErrorAssert.ExpectAsync(response, HttpStatusCode.NotFound, "Deleting nonexistent vessel returns a typed 404");
            }));

            cases.Add(CaseAsync("get_vessel_after_delete_returns_not_found", "Get Vessel After Delete Returns Not Found", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "DeleteThenGet", fleetId: fleetId);

                HttpResponseMessage deleteResp = await authClient.DeleteAsync("/api/v1/vessels/" + vesselId);
                AssertEqual(HttpStatusCode.NoContent, deleteResp.StatusCode);
                createdVesselIds.Remove(vesselId);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/vessels/" + vesselId);
                await E2eApiErrorAssert.ExpectAsync(getResp, HttpStatusCode.NotFound, "Should have Error or Message property");
            }));

            cases.Add(CaseAsync("delete_vessel_does_not_affect_other_vessels", "Delete Vessel Does Not Affect Other Vessels", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                string vesselId1 = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "KeepMe", fleetId: fleetId);
                string vesselId2 = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "DeleteMe", fleetId: fleetId);

                await authClient.DeleteAsync("/api/v1/vessels/" + vesselId2);
                createdVesselIds.Remove(vesselId2);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/vessels/" + vesselId1);
                AssertEqual(HttpStatusCode.OK, getResp.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(getResp);
                AssertStartsWith("KeepMe", vessel.Name);
            }));

            #endregion

            #region List - Empty and Basic

            cases.Add(CaseAsync("list_vessels_empty_returns_empty_array_with_correct_envelope", "List Vessels Empty Returns Empty Array With Correct Envelope", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                Assert(result.Objects != null, "Objects should not be null");
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("list_vessels_after_create_returns_vessel", "List Vessels After Create Returns Vessel", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "ListAfterCreate", fleetId: fleetId, repoUrl: "https://github.com/test/list");

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);
                Assert(result.Objects.Count >= 1, "Should have at least 1 object");
                Assert(result.TotalRecords >= 1, "Should have at least 1 total record");
            }));

            #endregion

            #region List - Pagination

            cases.Add(CaseAsync("list_vessels_25_items_pagesize_10_page_1_has_10_items", "List Vessels 25 Items PageSize 10 Page 1 Has 10 Items", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fleetId = await EnsureVesselsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels?pageSize=10&pageNumber=1&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(25, result.TotalRecords);
                AssertEqual(3, result.TotalPages);
                AssertEqual(1, result.PageNumber);
                AssertEqual(10, result.PageSize);
            }));

            cases.Add(CaseAsync("list_vessels_25_items_pagesize_10_page_2_has_10_items", "List Vessels 25 Items PageSize 10 Page 2 Has 10 Items", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fleetId = await EnsureVesselsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels?pageSize=10&pageNumber=2&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(10, result.Objects.Count);
                AssertEqual(2, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_vessels_25_items_pagesize_10_page_3_has_5_items", "List Vessels 25 Items PageSize 10 Page 3 Has 5 Items", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fleetId = await EnsureVesselsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels?pageSize=10&pageNumber=3&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(5, result.Objects.Count);
                AssertEqual(3, result.PageNumber);
            }));

            cases.Add(CaseAsync("list_vessels_25_items_verify_first_record_page_1_and_last_record_page_3", "List Vessels 25 Items Verify First Record Page 1 And Last Record Page 3", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fleetId = await EnsureVesselsSeededAsync(authClient);

                HttpResponseMessage page1Resp = await authClient.GetAsync(
                    "/api/v1/vessels?pageSize=10&pageNumber=1&order=CreatedAscending&fleetId=" + fleetId);
                EnumerationResult<Vessel> page1Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page1Resp);
                string firstItemName = page1Result.Objects[0].Name;

                HttpResponseMessage page3Resp = await authClient.GetAsync(
                    "/api/v1/vessels?pageSize=10&pageNumber=3&order=CreatedAscending&fleetId=" + fleetId);
                EnumerationResult<Vessel> page3Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page3Resp);
                string lastItemName = page3Result.Objects[page3Result.Objects.Count - 1].Name;

                AssertStartsWith("SharedPagVessel_00", firstItemName);
                AssertStartsWith("SharedPagVessel_24", lastItemName);
            }));

            cases.Add(CaseAsync("list_vessels_page_beyond_last_page_returns_empty_objects", "List Vessels Page Beyond Last Page Returns Empty Objects", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string fleetId = await EnsureVesselsSeededAsync(authClient);

                HttpResponseMessage response = await authClient.GetAsync(
                    "/api/v1/vessels?pageSize=10&pageNumber=99&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(0, result.Objects.Count);
            }));

            #endregion

            #region List - Ordering

            cases.Add(CaseAsync("list_vessels_order_created_ascending_oldest_first", "List Vessels Order Created Ascending Oldest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "OrderAscFleet");
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "AscFirst", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "AscSecond", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "AscThird", fleetId: fleetId);

                HttpResponseMessage response = await authClient.GetAsync(
                    "/api/v1/vessels?order=CreatedAscending&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                Assert(result.Objects.Count >= 3, "Should have at least 3 objects");
                string firstName = result.Objects[0].Name;
                string lastName = result.Objects[result.Objects.Count - 1].Name;
                AssertStartsWith("AscFirst", firstName);
                AssertStartsWith("AscThird", lastName);
            }));

            cases.Add(CaseAsync("list_vessels_order_created_descending_newest_first", "List Vessels Order Created Descending Newest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "OrderDescFleet");
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "DescFirst", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "DescSecond", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "DescThird", fleetId: fleetId);

                HttpResponseMessage response = await authClient.GetAsync(
                    "/api/v1/vessels?order=CreatedDescending&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                Assert(result.Objects.Count >= 3, "Should have at least 3 objects");
                string firstName = result.Objects[0].Name;
                string lastName = result.Objects[result.Objects.Count - 1].Name;
                AssertStartsWith("DescThird", firstName);
                AssertStartsWith("DescFirst", lastName);
            }));

            cases.Add(CaseAsync("list_vessels_order_created_ascending_timestamps_are_ascending", "List Vessels Order Created Ascending Timestamps Are Ascending", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "TimestampAscFleet");
                for (int i = 0; i < 5; i++)
                {
                    await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "TsAsc_" + i, fleetId: fleetId);
                }

                HttpResponseMessage response = await authClient.GetAsync(
                    "/api/v1/vessels?order=CreatedAscending&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                DateTime previous = DateTime.MinValue;
                foreach (Vessel v in result.Objects)
                {
                    DateTime created = v.CreatedUtc;
                    Assert(created >= previous, "Timestamps should be in ascending order");
                    previous = created;
                }
            }));

            cases.Add(CaseAsync("list_vessels_order_created_descending_timestamps_are_descending", "List Vessels Order Created Descending Timestamps Are Descending", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "TimestampDescFleet");
                for (int i = 0; i < 5; i++)
                {
                    await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "TsDesc_" + i, fleetId: fleetId);
                }

                HttpResponseMessage response = await authClient.GetAsync(
                    "/api/v1/vessels?order=CreatedDescending&fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                DateTime previous = DateTime.MaxValue;
                foreach (Vessel v in result.Objects)
                {
                    DateTime created = v.CreatedUtc;
                    Assert(created <= previous, "Timestamps should be in descending order");
                    previous = created;
                }
            }));

            #endregion

            #region List - Filter by FleetId

            cases.Add(CaseAsync("list_vessels_filter_by_fleet_id_returns_only_matching_vessels", "List Vessels Filter By FleetId Returns Only Matching Vessels", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId1 = await CreateFleetAsync(authClient, createdFleetIds, "FilterFleetA");
                string fleetId2 = await CreateFleetAsync(authClient, createdFleetIds, "FilterFleetB");

                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "VesselInA", fleetId: fleetId1);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "VesselInB", fleetId: fleetId2);

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels?fleetId=" + fleetId1);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(1, result.Objects.Count);
                AssertStartsWith("VesselInA", result.Objects[0].Name);
            }));

            cases.Add(CaseAsync("list_vessels_filter_by_fleet_id_multiple_fleets_correct_separation", "List Vessels Filter By FleetId Multiple Fleets Correct Separation", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetIdAlpha = await CreateFleetAsync(authClient, createdFleetIds, "AlphaFleet");
                string fleetIdBeta = await CreateFleetAsync(authClient, createdFleetIds, "BetaFleet");

                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Alpha1", fleetId: fleetIdAlpha);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Alpha2", fleetId: fleetIdAlpha);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Alpha3", fleetId: fleetIdAlpha);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Beta1", fleetId: fleetIdBeta);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Beta2", fleetId: fleetIdBeta);

                HttpResponseMessage alphaResp = await authClient.GetAsync("/api/v1/vessels?fleetId=" + fleetIdAlpha);
                EnumerationResult<Vessel> alphaResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(alphaResp);
                AssertEqual(3, alphaResult.Objects.Count);
                AssertEqual(3, alphaResult.TotalRecords);

                HttpResponseMessage betaResp = await authClient.GetAsync("/api/v1/vessels?fleetId=" + fleetIdBeta);
                EnumerationResult<Vessel> betaResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(betaResp);
                AssertEqual(2, betaResult.Objects.Count);
                AssertEqual(2, betaResult.TotalRecords);
            }));

            cases.Add(CaseAsync("list_vessels_filter_by_fleet_id_all_vessels_have_correct_fleet_id", "List Vessels Filter By FleetId All Vessels Have Correct FleetId", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "ConsistentFleet");
                for (int i = 0; i < 5; i++)
                {
                    await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Consistent_" + i, fleetId: fleetId);
                }

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels?fleetId=" + fleetId);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                foreach (Vessel vessel in result.Objects)
                {
                    AssertEqual(fleetId, vessel.FleetId);
                }
            }));

            cases.Add(CaseAsync("list_vessels_filter_by_nonexistent_fleet_id_returns_empty", "List Vessels Filter By Nonexistent FleetId Returns Empty", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage response = await authClient.GetAsync("/api/v1/vessels?fleetId=flt_doesnotexist");
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(0, result.Objects.Count);
                AssertEqual(0, result.TotalRecords);
            }));

            #endregion

            #region Enumerate (POST)

            cases.Add(CaseAsync("enumerate_default_query_returns_all_vessels", "Enumerate Default Query Returns All Vessels", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "EnumAllFleet");
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumAll1", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumAll2", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumAll3", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10 });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                Assert(result.Objects.Count >= 3, "Should have at least 3 objects");
                Assert(result.TotalRecords >= 3, "Should have at least 3 total records");
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("enumerate_with_pagesize_and_pagenumber", "Enumerate With PageSize And PageNumber", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "EnumPagFleet");
                for (int i = 0; i < 15; i++)
                {
                    await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumPag_" + i.ToString("D2"), fleetId: fleetId);
                }

                StringContent page1Content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5, FleetId = fleetId });
                HttpResponseMessage page1Resp = await authClient.PostAsync("/api/v1/vessels/enumerate", page1Content);
                EnumerationResult<Vessel> page1Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page1Resp);

                AssertEqual(5, page1Result.Objects.Count);
                AssertEqual(15, page1Result.TotalRecords);
                AssertEqual(3, page1Result.TotalPages);
                AssertEqual(1, page1Result.PageNumber);

                StringContent page2Content = JsonHelper.ToJsonContent(new { PageNumber = 2, PageSize = 5, FleetId = fleetId });
                HttpResponseMessage page2Resp = await authClient.PostAsync("/api/v1/vessels/enumerate", page2Content);
                EnumerationResult<Vessel> page2Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page2Resp);

                AssertEqual(5, page2Result.Objects.Count);
                AssertEqual(2, page2Result.PageNumber);

                StringContent page3Content = JsonHelper.ToJsonContent(new { PageNumber = 3, PageSize = 5, FleetId = fleetId });
                HttpResponseMessage page3Resp = await authClient.PostAsync("/api/v1/vessels/enumerate", page3Content);
                EnumerationResult<Vessel> page3Result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(page3Resp);

                AssertEqual(5, page3Result.Objects.Count);
                AssertEqual(3, page3Result.PageNumber);
            }));

            cases.Add(CaseAsync("enumerate_with_fleet_id_filter_returns_only_matching_vessels", "Enumerate With FleetId Filter Returns Only Matching Vessels", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId1 = await CreateFleetAsync(authClient, createdFleetIds, "EnumFilterFleet1");
                string fleetId2 = await CreateFleetAsync(authClient, createdFleetIds, "EnumFilterFleet2");

                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumFilter_A1", fleetId: fleetId1);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumFilter_A2", fleetId: fleetId1);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumFilter_B1", fleetId: fleetId2);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, FleetId = fleetId1 });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(2, result.Objects.Count);
                AssertEqual(2, result.TotalRecords);

                foreach (Vessel vessel in result.Objects)
                {
                    AssertEqual(fleetId1, vessel.FleetId);
                }
            }));

            cases.Add(CaseAsync("enumerate_order_created_ascending_oldest_first", "Enumerate Order Created Ascending Oldest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "EnumOrderAscFleet");
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumOrdAsc_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumOrdAsc_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumOrdAsc_Third", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedAscending", FleetId = fleetId });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertStartsWith("EnumOrdAsc_First", result.Objects[0].Name);
                AssertStartsWith("EnumOrdAsc_Third", result.Objects[result.Objects.Count - 1].Name);
            }));

            cases.Add(CaseAsync("enumerate_order_created_descending_newest_first", "Enumerate Order Created Descending Newest First", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "EnumOrderDescFleet");
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumOrdDesc_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumOrdDesc_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumOrdDesc_Third", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending", FleetId = fleetId });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertStartsWith("EnumOrdDesc_Third", result.Objects[0].Name);
                AssertStartsWith("EnumOrdDesc_First", result.Objects[result.Objects.Count - 1].Name);
            }));

            cases.Add(CaseAsync("enumerate_order_created_ascending_verify_created_utc_order", "Enumerate Order Created Ascending Verify CreatedUtc Order", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "EnumCreatedAscFleet");
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "CA_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "CA_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "CA_Third", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedAscending", FleetId = fleetId });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(3, result.Objects.Count);
                DateTime previous = DateTime.MinValue;
                foreach (Vessel v in result.Objects)
                {
                    DateTime created = v.CreatedUtc;
                    Assert(created >= previous, "CreatedUtc should be in ascending order");
                    previous = created;
                }
                AssertStartsWith("CA_First", result.Objects[0].Name);
                AssertStartsWith("CA_Third", result.Objects[2].Name);
            }));

            cases.Add(CaseAsync("enumerate_order_created_descending_verify_created_utc_order", "Enumerate Order Created Descending Verify CreatedUtc Order", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "EnumCreatedDescFleet2");
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "CD_First", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "CD_Second", fleetId: fleetId);
                await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "CD_Third", fleetId: fleetId);

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, Order = "CreatedDescending", FleetId = fleetId });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(3, result.Objects.Count);
                DateTime previous = DateTime.MaxValue;
                foreach (Vessel v in result.Objects)
                {
                    DateTime created = v.CreatedUtc;
                    Assert(created <= previous, "CreatedUtc should be in descending order");
                    previous = created;
                }
                AssertStartsWith("CD_Third", result.Objects[0].Name);
                AssertStartsWith("CD_First", result.Objects[2].Name);
            }));

            #endregion

            #region Enumerate - Pagination Consistency with GET

            cases.Add(CaseAsync("enumerate_pagination_consistent_with_get", "Enumerate Pagination Consistent With Get", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "ConsistencyFleet");
                for (int i = 0; i < 12; i++)
                {
                    await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "Consist_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage getResp = await authClient.GetAsync(
                    "/api/v1/vessels?pageSize=5&pageNumber=1&order=CreatedAscending&fleetId=" + fleetId);
                EnumerationResult<Vessel> getResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(getResp);

                StringContent enumContent = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 5, Order = "CreatedAscending", FleetId = fleetId });
                HttpResponseMessage enumResp = await authClient.PostAsync("/api/v1/vessels/enumerate", enumContent);
                EnumerationResult<Vessel> enumResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(enumResp);

                AssertEqual(getResult.TotalRecords, enumResult.TotalRecords);
                AssertEqual(getResult.Objects.Count, enumResult.Objects.Count);

                for (int i = 0; i < getResult.Objects.Count; i++)
                {
                    AssertEqual(getResult.Objects[i].Id, enumResult.Objects[i].Id);
                }
            }));

            cases.Add(CaseAsync("enumerate_page_2_consistent_with_get_page_2", "Enumerate Page 2 Consistent With Get Page 2", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "ConsistP2Fleet");
                for (int i = 0; i < 12; i++)
                {
                    await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "ConsistP2_" + i.ToString("D2"), fleetId: fleetId);
                }

                HttpResponseMessage getResp = await authClient.GetAsync(
                    "/api/v1/vessels?pageSize=5&pageNumber=2&order=CreatedAscending&fleetId=" + fleetId);
                EnumerationResult<Vessel> getResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(getResp);

                StringContent enumContent = JsonHelper.ToJsonContent(new { PageNumber = 2, PageSize = 5, Order = "CreatedAscending", FleetId = fleetId });
                HttpResponseMessage enumResp = await authClient.PostAsync("/api/v1/vessels/enumerate", enumContent);
                EnumerationResult<Vessel> enumResult = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(enumResp);

                AssertEqual(getResult.Objects.Count, enumResult.Objects.Count);
                for (int i = 0; i < getResult.Objects.Count; i++)
                {
                    AssertEqual(getResult.Objects[i].Id, enumResult.Objects[i].Id);
                }
            }));

            #endregion

            #region CRUD - ProjectContext and StyleGuide

            cases.Add(CaseAsync("create_vessel_with_project_context_and_style_guide_returns_both_fields", "Create Vessel With ProjectContext And StyleGuide Returns Both Fields", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "ContextFleet");

                StringContent content = JsonHelper.ToJsonContent(new
                {
                    Name = "ContextVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/context",
                    ProjectContext = "A .NET 8 web API with PostgreSQL.",
                    StyleGuide = "Use PascalCase for public members."
                });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);
                createdVesselIds.Add(vessel.Id);

                AssertEqual("A .NET 8 web API with PostgreSQL.", vessel.ProjectContext);
                AssertEqual("Use PascalCase for public members.", vessel.StyleGuide);
            }));

            cases.Add(CaseAsync("create_vessel_without_project_context_and_style_guide_returns_nulls", "Create Vessel Without ProjectContext And StyleGuide Returns Nulls", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "NullContextFleet");
                Vessel vessel = await CreateVesselAsync(authClient, createdVesselIds, "NullContextVessel", fleetId: fleetId);
                createdVesselIds.Add(vessel.Id);

                AssertTrue(vessel.ProjectContext == null, "ProjectContext should be null or absent");
                AssertTrue(vessel.StyleGuide == null, "StyleGuide should be null or absent");
            }));

            cases.Add(CaseAsync("update_vessel_project_context_and_style_guide_returns_updated_values", "Update Vessel ProjectContext And StyleGuide Returns Updated Values", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "UpdateContextFleet");
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "UpdateContextVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new
                {
                    Name = "UpdateContextVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/updatecontextvessel",
                    ProjectContext = "Updated project context",
                    StyleGuide = "Updated style guide"
                });
                HttpResponseMessage response = await authClient.PutAsync("/api/v1/vessels/" + vesselId, updateContent);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(response);

                AssertEqual("Updated project context", vessel.ProjectContext);
                AssertEqual("Updated style guide", vessel.StyleGuide);
            }));

            cases.Add(CaseAsync("update_vessel_project_context_and_style_guide_verify_via_get", "Update Vessel ProjectContext And StyleGuide Verify Via Get", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "GetContextFleet");
                string vesselId = await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "GetContextVessel", fleetId: fleetId);

                StringContent updateContent = JsonHelper.ToJsonContent(new
                {
                    Name = "GetContextVessel",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/getcontextvessel",
                    ProjectContext = "Persisted context",
                    StyleGuide = "Persisted style"
                });
                await authClient.PutAsync("/api/v1/vessels/" + vesselId, updateContent);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/vessels/" + vesselId);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(getResp);

                AssertEqual("Persisted context", vessel.ProjectContext);
                AssertEqual("Persisted style", vessel.StyleGuide);
            }));

            cases.Add(CaseAsync("update_vessel_clear_project_context_and_style_guide_to_null", "Update Vessel Clear ProjectContext And StyleGuide To Null", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "ClearContextFleet");

                StringContent createContent = JsonHelper.ToJsonContent(new
                {
                    Name = "ClearContextVessel-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/clearcontext",
                    ProjectContext = "To be cleared",
                    StyleGuide = "To be cleared"
                });
                HttpResponseMessage createResp = await authClient.PostAsync("/api/v1/vessels", createContent);
                Vessel created = await JsonHelper.DeserializeAsync<Vessel>(createResp);
                string vesselId = created.Id;
                createdVesselIds.Add(vesselId);

                StringContent clearContent = JsonHelper.ToJsonContent(new
                {
                    Name = "ClearContextVessel-cleared",
                    FleetId = fleetId,
                    RepoUrl = "https://github.com/test/clearcontext"
                });
                await authClient.PutAsync("/api/v1/vessels/" + vesselId, clearContent);

                HttpResponseMessage getResp = await authClient.GetAsync("/api/v1/vessels/" + vesselId);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(getResp);

                AssertTrue(vessel.ProjectContext == null, "ProjectContext should be null after clearing");
                AssertTrue(vessel.StyleGuide == null, "StyleGuide should be null after clearing");
            }));

            #endregion

            #region Enumerate - Edge Cases

            cases.Add(CaseAsync("enumerate_empty_database_returns_empty_result", "Enumerate Empty Database Returns Empty Result", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10 });

                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);
                AssertTrue(result.Success);
            }));

            cases.Add(CaseAsync("enumerate_page_beyond_last_page_returns_empty_objects", "Enumerate Page Beyond Last Page Returns Empty Objects", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                List<string> createdFleetIds = new List<string>();
                List<string> createdVesselIds = new List<string>();

                string fleetId = await CreateFleetAsync(authClient, createdFleetIds, "EnumBeyondFleet");
                for (int i = 0; i < 3; i++)
                {
                    await CreateVesselAndReturnIdAsync(authClient, createdVesselIds, "EnumBeyond_" + i, fleetId: fleetId);
                }

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 99, PageSize = 10, FleetId = fleetId });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(0, result.Objects.Count);
            }));

            cases.Add(CaseAsync("enumerate_with_nonexistent_fleet_id_returns_empty", "Enumerate With Nonexistent FleetId Returns Empty", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                StringContent content = JsonHelper.ToJsonContent(new { PageNumber = 1, PageSize = 10, FleetId = "flt_doesnotexist" });
                HttpResponseMessage response = await authClient.PostAsync("/api/v1/vessels/enumerate", content);
                EnumerationResult<Vessel> result = await JsonHelper.DeserializeAsync<EnumerationResult<Vessel>>(response);

                AssertEqual(0, result.Objects.Count);
                AssertEqual(0, result.TotalRecords);
            }));

            #endregion

            return new TestSuiteDescriptor(
                suiteId: "E2E.Vessel",
                displayName: "Vessel API Tests",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Creates a fleet and returns its ID.
        /// </summary>
        /// <summary>
        /// Ensure a shared fleet holding exactly 25 vessels exists, seeding it once for the suite, and
        /// return that fleet's ID. The pagination cases scope their queries with <c>?fleetId=</c>, so a
        /// single shared fleet of 25 vessels satisfies their exact-count assertions (25 total, 3 pages,
        /// page 3 has 5) without each case re-creating a fleet and 25 vessels over sequential HTTP
        /// round-trips. The vessels are named <c>SharedPagVessel_00..24</c> and created in order, so a
        /// CreatedAscending query returns _00 first and _24 last. The seed Task is memoized: the first
        /// pagination case pays the cost and the rest await the completed Task. Ordering and fleet-filter
        /// cases keep creating their own fleets, so they are unaffected by this shared dataset.
        /// </summary>
        /// <param name="authClient">Authenticated client for the shared e2e server.</param>
        /// <returns>The ID of the shared fleet holding the 25 seeded vessels.</returns>
        private Task<string> EnsureVesselsSeededAsync(HttpClient authClient)
        {
            lock (_SeedLock)
            {
                if (_SharedVesselFleet == null) _SharedVesselFleet = SeedSharedVesselsAsync(authClient);
                return _SharedVesselFleet;
            }
        }

        private static async Task<string> SeedSharedVesselsAsync(HttpClient authClient)
        {
            List<string> fleetIds = new List<string>();
            List<string> vesselIds = new List<string>();

            string fleetId = await CreateFleetAsync(authClient, fleetIds, "SharedPagFleet").ConfigureAwait(false);
            for (int i = 0; i < 25; i++)
            {
                await CreateVesselAndReturnIdAsync(authClient, vesselIds, "SharedPagVessel_" + i.ToString("D2"), fleetId: fleetId).ConfigureAwait(false);
            }

            return fleetId;
        }

        private static async Task<string> CreateFleetAsync(HttpClient client, List<string> createdFleetIds, string name = "TestFleet")
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName });
            HttpResponseMessage resp = await client.PostAsync("/api/v1/fleets", content);
            resp.EnsureSuccessStatusCode();
            Fleet fleet = await JsonHelper.DeserializeAsync<Fleet>(resp);
            createdFleetIds.Add(fleet.Id);
            return fleet.Id;
        }

        /// <summary>
        /// Creates a vessel and returns the typed Vessel object.
        /// </summary>
        private static async Task<Vessel> CreateVesselAsync(
            HttpClient client,
            List<string> createdVesselIds,
            string name,
            string? fleetId = null,
            string? repoUrl = null,
            string? localPath = null,
            string? workingDirectory = null,
            string? defaultBranch = null,
            bool? active = null)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string effectiveRepoUrl = repoUrl ?? "https://github.com/test/" + uniqueName.ToLowerInvariant().Replace(" ", "-");

            object body;
            if (fleetId != null && localPath != null && workingDirectory != null && defaultBranch != null && active != null)
                body = new { Name = uniqueName, FleetId = fleetId, RepoUrl = effectiveRepoUrl, LocalPath = localPath, WorkingDirectory = workingDirectory, DefaultBranch = defaultBranch, Active = active };
            else if (fleetId != null && defaultBranch != null)
                body = new { Name = uniqueName, FleetId = fleetId, RepoUrl = effectiveRepoUrl, DefaultBranch = defaultBranch };
            else if (fleetId != null)
                body = new { Name = uniqueName, FleetId = fleetId, RepoUrl = effectiveRepoUrl };
            else
                body = new { Name = uniqueName, RepoUrl = effectiveRepoUrl };

            StringContent content = JsonHelper.ToJsonContent(body);
            HttpResponseMessage resp = await client.PostAsync("/api/v1/vessels", content);
            resp.EnsureSuccessStatusCode();
            Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(resp);
            createdVesselIds.Add(vessel.Id);
            return vessel;
        }

        /// <summary>
        /// Creates a vessel and returns only its ID.
        /// </summary>
        private static async Task<string> CreateVesselAndReturnIdAsync(
            HttpClient client,
            List<string> createdVesselIds,
            string name,
            string? fleetId = null,
            string? repoUrl = null)
        {
            Vessel vessel = await CreateVesselAsync(client, createdVesselIds, name, fleetId: fleetId, repoUrl: repoUrl);
            return vessel.Id;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "E2E.Vessel",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
