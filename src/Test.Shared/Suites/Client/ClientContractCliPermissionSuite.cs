namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server for CLI tool permissions: request list, get, and decide (no prompt
    /// is pending on a fresh server, so get and decide of an unknown id must fail), rule create, get, list, update, and
    /// delete, and the captain and Ask thread policy overrides.
    /// </summary>
    public sealed class ClientContractCliPermissionSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.CliPermissions";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("requests", "CLI permission requests: list by status; get and decide an unknown id fail", async (c, fx) =>
            {
                List<CliPermissionRequest> pending = (await c.ListCliPermissionRequestsAsync(new CliPermissionRequestQuery { Status = CliPermissionRequestStatusEnum.Pending }))!;
                AssertNotNull(pending, "list");
                AssertTrue(pending.All(r => r.Status == CliPermissionRequestStatusEnum.Pending), "status filter");
                await ClientContract.ExpectErrorAsync(() => c.GetCliPermissionRequestAsync("cpr_missing"), "get unknown", 404, 404);
                CliPermissionDecisionRequest decision = new CliPermissionDecisionRequest { Decision = CliPermissionDecisionEnum.AllowOnce };
                await ClientContract.ExpectErrorAsync(() => c.DecideCliPermissionRequestAsync("cpr_missing", decision), "decide unknown", 404, 404);
            }));

            cases.Add(Case("rules", "CLI permission rules: create, get, list, update, delete", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                CliPermissionRule rule = new CliPermissionRule();
                rule.Pattern = "Bash(git status " + s + ":*)";
                rule.Action = CliPermissionRuleActionEnum.Allow;
                rule.Scope = CliPermissionRuleScopeEnum.Global;
                rule.Description = "contract";
                CliPermissionRule created = (await c.CreateCliPermissionRuleAsync(rule))!;
                AssertEqual(rule.Pattern, created.Pattern, "created pattern");
                AssertEqual(rule.Pattern, (await c.GetCliPermissionRuleAsync(created.Id))?.Pattern, "get");
                List<CliPermissionRule> global = (await c.ListCliPermissionRulesAsync(new CliPermissionRuleQuery { Scope = CliPermissionRuleScopeEnum.Global }))!;
                AssertTrue(global.Any(r => r.Id == created.Id), "listed");
                created.Description = "updated";
                created.Action = CliPermissionRuleActionEnum.Deny;
                CliPermissionRule updated = (await c.UpdateCliPermissionRuleAsync(created.Id, created))!;
                AssertEqual("updated", updated.Description, "description updated");
                AssertEqual(CliPermissionRuleActionEnum.Deny, updated.Action, "action updated");
                await c.DeleteCliPermissionRuleAsync(created.Id);
                await ClientContract.ExpectErrorAsync(() => c.GetCliPermissionRuleAsync(created.Id), "deleted", 404, 404);
            }));

            cases.Add(Case("policies", "Captain and Ask thread CLI permission policy set and clear", async (c, fx) =>
            {
                Captain captain = await LiveServerSetup.CreateCaptainAsync(c, "contract-cli-" + ClientContract.Suffix());
                Captain? refused = await c.SetCaptainCliPermissionPolicyAsync(captain.Id, CliPermissionPolicyEnum.Refuse);
                AssertEqual(CliPermissionPolicyEnum.Refuse, refused?.CliPermissionPolicy, "captain policy set");
                Captain? cleared = await c.SetCaptainCliPermissionPolicyAsync(captain.Id, null);
                AssertNull(cleared?.CliPermissionPolicy, "captain policy cleared");

                AskThread thread = (await c.CreateAskThreadAsync(new AskThreadCreateRequest { Title = "Contract CLI policy", CaptainId = captain.Id }))!;
                AskThread? overridden = await c.SetAskThreadCliPermissionPolicyAsync(thread.Id, CliPermissionPolicyEnum.Refuse);
                AssertEqual(CliPermissionPolicyEnum.Refuse, overridden?.CliPermissionPolicy, "thread override set");
                AssertEqual(CliPermissionPolicySourceEnum.AskThread, overridden?.CliPermission?.Source, "resolution names the thread");
                AskThread? inherited = await c.SetAskThreadCliPermissionPolicyAsync(thread.Id, null);
                AssertNull(inherited?.CliPermissionPolicy, "thread override cleared");
                await c.DeleteAskThreadAsync(thread.Id);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: CLI tool permissions", cases: cases);
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body);
        }

        #endregion
    }
}
