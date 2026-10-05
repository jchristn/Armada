namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The CLI permission prompt flow on a real database: a prompt waits as a pending request (with an Ask card) until
    /// an approver allows or denies it, it times out, or its session ends; rules allow or deny without waiting; and the
    /// authorization rules (admins decide, owners only with Permissions.AllowOwnerApproval, captain sessions never,
    /// remembering and Bypass only for admins), the inbox kind, and the Ask turn wiring.
    /// </summary>
    public sealed class CliPermissionServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CliPermissionService";
        private const string BashInput = "{\"command\":\"git push origin main\",\"description\":\"push\"}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("prompt_waits_until_an_admin_allows", "A prompt is stored as Pending with an Ask card and returns allowed once an admin allows it", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                AuthContext owner = AskTestHarness.User("usr_cpo1", false);
                Captain captain = await h.Db.Driver.Captains.CreateAsync(new Captain("cp-cap-1") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);

                Task<CliPermissionPromptOutcome> prompt = svc.PromptAsync(ThreadContext(thread, captain), "Bash", BashInput);
                CliPermissionRequest pending = await WaitForPendingAsync(h, thread.Id).ConfigureAwait(false);
                AssertFalse(prompt.IsCompleted, "the captain is waiting");
                AssertEqual(1, svc.WaitingCount);
                AssertEqual("Bash", pending.ToolName);
                AssertEqual("git push origin main", pending.SummaryText);
                AssertEqual("Bash(git push:*)", pending.SuggestedRule);
                AssertEqual(owner.UserId, pending.UserId);
                AssertEqual(captain.Id, pending.CaptainId);
                AssertTrue(pending.ExpiresUtc > DateTime.UtcNow, "expires in the future");
                AssertNotNull(pending.MessageId, "Ask card linked");
                AskMessage? card = await h.Db.Driver.AskMessages.ReadAsync(Constants.DefaultTenantId, pending.MessageId!).ConfigureAwait(false);
                AssertEqual(AskMessageKindEnum.CliPermission, card!.Kind);
                AssertTrue(h.EventsFor(owner.UserId!, "ask.message").Count > 0, "card announced to the owner");

                CliPermissionRequest asOwner = await svc.GetAsync(owner, pending.Id).ConfigureAwait(false);
                AssertFalse(asOwner.CanDecide, "the owner cannot decide by default");
                AuthContext admin = TenantAdmin("usr_cpa1");
                CliPermissionRequest asAdmin = await svc.GetAsync(admin, pending.Id).ConfigureAwait(false);
                AssertTrue(asAdmin.CanDecide && asAdmin.CanRemember, "a tenant admin can decide and remember");
                AssertEqual(captain.Name, asAdmin.CaptainName);

                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, new AskMessageEnumerateRequest()).ConfigureAwait(false))!;
                AskMessage hydrated = page.Messages.Single(m => m.Id == pending.MessageId);
                AssertEqual(pending.Id, hydrated.CliPermissionRequest!.Id, "card hydrated with its request");

                CliPermissionRequest decided = await svc.DecideAsync(admin, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowOnce }).ConfigureAwait(false);
                AssertEqual(CliPermissionRequestStatusEnum.Allowed, decided.Status);
                AssertEqual(CliPermissionDecisionSourceEnum.Approver, decided.DecisionSource);
                AssertEqual(admin.UserId, decided.DecidedByUserId);
                CliPermissionPromptOutcome outcome = await prompt.ConfigureAwait(false);
                AssertTrue(outcome.Allowed, "the waiting prompt is allowed");
                AssertEqual(0, svc.WaitingCount);
                AssertEqual(0, (await h.Db.Driver.CliPermissionRules.EnumerateAsync(new CliPermissionRuleQuery()).ConfigureAwait(false)).Count, "allow once stores no rule");
                await AssertThrowsAsync<InvalidOperationException>(() => svc.DecideAsync(admin, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.Deny }), "a second decision is a conflict").ConfigureAwait(false);
            }));

            cases.Add(Case("deny_returns_the_reason", "A denial returns deny with the approver's reason", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                AskThread thread = await NewThreadAsync(h, "usr_cpo2").ConfigureAwait(false);
                Task<CliPermissionPromptOutcome> prompt = svc.PromptAsync(ThreadContext(thread, null), "Bash", BashInput);
                CliPermissionRequest pending = await WaitForPendingAsync(h, thread.Id).ConfigureAwait(false);
                await svc.DecideAsync(TenantAdmin("usr_cpa2"), pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.Deny, Message = "no pushes on Fridays" }).ConfigureAwait(false);
                CliPermissionPromptOutcome outcome = await prompt.ConfigureAwait(false);
                AssertFalse(outcome.Allowed);
                AssertContains("no pushes on Fridays", outcome.Message);
                AssertEqual(CliPermissionRequestStatusEnum.Denied, outcome.Request!.Status);
            }));

            cases.Add(Case("timeout_denies", "With no decision before the timeout the prompt is denied as Expired", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                svc.PromptTimeoutOverride = TimeSpan.FromMilliseconds(300);
                AskThread thread = await NewThreadAsync(h, "usr_cpo3").ConfigureAwait(false);
                CliPermissionPromptOutcome outcome = await svc.PromptAsync(ThreadContext(thread, null), "Bash", BashInput).ConfigureAwait(false);
                AssertFalse(outcome.Allowed, "denied on timeout");
                AssertEqual(CliPermissionRequestStatusEnum.Expired, outcome.Request!.Status);
                AssertEqual(CliPermissionDecisionSourceEnum.Timeout, outcome.Request.DecisionSource);
                AssertContains("denied", outcome.Message);
                await AssertThrowsAsync<InvalidOperationException>(() => svc.DecideAsync(TenantAdmin("usr_cpa3"), outcome.Request.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowOnce }), "cannot allow after expiry").ConfigureAwait(false);
            }));

            cases.Add(Case("rules_allow_and_deny_without_waiting", "A matching allow rule allows and a matching deny rule denies at once; both are recorded", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                AuthContext admin = TenantAdmin("usr_cpa4");
                Captain captain = await h.Db.Driver.Captains.CreateAsync(new Captain("cp-cap-4") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                CliPermissionRule allow = await svc.CreateRuleAsync(admin, new CliPermissionRule { Pattern = "Bash(git status:*)", Action = CliPermissionRuleActionEnum.Allow, Scope = CliPermissionRuleScopeEnum.Captain, CaptainId = captain.Id }).ConfigureAwait(false);
                CliPermissionRule deny = await svc.CreateRuleAsync(admin, new CliPermissionRule { Pattern = "Bash(rm:*)", Action = CliPermissionRuleActionEnum.Deny }).ConfigureAwait(false);
                AssertEqual(Constants.DefaultTenantId, deny.TenantId, "a tenant admin's rule belongs to the tenant");
                AskThread thread = await NewThreadAsync(h, "usr_cpo4").ConfigureAwait(false);

                CliPermissionPromptOutcome allowed = await svc.PromptAsync(ThreadContext(thread, captain), "Bash", "{\"command\":\"git status\"}").ConfigureAwait(false);
                AssertTrue(allowed.Allowed, "allow rule");
                AssertEqual(CliPermissionDecisionSourceEnum.AllowRule, allowed.Request!.DecisionSource);
                AssertEqual(allow.Id, allowed.Request.RuleId);

                CliPermissionPromptOutcome denied = await svc.PromptAsync(ThreadContext(thread, captain), "Bash", "{\"command\":\"git status && rm -rf /\"}").ConfigureAwait(false);
                AssertFalse(denied.Allowed, "deny rule");
                AssertEqual(CliPermissionDecisionSourceEnum.DenyRule, denied.Request!.DecisionSource);
                AssertEqual(deny.Id, denied.Request.RuleId);
                AssertContains("Bash(rm:*)", denied.Message);

                CliPermissionPromptOutcome otherCaptain = await PromptWithQuickDecisionAsync(svc, h, ThreadContext(thread, null), "Bash", "{\"command\":\"git status\"}", admin, CliPermissionDecisionEnum.Deny).ConfigureAwait(false);
                AssertFalse(otherCaptain.Allowed, "a Captain rule does not apply to another captain");
                AssertEqual(0, (await h.Db.Driver.CliPermissionRequests.EnumerateAsync(new CliPermissionRequestQuery { Status = CliPermissionRequestStatusEnum.Pending }).ConfigureAwait(false)).Count);
                AssertEqual(3, (await h.Db.Driver.CliPermissionRequests.EnumerateAsync(new CliPermissionRequestQuery { ThreadId = thread.Id }).ConfigureAwait(false)).Count, "every prompt is recorded");
                await AssertThrowsAsync<ArgumentException>(() => svc.CreateRuleAsync(admin, new CliPermissionRule { Pattern = "Bash(", Action = CliPermissionRuleActionEnum.Allow }), "invalid pattern").ConfigureAwait(false);
            }));

            cases.Add(Case("owner_approval_follows_the_setting", "The owner may decide only with Permissions.AllowOwnerApproval, and never remember", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                AuthContext owner = AskTestHarness.User("usr_cpo5", false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                Task<CliPermissionPromptOutcome> prompt = svc.PromptAsync(ThreadContext(thread, null), "Bash", BashInput);
                CliPermissionRequest pending = await WaitForPendingAsync(h, thread.Id).ConfigureAwait(false);

                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.DecideAsync(owner, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowOnce }), "owner refused by default").ConfigureAwait(false);
                h.Settings.Permissions.AllowOwnerApproval = true;
                AssertTrue((await svc.GetAsync(owner, pending.Id).ConfigureAwait(false)).CanDecide, "owner can decide with the setting");
                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.DecideAsync(owner, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowAndRemember }), "owner cannot remember").ConfigureAwait(false);
                AuthContext otherUser = AskTestHarness.User("usr_cpx5", false);
                await AssertThrowsAsync<KeyNotFoundException>(() => svc.DecideAsync(otherUser, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowOnce }), "another user does not see it").ConfigureAwait(false);
                await svc.DecideAsync(owner, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowOnce }).ConfigureAwait(false);
                AssertTrue((await prompt.ConfigureAwait(false)).Allowed, "allowed by the owner");
            }));

            cases.Add(Case("captain_sessions_never_decide", "A caller with a mission- or thread-scoped session cannot see, decide, or remember a request, even as an admin", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                AskThread thread = await NewThreadAsync(h, "usr_cpo6").ConfigureAwait(false);
                Task<CliPermissionPromptOutcome> prompt = svc.PromptAsync(ThreadContext(thread, null), "Bash", BashInput);
                CliPermissionRequest pending = await WaitForPendingAsync(h, thread.Id).ConfigureAwait(false);

                AuthContext threadCaptain = AuthContext.Authenticated(Constants.DefaultTenantId, "usr_cpo6", true, true, "Token");
                threadCaptain.AskThreadId = thread.Id;
                AuthContext missionCaptain = AuthContext.Authenticated(Constants.DefaultTenantId, "usr_cpo6", false, true, "Token");
                missionCaptain.MissionId = "msn_x";
                foreach (AuthContext captain in new[] { threadCaptain, missionCaptain })
                {
                    await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.DecideAsync(captain, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowOnce }), "captain session").ConfigureAwait(false);
                    AssertEqual(0, (await svc.ListAsync(captain, null).ConfigureAwait(false)).Count, "captain sessions list nothing");
                    await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.CreateRuleAsync(captain, new CliPermissionRule { Pattern = "Bash", Action = CliPermissionRuleActionEnum.Allow }), "captain cannot create rules").ConfigureAwait(false);
                }

                AssertFalse(CliPermissionAccess.CanDecide(threadCaptain, pending, new Armada.Core.Settings.CliPermissionSettings { AllowOwnerApproval = true }), "owner approval does not extend to the captain session");
                await svc.CancelPendingAsync(thread.Id, null).ConfigureAwait(false);
                CliPermissionPromptOutcome outcome = await prompt.ConfigureAwait(false);
                AssertFalse(outcome.Allowed, "cancelled when the turn ends");
                AssertEqual(CliPermissionRequestStatusEnum.Cancelled, outcome.Request!.Status);
            }));

            cases.Add(Case("tenant_scoping", "A tenant admin of another tenant neither sees nor decides a request; a global admin does", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                AskThread thread = await NewThreadAsync(h, "usr_cpo7").ConfigureAwait(false);
                Task<CliPermissionPromptOutcome> prompt = svc.PromptAsync(ThreadContext(thread, null), "WebFetch", "{\"url\":\"https://example.com/x\"}");
                CliPermissionRequest pending = await WaitForPendingAsync(h, thread.Id).ConfigureAwait(false);

                AuthContext stranger = AuthContext.Authenticated("ten_other", "usr_other", false, true, "Test");
                await AssertThrowsAsync<KeyNotFoundException>(() => svc.GetAsync(stranger, pending.Id), "invisible across tenants").ConfigureAwait(false);
                AssertEqual(0, (await svc.ListAsync(stranger, null).ConfigureAwait(false)).Count);
                AuthContext global = AuthContext.Authenticated("ten_other", "usr_global", true, true, "Test");
                AssertEqual(1, (await svc.ListAsync(global, new CliPermissionRequestQuery { ThreadId = thread.Id }).ConfigureAwait(false)).Count, "global admin sees every tenant");
                await svc.DecideAsync(global, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowAndRemember, RuleScope = CliPermissionRuleScopeEnum.Global }).ConfigureAwait(false);
                AssertTrue((await prompt.ConfigureAwait(false)).Allowed);
                CliPermissionRule remembered = (await h.Db.Driver.CliPermissionRules.EnumerateAsync(new CliPermissionRuleQuery()).ConfigureAwait(false)).Single();
                AssertEqual("WebFetch(domain:example.com)", remembered.Pattern, "remembered with the suggested rule");
                AssertEqual(Constants.DefaultTenantId, remembered.TenantId, "a remembered rule belongs to the request's tenant");
                AssertEqual(CliPermissionRuleScopeEnum.Global, remembered.Scope);

                CliPermissionPromptOutcome next = await svc.PromptAsync(ThreadContext(thread, null), "WebFetch", "{\"url\":\"https://example.com/other\"}").ConfigureAwait(false);
                AssertTrue(next.Allowed, "the remembered rule allows the next call without waiting");
                AssertEqual(remembered.Id, next.Request!.RuleId);
                AssertEqual(0, (await svc.ListRulesAsync(stranger, null).ConfigureAwait(false)).Count, "another tenant does not see the rule");
                await AssertThrowsAsync<KeyNotFoundException>(() => svc.DeleteRuleAsync(stranger, remembered.Id), "another tenant cannot delete it").ConfigureAwait(false);
            }));

            cases.Add(Case("bypass_only_for_admins", "Only admins set Bypass on a thread; captain policies need an admin of the captain's tenant", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                AuthContext user = AskTestHarness.User("usr_cpo8", false);
                AskThread thread = await h.Threads.CreateThreadAsync(user, null).ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.SetThreadPolicyAsync(user, thread.Id, CliPermissionPolicyEnum.Bypass), "a user cannot set Bypass").ConfigureAwait(false);
                AskThread refused = await svc.SetThreadPolicyAsync(user, thread.Id, CliPermissionPolicyEnum.Refuse).ConfigureAwait(false);
                AssertEqual(CliPermissionPolicyEnum.Refuse, refused.CliPermissionPolicy);
                await svc.SetThreadPolicyAsync(user, thread.Id, null).ConfigureAwait(false);
                AssertNull((await h.Db.Driver.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false))!.CliPermissionPolicy, "cleared");
                await AssertThrowsAsync<KeyNotFoundException>(() => svc.SetThreadPolicyAsync(AskTestHarness.User("usr_cpz8", true), thread.Id, CliPermissionPolicyEnum.Refuse), "only the owner").ConfigureAwait(false);

                AuthContext adminOwner = AskTestHarness.User("usr_cpa8", true);
                AskThread adminThread = await h.Threads.CreateThreadAsync(adminOwner, null).ConfigureAwait(false);
                AskThread bypassed = await svc.SetThreadPolicyAsync(adminOwner, adminThread.Id, CliPermissionPolicyEnum.Bypass).ConfigureAwait(false);
                AssertEqual(CliPermissionPolicyEnum.Bypass, bypassed.CliPermissionPolicy, "a tenant admin may set Bypass on their own thread");

                Captain captain = await h.Db.Driver.Captains.CreateAsync(new Captain("cp-cap-8") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.SetCaptainPolicyAsync(user, captain.Id, CliPermissionPolicyEnum.Bypass), "a user cannot change a captain").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.SetCaptainPolicyAsync(user, captain.Id, CliPermissionPolicyEnum.Refuse), "a user cannot change a captain at all").ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => svc.SetCaptainPolicyAsync(AuthContext.Authenticated("ten_other", "usr_o", false, true, "Test"), captain.Id, CliPermissionPolicyEnum.Bypass), "another tenant's admin").ConfigureAwait(false);
                Captain updated = await svc.SetCaptainPolicyAsync(adminOwner, captain.Id, CliPermissionPolicyEnum.Bypass).ConfigureAwait(false);
                AssertEqual(CliPermissionPolicyEnum.Bypass, updated.CliPermissionPolicy);

                updated.Name = "renamed";
                updated.CliPermissionPolicy = null;
                await h.Db.Driver.Captains.UpdateAsync(updated).ConfigureAwait(false);
                AssertEqual(CliPermissionPolicyEnum.Bypass, (await h.Db.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.CliPermissionPolicy, "an ordinary captain update keeps the policy");
            }));

            cases.Add(Case("sweep_resolves_orphaned_requests", "A pending request no call is waiting for (after a restart) is cancelled by the sweep", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                CliPermissionRequest orphan = new CliPermissionRequest { TenantId = Constants.DefaultTenantId, UserId = "usr_cpo9", ToolName = "Bash", MissionId = "msn_orphan", ExpiresUtc = DateTime.UtcNow.AddMinutes(5) };
                await h.Db.Driver.CliPermissionRequests.CreateAsync(orphan).ConfigureAwait(false);
                AssertEqual(1, await svc.SweepAsync().ConfigureAwait(false));
                CliPermissionRequest? swept = await h.Db.Driver.CliPermissionRequests.ReadAsync(orphan.Id).ConfigureAwait(false);
                AssertEqual(CliPermissionRequestStatusEnum.Cancelled, swept!.Status);
                AssertEqual(0, await svc.SweepAsync().ConfigureAwait(false), "nothing left");
            }));

            cases.Add(Case("inbox_lists_cli_permission_requests", "The inbox has a cli_permission item (Warning) for admins and the owner while the request is pending", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                AuthContext owner = AskTestHarness.User("usr_cpo10", false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                Task<CliPermissionPromptOutcome> prompt = svc.PromptAsync(ThreadContext(thread, null), "Bash", BashInput);
                CliPermissionRequest pending = await WaitForPendingAsync(h, thread.Id).ConfigureAwait(false);
                InboxService inbox = new InboxService(h.Db.Driver, h.Logging, h.Settings);

                AuthContext admin = TenantAdmin("usr_cpa10");
                InboxItem adminItem = (await inbox.GetInboxAsync(admin).ConfigureAwait(false)).Single(i => i.Kind == InboxItemKinds.CliPermission);
                AssertEqual(InboxSeverityEnum.Warning, adminItem.Severity);
                AssertEqual(pending.Id, adminItem.EntityId);
                AssertTrue(adminItem.CliPermission!.CanDecide, "admin can decide");
                AssertNotNull(adminItem.ExpiresUtc, "expiry for a countdown");
                AssertEqual("/cli-permissions?request=" + pending.Id, adminItem.Href);
                InboxItem ownerItem = (await inbox.GetInboxAsync(owner).ConfigureAwait(false)).Single(i => i.Kind == InboxItemKinds.CliPermission);
                AssertFalse(ownerItem.CliPermission!.CanDecide, "owner sees it but cannot decide");
                AssertEqual("/ask/" + thread.Id, ownerItem.Href);
                AssertFalse((await inbox.GetInboxAsync(AskTestHarness.User("usr_cpq10", false)).ConfigureAwait(false)).Any(i => i.Kind == InboxItemKinds.CliPermission), "other users do not see it");

                await svc.DecideAsync(admin, pending.Id, new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.Deny }).ConfigureAwait(false);
                await prompt.ConfigureAwait(false);
                AssertFalse((await inbox.GetInboxAsync(admin).ConfigureAwait(false)).Any(i => i.Kind == InboxItemKinds.CliPermission), "gone once decided");
            }));

            cases.Add(Case("ask_turns_carry_the_policy", "Ask turns hand the runner the resolved policy, persist permission denials, and cancel pending prompts when the turn ends", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                CliPermissionService svc = Service(h);
                h.Turns.CliPermissions = svc;
                AuthContext owner = AskTestHarness.User("usr_cpo11", true);
                Captain captain = await h.Db.Driver.Captains.CreateAsync(new Captain("cp-cap-11") { TenantId = Constants.DefaultTenantId, Runtime = AgentRuntimeEnum.ClaudeCode }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AskThread? read = await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, read!.CliPermission!.Effective, "thread reports its resolved policy");

                h.Runner.ToolCalls = new List<AskMessageToolCall> { new AskMessageToolCall { CallId = "toolu_9", ToolName = "Bash", Ok = false, ResultText = "refused", PermissionDenied = true } };
                CaptainChatTurnOptions first = await RunTurnAsync(h, owner, thread.Id).ConfigureAwait(false);
                AssertEqual(CliPermissionPolicyEnum.ApproveInArmada, first.CliPermissionPolicy);
                AssertFalse(CaptainRuntimeOptions.GetAutoApprove(first.Captain), "no bypass flag");
                AssertEqual(h.Settings.Permissions.PromptTimeoutSeconds, first.PermissionPromptTimeoutSeconds);
                AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, thread.Id, new AskMessageEnumerateRequest()).ConfigureAwait(false))!;
                AskMessageToolCall persisted = page.Messages.SelectMany(m => m.ToolCalls).Single(c => c.CallId == "toolu_9");
                AssertEqual(true, persisted.PermissionDenied, "permission denial persisted with the reply");

                await svc.SetThreadPolicyAsync(owner, thread.Id, CliPermissionPolicyEnum.Bypass).ConfigureAwait(false);
                h.Runner.ToolCalls = new List<AskMessageToolCall>();
                CaptainChatTurnOptions second = await RunTurnAsync(h, owner, thread.Id).ConfigureAwait(false);
                AssertEqual(CliPermissionPolicyEnum.Bypass, second.CliPermissionPolicy);
                AssertTrue(CaptainRuntimeOptions.GetAutoApprove(second.Captain), "Bypass launches with the bypass flag");

                await svc.SetThreadPolicyAsync(owner, thread.Id, null).ConfigureAwait(false);
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "push it" }).ConfigureAwait(false);
                Task<CliPermissionPromptOutcome> prompt = svc.PromptAsync(ThreadContext(thread, captain), "Bash", BashInput);
                await WaitForPendingAsync(h, thread.Id).ConfigureAwait(false);
                h.Runner.Gate.TrySetResult(true);
                CliPermissionPromptOutcome outcome = await prompt.ConfigureAwait(false);
                AssertEqual(CliPermissionRequestStatusEnum.Cancelled, outcome.Request!.Status, "the turn's end cancels its pending prompt");
            }));

            cases.Add(Case("ask_gate_exempts_the_prompt_and_refuses_decisions", "In an Ask thread the prompt tool runs without a proposal and decision tools are refused, never proposed", async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AskTestHarness.User("usr_cpo12", true);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, null).ConfigureAwait(false);
                h.RegisterStub("cli_permission_prompt", _ => new { behavior = "allow" });
                h.RegisterStub("decide_cli_permission_request", _ => new { decided = true });
                h.RegisterStub("create_cli_permission_rule", _ => new { created = true });

                object prompt = await h.CallAsync("cli_permission_prompt", "{\"tool_name\":\"Bash\",\"input\":{}}", owner, thread.Id).ConfigureAwait(false);
                AssertFalse(prompt is string, "the prompt tool ran (no proposal text)");
                AssertEqual(1, h.Invocations.Count(i => i.ToolName == "cli_permission_prompt"));

                foreach (string tool in new[] { "decide_cli_permission_request", "create_cli_permission_rule" })
                {
                    object refused = await h.CallAsync(tool, "{}", owner, thread.Id).ConfigureAwait(false);
                    AssertTrue(refused is McpToolError, tool + " refused");
                    AssertEqual(McpToolErrorCodeEnum.Forbidden, ((McpToolError)refused).ErrorCode);
                    AssertEqual(0, h.Invocations.Count(i => i.ToolName == tool), tool + " never ran");
                }

                AssertEqual(0, (await h.Db.Driver.AskActionProposals.EnumerateByThreadAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false)).Count, "nothing proposed");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "CLI permission service", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static CliPermissionService Service(AskTestHarness h)
        {
            CliPermissionService svc = new CliPermissionService(h.Db.Driver, h.Settings, h.Logging);
            svc.AskThreads = h.Threads;
            return svc;
        }

        private static AuthContext TenantAdmin(string userId)
        {
            return AskTestHarness.User(userId, true);
        }

        private static async Task<AskThread> NewThreadAsync(AskTestHarness h, string userId)
        {
            return await h.Threads.CreateThreadAsync(AskTestHarness.User(userId, false), null).ConfigureAwait(false);
        }

        private static CliPermissionPromptContext ThreadContext(AskThread thread, Captain? captain)
        {
            return new CliPermissionPromptContext
            {
                TenantId = thread.TenantId,
                UserId = thread.UserId,
                ThreadId = thread.Id,
                CaptainId = captain?.Id,
                Runtime = AgentRuntimeEnum.ClaudeCode
            };
        }

        private static async Task<CliPermissionRequest> WaitForPendingAsync(AskTestHarness h, string threadId)
        {
            CliPermissionRequest? found = null;
            bool ok = await AskTestHarness.WaitUntilAsync(async () =>
            {
                List<CliPermissionRequest> pending = await h.Db.Driver.CliPermissionRequests.EnumerateAsync(new CliPermissionRequestQuery { ThreadId = threadId, Status = CliPermissionRequestStatusEnum.Pending }).ConfigureAwait(false);
                found = pending.FirstOrDefault(p => !String.IsNullOrEmpty(p.MessageId));
                return found != null;
            }, 10000).ConfigureAwait(false);
            AssertTrue(ok, "a pending request appeared");
            return found!;
        }

        private static async Task<CliPermissionPromptOutcome> PromptWithQuickDecisionAsync(CliPermissionService svc, AskTestHarness h, CliPermissionPromptContext context, string tool, string input, AuthContext admin, CliPermissionDecisionEnum decision)
        {
            Task<CliPermissionPromptOutcome> prompt = svc.PromptAsync(context, tool, input);
            CliPermissionRequest pending = await WaitForPendingAsync(h, context.ThreadId!).ConfigureAwait(false);
            await svc.DecideAsync(admin, pending.Id, new CliPermissionDecisionRequest { Decision = decision }).ConfigureAwait(false);
            return await prompt.ConfigureAwait(false);
        }

        private static async Task<CaptainChatTurnOptions> RunTurnAsync(AskTestHarness h, AuthContext owner, string threadId)
        {
            int before;
            lock (h.Runner.Calls) before = h.Runner.Calls.Count;
            h.Runner.Reply = "ok";
            await h.Turns.SendMessageAsync(owner, threadId, new AskMessageSendRequest { Content = "hello" }).ConfigureAwait(false);
            bool done = await AskTestHarness.WaitUntilAsync(() =>
            {
                int count;
                lock (h.Runner.Calls) count = h.Runner.Calls.Count;
                return Task.FromResult(count > before && h.Turns.ActiveTurnId(threadId) == null);
            }, 10000).ConfigureAwait(false);
            AssertTrue(done, "turn finished");
            lock (h.Runner.Calls) return h.Runner.Calls.Last();
        }

        private static async Task AssertThrowsAsync<TException>(Func<Task> action, string label) where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionException(label + ": expected " + typeof(TException).Name + " but got " + ex.GetType().Name + ": " + ex.Message);
            }

            throw new AssertionException(label + ": expected " + typeof(TException).Name + " but nothing was thrown");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
