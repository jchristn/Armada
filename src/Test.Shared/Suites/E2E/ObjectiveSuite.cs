namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end descriptors for internal-first objective capture and history linkage, ported 1:1
    /// from the retired automated ObjectiveTests suite. Cases share the singleton e2e server fixture
    /// and carry created objective, vessel, voyage, release, captain, and refinement-session
    /// identifiers across cases; the working directory is provisioned by the first case and torn
    /// down by a trailing cleanup case, mirroring the legacy try/finally teardown.
    /// </summary>
    public sealed class ObjectiveSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.Objective";

        private string _ObjectiveId = String.Empty;
        private string _VesselId = String.Empty;
        private string _VoyageId = String.Empty;
        private string _ReleaseId = String.Empty;
        private string _CaptainId = String.Empty;
        private string _RefinementSessionId = String.Empty;
        private string _WorkingDirectory = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Objectives suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("objectives_create_list_read_update_delete_and_history_filter", "Objectives_CreateListReadUpdateDeleteAndHistoryFilter", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                _WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada-objective-follow-through-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_WorkingDirectory);

                HttpResponseMessage createResponse = await authClient.PostAsync("/api/v1/objectives",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "Objective REST Coverage",
                        Description = "Track internal-first scoped work.",
                        Status = ObjectiveStatusEnum.Scoped,
                        Owner = "qa",
                        Tags = new[] { "history", "objective" },
                        AcceptanceCriteria = new[] { "REST CRUD", "History linkage" }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResponse.StatusCode);

                Objective created = await JsonHelper.DeserializeAsync<Objective>(createResponse).ConfigureAwait(false);
                _ObjectiveId = created.Id;
                AssertStartsWith("obj_", _ObjectiveId);
                AssertEqual(ObjectiveStatusEnum.Scoped, created.Status);

                HttpResponseMessage getResponse = await authClient.GetAsync("/api/v1/objectives/" + _ObjectiveId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, getResponse.StatusCode);
                Objective loaded = await JsonHelper.DeserializeAsync<Objective>(getResponse).ConfigureAwait(false);
                AssertEqual(_ObjectiveId, loaded.Id);

                HttpResponseMessage listResponse = await authClient.GetAsync("/api/v1/objectives?pageSize=100").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, listResponse.StatusCode);
                EnumerationResult<Objective> objectives = await JsonHelper.DeserializeAsync<EnumerationResult<Objective>>(listResponse).ConfigureAwait(false);
                AssertTrue(objectives.Objects.Exists(current => current.Id == _ObjectiveId), "Expected objective in list.");

                HttpResponseMessage historyResponse = await authClient.GetAsync(
                    "/api/v1/history?objectiveId=" + Uri.EscapeDataString(_ObjectiveId) + "&pageSize=100").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, historyResponse.StatusCode);
                EnumerationResult<HistoricalTimelineEntry> history = await JsonHelper.DeserializeAsync<EnumerationResult<HistoricalTimelineEntry>>(historyResponse).ConfigureAwait(false);
                AssertTrue(history.Objects.Exists(entry => entry.SourceType == "Objective" && entry.SourceId == _ObjectiveId), "Expected objective timeline entry.");

                HttpResponseMessage updateResponse = await authClient.PutAsync("/api/v1/objectives/" + _ObjectiveId,
                    JsonHelper.ToJsonContent(new
                    {
                        Status = ObjectiveStatusEnum.Completed,
                        EvidenceLinks = new[] { "https://example.test/objective/rest" }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, updateResponse.StatusCode);
                Objective updated = await JsonHelper.DeserializeAsync<Objective>(updateResponse).ConfigureAwait(false);
                AssertEqual(ObjectiveStatusEnum.Completed, updated.Status);
                AssertTrue(updated.CompletedUtc.HasValue, "Expected completion timestamp.");

                HttpResponseMessage deleteResponse = await authClient.DeleteAsync("/api/v1/objectives/" + _ObjectiveId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);
                _ObjectiveId = String.Empty;

                HttpResponseMessage deletedResponse = await authClient.GetAsync("/api/v1/objectives/" + updated.Id).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, deletedResponse.StatusCode);
            }));

            cases.Add(CaseAsync("objectives_create_without_auth_returns_401", "Objectives_CreateWithoutAuthReturns401", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient unauthClient = fx.UnauthClient;

                HttpResponseMessage response = await unauthClient.PostAsync("/api/v1/objectives",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "Unauthorized Objective"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            }));

            cases.Add(CaseAsync("backlog_alias_create_read_reorder_and_delete", "BacklogAlias_CreateReadReorderAndDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage createResponse = await authClient.PostAsync("/api/v1/backlog",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "Backlog REST Coverage",
                        Description = "Exercise the backlog alias surface.",
                        Status = ObjectiveStatusEnum.Scoped,
                        Kind = ObjectiveKindEnum.Feature,
                        Priority = ObjectivePriorityEnum.P1,
                        Rank = 25,
                        BacklogState = ObjectiveBacklogStateEnum.Inbox,
                        Effort = ObjectiveEffortEnum.M,
                        TargetVersion = "0.8.0"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResponse.StatusCode);

                Objective created = await JsonHelper.DeserializeAsync<Objective>(createResponse).ConfigureAwait(false);
                _ObjectiveId = created.Id;
                AssertStartsWith("obj_", _ObjectiveId);
                AssertEqual(ObjectiveKindEnum.Feature, created.Kind);
                AssertEqual(ObjectivePriorityEnum.P1, created.Priority);
                AssertEqual(25, created.Rank);
                AssertEqual(ObjectiveBacklogStateEnum.Inbox, created.BacklogState);

                HttpResponseMessage getBacklogResponse = await authClient.GetAsync("/api/v1/backlog/" + _ObjectiveId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, getBacklogResponse.StatusCode);
                Objective loadedBacklog = await JsonHelper.DeserializeAsync<Objective>(getBacklogResponse).ConfigureAwait(false);
                AssertEqual(_ObjectiveId, loadedBacklog.Id);
                AssertEqual("0.8.0", loadedBacklog.TargetVersion);

                HttpResponseMessage objectiveCompatibilityResponse = await authClient.GetAsync("/api/v1/objectives/" + _ObjectiveId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, objectiveCompatibilityResponse.StatusCode);
                Objective compatibilityLoad = await JsonHelper.DeserializeAsync<Objective>(objectiveCompatibilityResponse).ConfigureAwait(false);
                AssertEqual(_ObjectiveId, compatibilityLoad.Id);

                HttpResponseMessage listResponse = await authClient.GetAsync("/api/v1/backlog?pageSize=100").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, listResponse.StatusCode);
                EnumerationResult<Objective> listed = await JsonHelper.DeserializeAsync<EnumerationResult<Objective>>(listResponse).ConfigureAwait(false);
                AssertTrue(listed.Objects.Exists(current => current.Id == _ObjectiveId), "Expected backlog item in backlog list.");

                HttpResponseMessage sessionsResponse = await authClient.GetAsync("/api/v1/backlog/" + _ObjectiveId + "/refinement-sessions").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, sessionsResponse.StatusCode);
                List<ObjectiveRefinementSession> sessions = await JsonHelper.DeserializeAsync<List<ObjectiveRefinementSession>>(sessionsResponse).ConfigureAwait(false);
                AssertEqual(0, sessions.Count);

                HttpResponseMessage reorderResponse = await authClient.PostAsync("/api/v1/backlog/reorder",
                    JsonHelper.ToJsonContent(new
                    {
                        Items = new[]
                        {
                            new
                            {
                                ObjectiveId = _ObjectiveId,
                                Rank = 5
                            }
                        }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, reorderResponse.StatusCode);
                List<Objective> reordered = await JsonHelper.DeserializeAsync<List<Objective>>(reorderResponse).ConfigureAwait(false);
                AssertEqual(1, reordered.Count);
                AssertEqual(5, reordered[0].Rank);

                HttpResponseMessage deleteResponse = await authClient.DeleteAsync("/api/v1/backlog/" + _ObjectiveId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);
                _ObjectiveId = String.Empty;
            }));

            cases.Add(CaseAsync("backlog_refinement_routes_create_send_summarize_apply_and_delete", "BacklogRefinementRoutes_CreateSendSummarizeApplyAndDelete", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage createResponse = await authClient.PostAsync("/api/v1/backlog",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "Backlog refinement REST coverage",
                        Description = "Exercise captain-backed backlog refinement routes.",
                        Status = ObjectiveStatusEnum.Draft,
                        Kind = ObjectiveKindEnum.Feature,
                        BacklogState = ObjectiveBacklogStateEnum.Inbox
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createResponse.StatusCode);
                Objective created = await JsonHelper.DeserializeAsync<Objective>(createResponse).ConfigureAwait(false);
                _ObjectiveId = created.Id;

                Captain captain = await CreateCaptainAsync(authClient, "objective-refinement-rest").ConfigureAwait(false);
                _CaptainId = captain.Id;

                HttpResponseMessage createSessionResponse = await authClient.PostAsync(
                    "/api/v1/backlog/" + _ObjectiveId + "/refinement-sessions",
                    JsonHelper.ToJsonContent(new
                    {
                        CaptainId = _CaptainId,
                        Title = "Backlog refinement REST coverage session"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createSessionResponse.StatusCode);
                ObjectiveRefinementSessionDetail createdSession = await JsonHelper.DeserializeAsync<ObjectiveRefinementSessionDetail>(createSessionResponse).ConfigureAwait(false);
                _RefinementSessionId = createdSession.Session.Id;
                AssertEqual(_ObjectiveId, createdSession.Session.ObjectiveId);
                AssertEqual(_CaptainId, createdSession.Session.CaptainId);
                AssertEqual(0, createdSession.Messages.Count);

                await SetCaptainRuntimeAsync(authClient, _CaptainId, "Custom").ConfigureAwait(false);

                HttpResponseMessage sendResponse = await authClient.PostAsync(
                    "/api/v1/objective-refinement-sessions/" + _RefinementSessionId + "/messages",
                    JsonHelper.ToJsonContent(new
                    {
                        Content = "Clarify rollout constraints and acceptance criteria for the backlog item."
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, sendResponse.StatusCode);

                ObjectiveRefinementSessionDetail activeDetail = await WaitForSessionDetailAsync(
                    authClient,
                    _RefinementSessionId,
                    detail => detail.Session.Status == ObjectiveRefinementSessionStatusEnum.Active
                        && detail.Messages.Exists(message => message.Role == "Assistant" && !String.IsNullOrWhiteSpace(message.Content))).ConfigureAwait(false);

                ObjectiveRefinementMessage assistantMessage = activeDetail.Messages.Find(message => message.Role == "Assistant")
                    ?? throw new Exception("Expected assistant refinement transcript message");
                AssertEqual(2, activeDetail.Messages.Count);
                AssertContains("Refinement response failed", assistantMessage.Content);

                HttpResponseMessage summarizeResponse = await authClient.PostAsync(
                    "/api/v1/objective-refinement-sessions/" + _RefinementSessionId + "/summarize",
                    JsonHelper.ToJsonContent(new
                    {
                        MessageId = assistantMessage.Id
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, summarizeResponse.StatusCode);
                ObjectiveRefinementSummaryResponse summary = await JsonHelper.DeserializeAsync<ObjectiveRefinementSummaryResponse>(summarizeResponse).ConfigureAwait(false);
                AssertEqual("assistant-fallback", summary.Method);
                AssertEqual(_RefinementSessionId, summary.SessionId);
                AssertEqual(assistantMessage.Id, summary.MessageId);

                HttpResponseMessage applyResponse = await authClient.PostAsync(
                    "/api/v1/objective-refinement-sessions/" + _RefinementSessionId + "/apply",
                    JsonHelper.ToJsonContent(new
                    {
                        MessageId = assistantMessage.Id,
                        MarkMessageSelected = true,
                        PromoteBacklogState = true
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, applyResponse.StatusCode);
                ObjectiveRefinementApplyResponse applied = await JsonHelper.DeserializeAsync<ObjectiveRefinementApplyResponse>(applyResponse).ConfigureAwait(false);
                AssertEqual(ObjectiveStatusEnum.Scoped, applied.Objective.Status);
                AssertEqual(ObjectiveBacklogStateEnum.Triaged, applied.Objective.BacklogState);
                AssertTrue(applied.Objective.RefinementSessionIds.Contains(_RefinementSessionId), "Expected refinement session link after apply.");
                AssertContains("Refinement response failed", applied.Objective.RefinementSummary ?? String.Empty);

                ObjectiveRefinementSessionDetail selectedDetail = await WaitForSessionDetailAsync(
                    authClient,
                    _RefinementSessionId,
                    detail => detail.Messages.Exists(message => message.Id == assistantMessage.Id && message.IsSelected)).ConfigureAwait(false);
                AssertTrue(selectedDetail.Messages.Exists(message => message.Id == assistantMessage.Id && message.IsSelected), "Expected selected refinement message.");

                HttpResponseMessage deleteSessionResponse = await authClient.DeleteAsync("/api/v1/objective-refinement-sessions/" + _RefinementSessionId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteSessionResponse.StatusCode);
                _RefinementSessionId = String.Empty;

                HttpResponseMessage deletedSessionResponse = await authClient.GetAsync("/api/v1/objective-refinement-sessions/" + createdSession.Session.Id).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NotFound, deletedSessionResponse.StatusCode);

                HttpResponseMessage objectiveResponse = await authClient.GetAsync("/api/v1/backlog/" + _ObjectiveId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, objectiveResponse.StatusCode);
                Objective updatedObjective = await JsonHelper.DeserializeAsync<Objective>(objectiveResponse).ConfigureAwait(false);
                AssertFalse(updatedObjective.RefinementSessionIds.Contains(createdSession.Session.Id), "Expected refinement session unlink after delete.");

                HttpResponseMessage deleteObjectiveResponse = await authClient.DeleteAsync("/api/v1/backlog/" + _ObjectiveId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteObjectiveResponse.StatusCode);
                _ObjectiveId = String.Empty;

                HttpResponseMessage deleteCaptainResponse = await authClient.DeleteAsync("/api/v1/captains/" + _CaptainId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleteCaptainResponse.StatusCode);
                _CaptainId = String.Empty;
            }));

            cases.Add(CaseAsync("objectives_voyage_and_release_creation_link_back_to_objective", "Objectives_VoyageAndReleaseCreationLinkBackToObjective", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                HttpResponseMessage vesselResponse = await authClient.PostAsync("/api/v1/vessels",
                    JsonHelper.ToJsonContent(new
                    {
                        Name = "Objective Follow Through Vessel",
                        RepoUrl = "file:///tmp/objective-follow-through.git",
                        LocalPath = _WorkingDirectory,
                        WorkingDirectory = _WorkingDirectory,
                        DefaultBranch = "main"
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, vesselResponse.StatusCode);
                Vessel vessel = await JsonHelper.DeserializeAsync<Vessel>(vesselResponse).ConfigureAwait(false);
                _VesselId = vessel.Id;

                HttpResponseMessage createObjectiveResponse = await authClient.PostAsync("/api/v1/objectives",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "Objective Follow Through",
                        Status = ObjectiveStatusEnum.Scoped,
                        VesselIds = new[] { _VesselId }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createObjectiveResponse.StatusCode);
                Objective createdObjective = await JsonHelper.DeserializeAsync<Objective>(createObjectiveResponse).ConfigureAwait(false);
                _ObjectiveId = createdObjective.Id;

                HttpResponseMessage createVoyageResponse = await authClient.PostAsync("/api/v1/voyages",
                    JsonHelper.ToJsonContent(new
                    {
                        Title = "Objective Voyage",
                        Description = "Objective follow-through voyage",
                        VesselId = _VesselId,
                        ObjectiveId = _ObjectiveId,
                        Missions = new[]
                        {
                            new
                            {
                                Title = "Objective Mission",
                                Description = "Implement follow-through"
                            }
                        }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createVoyageResponse.StatusCode);
                Voyage voyage = await JsonHelper.DeserializeAsync<Voyage>(createVoyageResponse).ConfigureAwait(false);
                _VoyageId = voyage.Id;

                HttpResponseMessage createReleaseResponse = await authClient.PostAsync("/api/v1/releases",
                    JsonHelper.ToJsonContent(new
                    {
                        VesselId = _VesselId,
                        Title = "Objective Draft Release",
                        ObjectiveIds = new[] { _ObjectiveId }
                    })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, createReleaseResponse.StatusCode);
                Release release = await JsonHelper.DeserializeAsync<Release>(createReleaseResponse).ConfigureAwait(false);
                _ReleaseId = release.Id;

                HttpResponseMessage objectiveResponse = await authClient.GetAsync("/api/v1/objectives/" + _ObjectiveId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, objectiveResponse.StatusCode);
                Objective linkedObjective = await JsonHelper.DeserializeAsync<Objective>(objectiveResponse).ConfigureAwait(false);
                AssertTrue(linkedObjective.VoyageIds.Contains(_VoyageId), "Expected voyage link on objective.");
                AssertTrue(linkedObjective.ReleaseIds.Contains(_ReleaseId), "Expected release link on objective.");
                AssertEqual(ObjectiveStatusEnum.Released, linkedObjective.Status);
            }));

            cases.Add(CaseAsync("objective_cleanup_resources", "Objective_CleanupResources", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this);
                HttpClient authClient = fx.AuthClient;

                if (!String.IsNullOrWhiteSpace(_ObjectiveId))
                {
                    try { await authClient.DeleteAsync("/api/v1/objectives/" + _ObjectiveId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_ReleaseId))
                {
                    try { await authClient.DeleteAsync("/api/v1/releases/" + _ReleaseId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_RefinementSessionId))
                {
                    try { await authClient.DeleteAsync("/api/v1/objective-refinement-sessions/" + _RefinementSessionId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_CaptainId))
                {
                    try { await authClient.DeleteAsync("/api/v1/captains/" + _CaptainId).ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_VoyageId))
                {
                    try { await authClient.DeleteAsync("/api/v1/voyages/" + _VoyageId).ConfigureAwait(false); } catch { }
                    try { await authClient.DeleteAsync("/api/v1/voyages/" + _VoyageId + "/purge").ConfigureAwait(false); } catch { }
                }
                if (!String.IsNullOrWhiteSpace(_VesselId))
                {
                    try { await authClient.DeleteAsync("/api/v1/vessels/" + _VesselId).ConfigureAwait(false); } catch { }
                }
                try
                {
                    if (!String.IsNullOrWhiteSpace(_WorkingDirectory) && Directory.Exists(_WorkingDirectory))
                        Directory.Delete(_WorkingDirectory, true);
                }
                catch
                {
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Objectives",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Captain> CreateCaptainAsync(HttpClient authClient, string prefix, string runtime = "ClaudeCode")
        {
            HttpResponseMessage response = await authClient.PostAsync(
                "/api/v1/captains",
                JsonHelper.ToJsonContent(new
                {
                    Name = prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Runtime = runtime
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode);
            return await JsonHelper.DeserializeAsync<Captain>(response).ConfigureAwait(false);
        }

        private static async Task SetCaptainRuntimeAsync(HttpClient authClient, string captainId, string runtime)
        {
            HttpResponseMessage getResponse = await authClient.GetAsync("/api/v1/captains/" + captainId).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, getResponse.StatusCode);
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(getResponse).ConfigureAwait(false);

            HttpResponseMessage updateResponse = await authClient.PutAsync(
                "/api/v1/captains/" + captainId,
                JsonHelper.ToJsonContent(new
                {
                    Name = captain.Name,
                    Runtime = runtime
                })).ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        }

        private static async Task<ObjectiveRefinementSessionDetail> WaitForSessionDetailAsync(
            HttpClient authClient,
            string sessionId,
            Func<ObjectiveRefinementSessionDetail, bool> predicate,
            int timeoutMs = 15000)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
            while (!deadline.Passed)
            {
                HttpResponseMessage response = await authClient.GetAsync("/api/v1/objective-refinement-sessions/" + sessionId).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                ObjectiveRefinementSessionDetail detail = await JsonHelper.DeserializeAsync<ObjectiveRefinementSessionDetail>(response).ConfigureAwait(false);
                if (predicate(detail))
                    return detail;

                await Task.Delay(100).ConfigureAwait(false);
            }

            throw new TimeoutException("Timed out waiting for refinement session " + sessionId);
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
