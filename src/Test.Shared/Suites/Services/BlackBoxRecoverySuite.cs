namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// IBlackBoxService.RunRecoveryAsync and ApplyRecommendationAsync (plan section 9 safety): dry run, then a confirmed
    /// run that executes exactly the recorded actions once, annotates the entry and emits blackbox.recovery_run; and every
    /// refusal (no dry run, expired, wrong path, reused, mission or vessel changed, data loss without acknowledgement,
    /// another caller, unauthorized caller, advice-only recommendation, no executor).
    /// </summary>
    public sealed class BlackBoxRecoverySuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxRecovery";
        private const string PullRequestPath = "bbx-land-001.pull-request";
        private const string StashPath = "bbx-land-001.stash-retry";
        private const string LandingModeRec = "bbx-land-001.landing-mode";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("dry_run_then_confirm", "A dry run returns the plan and a 10-minute confirm token and runs nothing; the confirmed run executes the recorded steps once, annotates the entry and emits blackbox.recovery_run", TestTags.Positive, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync().ConfigureAwait(false);
                BlackBoxRecoveryRunResult dry = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, new BlackBoxRecoveryRunRequest()).ConfigureAwait(false);
                AssertTrue(dry.DryRun, "dry run by default"); AssertTrue(dry.Succeeded);
                AssertNotNull(dry.ConfirmToken, "token"); AssertStartsWith("bbc_", dry.ConfirmToken!);
                AssertTrue(dry.ConfirmTokenExpiresUtc > DateTime.UtcNow.AddMinutes(9) && dry.ConfirmTokenExpiresUtc <= DateTime.UtcNow.AddMinutes(10).AddSeconds(5), "10-minute expiry");
                AssertEqual("update_vessel,retry_landing", String.Join(",", dry.Plan.Select(p => p.Action!.Tool)), "plan");
                AssertContains("Would run: update_vessel", dry.Message ?? "");
                AssertEqual(0, f.Executor.Calls.Count, "a dry run runs nothing");
                AssertEqual(1, f.Sink.Count(BlackBoxEventTypes.RecoveryRun), "dry run event");

                BlackBoxRecoveryRunResult run = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(dry.ConfirmToken)).ConfigureAwait(false);
                AssertFalse(run.DryRun); AssertTrue(run.Succeeded, run.Message);
                AssertEqual(2, run.StepResults.Count); AssertTrue(run.StepResults.All(s => s.Succeeded), "every step");
                AssertEqual("update_vessel,retry_landing", String.Join(",", f.Executor.Tools), "executed exactly the recorded steps, once, in order");
                BlackBoxAction first = f.Executor.Calls.First();
                AssertEqual(f.VesselId, first.Arguments["vesselId"]); AssertEqual("PullRequest", first.Arguments["landingMode"]);
                AssertEqual(f.MissionId, f.Executor.Calls.Last().Arguments["missionId"]);
                AssertTrue(f.Executor.Callers.All(c => c.UserId == f.Admin.UserId), "run as the caller");
                List<BlackBoxNote> notes = (await f.Service.EnumerateNotesAsync(f.Admin, f.EntryId, null).ConfigureAwait(false)).Objects;
                BlackBoxNote note = notes.Single();
                AssertEqual(BlackBoxNoteAuthorKindEnum.Agent, note.AuthorKind, "annotated as Agent by default");
                AssertContains(PullRequestPath, note.Body); AssertContains("succeeded", note.Body);
                AssertEqual(2, f.Sink.Count(BlackBoxEventTypes.RecoveryRun), "run event");
                BlackBoxRecoveryRunPayload payload = (BlackBoxRecoveryRunPayload)f.Sink.Events.Last(e => e.EventType == BlackBoxEventTypes.RecoveryRun).Payload!;
                AssertFalse(payload.DryRun); AssertTrue(payload.Succeeded); AssertEqual(2, payload.StepsRun); AssertEqual(PullRequestPath, payload.PathId);

                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(dry.ConfirmToken)), "a token works once").ConfigureAwait(false);
                AssertEqual(2, f.Executor.Calls.Count, "nothing ran again");

                BlackBoxRecoveryRunResult operatorDry = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, StashPath, new BlackBoxRecoveryRunRequest()).ConfigureAwait(false);
                BlackBoxRecoveryRunRequest operatorRun = Confirm(operatorDry.ConfirmToken);
                operatorRun.AuthorKind = BlackBoxNoteAuthorKindEnum.Operator;
                BlackBoxRecoveryRunResult manual = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, StashPath, operatorRun).ConfigureAwait(false);
                AssertTrue(manual.Succeeded);
                AssertNull(manual.StepResults[0].Tool, "a manual step is not run"); AssertContains("Manual step", manual.StepResults[0].Message ?? "");
                AssertEqual("retry_landing", f.Executor.Tools.Last());
                AssertTrue((await f.Service.EnumerateNotesAsync(f.Admin, f.EntryId, null).ConfigureAwait(false)).Objects.Any(n => n.AuthorKind == BlackBoxNoteAuthorKindEnum.Operator), "operator author kind honored");
            }));

            cases.Add(Case("refusals", "Refused: confirm without a dry run, expired token, token for another path, another caller's token, mission changed since the dry run, unknown path", TestTags.Negative, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync().ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(null)), "no dry run").ConfigureAwait(false);
                foreach (string message in new[]
                {
                    await RefusalMessageAsync(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(null))).ConfigureAwait(false),
                    await RefusalMessageAsync(() => f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest { DryRun = false })).ConfigureAwait(false)
                })
                {
                    AssertContains("dry run", message, "the refusal says to dry run first");
                    AssertFalse(message.Contains("run_blackbox_recovery", StringComparison.Ordinal) || message.Contains("apply_blackbox_recommendation", StringComparison.Ordinal), "the refusal is surface-neutral (REST, MCP, dashboard): " + message);
                }
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm("bbc_made_up")), "made-up token").ConfigureAwait(false);

                BlackBoxRecoveryRunResult dry = await DryAsync(f, PullRequestPath).ConfigureAwait(false);
                f.Service.Confirmations.Clock = () => DateTime.UtcNow.AddMinutes(11);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(dry.ConfirmToken)), "expired").ConfigureAwait(false);
                f.Service.Confirmations.Clock = () => DateTime.UtcNow;

                BlackBoxRecoveryRunResult stash = await DryAsync(f, StashPath).ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(stash.ConfirmToken)), "token for another path").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, StashPath, Confirm(stash.ConfirmToken)), "a token presented for the wrong path is used up").ConfigureAwait(false);

                BlackBoxRecoveryRunResult mine = await DryAsync(f, PullRequestPath).ConfigureAwait(false);
                AuthContext otherAdmin = BlackBoxTestKit.User("ten_bb", "usr_other_admin", true);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(otherAdmin, f.EntryId, PullRequestPath, Confirm(mine.ConfirmToken)), "another caller's token").ConfigureAwait(false);

                BlackBoxRecoveryRunResult before = await DryAsync(f, PullRequestPath).ConfigureAwait(false);
                Mission mission = (await f.Db.Missions.ReadAsync(f.MissionId).ConfigureAwait(false))!;
                mission.Title = mission.Title + " (edited)";
                await Task.Delay(20).ConfigureAwait(false);
                await f.Db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(before.ConfirmToken)), "mission changed since the dry run").ConfigureAwait(false);

                await AssertThrowsAsync<BlackBoxNotFoundException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, "bbx-land-001.no-such-path", new BlackBoxRecoveryRunRequest()), "unknown path").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, " ", new BlackBoxRecoveryRunRequest()), "blank path").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => f.Service.RunRecoveryAsync(f.Admin, "bbx_missing", PullRequestPath, new BlackBoxRecoveryRunRequest()), "unknown entry").ConfigureAwait(false);
                AssertEqual(0, f.Executor.Calls.Count, "no refused call ran anything");
            }));

            cases.Add(Case("data_loss_and_authorization", "A data-loss path needs acknowledgeDataLoss; a caller who could not call the step's tool directly is refused before anything runs", TestTags.Negative, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync(BlackBoxStepEnum.Stall, MissionFailureKindEnum.StallRecoveryExhausted).ConfigureAwait(false);
                const string unstick = "bbx-exec-002.unstick-restart";
                BlackBoxRecoveryRunResult dry = await DryAsync(f, unstick).ConfigureAwait(false);
                AssertTrue(dry.Plan[0].DataLossRisk, "unstick_dock is a data-loss step");
                AssertContains("data-loss", dry.Message ?? "", "the dry run warns");
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, unstick, Confirm(dry.ConfirmToken)), "data loss not acknowledged").ConfigureAwait(false);
                AssertEqual(0, f.Executor.Calls.Count);
                BlackBoxRecoveryRunRequest ack = Confirm(dry.ConfirmToken);
                ack.AcknowledgeDataLoss = true;
                BlackBoxRecoveryRunResult run = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, unstick, ack).ConfigureAwait(false);
                AssertTrue(run.Succeeded, run.Message);
                AssertEqual("unstick_dock,restart_mission", String.Join(",", f.Executor.Tools), "the token survived the unacknowledged attempt");
                AssertEqual(f.DockId, f.Executor.Calls.First().Arguments["dockId"]);

                AuthContext owner = BlackBoxTestKit.User("ten_bb", "usr_bb");
                await AssertThrowsAsync<UnauthorizedAccessException>(() => f.Service.RunRecoveryAsync(owner, f.EntryId, "bbx-exec-002.restart", new BlackBoxRecoveryRunRequest()), "a user who cannot call restart_mission directly").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => f.Service.RunRecoveryAsync(BlackBoxTestKit.User("ten_other", "usr_x", true), f.EntryId, "bbx-exec-002.restart", new BlackBoxRecoveryRunRequest()), "another tenant cannot see the entry").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => f.Service.RunRecoveryAsync(new AuthContext(), f.EntryId, "bbx-exec-002.restart", new BlackBoxRecoveryRunRequest()), "unauthenticated").ConfigureAwait(false);
                AssertEqual(2, f.Executor.Calls.Count, "refused callers ran nothing");
            }));

            cases.Add(Case("failures_and_no_executor", "A failed step stops the run and the rest are not run; without an executor a confirmed run is not implemented", TestTags.Negative, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync().ConfigureAwait(false);
                f.Executor.FailTools.Add("update_vessel");
                BlackBoxRecoveryRunResult dry = await DryAsync(f, PullRequestPath).ConfigureAwait(false);
                BlackBoxRecoveryRunResult run = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(dry.ConfirmToken)).ConfigureAwait(false);
                AssertFalse(run.Succeeded, "failed");
                AssertFalse(run.StepResults[0].Succeeded); AssertEqual("fake failure", run.StepResults[0].Message);
                AssertFalse(run.StepResults[1].Succeeded); AssertContains("Not run", run.StepResults[1].Message ?? "");
                AssertEqual("update_vessel", String.Join(",", f.Executor.Tools), "later steps not run");
                AssertContains("failed", (await f.Service.EnumerateNotesAsync(f.Admin, f.EntryId, null).ConfigureAwait(false)).Objects.Single().Body, "failure annotated");
                BlackBoxRecoveryRunPayload payload = (BlackBoxRecoveryRunPayload)f.Sink.Events.Last(e => e.EventType == BlackBoxEventTypes.RecoveryRun).Payload!;
                AssertFalse(payload.Succeeded); AssertEqual(1, payload.StepsFailed);

                f.Service.ActionExecutor = null;
                BlackBoxRecoveryRunResult again = await DryAsync(f, PullRequestPath).ConfigureAwait(false);
                await AssertThrowsAsync<NotImplementedException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(again.ConfirmToken)), "no executor").ConfigureAwait(false);
            }));

            cases.Add(Case("preconditions", "A restart path on a mission that is already Pending is refused on the dry run (no token) and nothing runs; a precondition that fails between the dry run and the confirm (the dock was deleted) refuses the confirmed run instead of reporting success", TestTags.Negative, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync(BlackBoxStepEnum.Stall, MissionFailureKindEnum.StallRecoveryExhausted).ConfigureAwait(false);
                const string restart = "bbx-exec-002.restart";
                const string unstick = "bbx-exec-002.unstick-restart";
                Mission mission = (await f.Db.Missions.ReadAsync(f.MissionId).ConfigureAwait(false))!;
                mission.Status = MissionStatusEnum.Pending;
                await f.Db.Missions.UpdateAsync(mission).ConfigureAwait(false);

                BlackBoxRecoveryRunResult pendingDry = await DryAsync(f, restart).ConfigureAwait(false);
                AssertTrue(pendingDry.DryRun, "dry run");
                AssertTrue(pendingDry.Refused, "refused: the mission is already Pending");
                AssertFalse(pendingDry.Succeeded, "a refused dry run is not a success");
                AssertNull(pendingDry.ConfirmToken, "no confirm token for a path that cannot run");
                AssertContains("already Pending", String.Join(" ", pendingDry.UnmetPreconditions), "the dry run names the unmet precondition");
                AssertContains("cannot run now", pendingDry.Message ?? "", "the dry run says it cannot run");
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.RunRecoveryAsync(f.Admin, f.EntryId, restart, Confirm(pendingDry.ConfirmToken)), "no token to confirm").ConfigureAwait(false);
                AssertEqual(0, f.Executor.Calls.Count, "nothing ran against the Pending mission");

                mission = (await f.Db.Missions.ReadAsync(f.MissionId).ConfigureAwait(false))!;
                mission.Status = MissionStatusEnum.Failed;
                await Task.Delay(20).ConfigureAwait(false);
                await f.Db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                BlackBoxRecoveryRunResult failedDry = await DryAsync(f, restart).ConfigureAwait(false);
                AssertFalse(failedDry.Refused, "a Failed mission can be restarted");
                AssertTrue(failedDry.Succeeded); AssertNotNull(failedDry.ConfirmToken, "token issued");
                AssertEqual(0, failedDry.UnmetPreconditions.Count, "no unmet preconditions");

                BlackBoxRecoveryRunResult unstickDry = await DryAsync(f, unstick).ConfigureAwait(false);
                AssertFalse(unstickDry.Refused, "the dock exists at the dry run");
                await f.Db.Docks.DeleteAsync(f.DockId).ConfigureAwait(false);
                BlackBoxRecoveryRunRequest ack = Confirm(unstickDry.ConfirmToken);
                ack.AcknowledgeDataLoss = true;
                BlackBoxRecoveryRunResult refused = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, unstick, ack).ConfigureAwait(false);
                AssertFalse(refused.DryRun);
                AssertTrue(refused.Refused, "refused at confirm time: " + refused.Message);
                AssertFalse(refused.Succeeded, "a refused run is not a success");
                AssertContains("dock no longer exists", String.Join(" ", refused.UnmetPreconditions));
                AssertTrue(refused.StepResults.All(r => !r.Succeeded && (r.Message ?? "").Contains("Not run", StringComparison.Ordinal)), "every step reported as not run");
                AssertEqual(0, f.Executor.Calls.Count, "nothing ran");
                AssertContains("Refused", (await f.Service.EnumerateNotesAsync(f.Admin, f.EntryId, null).ConfigureAwait(false)).Objects.Single().Body, "the refusal is annotated");
                BlackBoxRecoveryRunPayload payload = (BlackBoxRecoveryRunPayload)f.Sink.Events.Last(e => e.EventType == BlackBoxEventTypes.RecoveryRun).Payload!;
                AssertFalse(payload.Succeeded, "the event reports the refusal");

                BlackBoxAction restartAction = new BlackBoxAction { Tool = "restart_mission" };
                BlackBoxAction retryAction = new BlackBoxAction { Tool = "retry_landing" };
                BlackBoxAction cancelAction = new BlackBoxAction { Tool = "cancel_mission" };
                AssertNull(BlackBoxActionPreconditions.Check(restartAction, MissionStatusEnum.Failed, false, true, true), "restart a Failed mission");
                AssertNotNull(BlackBoxActionPreconditions.Check(restartAction, MissionStatusEnum.InProgress, false, true, true), "restart an InProgress mission");
                AssertNotNull(BlackBoxActionPreconditions.Check(restartAction, null, false, true, true), "restart a deleted mission");
                AssertNull(BlackBoxActionPreconditions.Check(retryAction, MissionStatusEnum.LandingFailed, false, true, true), "retry a LandingFailed landing");
                AssertNotNull(BlackBoxActionPreconditions.Check(retryAction, MissionStatusEnum.Pending, false, true, true), "retry the landing of a Pending mission");
                AssertNotNull(BlackBoxActionPreconditions.Check(cancelAction, MissionStatusEnum.Cancelled, false, true, true), "cancel a cancelled mission");
                MissionStatusEnum? afterCancel = BlackBoxActionPreconditions.StatusAfter("cancel_mission", MissionStatusEnum.InProgress);
                AssertNull(BlackBoxActionPreconditions.Check(restartAction, afterCancel, false, true, true), "a cancel followed by a restart is valid");
            }));

            cases.Add(Case("recommendation_apply", "A recommendation applies through its recorded update_vessel patch after a dry run; refusals mirror recovery runs", TestTags.Positive, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync().ConfigureAwait(false);
                BlackBoxRecommendationApplyResult dry = await f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest()).ConfigureAwait(false);
                AssertTrue(dry.DryRun); AssertNotNull(dry.ConfirmToken); AssertEqual("update_vessel", dry.Action!.Tool);
                AssertContains("landingMode", dry.Message ?? ""); AssertEqual(0, f.Executor.Calls.Count, "dry run ran nothing");
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest { DryRun = false }), "no token").ConfigureAwait(false);
                BlackBoxRecommendationApplyResult applied = await f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest { DryRun = false, ConfirmToken = dry.ConfirmToken }).ConfigureAwait(false);
                AssertTrue(applied.Succeeded, applied.Message);
                AssertEqual("update_vessel", String.Join(",", f.Executor.Tools)); AssertEqual("PullRequest", f.Executor.Calls.Single().Arguments["landingMode"]);
                AssertContains(LandingModeRec, (await f.Service.EnumerateNotesAsync(f.Admin, f.EntryId, null).ConfigureAwait(false)).Objects.Single().Body, "annotated");
                BlackBoxRecoveryRunPayload payload = (BlackBoxRecoveryRunPayload)f.Sink.Events.Last(e => e.EventType == BlackBoxEventTypes.RecoveryRun).Payload!;
                AssertEqual(LandingModeRec, payload.RecommendationId);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest { DryRun = false, ConfirmToken = dry.ConfirmToken }), "reused").ConfigureAwait(false);

                BlackBoxRecommendationApplyResult before = await f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest()).ConfigureAwait(false);
                Vessel vessel = (await f.Db.Vessels.ReadAsync(f.VesselId).ConfigureAwait(false))!;
                vessel.ProjectContext = "changed";
                await Task.Delay(20).ConfigureAwait(false);
                await f.Db.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest { DryRun = false, ConfirmToken = before.ConfirmToken }), "vessel changed since the dry run").ConfigureAwait(false);

                BlackBoxRecoveryRunResult pathToken = await DryAsync(f, PullRequestPath).ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxConfirmationException>(() => f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest { DryRun = false, ConfirmToken = pathToken.ConfirmToken }), "a recovery token does not apply a recommendation").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => f.Service.ApplyRecommendationAsync(f.Admin, f.EntryId, "bbx-land-001.nope", new BlackBoxRecommendationApplyRequest()), "unknown recommendation").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => f.Service.ApplyRecommendationAsync(BlackBoxTestKit.User("ten_bb", "usr_bb"), f.EntryId, LandingModeRec, new BlackBoxRecommendationApplyRequest()), "unauthorized").ConfigureAwait(false);
                AssertEqual(1, f.Executor.Calls.Count, "refusals ran nothing");

                using BlackBoxRecoveryFixture stall = await BlackBoxRecoveryFixture.CreateAsync(BlackBoxStepEnum.Stall, MissionFailureKindEnum.StallRecoveryExhausted).ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => stall.Service.ApplyRecommendationAsync(stall.Admin, stall.EntryId, "bbx-exec-002.stall-threshold", new BlackBoxRecommendationApplyRequest()), "advice-only recommendation").ConfigureAwait(false);
            }));

            cases.Add(Case("confirmation_store", "Confirm tokens are random, single use, bounded and pruned on expiry", TestTags.Negative, () =>
            {
                BlackBoxConfirmationStore store = new BlackBoxConfirmationStore { MaxPending = 10 };
                AuthContext caller = BlackBoxTestKit.Admin();
                BlackBoxPendingConfirmation a = store.Issue(BlackBoxConfirmationStore.RecoveryKind, "bbx_1", "p", "v", "h", caller);
                BlackBoxPendingConfirmation b = store.Issue(BlackBoxConfirmationStore.RecoveryKind, "bbx_1", "p", "v", "h", caller);
                AssertNotEqual(a.Token, b.Token, "random");
                AssertEqual("v", store.Consume(a.Token, BlackBoxConfirmationStore.RecoveryKind, "bbx_1", "p", caller).SubjectVersion);
                AssertThrows<BlackBoxConfirmationException>(() => store.Consume(a.Token, BlackBoxConfirmationStore.RecoveryKind, "bbx_1", "p", caller), "single use");
                AssertThrows<BlackBoxConfirmationException>(() => store.Consume(b.Token, BlackBoxConfirmationStore.RecommendationKind, "bbx_1", "p", caller), "kind is bound");
                for (int i = 0; i < 30; i++) store.Issue(BlackBoxConfirmationStore.RecoveryKind, "bbx_" + i, "p", "v", "h", caller);
                AssertTrue(store.Count <= 10, "bounded: " + store.Count);
                store.Clock = () => DateTime.UtcNow.AddHours(2);
                store.Issue(BlackBoxConfirmationStore.RecoveryKind, "bbx_late", "p", "v", "h", caller);
                AssertEqual(1, store.Count, "expired tokens pruned");
                store.Lifetime = TimeSpan.FromDays(3);
                AssertEqual(TimeSpan.FromHours(1), store.Lifetime, "lifetime clamped");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box recovery runs and recommendations", cases: cases);
        }

        private static Task<BlackBoxRecoveryRunResult> DryAsync(BlackBoxRecoveryFixture f, string pathId)
        {
            return f.Service.RunRecoveryAsync(f.Admin, f.EntryId, pathId, new BlackBoxRecoveryRunRequest());
        }

        private static async Task<string> RefusalMessageAsync(Func<Task> call)
        {
            try
            {
                await call().ConfigureAwait(false);
            }
            catch (BlackBoxConfirmationException e)
            {
                return e.Message;
            }
            throw new InvalidOperationException("expected a BlackBoxConfirmationException");
        }

        private static BlackBoxRecoveryRunRequest Confirm(string? token)
        {
            return new BlackBoxRecoveryRunRequest { DryRun = false, ConfirmToken = token };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
