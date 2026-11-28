namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server for stage outcomes and failure-cause rules: list (with filters and
    /// paging), get, classify and hand back, reclassify, draft a rule, and rule list, get, create, update and delete,
    /// including the error replies for unknown ids and invalid rules.
    /// </summary>
    public sealed class ClientContractStageOutcomeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.StageOutcomes";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("outcomes", "Stage outcomes: list by mission, get, classify, hand back, reclassify, draft a rule", async (c, fx) =>
            {
                string missionId = "msn_contract_" + ClientContract.Suffix();
                StageOutcome seeded = await SeedAsync(fx, missionId).ConfigureAwait(false);

                EnumerationResult<StageOutcome> page = (await c.StageOutcomes.ListAsync(new StageOutcomeQuery { MissionId = missionId, Disposition = StageDispositionEnum.Failed, PageSize = 5 }))!;
                AssertEqual(1, (int)page.TotalRecords, "filtered total");
                AssertEqual(seeded.Id, page.Objects.Single().Id, "listed");
                AssertEqual(5, page.PageSize, "page size sent");
                EnumerationResult<StageOutcome> none = (await c.StageOutcomes.ListAsync(new StageOutcomeQuery { MissionId = missionId, SuspectedCause = FailureCauseEnum.Capability }))!;
                AssertEqual(0, (int)none.TotalRecords, "enum filter sent");
                AssertNotNull(await c.StageOutcomes.ListAsync(), "no query");
                AssertEqual(missionId, (await c.StageOutcomes.GetAsync(seeded.Id))?.MissionId, "get");
                await ClientContract.ExpectErrorAsync(() => c.StageOutcomes.GetAsync("sto_missing"), "get unknown", 404, 404);

                StageOutcome manual = (await c.StageOutcomes.ClassifyAsync(seeded.Id, FailureCauseEnum.Protocol, "contract"))!;
                AssertEqual(FailureCauseSourceEnum.Manual, manual.CauseSource, "manual");
                AssertEqual("contract", manual.CauseNote, "note sent");
                await ClientContract.ExpectErrorAsync(() => c.StageOutcomes.ClassifyAsync(seeded.Id, FailureCauseEnum.None), "cause None", 400, 400);

                FailureCauseRule draft = (await c.StageOutcomes.DraftFailureCauseRuleAsync(seeded.Id))!;
                AssertEqual(FailureCauseEnum.Protocol, draft.Cause, "draft cause");
                AssertEqual(MissionFailureKindEnum.Infra, draft.Match.FailureKinds.Single(), "draft match");

                StageOutcome ruled = (await c.StageOutcomes.ClassifyAsync(seeded.Id, null))!;
                AssertEqual(FailureCauseSourceEnum.Rule, ruled.CauseSource, "handed back");
                AssertEqual(FailureCauseEnum.NotTheModel, ruled.SuspectedCause, "Infra built-in");

                StageOutcomeReclassifyResult result = (await c.StageOutcomes.ReclassifyAsync(new StageOutcomeQuery { MissionId = missionId }))!;
                AssertEqual(1, result.Examined, "filter sent");
                AssertEqual(0, result.Changed, "already current");
                AssertNotNull(await c.StageOutcomes.ReclassifyAsync(new StageOutcomeQuery { MissionId = "msn_none_" + ClientContract.Suffix() }), "empty match");
            }));

            cases.Add(Case("rules", "Failure-cause rules: list, create, get, update, delete; invalid rules fail", async (c, fx) =>
            {
                EnumerationResult<FailureCauseRule> first = (await c.StageOutcomes.ListFailureCauseRulesAsync(1, 2))!;
                AssertEqual(2, first.Objects.Count, "page of two");
                AssertTrue(first.TotalRecords >= 13, "built-ins counted");
                EnumerationResult<FailureCauseRule> defaults = (await c.StageOutcomes.ListFailureCauseRulesAsync())!;
                AssertEqual(10, defaults.PageSize, "server default page size");

                FailureCauseRule rule = new FailureCauseRule();
                rule.TenantId = "ten_contract_" + ClientContract.Suffix();
                rule.Name = "Contract rule " + ClientContract.Suffix();
                rule.Priority = 8;
                rule.Cause = FailureCauseEnum.Context;
                rule.Match.TextPattern = "context window";
                FailureCauseRule created = (await c.StageOutcomes.CreateFailureCauseRuleAsync(rule))!;
                AssertEqual(rule.Name, created.Name, "created");
                AssertEqual(rule.TenantId, created.TenantId, "a global admin sets the tenant");
                AssertNotEqual(rule.Id, created.Id, "server assigns the id");
                AssertEqual(rule.Name, (await c.StageOutcomes.GetFailureCauseRuleAsync(created.Id))?.Name, "get");

                created.Enabled = false;
                created.Match.Dispositions.Add(StageDispositionEnum.Failed);
                FailureCauseRule updated = (await c.StageOutcomes.UpdateFailureCauseRuleAsync(created.Id, created))!;
                AssertFalse(updated.Enabled, "updated");
                AssertEqual(StageDispositionEnum.Failed, updated.Match.Dispositions.Single(), "match sent");

                FailureCauseRule invalid = new FailureCauseRule { Name = "No criteria " + ClientContract.Suffix(), Cause = FailureCauseEnum.Protocol };
                ArmadaApiException error = await ClientContract.ExpectErrorAsync(() => c.StageOutcomes.CreateFailureCauseRuleAsync(invalid), "no criteria", 400, 400);
                AssertContains("criterion", error.Message);

                await c.StageOutcomes.DeleteFailureCauseRuleAsync(created.Id);
                await ClientContract.ExpectErrorAsync(() => c.StageOutcomes.GetFailureCauseRuleAsync(created.Id), "deleted", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.StageOutcomes.DeleteFailureCauseRuleAsync(created.Id), "delete unknown", 404, 404);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: stage outcomes and failure-cause rules", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<StageOutcome> SeedAsync(E2EServerFixture fx, string missionId)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
            {
                await db.InitializeAsync().ConfigureAwait(false);
                StageOutcome outcome = new StageOutcome
                {
                    TenantId = "ten_contract",
                    MissionId = missionId,
                    Persona = "Worker",
                    Disposition = StageDispositionEnum.Failed,
                    FailureKind = MissionFailureKindEnum.Infra,
                    SuspectedCause = FailureCauseEnum.NotTheModel,
                    CauseSource = FailureCauseSourceEnum.Rule
                };
                return await db.StageOutcomes.CreateAsync(outcome).ConfigureAwait(false);
            }
        }

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body);
        }

        #endregion
    }
}
