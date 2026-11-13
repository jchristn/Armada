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
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server for model profiles: list (querystring filters), enumerate, get with
    /// revisions, update, and mark revisions compatible; unknown ids fail with 404.
    /// </summary>
    public sealed class ClientContractModelProfileSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.ModelProfiles";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(ClientContract.Case(Suite, this, "profiles", "List, enumerate, get, update and mark revisions compatible; unknown ids fail", async (c, fx) =>
            {
                string runtime = "ContractRuntime" + ClientContract.Suffix();
                ModelProfileResolution first;
                ModelProfileResolution second;
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
                using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
                {
                    await db.InitializeAsync().ConfigureAwait(false);
                    ModelProfileService service = new ModelProfileService(db, quiet);
                    ModelProfileObservation observation = new ModelProfileObservation
                    {
                        TenantId = Armada.Core.Constants.DefaultTenantId, Runtime = runtime, Provider = "Ollama", Model = "contract-model", RuntimeVersion = "1.0.0"
                    };
                    first = await service.ResolveAsync(observation).ConfigureAwait(false);
                    observation.RuntimeVersion = "2.0.0";
                    second = await service.ResolveAsync(observation).ConfigureAwait(false);
                }

                EnumerationResult<ModelProfile> listed = (await c.ListModelProfilesAsync(new ModelProfileQuery { Runtime = runtime, ModelContains = "CONTRACT", PageSize = 5 }))!;
                AssertEqual(first.Profile.Id, listed.Objects.Single().Id, "list with filters");
                AssertEqual(5, listed.PageSize, "page size sent");
                AssertTrue((await c.ListModelProfilesAsync())!.TotalRecords >= 1, "list without a query");
                EnumerationResult<ModelProfile> enumerated = (await c.EnumerateModelProfilesAsync(new ModelProfileQuery { Runtime = runtime }))!;
                AssertEqual(first.Profile.Id, enumerated.Objects.Single().Id, "enumerate");

                ModelProfile got = (await c.GetModelProfileAsync(first.Profile.Id))!;
                AssertEqual(2, got.Revisions.Count, "revisions");
                AssertEqual(first.Revision.Id, got.Revisions[0].Id, "oldest first");

                ModelProfile updated = (await c.UpdateModelProfileAsync(first.Profile.Id, new ModelProfileUpdateRequest
                {
                    Rendering = new ModelProfileRendering { Template = "Compact", MaxFilesPerMission = 2 },
                    Notes = "contract"
                }))!;
                AssertEqual("Compact", updated.Rendering.Template, "template");
                AssertEqual((int?)2, updated.Rendering.MaxFilesPerMission, "max files");
                AssertEqual("contract", updated.Notes, "notes");

                ModelProfileRevision marked = (await c.SetModelProfileRevisionCompatibilityAsync(first.Profile.Id, first.Revision.Id,
                    new ModelProfileCompatibilityRequest { CompatibleWith = new List<string> { second.Revision.Id } }))!;
                AssertTrue(marked.CompatibleWith.SequenceEqual(new[] { second.Revision.Id }), "marked");

                ModelScorecard scorecard = (await c.GetModelScorecardAsync(first.Profile.Id, new ModelScorecardRequest { Persona = "Worker", WindowDays = 7, WindowMissions = 5 }))!;
                AssertEqual(first.Profile.Id, scorecard.ProfileId, "scorecard profile");
                AssertEqual(7, scorecard.WindowDays, "scorecard days");
                AssertEqual(5, scorecard.WindowMissions, "scorecard missions");
                AssertEqual(1, scorecard.Groups.Count, "the two revisions were just marked compatible, so they pool");
                AssertTrue(scorecard.Groups[0].Pooled && scorecard.Groups[0].RevisionIds.Count == 2, "pooled group");
                await ClientContract.ExpectErrorAsync(() => c.GetModelScorecardAsync(first.Profile.Id, new ModelScorecardRequest()), "scorecard without persona", 400, 400);
                await ClientContract.ExpectErrorAsync(() => c.GetModelScorecardAsync("mdl_missing", new ModelScorecardRequest { Persona = "Worker" }), "unknown profile scorecard", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.GetModelProfileAsync("mdl_missing"), "unknown profile", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.UpdateModelProfileAsync("mdl_missing", new ModelProfileUpdateRequest()), "unknown profile update", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.SetModelProfileRevisionCompatibilityAsync(first.Profile.Id, "mdv_missing", new ModelProfileCompatibilityRequest { CompatibleWith = new List<string>() }), "unknown revision", 404, 404);
            }));

            cases.Add(ClientContract.Case(Suite, this, "alerts", "Evaluate, list, enumerate, get, apply, ignore and thresholds of model alerts; unknown ids fail", async (c, fx) =>
            {
                ModelAlertEvaluation evaluation = (await c.EvaluateModelAlertsAsync())!;
                AssertTrue(evaluation.Pairs >= 0, "evaluation returned");

                ModelAlert seeded;
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
                using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
                {
                    await db.InitializeAsync().ConfigureAwait(false);
                    ModelProfileResolution resolved = await new ModelProfileService(db, quiet).ResolveAsync(new ModelProfileObservation
                    {
                        TenantId = Armada.Core.Constants.DefaultTenantId, Runtime = "AlertRuntime" + ClientContract.Suffix(), Provider = "Ollama", Model = "alert-model", RuntimeVersion = "1.0.0"
                    }).ConfigureAwait(false);
                    ModelAlert alert = new ModelAlert
                    {
                        TenantId = Armada.Core.Constants.DefaultTenantId, ProfileId = resolved.Profile.Id, Model = "alert-model", RevisionGroup = resolved.Revision.Id,
                        Persona = "Worker", Rule = ModelAlertRuleEnum.FirstPassSuccess, Title = "alert-model as Worker on all vessels: first pass 1 of 9", Detail = "contract"
                    };
                    seeded = await db.ModelAlerts.CreateAsync(alert, ModelAlertRules.Key(alert.TenantId, alert.ProfileId, alert.RevisionGroup, alert.Persona, null, alert.Rule)).ConfigureAwait(false);
                }

                EnumerationResult<ModelAlert> listed = (await c.ListModelAlertsAsync(new ModelAlertQuery { ProfileId = seeded.ProfileId, Status = ModelAlertStatusEnum.Open, PageSize = 5 }))!;
                AssertEqual(seeded.Id, listed.Objects.Single().Id, "list with filters");
                AssertTrue((await c.ListModelAlertsAsync())!.TotalRecords >= 1, "list without a query");
                AssertEqual(seeded.Id, (await c.EnumerateModelAlertsAsync(new ModelAlertQuery { ProfileId = seeded.ProfileId }))!.Objects.Single().Id, "enumerate");
                ModelAlert read = (await c.GetModelAlertAsync(seeded.Id))!;
                AssertEqual(5, read.SuggestedActions.Count, "suggested actions");
                ModelAlertApplyResult applied = (await c.ApplyModelAlertActionAsync(seeded.Id, new ModelAlertApplyRequest { Action = ModelAlertActionEnum.Ignore, Days = 1 }))!;
                AssertEqual(ModelAlertActionEnum.Ignore, applied.Record.Action, "applied");
                ModelAlertApplyResult ignored = (await c.IgnoreModelAlertAsync(seeded.Id, 2))!;
                AssertEqual(2, ignored.Alert.Actions.Count, "both recorded");
                ModelAlertThresholds thresholds = (await c.GetModelAlertThresholdsAsync())!;
                AssertEqual((int?)3, thresholds.Defaults.EscalationsInARow, "defaults");
                ModelAlertThresholds stored = (await c.UpdateModelAlertThresholdsAsync(new ModelAlertThresholds()))!;
                AssertEqual((double?)0.3, stored.Defaults.ProtocolRepairMaxRate, "stored defaults");
                await ClientContract.ExpectErrorAsync(() => c.GetModelAlertAsync("mal_missing"), "unknown alert", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.ApplyModelAlertActionAsync(seeded.Id, new ModelAlertApplyRequest { Action = ModelAlertActionEnum.CompactTemplate }), "unavailable action", 400, 400);
                await ClientContract.ExpectErrorAsync(() => c.IgnoreModelAlertAsync("mal_missing", 1), "unknown alert ignore", 404, 404);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: model profiles", cases: cases);
        }

        #endregion
    }
}
