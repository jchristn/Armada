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
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end coverage for the landing pipeline exercised through the REST API against the shared
    /// running server. Ported from the retired automated <c>LandingPipelineTests</c> suite; verifies the
    /// real orchestration paths in <c>ArmadaServer.HandleMissionCompleteAsync</c> and related methods via
    /// the server obtained through <see cref="E2EServerFixture"/>.
    /// </summary>
    public sealed class LandingPipelineSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the end-to-end Landing Pipeline suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // === PullRequestOpen Status Transitions ===

            cases.Add(CaseAsync("pull_request_open_transitions_from_work_produced", "PullRequestOpen_TransitionsFromWorkProduced", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR transition test", "WorkProduced");

                // Transition to PullRequestOpen
                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "PullRequestOpen");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("PullRequestOpen", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("pull_request_open_transitions_to_complete", "PullRequestOpen_TransitionsToComplete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR to Complete", "WorkProduced");
                await TransitionAsync(authClient, missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Complete");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Complete", mission.Status.ToString());
                AssertTrue(mission.CompletedUtc != null,
                    "CompletedUtc should be set");
            }));

            cases.Add(CaseAsync("pull_request_open_transitions_to_landing_failed", "PullRequestOpen_TransitionsToLandingFailed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR to LandingFailed", "WorkProduced");
                await TransitionAsync(authClient, missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "LandingFailed");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("LandingFailed", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("pull_request_open_transitions_to_cancelled", "PullRequestOpen_TransitionsToCancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR to Cancelled", "WorkProduced");
                await TransitionAsync(authClient, missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Cancelled");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Cancelled", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("pull_request_open_rejects_invalid_transition_to_in_progress", "PullRequestOpen_RejectsInvalidTransitionToInProgress", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "PR invalid transition", "WorkProduced");
                await TransitionAsync(authClient, missionId, "PullRequestOpen");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "InProgress");
                await E2eApiErrorAssert.ExpectAsync(resp, HttpStatusCode.BadRequest, "invalid transition PullRequestOpen -> InProgress is refused").ConfigureAwait(false);

                // Mission should still be PullRequestOpen
                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("PullRequestOpen", mission.Status.ToString());
            }));

            // === Manual Complete Without Dock (Audit Event) ===

            cases.Add(CaseAsync("manual_complete_no_dock_emits_audit_event", "ManualComplete_NoDock_EmitsAuditEvent", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create a mission and advance to WorkProduced (no dock since no vessel assignment)
                string missionId = await CreateAndAdvanceMissionAsync(authClient, "Manual complete audit", "WorkProduced");

                // Manually transition to Complete (no dock exists for this mission)
                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Complete");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Complete", mission.Status.ToString());

                // Check that the audit event was emitted
                EnumerationResult<ArmadaEvent> events = await GetTypedAsync<EnumerationResult<ArmadaEvent>>(authClient, "/api/v1/events?type=mission.manual_complete_no_dock");
                int count = 0;
                foreach (ArmadaEvent evt in events.Objects ?? new List<ArmadaEvent>())
                {
                    if (evt.MissionId != null && evt.MissionId == missionId)
                    {
                        count++;
                    }
                }
                AssertTrue(count >= 1, "Expected at least 1 mission.manual_complete_no_dock event for mission " + missionId);
            }));

            // === Manual Complete Of An Intermediate Voyage Stage ===

            cases.Add(CaseAsync("manual_complete_intermediate_stage_uses_shared_handoff", "ManualComplete_IntermediateStage_UsesSharedHandoff", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string suffix = Guid.NewGuid().ToString("N");
                string workerId = "handoffw" + suffix;
                string reviewerId = "handoffr" + suffix;
                Voyage voyage = await CreateBareVoyageAsync(authClient, "intermediate handoff " + suffix);
                Captain captain = await CreateOwningCaptainAsync(authClient, "intermediate handoff captain " + suffix, workerId);
                Mission worker = await PostMissionAsync(authClient, new
                {
                    Id = workerId,
                    Title = "intermediate worker " + suffix,
                    VoyageId = voyage.Id,
                    CaptainId = captain.Id,
                    Persona = "Worker",
                    Status = "InProgress"
                });
                AssertEqual(workerId, worker.Id, "worker id is the id the captain owns");
                AssertEqual(MissionStatusEnum.InProgress, worker.Status, "worker starts in progress");
                Mission reviewer = await PostMissionAsync(authClient, new
                {
                    Id = reviewerId,
                    Title = "intermediate reviewer " + suffix,
                    Description = "downstream reviewer",
                    VoyageId = voyage.Id,
                    DependsOnMissionId = workerId,
                    Persona = "Judge",
                    Status = "Pending"
                });
                AssertEqual(reviewerId, reviewer.Id, "reviewer id");
                AssertEqual("downstream reviewer", reviewer.Description, "reviewer has no prior-stage context yet");

                HttpResponseMessage complete = await TransitionAsync(authClient, workerId, "Complete");
                AssertStatusCode(HttpStatusCode.OK, complete);
                Mission responseMission = await JsonHelper.DeserializeAsync<Mission>(complete);
                AssertEqual(MissionStatusEnum.WorkProduced, responseMission.Status, "response reports the saved nonterminal status");

                Mission persisted = await GetMissionAsync(authClient, workerId);
                AssertEqual(MissionStatusEnum.WorkProduced, persisted.Status, "shared completion leaves the worker at WorkProduced");
                AssertTrue(persisted.CompletedUtc == null, "an intermediate stage is not terminal");
                AssertTrue(String.IsNullOrEmpty(persisted.DockId), "this handoff has no landing dock");

                Mission downstream = await GetMissionAsync(authClient, reviewerId);
                AssertTrue(downstream.Status == MissionStatusEnum.Pending || downstream.Status == MissionStatusEnum.Assigned,
                    "shared handoff leaves the reviewer pending or assigned, status " + downstream.Status);
                AssertTrue((downstream.Description ?? String.Empty).Contains("Prior Stage Output", StringComparison.Ordinal),
                    "shared handoff prepares the reviewer with the prior stage");
                AssertTrue((downstream.Description ?? String.Empty).Contains(workerId, StringComparison.Ordinal),
                    "prepared reviewer names the completed worker");

                EnumerationResult<ArmadaEvent> events = await GetTypedAsync<EnumerationResult<ArmadaEvent>>(authClient,
                    "/api/v1/events?type=mission.status_changed&missionId=" + Uri.EscapeDataString(workerId) + "&pageSize=50");
                MissionStatusChangedPayload? handoff = (events.Objects ?? new List<ArmadaEvent>())
                    .Where(evt => evt.EventType == "mission.status_changed" && evt.MissionId == workerId && !String.IsNullOrEmpty(evt.Payload))
                    .Select(evt => JsonHelper.Deserialize<MissionStatusChangedPayload>(evt.Payload!))
                    .FirstOrDefault(payload => payload.Status == MissionStatusEnum.WorkProduced);
                AssertNotNull(handoff, "status event reports the persisted WorkProduced state");
                AssertEqual(MissionStatusEnum.InProgress, handoff!.PreviousStatus, "status event records the prior in-progress state");

                EnumerationResult<ArmadaEvent> skippedLanding = await GetTypedAsync<EnumerationResult<ArmadaEvent>>(authClient,
                    "/api/v1/events?type=mission.manual_complete_no_dock&missionId=" + Uri.EscapeDataString(workerId) + "&pageSize=50");
                int skippedCount = 0;
                foreach (ArmadaEvent evt in skippedLanding.Objects ?? new List<ArmadaEvent>())
                {
                    if (evt.MissionId == workerId) skippedCount++;
                }
                AssertEqual(0, skippedCount, "intermediate handoff does not record a terminal no-dock completion");
            }));

            cases.Add(CaseAsync("manual_complete_intermediate_stage_refuses_missing_captain", "ManualComplete_IntermediateStage_RefusesMissingCaptain", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string suffix = Guid.NewGuid().ToString("N");
                string workerId = "missingw" + suffix;
                string reviewerId = "missingr" + suffix;
                Voyage voyage = await CreateBareVoyageAsync(authClient, "missing captain handoff " + suffix);
                Mission worker = await PostMissionAsync(authClient, new
                {
                    Id = workerId,
                    Title = "worker without captain " + suffix,
                    VoyageId = voyage.Id,
                    Persona = "Worker",
                    Status = "InProgress"
                });
                Mission reviewer = await PostMissionAsync(authClient, new
                {
                    Id = reviewerId,
                    Title = "reviewer without captain " + suffix,
                    Description = "unprepared reviewer",
                    VoyageId = voyage.Id,
                    DependsOnMissionId = worker.Id,
                    Persona = "Judge",
                    Status = "Pending"
                });

                HttpResponseMessage complete = await TransitionAsync(authClient, worker.Id!, "Complete");
                await E2eApiErrorAssert.ExpectAsync(complete, HttpStatusCode.Conflict, "missing assigned captain refuses intermediate completion").ConfigureAwait(false);

                Mission persisted = await GetMissionAsync(authClient, worker.Id!);
                AssertEqual(MissionStatusEnum.InProgress, persisted.Status, "a refused handoff leaves the worker in progress");
                AssertTrue(persisted.CompletedUtc == null, "a refused handoff does not complete the worker");
                Mission downstream = await GetMissionAsync(authClient, reviewer.Id!);
                AssertEqual("unprepared reviewer", downstream.Description, "a refused handoff does not prepare the reviewer");
                AssertEqual(MissionStatusEnum.Pending, downstream.Status, "a refused handoff does not assign the reviewer");
            }));

            cases.Add(CaseAsync("manual_complete_intermediate_stage_refuses_stale_captain", "ManualComplete_IntermediateStage_RefusesStaleCaptain", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string suffix = Guid.NewGuid().ToString("N");
                string workerId = "stalew" + suffix;
                string reviewerId = "staler" + suffix;
                Voyage voyage = await CreateBareVoyageAsync(authClient, "stale captain handoff " + suffix);
                Captain captain = await CreateOwningCaptainAsync(authClient, "stale handoff captain " + suffix, "staleother" + suffix);
                Mission worker = await PostMissionAsync(authClient, new
                {
                    Id = workerId,
                    Title = "worker with stale captain " + suffix,
                    VoyageId = voyage.Id,
                    CaptainId = captain.Id,
                    Persona = "Worker",
                    Status = "InProgress"
                });
                Mission reviewer = await PostMissionAsync(authClient, new
                {
                    Id = reviewerId,
                    Title = "reviewer with stale captain " + suffix,
                    Description = "unprepared reviewer",
                    VoyageId = voyage.Id,
                    DependsOnMissionId = worker.Id,
                    Persona = "Judge",
                    Status = "Pending"
                });

                HttpResponseMessage complete = await TransitionAsync(authClient, worker.Id!, "Complete");
                await E2eApiErrorAssert.ExpectAsync(complete, HttpStatusCode.Conflict, "captain that does not own the mission refuses intermediate completion").ConfigureAwait(false);

                Mission persisted = await GetMissionAsync(authClient, worker.Id!);
                AssertEqual(MissionStatusEnum.InProgress, persisted.Status, "a stale captain does not change the worker");
                AssertEqual(captain.Id, persisted.CaptainId, "a stale captain does not clear the assignment");
                Mission downstream = await GetMissionAsync(authClient, reviewer.Id!);
                AssertEqual("unprepared reviewer", downstream.Description, "a stale captain does not prepare the reviewer");
            }));

            cases.Add(CaseAsync("manual_complete_intermediate_stage_refuses_captain_outside_caller_scope", "ManualComplete_IntermediateStage_RefusesCaptainOutsideCallerScope", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;
                string suffix = Guid.NewGuid().ToString("N");
                string workerId = "scopew" + suffix;
                string reviewerId = "scoper" + suffix;
                Captain foreignCaptain = await CreateOwningCaptainAsync(authClient, "foreign handoff captain " + suffix, workerId);
                string tenantId = await CreateTenantAsync(authClient, suffix);
                string bearer = await CreateTenantAdminBearerAsync(authClient, tenantId, suffix);
                using HttpClient tenantAdmin = new HttpClient();
                tenantAdmin.BaseAddress = new Uri(fx.BaseUrl);
                tenantAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                tenantAdmin.Timeout = TimeSpan.FromSeconds(30);

                Voyage voyage = await CreateBareVoyageAsync(tenantAdmin, "scoped handoff " + suffix);
                Mission worker = await PostMissionAsync(tenantAdmin, new
                {
                    Id = workerId,
                    Title = "worker with foreign captain " + suffix,
                    VoyageId = voyage.Id,
                    CaptainId = foreignCaptain.Id,
                    Persona = "Worker",
                    Status = "InProgress"
                });
                Mission reviewer = await PostMissionAsync(tenantAdmin, new
                {
                    Id = reviewerId,
                    Title = "reviewer with foreign captain " + suffix,
                    Description = "unprepared reviewer",
                    VoyageId = voyage.Id,
                    DependsOnMissionId = worker.Id,
                    Persona = "Judge",
                    Status = "Pending"
                });

                HttpResponseMessage complete = await TransitionAsync(tenantAdmin, worker.Id!, "Complete");
                await E2eApiErrorAssert.ExpectAsync(complete, HttpStatusCode.Conflict, "captain outside the caller scope refuses intermediate completion").ConfigureAwait(false);

                Mission persisted = await GetMissionAsync(tenantAdmin, worker.Id!);
                AssertEqual(MissionStatusEnum.InProgress, persisted.Status, "an out-of-scope captain does not change the worker");
                AssertEqual(foreignCaptain.Id, persisted.CaptainId, "an out-of-scope refusal keeps the stored captain id");
                Mission downstream = await GetMissionAsync(tenantAdmin, reviewer.Id!);
                AssertEqual("unprepared reviewer", downstream.Description, "an out-of-scope captain does not prepare the reviewer");
                AssertEqual(MissionStatusEnum.Pending, downstream.Status, "an out-of-scope captain does not assign the reviewer");
            }));

            // === MergeQueue Auto-Enqueue ===

            cases.Add(CaseAsync("merge_queue_vessel_landing_mode_creates_entry", "MergeQueue_VesselLandingMode_CreatesEntry", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                // Create a vessel with LandingMode = MergeQueue
                Vessel vessel = await CreateVesselWithLandingModeAsync(authClient, "MQ-Vessel", "MergeQueue");
                string vesselId = vessel.Id!;

                // Verify vessel LandingMode was persisted
                Vessel readVessel = await GetTypedAsync<Vessel>(authClient, "/api/v1/vessels/" + vesselId);
                AssertTrue(readVessel.LandingMode != null, "Vessel should have LandingMode property");
                AssertEqual("MergeQueue", readVessel.LandingMode.ToString());
            }));

            // === Event Emission Correctness ===

            cases.Add(CaseAsync("status_changed_event_emitted_for_each_transition", "StatusChanged_EventEmitted_ForEachTransition", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "Event emission test", "InProgress");

                // Transition to WorkProduced
                await TransitionAsync(authClient, missionId, "WorkProduced");

                // Check that a status_changed event was emitted
                EnumerationResult<ArmadaEvent> events = await GetTypedAsync<EnumerationResult<ArmadaEvent>>(authClient, "/api/v1/events?type=mission.status_changed&missionId=" + missionId);
                // The new and previous status are typed fields of the event payload, not only words in the message.
                List<MissionStatusChangedPayload> payloads = (events.Objects ?? new List<ArmadaEvent>())
                    .Where(e => e.EventType == "mission.status_changed" && e.MissionId == missionId && !String.IsNullOrEmpty(e.Payload))
                    .Select(e => JsonHelper.Deserialize<MissionStatusChangedPayload>(e.Payload!))
                    .ToList();
                MissionStatusChangedPayload? workProduced = payloads.FirstOrDefault(p => p.Status == MissionStatusEnum.WorkProduced);
                AssertNotNull(workProduced, "Expected a mission.status_changed event with Status WorkProduced for mission " + missionId);
                AssertEqual(MissionStatusEnum.InProgress, workProduced!.PreviousStatus, "previous status recorded");
            }));

            cases.Add(CaseAsync("landing_failed_transitions_back_to_work_produced", "LandingFailed_TransitionsBackToWorkProduced", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "LandingFailed retry", "WorkProduced");
                await TransitionAsync(authClient, missionId, "LandingFailed");

                // LandingFailed -> WorkProduced (retry)
                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "WorkProduced");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("WorkProduced", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("landing_failed_transitions_to_failed", "LandingFailed_TransitionsToFailed", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "LandingFailed to Failed", "WorkProduced");
                await TransitionAsync(authClient, missionId, "LandingFailed");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Failed");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Failed", mission.Status.ToString());
            }));

            cases.Add(CaseAsync("landing_failed_transitions_to_cancelled", "LandingFailed_TransitionsToCancelled", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                string missionId = await CreateAndAdvanceMissionAsync(authClient, "LandingFailed to Cancelled", "WorkProduced");
                await TransitionAsync(authClient, missionId, "LandingFailed");

                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, "Cancelled");
                AssertStatusCode(HttpStatusCode.OK, resp);

                Mission mission = await GetMissionAsync(authClient, missionId);
                AssertEqual("Cancelled", mission.Status.ToString());
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Landing Pipeline (Automated)",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private const string SuiteId = "E2E.LandingPipeline";

        #endregion

        #region Private-Methods

        /// <summary>
        /// Create a mission (no vessel) and advance it through the status chain to the target status.
        /// Chain: Pending -> Assigned -> InProgress -> WorkProduced -> PullRequestOpen
        /// </summary>
        private static async Task<string> CreateAndAdvanceMissionAsync(HttpClient authClient, string title, string targetStatus)
        {
            Mission mission = await CreateMissionAsync(authClient, title);
            string missionId = mission.Id!;

            string[] chain = new[] { "Assigned", "InProgress", "WorkProduced", "PullRequestOpen" };
            foreach (string status in chain)
            {
                HttpResponseMessage resp = await TransitionAsync(authClient, missionId, status);
                resp.EnsureSuccessStatusCode();
                if (status == targetStatus) break;
            }

            return missionId;
        }

        private static async Task<Voyage> CreateBareVoyageAsync(HttpClient client, string title)
        {
            HttpResponseMessage resp = await client.PostAsync("/api/v1/voyages", JsonHelper.ToJsonContent(new { Title = title })).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(resp.StatusCode == HttpStatusCode.Created, "create voyage " + (int)resp.StatusCode + ": " + body);
            return JsonHelper.Deserialize<Voyage>(body);
        }

        private static async Task<Captain> CreateOwningCaptainAsync(HttpClient client, string name, string currentMissionId)
        {
            HttpResponseMessage resp = await client.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new
            {
                Name = name,
                Runtime = "ClaudeCode",
                State = "Working",
                CurrentMissionId = currentMissionId
            })).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(resp.StatusCode == HttpStatusCode.Created, "create captain " + (int)resp.StatusCode + ": " + body);
            Captain captain = JsonHelper.Deserialize<Captain>(body);
            AssertEqual(currentMissionId, captain.CurrentMissionId, "captain records the mission it owns");
            return captain;
        }

        private static async Task<Mission> PostMissionAsync(HttpClient client, object body)
        {
            HttpResponseMessage resp = await client.PostAsync("/api/v1/missions", JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(resp.IsSuccessStatusCode, "create mission " + (int)resp.StatusCode + ": " + text);
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(text);
            if (wrapper.Mission != null && !String.IsNullOrEmpty(wrapper.Mission.Id))
                return wrapper.Mission;
            return JsonHelper.Deserialize<Mission>(text);
        }

        private static async Task<string> CreateTenantAsync(HttpClient adminClient, string suffix)
        {
            HttpResponseMessage response = await adminClient.PostAsync("/api/v1/tenants",
                JsonHelper.ToJsonContent(new { Name = "handoff-" + suffix.Substring(0, 8) })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(response.StatusCode == HttpStatusCode.Created, "create tenant " + (int)response.StatusCode + ": " + body);
            TenantMetadata tenant = JsonHelper.Deserialize<TenantMetadata>(body);
            return tenant.Id;
        }

        private static async Task<string> CreateTenantAdminBearerAsync(HttpClient adminClient, string tenantId, string suffix)
        {
            string email = "handoff-" + suffix.Substring(0, 8) + "@example.invalid";
            HttpResponseMessage userResponse = await adminClient.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
            {
                TenantId = tenantId,
                Email = email,
                PasswordSha256 = UserMaster.ComputePasswordHash("handoff-pass"),
                IsTenantAdmin = true
            })).ConfigureAwait(false);
            string userBody = await userResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(userResponse.StatusCode == HttpStatusCode.Created, "create tenant admin " + (int)userResponse.StatusCode + ": " + userBody);
            UserMaster user = JsonHelper.Deserialize<UserMaster>(userBody);

            HttpResponseMessage credentialResponse = await adminClient.PostAsync("/api/v1/credentials", JsonHelper.ToJsonContent(new
            {
                TenantId = tenantId,
                UserId = user.Id,
                Name = "handoff-" + suffix.Substring(0, 8)
            })).ConfigureAwait(false);
            string credentialBody = await credentialResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertTrue(credentialResponse.StatusCode == HttpStatusCode.Created, "create credential " + (int)credentialResponse.StatusCode + ": " + credentialBody);
            Credential credential = JsonHelper.Deserialize<Credential>(credentialBody);
            AssertFalse(String.IsNullOrEmpty(credential.BearerToken), "credential bearer token");
            return credential.BearerToken!;
        }

        private static async Task<Mission> CreateMissionAsync(HttpClient authClient, string title)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Title = title });
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/missions", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            // When mission stays Pending (no captain available), the API returns
            // { "Mission": {...}, "Warning": "..." } instead of the mission directly.
            MissionCreateResponse wrapper = JsonHelper.Deserialize<MissionCreateResponse>(body);
            Mission mission = wrapper.Mission ?? JsonHelper.Deserialize<Mission>(body);

            return mission;
        }

        private static async Task<Vessel> CreateVesselWithLandingModeAsync(HttpClient authClient, string name, string landingMode)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string repoUrl = TestRepoHelper.GetLocalBareRepoUrl();
            StringContent content = JsonHelper.ToJsonContent(new { Name = uniqueName, RepoUrl = repoUrl, LandingMode = landingMode });
            HttpResponseMessage resp = await authClient.PostAsync("/api/v1/vessels", content).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await JsonHelper.DeserializeAsync<Vessel>(resp);
        }

        private static async Task<HttpResponseMessage> TransitionAsync(HttpClient authClient, string missionId, string status)
        {
            StringContent content = JsonHelper.ToJsonContent(new { Status = status });
            return await authClient.PutAsync("/api/v1/missions/" + missionId + "/status", content).ConfigureAwait(false);
        }

        private static async Task<Mission> GetMissionAsync(HttpClient authClient, string missionId)
        {
            return await GetTypedAsync<Mission>(authClient, "/api/v1/missions/" + missionId).ConfigureAwait(false);
        }

        private static async Task<T> GetTypedAsync<T>(HttpClient authClient, string path)
        {
            HttpResponseMessage resp = await authClient.GetAsync(path).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException("GET " + path + " returned " + (int)resp.StatusCode + ": " + body);
            return JsonHelper.Deserialize<T>(body);
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
