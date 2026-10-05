namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the CLI tool permission persistence added by migration 78: cli_permission_requests (round trip,
    /// message link, compare-and-set decisions, filtered newest-first listing), cli_permission_rules (CRUD, tenant
    /// listing that includes rules for every tenant, applicable-rule selection), the captain and Ask thread
    /// cli_permission_policy columns (dedicated setters that ordinary updates never overwrite), the
    /// ask_message_tool_calls permission_denied flag, retention deletes of decided requests (pending requests are kept),
    /// and the delete of a thread's requests with the thread.
    /// </summary>
    public sealed class CliPermissionDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.CliPermissions";
        private static readonly DateTime _Fixed = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("request_roundtrip_all_fields", "A permission request round-trips every persisted field", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                CliPermissionRequest request = new CliPermissionRequest();
                request.TenantId = "ten_rt";
                request.UserId = "usr_rt";
                request.CaptainId = "cpt_rt";
                request.MissionId = "msn_rt";
                request.VoyageId = "vyg_rt";
                request.VesselId = "vsl_rt";
                request.ThreadId = "ath_rt";
                request.MessageId = "amg_rt";
                request.Runtime = AgentRuntimeEnum.Codex;
                request.ToolName = "Bash";
                request.InputText = "{\"command\":\"git status\"}";
                request.SummaryText = "git status";
                request.SuggestedRule = "Bash(git status:*)";
                request.Status = CliPermissionRequestStatusEnum.Denied;
                request.DecisionSource = CliPermissionDecisionSourceEnum.DenyRule;
                request.RuleId = "cpl_rt";
                request.DecidedByUserId = "usr_admin";
                request.DecisionMessage = "not allowed";
                request.ExpiresUtc = _Fixed.AddMinutes(10);
                request.DecidedUtc = _Fixed.AddMinutes(1);
                request.CreatedUtc = _Fixed;
                request.CaptainName = "not persisted";
                request.CanDecide = true;
                await db.CliPermissionRequests.CreateAsync(request).ConfigureAwait(false);

                CliPermissionRequest? read = await db.CliPermissionRequests.ReadAsync(request.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertEqual(request.Id, read!.Id);
                AssertEqual("ten_rt", read.TenantId);
                AssertEqual("usr_rt", read.UserId);
                AssertEqual("cpt_rt", read.CaptainId);
                AssertEqual("msn_rt", read.MissionId);
                AssertEqual("vyg_rt", read.VoyageId);
                AssertEqual("vsl_rt", read.VesselId);
                AssertEqual("ath_rt", read.ThreadId);
                AssertEqual("amg_rt", read.MessageId);
                AssertEqual(AgentRuntimeEnum.Codex, read.Runtime);
                AssertEqual("Bash", read.ToolName);
                AssertEqual("{\"command\":\"git status\"}", read.InputText);
                AssertEqual("git status", read.SummaryText);
                AssertEqual("Bash(git status:*)", read.SuggestedRule);
                AssertEqual(CliPermissionRequestStatusEnum.Denied, read.Status);
                AssertEqual((CliPermissionDecisionSourceEnum?)CliPermissionDecisionSourceEnum.DenyRule, read.DecisionSource);
                AssertEqual("cpl_rt", read.RuleId);
                AssertEqual("usr_admin", read.DecidedByUserId);
                AssertEqual("not allowed", read.DecisionMessage);
                AssertSameInstant(_Fixed.AddMinutes(10), read.ExpiresUtc, "expires utc");
                AssertNotNull(read.DecidedUtc, "decided utc");
                AssertSameInstant(_Fixed.AddMinutes(1), read.DecidedUtc!.Value, "decided utc");
                AssertSameInstant(_Fixed, read.CreatedUtc, "created utc");
                AssertNull(read.CaptainName, "computed captain name is not persisted");
                AssertFalse(read.CanDecide, "computed can-decide is not persisted");

                CliPermissionRequest minimal = NewRequest("ten_rt", _Fixed.AddMinutes(2));
                minimal.UserId = null;
                minimal.CaptainId = null;
                await db.CliPermissionRequests.CreateAsync(minimal).ConfigureAwait(false);
                CliPermissionRequest? readMinimal = await db.CliPermissionRequests.ReadAsync(minimal.Id).ConfigureAwait(false);
                AssertNotNull(readMinimal, "minimal read back");
                AssertEqual(CliPermissionRequestStatusEnum.Pending, readMinimal!.Status);
                AssertNull(readMinimal.DecisionSource, "no decision source while pending");
                AssertNull(readMinimal.DecidedUtc, "no decided utc while pending");
                AssertNull(readMinimal.UserId, "null user");
                AssertNull(readMinimal.MessageId, "null message");
                AssertNull(readMinimal.SuggestedRule, "null suggested rule");

                AssertNull(await db.CliPermissionRequests.ReadAsync("cpr_missing").ConfigureAwait(false), "missing id");
            }));

            cases.Add(CaseAsync("request_update_message", "UpdateMessageAsync sets and clears the request card message", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                CliPermissionRequest request = await db.CliPermissionRequests.CreateAsync(NewRequest("ten_msg", _Fixed)).ConfigureAwait(false);
                await db.CliPermissionRequests.UpdateMessageAsync(request.Id, "amg_card").ConfigureAwait(false);
                CliPermissionRequest? read = await db.CliPermissionRequests.ReadAsync(request.Id).ConfigureAwait(false);
                AssertEqual("amg_card", read!.MessageId);
                AssertEqual(CliPermissionRequestStatusEnum.Pending, read.Status, "status untouched");

                await db.CliPermissionRequests.UpdateMessageAsync(request.Id, null).ConfigureAwait(false);
                read = await db.CliPermissionRequests.ReadAsync(request.Id).ConfigureAwait(false);
                AssertNull(read!.MessageId, "message cleared");
            }));

            cases.Add(CaseAsync("request_try_decide_compare_and_set", "TryDecideAsync decides a pending request exactly once", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                CliPermissionRequest request = await db.CliPermissionRequests.CreateAsync(NewRequest("ten_cas", _Fixed)).ConfigureAwait(false);
                DateTime before = DateTime.UtcNow.AddSeconds(-5);

                bool first = await db.CliPermissionRequests.TryDecideAsync(request.Id, CliPermissionRequestStatusEnum.Allowed, CliPermissionDecisionSourceEnum.Approver, "cpl_remember", "usr_approver", "go ahead").ConfigureAwait(false);
                AssertTrue(first, "first decision wins");

                bool second = await db.CliPermissionRequests.TryDecideAsync(request.Id, CliPermissionRequestStatusEnum.Denied, CliPermissionDecisionSourceEnum.Timeout, null, null, "expired").ConfigureAwait(false);
                AssertFalse(second, "second decision loses");

                CliPermissionRequest? read = await db.CliPermissionRequests.ReadAsync(request.Id).ConfigureAwait(false);
                AssertEqual(CliPermissionRequestStatusEnum.Allowed, read!.Status);
                AssertEqual((CliPermissionDecisionSourceEnum?)CliPermissionDecisionSourceEnum.Approver, read.DecisionSource);
                AssertEqual("cpl_remember", read.RuleId);
                AssertEqual("usr_approver", read.DecidedByUserId);
                AssertEqual("go ahead", read.DecisionMessage);
                AssertNotNull(read.DecidedUtc, "decided utc set");
                AssertTrue(read.DecidedUtc!.Value >= before, "decided utc is current");

                AssertFalse(await db.CliPermissionRequests.TryDecideAsync("cpr_missing", CliPermissionRequestStatusEnum.Expired, CliPermissionDecisionSourceEnum.Timeout, null, null, null).ConfigureAwait(false), "missing request");

                bool threw = false;
                try
                {
                    await db.CliPermissionRequests.TryDecideAsync(request.Id, CliPermissionRequestStatusEnum.Pending, CliPermissionDecisionSourceEnum.Approver, null, null, null).ConfigureAwait(false);
                }
                catch (ArgumentException)
                {
                    threw = true;
                }
                AssertTrue(threw, "deciding to Pending is rejected");
            }));

            cases.Add(CaseAsync("request_enumerate_filters", "Request listing filters by tenant, status, mission, thread, captain, vessel, and user, newest first with a limit", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                CliPermissionRequest r1 = NewRequest("ten_a", _Fixed.AddMinutes(1));
                r1.UserId = "usr_1"; r1.CaptainId = "cpt_1"; r1.VesselId = "vsl_1"; r1.MissionId = "msn_1";
                CliPermissionRequest r2 = NewRequest("ten_a", _Fixed.AddMinutes(2));
                r2.UserId = "usr_2"; r2.CaptainId = "cpt_2"; r2.VesselId = "vsl_2"; r2.ThreadId = "ath_1";
                CliPermissionRequest r3 = NewRequest("ten_b", _Fixed.AddMinutes(3));
                r3.UserId = "usr_1"; r3.CaptainId = "cpt_1"; r3.VesselId = "vsl_1"; r3.MissionId = "msn_1";
                foreach (CliPermissionRequest r in new[] { r1, r2, r3 }) await db.CliPermissionRequests.CreateAsync(r).ConfigureAwait(false);
                AssertTrue(await db.CliPermissionRequests.TryDecideAsync(r1.Id, CliPermissionRequestStatusEnum.Allowed, CliPermissionDecisionSourceEnum.AllowRule, "cpl_1", null, null).ConfigureAwait(false), "decide r1");

                List<CliPermissionRequest> all = await db.CliPermissionRequests.EnumerateAsync(new CliPermissionRequestQuery()).ConfigureAwait(false);
                AssertEqual(3, all.Count, "no filters");
                AssertEqual(r3.Id, all[0].Id, "newest first");
                AssertEqual(r2.Id, all[1].Id, "newest first");
                AssertEqual(r1.Id, all[2].Id, "newest first");

                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.TenantId = "ten_a")).ConfigureAwait(false), "tenant", r2.Id, r1.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.TenantId = "ten_none")).ConfigureAwait(false), "unknown tenant");
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.Status = CliPermissionRequestStatusEnum.Pending)).ConfigureAwait(false), "pending", r3.Id, r2.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.Status = CliPermissionRequestStatusEnum.Allowed)).ConfigureAwait(false), "allowed", r1.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => { q.TenantId = "ten_a"; q.Status = CliPermissionRequestStatusEnum.Pending; })).ConfigureAwait(false), "tenant and status", r2.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.MissionId = "msn_1")).ConfigureAwait(false), "mission", r3.Id, r1.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.ThreadId = "ath_1")).ConfigureAwait(false), "thread", r2.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.CaptainId = "cpt_2")).ConfigureAwait(false), "captain", r2.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.VesselId = "vsl_1")).ConfigureAwait(false), "vessel", r3.Id, r1.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.UserId = "usr_1")).ConfigureAwait(false), "user", r3.Id, r1.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => { q.UserId = "usr_1"; q.TenantId = "ten_b"; })).ConfigureAwait(false), "user and tenant", r3.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => q.Limit = 2)).ConfigureAwait(false), "limit keeps the newest", r3.Id, r2.Id);
                AssertIds(await db.CliPermissionRequests.EnumerateAsync(Query(q => { q.TenantId = "ten_a"; q.Limit = 1; })).ConfigureAwait(false), "tenant with limit", r2.Id);
            }));

            cases.Add(CaseAsync("rule_crud", "Rules create, read, update (pattern, action, description), and delete", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                CliPermissionRule rule = NewRule("ten_crud", CliPermissionRuleScopeEnum.Vessel, _Fixed);
                rule.VesselId = "vsl_crud";
                rule.Pattern = "Bash(git status:*)";
                rule.Action = CliPermissionRuleActionEnum.Allow;
                rule.Description = "status is safe";
                rule.CreatedByUserId = "usr_admin";
                await db.CliPermissionRules.CreateAsync(rule).ConfigureAwait(false);

                CliPermissionRule? read = await db.CliPermissionRules.ReadAsync(rule.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertEqual("ten_crud", read!.TenantId);
                AssertEqual(CliPermissionRuleScopeEnum.Vessel, read.Scope);
                AssertEqual("vsl_crud", read.VesselId);
                AssertNull(read.CaptainId, "no captain");
                AssertEqual("Bash(git status:*)", read.Pattern);
                AssertEqual(CliPermissionRuleActionEnum.Allow, read.Action);
                AssertEqual("status is safe", read.Description);
                AssertEqual("usr_admin", read.CreatedByUserId);
                AssertSameInstant(_Fixed, read.CreatedUtc, "created utc");

                read.Pattern = "WebFetch(domain:example.com)";
                read.Action = CliPermissionRuleActionEnum.Deny;
                read.Description = null;
                read.Scope = CliPermissionRuleScopeEnum.Global;
                read.TenantId = "ten_other";
                await db.CliPermissionRules.UpdateAsync(read).ConfigureAwait(false);

                CliPermissionRule? updated = await db.CliPermissionRules.ReadAsync(rule.Id).ConfigureAwait(false);
                AssertEqual("WebFetch(domain:example.com)", updated!.Pattern);
                AssertEqual(CliPermissionRuleActionEnum.Deny, updated.Action);
                AssertNull(updated.Description, "description cleared");
                AssertEqual(CliPermissionRuleScopeEnum.Vessel, updated.Scope, "scope is not updatable");
                AssertEqual("ten_crud", updated.TenantId, "tenant is not updatable");
                AssertEqual("vsl_crud", updated.VesselId, "vessel is not updatable");

                AssertTrue(await db.CliPermissionRules.DeleteAsync(rule.Id).ConfigureAwait(false), "delete");
                AssertNull(await db.CliPermissionRules.ReadAsync(rule.Id).ConfigureAwait(false), "gone");
                AssertFalse(await db.CliPermissionRules.DeleteAsync(rule.Id).ConfigureAwait(false), "second delete");
            }));

            cases.Add(CaseAsync("rule_enumerate_filters", "Rule listing includes rules for every tenant under a tenant filter and filters by scope, vessel, and captain", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                CliPermissionRule everyTenant = NewRule(null, CliPermissionRuleScopeEnum.Global, _Fixed.AddMinutes(1));
                CliPermissionRule tenantGlobal = NewRule("ten_a", CliPermissionRuleScopeEnum.Global, _Fixed.AddMinutes(2));
                CliPermissionRule tenantVessel = NewRule("ten_a", CliPermissionRuleScopeEnum.Vessel, _Fixed.AddMinutes(3));
                tenantVessel.VesselId = "vsl_1";
                CliPermissionRule tenantCaptain = NewRule("ten_a", CliPermissionRuleScopeEnum.Captain, _Fixed.AddMinutes(4));
                tenantCaptain.CaptainId = "cpt_1";
                CliPermissionRule otherTenant = NewRule("ten_b", CliPermissionRuleScopeEnum.Vessel, _Fixed.AddMinutes(5));
                otherTenant.VesselId = "vsl_1";
                foreach (CliPermissionRule r in new[] { everyTenant, tenantGlobal, tenantVessel, tenantCaptain, otherTenant }) await db.CliPermissionRules.CreateAsync(r).ConfigureAwait(false);

                List<CliPermissionRule> all = await db.CliPermissionRules.EnumerateAsync(new CliPermissionRuleQuery()).ConfigureAwait(false);
                AssertRuleIds(all, "no filters", otherTenant.Id, tenantCaptain.Id, tenantVessel.Id, tenantGlobal.Id, everyTenant.Id);

                AssertRuleIds(await db.CliPermissionRules.EnumerateAsync(RuleQuery(q => q.TenantId = "ten_a")).ConfigureAwait(false), "tenant plus every-tenant rules", tenantCaptain.Id, tenantVessel.Id, tenantGlobal.Id, everyTenant.Id);
                AssertRuleIds(await db.CliPermissionRules.EnumerateAsync(RuleQuery(q => q.TenantId = "ten_b")).ConfigureAwait(false), "other tenant", otherTenant.Id, everyTenant.Id);
                AssertRuleIds(await db.CliPermissionRules.EnumerateAsync(RuleQuery(q => q.Scope = CliPermissionRuleScopeEnum.Global)).ConfigureAwait(false), "global scope", tenantGlobal.Id, everyTenant.Id);
                AssertRuleIds(await db.CliPermissionRules.EnumerateAsync(RuleQuery(q => q.VesselId = "vsl_1")).ConfigureAwait(false), "vessel", otherTenant.Id, tenantVessel.Id);
                AssertRuleIds(await db.CliPermissionRules.EnumerateAsync(RuleQuery(q => { q.VesselId = "vsl_1"; q.TenantId = "ten_a"; })).ConfigureAwait(false), "vessel in tenant", tenantVessel.Id);
                AssertRuleIds(await db.CliPermissionRules.EnumerateAsync(RuleQuery(q => q.CaptainId = "cpt_1")).ConfigureAwait(false), "captain", tenantCaptain.Id);
                AssertRuleIds(await db.CliPermissionRules.EnumerateAsync(RuleQuery(q => q.Scope = CliPermissionRuleScopeEnum.Captain)).ConfigureAwait(false), "captain scope", tenantCaptain.Id);
            }));

            cases.Add(CaseAsync("rule_enumerate_applicable", "Applicable rules are every-tenant and tenant Global rules plus the matching vessel and captain rules", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                CliPermissionRule everyTenant = NewRule(null, CliPermissionRuleScopeEnum.Global, _Fixed.AddMinutes(1));
                CliPermissionRule tenantGlobal = NewRule("ten_a", CliPermissionRuleScopeEnum.Global, _Fixed.AddMinutes(2));
                CliPermissionRule vesselMatch = NewRule("ten_a", CliPermissionRuleScopeEnum.Vessel, _Fixed.AddMinutes(3));
                vesselMatch.VesselId = "vsl_1";
                CliPermissionRule vesselOther = NewRule("ten_a", CliPermissionRuleScopeEnum.Vessel, _Fixed.AddMinutes(4));
                vesselOther.VesselId = "vsl_2";
                CliPermissionRule captainMatch = NewRule("ten_a", CliPermissionRuleScopeEnum.Captain, _Fixed.AddMinutes(5));
                captainMatch.CaptainId = "cpt_1";
                CliPermissionRule captainOther = NewRule("ten_a", CliPermissionRuleScopeEnum.Captain, _Fixed.AddMinutes(6));
                captainOther.CaptainId = "cpt_2";
                CliPermissionRule otherTenantGlobal = NewRule("ten_b", CliPermissionRuleScopeEnum.Global, _Fixed.AddMinutes(7));
                CliPermissionRule otherTenantVessel = NewRule("ten_b", CliPermissionRuleScopeEnum.Vessel, _Fixed.AddMinutes(8));
                otherTenantVessel.VesselId = "vsl_1";
                CliPermissionRule everyTenantVessel = NewRule(null, CliPermissionRuleScopeEnum.Vessel, _Fixed.AddMinutes(9));
                everyTenantVessel.VesselId = "vsl_1";
                foreach (CliPermissionRule r in new[] { everyTenant, tenantGlobal, vesselMatch, vesselOther, captainMatch, captainOther, otherTenantGlobal, otherTenantVessel, everyTenantVessel })
                    await db.CliPermissionRules.CreateAsync(r).ConfigureAwait(false);

                List<CliPermissionRule> applicable = await db.CliPermissionRules.EnumerateApplicableAsync("ten_a", "cpt_1", "vsl_1").ConfigureAwait(false);
                AssertRuleIds(applicable, "applicable (oldest first)", everyTenant.Id, tenantGlobal.Id, vesselMatch.Id, captainMatch.Id, everyTenantVessel.Id);

                List<CliPermissionRule> noVessel = await db.CliPermissionRules.EnumerateApplicableAsync("ten_a", "cpt_1", null).ConfigureAwait(false);
                AssertRuleIds(noVessel, "no vessel (Ask turn)", everyTenant.Id, tenantGlobal.Id, captainMatch.Id);

                List<CliPermissionRule> noTenant = await db.CliPermissionRules.EnumerateApplicableAsync(null, "cpt_9", null).ConfigureAwait(false);
                AssertRuleIds(noTenant, "no tenant", everyTenant.Id);
            }));

            cases.Add(CaseAsync("captain_cli_permission_policy", "Captain policy is set and cleared by its own setter and never overwritten by UpdateAsync", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                Captain created = new Captain("cli-policy-captain");
                created.CliPermissionPolicy = CliPermissionPolicyEnum.Refuse;
                await db.Captains.CreateAsync(created).ConfigureAwait(false);
                Captain? read = await db.Captains.ReadAsync(created.Id).ConfigureAwait(false);
                AssertEqual((CliPermissionPolicyEnum?)CliPermissionPolicyEnum.Refuse, read!.CliPermissionPolicy, "insert writes the policy");

                AssertTrue(await db.Captains.UpdateCliPermissionPolicyAsync(created.Id, CliPermissionPolicyEnum.ApproveInArmada).ConfigureAwait(false), "set");
                read = await db.Captains.ReadAsync(created.Id).ConfigureAwait(false);
                AssertEqual((CliPermissionPolicyEnum?)CliPermissionPolicyEnum.ApproveInArmada, read!.CliPermissionPolicy, "set value");

                read.CliPermissionPolicy = CliPermissionPolicyEnum.Bypass;
                read.Name = "cli-policy-captain-renamed";
                await db.Captains.UpdateAsync(read).ConfigureAwait(false);
                Captain? afterUpdate = await db.Captains.ReadAsync(created.Id).ConfigureAwait(false);
                AssertEqual("cli-policy-captain-renamed", afterUpdate!.Name, "ordinary update applied");
                AssertEqual((CliPermissionPolicyEnum?)CliPermissionPolicyEnum.ApproveInArmada, afterUpdate.CliPermissionPolicy, "UpdateAsync does not write the policy");

                AssertTrue(await db.Captains.UpdateCliPermissionPolicyAsync(created.Id, null).ConfigureAwait(false), "clear");
                read = await db.Captains.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNull(read!.CliPermissionPolicy, "cleared");

                AssertFalse(await db.Captains.UpdateCliPermissionPolicyAsync("cpt_missing", CliPermissionPolicyEnum.Bypass).ConfigureAwait(false), "missing captain");
            }));

            cases.Add(CaseAsync("thread_cli_permission_policy", "Ask thread policy is set and cleared by its own tenant-scoped setter and never overwritten by UpdateAsync", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = NewThread("usr_policy");
                thread.CliPermissionPolicy = CliPermissionPolicyEnum.Refuse;
                await db.AskThreads.CreateAsync(thread).ConfigureAwait(false);
                AskThread? read = await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false);
                AssertEqual((CliPermissionPolicyEnum?)CliPermissionPolicyEnum.Refuse, read!.CliPermissionPolicy, "insert writes the policy");

                AssertTrue(await db.AskThreads.UpdateCliPermissionPolicyAsync(Constants.DefaultTenantId, thread.Id, CliPermissionPolicyEnum.ApproveInArmada).ConfigureAwait(false), "set");
                read = await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false);
                AssertEqual((CliPermissionPolicyEnum?)CliPermissionPolicyEnum.ApproveInArmada, read!.CliPermissionPolicy, "set value");

                AssertFalse(await db.AskThreads.UpdateCliPermissionPolicyAsync("ten_other", thread.Id, CliPermissionPolicyEnum.Bypass).ConfigureAwait(false), "another tenant cannot set");
                read = await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false);
                AssertEqual((CliPermissionPolicyEnum?)CliPermissionPolicyEnum.ApproveInArmada, read!.CliPermissionPolicy, "unchanged by another tenant");

                read.CliPermissionPolicy = CliPermissionPolicyEnum.Bypass;
                read.Title = "Renamed";
                await db.AskThreads.UpdateAsync(read).ConfigureAwait(false);
                AskThread? afterUpdate = await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false);
                AssertEqual("Renamed", afterUpdate!.Title, "ordinary update applied");
                AssertEqual((CliPermissionPolicyEnum?)CliPermissionPolicyEnum.ApproveInArmada, afterUpdate.CliPermissionPolicy, "UpdateAsync does not write the policy");

                AssertTrue(await db.AskThreads.UpdateCliPermissionPolicyAsync(Constants.DefaultTenantId, thread.Id, null).ConfigureAwait(false), "clear");
                read = await db.AskThreads.ReadByIdAsync(thread.Id).ConfigureAwait(false);
                AssertNull(read!.CliPermissionPolicy, "cleared");
            }));

            cases.Add(CaseAsync("tool_call_permission_denied_roundtrip", "Ask tool call PermissionDenied round-trips true, false, and null", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread thread = await db.AskThreads.CreateAsync(NewThread("usr_tools")).ConfigureAwait(false);
                AskMessage message = new AskMessage();
                message.TenantId = thread.TenantId;
                message.UserId = thread.UserId;
                message.ThreadId = thread.Id;
                message.ContentText = "tools";
                message = await db.AskMessages.CreateAsync(message, false).ConfigureAwait(false);

                AskMessageToolCall denied = NewToolCall(message, "Bash");
                denied.PermissionDenied = true;
                AskMessageToolCall allowed = NewToolCall(message, "Read");
                allowed.PermissionDenied = false;
                AskMessageToolCall unknown = NewToolCall(message, "Grep");
                unknown.PermissionDenied = null;
                await db.AskMessageToolCalls.CreateManyAsync(new List<AskMessageToolCall> { denied, allowed, unknown }).ConfigureAwait(false);

                List<AskMessageToolCall> calls = await db.AskMessageToolCalls.EnumerateByMessagesAsync(Constants.DefaultTenantId, thread.Id, new List<string> { message.Id }).ConfigureAwait(false);
                AssertEqual(3, calls.Count, "three calls");
                AssertEqual((bool?)true, calls.Single(c => c.Id == denied.Id).PermissionDenied, "denied");
                AssertEqual((bool?)false, calls.Single(c => c.Id == allowed.Id).PermissionDenied, "allowed");
                AssertNull(calls.Single(c => c.Id == unknown.Id).PermissionDenied, "unknown");
            }));

            cases.Add(CaseAsync("request_delete_finished_before", "DeleteFinishedBeforeAsync deletes decided, expired, and cancelled requests older than the cutoff and keeps pending and newer ones", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                DateTime now = DateTime.UtcNow;

                CliPermissionRequest oldPending = NewRequest("ten_ret", now.AddDays(-200));
                CliPermissionRequest oldAllowed = Decided(NewRequest("ten_ret", now.AddDays(-101)), CliPermissionRequestStatusEnum.Allowed, now.AddDays(-100));
                CliPermissionRequest oldDenied = Decided(NewRequest("ten_ret", now.AddDays(-96)), CliPermissionRequestStatusEnum.Denied, now.AddDays(-95));
                CliPermissionRequest oldExpired = Decided(NewRequest("ten_ret", now.AddDays(-121)), CliPermissionRequestStatusEnum.Expired, now.AddDays(-120));
                CliPermissionRequest oldCancelled = Decided(NewRequest("ten_ret", now.AddDays(-92)), CliPermissionRequestStatusEnum.Cancelled, now.AddDays(-91));
                CliPermissionRequest recentAllowed = Decided(NewRequest("ten_ret", now.AddDays(-11)), CliPermissionRequestStatusEnum.Allowed, now.AddDays(-10));
                CliPermissionRequest undatedOld = NewRequest("ten_ret", now.AddDays(-150));
                undatedOld.Status = CliPermissionRequestStatusEnum.Denied;
                foreach (CliPermissionRequest r in new[] { oldPending, oldAllowed, oldDenied, oldExpired, oldCancelled, recentAllowed, undatedOld })
                    await db.CliPermissionRequests.CreateAsync(r).ConfigureAwait(false);

                AssertEqual(5, await db.CliPermissionRequests.DeleteFinishedBeforeAsync(now.AddDays(-90)).ConfigureAwait(false), "five finished requests older than 90 days");
                List<CliPermissionRequest> left = await db.CliPermissionRequests.EnumerateAsync(Query(q => q.TenantId = "ten_ret")).ConfigureAwait(false);
                AssertEqual(String.Join(",", new[] { oldPending.Id, recentAllowed.Id }.OrderBy(i => i, StringComparer.Ordinal)), String.Join(",", left.Select(r => r.Id).OrderBy(i => i, StringComparer.Ordinal)), "pending and recent requests kept");
                AssertEqual(0, await db.CliPermissionRequests.DeleteFinishedBeforeAsync(now.AddDays(-90)).ConfigureAwait(false), "second pass deletes nothing");
                AssertEqual(1, await db.CliPermissionRequests.DeleteFinishedBeforeAsync(now).ConfigureAwait(false), "a later cutoff deletes the recent decided request but never the pending one");
                AssertNotNull(await db.CliPermissionRequests.ReadAsync(oldPending.Id).ConfigureAwait(false), "pending request kept");
            }));

            cases.Add(CaseAsync("thread_delete_removes_requests", "Deleting an Ask thread deletes its CLI permission requests and keeps other threads' and missions' requests", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AskThread doomed = await db.AskThreads.CreateAsync(NewThread("usr_cascade")).ConfigureAwait(false);
                AskThread kept = await db.AskThreads.CreateAsync(NewThread("usr_cascade")).ConfigureAwait(false);
                CliPermissionRequest pending = NewRequest(Constants.DefaultTenantId, _Fixed);
                pending.ThreadId = doomed.Id;
                CliPermissionRequest decided = Decided(NewRequest(Constants.DefaultTenantId, _Fixed), CliPermissionRequestStatusEnum.Allowed, _Fixed.AddMinutes(1));
                decided.ThreadId = doomed.Id;
                CliPermissionRequest other = NewRequest(Constants.DefaultTenantId, _Fixed);
                other.ThreadId = kept.Id;
                CliPermissionRequest mission = NewRequest(Constants.DefaultTenantId, _Fixed);
                mission.MissionId = "msn_cascade";
                foreach (CliPermissionRequest r in new[] { pending, decided, other, mission })
                    await db.CliPermissionRequests.CreateAsync(r).ConfigureAwait(false);

                AssertTrue(await db.AskThreads.DeleteAsync(Constants.DefaultTenantId, "usr_cascade", doomed.Id).ConfigureAwait(false), "thread deleted");
                AssertNull(await db.CliPermissionRequests.ReadAsync(pending.Id).ConfigureAwait(false), "pending request of the thread deleted");
                AssertNull(await db.CliPermissionRequests.ReadAsync(decided.Id).ConfigureAwait(false), "decided request of the thread deleted");
                AssertNotNull(await db.CliPermissionRequests.ReadAsync(other.Id).ConfigureAwait(false), "another thread's request kept");
                AssertNotNull(await db.CliPermissionRequests.ReadAsync(mission.Id).ConfigureAwait(false), "a mission's request kept");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "CLI Permission Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static CliPermissionRequest NewRequest(string? tenantId, DateTime createdUtc)
        {
            CliPermissionRequest request = new CliPermissionRequest();
            request.TenantId = tenantId;
            request.UserId = "usr_req";
            request.CaptainId = "cpt_req";
            request.ToolName = "Bash";
            request.InputText = "{\"command\":\"ls\"}";
            request.SummaryText = "ls";
            request.ExpiresUtc = createdUtc.AddMinutes(10);
            request.CreatedUtc = createdUtc;
            return request;
        }

        private static CliPermissionRequest Decided(CliPermissionRequest request, CliPermissionRequestStatusEnum status, DateTime decidedUtc)
        {
            request.Status = status;
            request.DecisionSource = status == CliPermissionRequestStatusEnum.Expired ? CliPermissionDecisionSourceEnum.Timeout : CliPermissionDecisionSourceEnum.Approver;
            request.DecidedUtc = decidedUtc;
            return request;
        }

        private static CliPermissionRule NewRule(string? tenantId, CliPermissionRuleScopeEnum scope, DateTime createdUtc)
        {
            CliPermissionRule rule = new CliPermissionRule();
            rule.TenantId = tenantId;
            rule.Scope = scope;
            rule.Pattern = "Bash(ls:*)";
            rule.Action = CliPermissionRuleActionEnum.Allow;
            rule.CreatedUtc = createdUtc;
            return rule;
        }

        private static CliPermissionRequestQuery Query(Action<CliPermissionRequestQuery> configure)
        {
            CliPermissionRequestQuery query = new CliPermissionRequestQuery();
            configure(query);
            return query;
        }

        private static CliPermissionRuleQuery RuleQuery(Action<CliPermissionRuleQuery> configure)
        {
            CliPermissionRuleQuery query = new CliPermissionRuleQuery();
            configure(query);
            return query;
        }

        private static void AssertIds(List<CliPermissionRequest> actual, string label, params string[] expected)
        {
            AssertEqual(String.Join(",", expected), String.Join(",", actual.Select(r => r.Id)), label);
        }

        private static void AssertRuleIds(List<CliPermissionRule> actual, string label, params string[] expected)
        {
            AssertEqual(String.Join(",", expected), String.Join(",", actual.Select(r => r.Id)), label);
        }

        private static AskThread NewThread(string userId)
        {
            AskThread thread = new AskThread();
            thread.TenantId = Constants.DefaultTenantId;
            thread.UserId = userId;
            return thread;
        }

        private static AskMessageToolCall NewToolCall(AskMessage message, string toolName)
        {
            AskMessageToolCall call = new AskMessageToolCall();
            call.TenantId = message.TenantId;
            call.UserId = message.UserId;
            call.ThreadId = message.ThreadId;
            call.MessageId = message.Id;
            call.CallId = "call_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            call.ToolName = toolName;
            return call;
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1.0, label + ": expected " + expected.ToString("o") + " but was " + actual.ToString("o"));
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
