namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Approve in Armada for pi, end to end against the real server: the shipped pi permission gate extension (run under
    /// node) calls cli_permission_prompt with an Ask thread's scoped token; the request shows up pending in the approvals
    /// list and as a card in the Ask conversation; an admin's allow lets the command run and a deny blocks it with the
    /// approver's message. Skipped when node is not on PATH.
    /// </summary>
    public sealed class PiPermissionGateE2ESuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.PiPermissionGate";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            string? skip = PiPermissionGateHarness.SkipReason();
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "pi_gate_prompt_reaches_dashboard_and_ask",
                displayName: "The pi gate's prompt appears in Approvals and the Ask thread; allow runs the command, deny blocks it with the approver's message",
                executeAsync: (CancellationToken ct) => RunAsync(),
                tags: new List<string> { TestTags.EndToEnd },
                skipReason: skip));

            return new TestSuiteDescriptor(SuiteId, "pi permission gate (E2E)", cases);
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync()
        {
            E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
            E2ETenantUser admin = await E2ETenantUser.CreateAsync(fx.AuthClient, "pig-admin", true).ConfigureAwait(false);
            E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "pig-owner", false, admin.TenantId).ConfigureAwait(false);
            using HttpClient a = admin.CreateClient(fx.BaseUrl);
            using HttpClient o = owner.CreateClient(fx.BaseUrl);
            AskThread thread = await JsonHelper.DeserializeAsync<AskThread>(await o.PostAsync("/api/v1/ask/threads", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
            string token = new SessionTokenService(fx.SessionTokenEncryptionKey).CreateThreadScopedToken(owner.TenantId, owner.UserId, thread.Id, TimeSpan.FromMinutes(10)).Token!;
            string url = "http://127.0.0.1:" + fx.McpPort + "/mcp";

            // Allow.
            Task<List<PiGateOutcome>> allowRun = PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("git status --short", "pi_call_1") }, url, token, 120);
            CliPermissionRequest first = await WaitForPendingAsync(a, thread.Id, "git status --short", allowRun).ConfigureAwait(false);
            AssertEqual("Bash", first.ToolName, "pi's bash is reported as Bash");
            AssertTrue(first.CanDecide, "the tenant admin can decide it");
            AskThreadDetail detail = await JsonHelper.DeserializeAsync<AskThreadDetail>(await o.GetAsync("/api/v1/ask/threads/" + thread.Id).ConfigureAwait(false)).ConfigureAwait(false);
            AssertTrue(detail.PendingCliPermissionCount >= 1, "pending counted on the Ask thread");
            EnumerationResult<CliPermissionRequest> threadPending = await JsonHelper.DeserializeAsync<EnumerationResult<CliPermissionRequest>>(await o.PostAsync("/api/v1/cli-permissions/requests/enumerate", JsonHelper.ToJsonContent(new { ThreadId = thread.Id, Status = "Pending" })).ConfigureAwait(false)).ConfigureAwait(false);
            AssertTrue(threadPending.Objects.Any(p => p.Id == first.Id), "pending on the Ask thread");
            AskMessagePage page = await JsonHelper.DeserializeAsync<AskMessagePage>(await o.PostAsync("/api/v1/ask/threads/" + thread.Id + "/messages/enumerate", JsonHelper.ToJsonContent(new { })).ConfigureAwait(false)).ConfigureAwait(false);
            AssertTrue(page.Messages.Any(m => m.Kind == AskMessageKindEnum.CliPermission && m.CliPermissionRequest?.Id == first.Id), "the Ask conversation shows the approval card");
            AssertFalse(allowRun.IsCompleted, "the tool waits for the decision");
            AssertStatusCode(HttpStatusCode.OK, await a.PostAsync("/api/v1/cli-permissions/requests/" + first.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "AllowOnce" })).ConfigureAwait(false));
            List<PiGateOutcome> allowed = await allowRun.ConfigureAwait(false);
            AssertTrue(allowed[0].Allowed, "approved: the command runs");

            // Deny.
            Task<List<PiGateOutcome>> denyRun = PiPermissionGateHarness.RunAsync(new List<PiGateEvent> { PiGateEvent.Bash("git push origin main", "pi_call_2") }, url, token, 120);
            CliPermissionRequest second = await WaitForPendingAsync(a, thread.Id, "git push origin main", denyRun).ConfigureAwait(false);
            AssertStatusCode(HttpStatusCode.OK, await a.PostAsync("/api/v1/cli-permissions/requests/" + second.Id + "/decide", JsonHelper.ToJsonContent(new { Decision = "Deny", Message = "no pushes from captains" })).ConfigureAwait(false));
            List<PiGateOutcome> denied = await denyRun.ConfigureAwait(false);
            AssertFalse(denied[0].Allowed, "denied: the command does not run");
            AssertContains("no pushes from captains", denied[0].Result!.Reason ?? "", "the approver's message reaches the model");

            List<CliPermissionRequest> recorded = (await JsonHelper.DeserializeAsync<EnumerationResult<CliPermissionRequest>>(await a.PostAsync("/api/v1/cli-permissions/requests/enumerate", JsonHelper.ToJsonContent(new { ThreadId = thread.Id, PageSize = 1000 })).ConfigureAwait(false)).ConfigureAwait(false)).Objects;
            AssertEqual(CliPermissionRequestStatusEnum.Allowed, recorded.Single(r => r.Id == first.Id).Status);
            AssertEqual(CliPermissionRequestStatusEnum.Denied, recorded.Single(r => r.Id == second.Id).Status);
        }

        private static async Task<CliPermissionRequest> WaitForPendingAsync(HttpClient client, string threadId, string summary, Task run)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (run.IsCompleted)
                {
                    string detail = run.Exception != null ? run.Exception.GetBaseException().Message : "completed: " + String.Join(", ", ((Task<List<PiGateOutcome>>)run).Result.Select(r => r.Result?.Reason ?? "allowed"));
                    AssertTrue(false, "the gate finished before its request was pending: " + detail);
                }

                HttpResponseMessage resp = await client.PostAsync("/api/v1/cli-permissions/requests/enumerate", JsonHelper.ToJsonContent(new { Status = "Pending", ThreadId = threadId, PageSize = 1000 })).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    List<CliPermissionRequest> rows = (await JsonHelper.DeserializeAsync<EnumerationResult<CliPermissionRequest>>(resp).ConfigureAwait(false)).Objects;
                    CliPermissionRequest? found = CliPermissionPendingWait.FirstWithCard(rows.Where(r => r.SummaryText == summary).ToList());
                    if (found != null) return found;
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            AssertTrue(false, "a pending request for '" + summary + "' appeared within 30 s");
            throw new InvalidOperationException("unreachable");
        }

        #endregion
    }
}
