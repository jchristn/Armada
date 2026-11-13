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
    /// Armada.Client contract against a live server for Evaluate: evaluate a captain and a model endpoint (Quick; Full
    /// parameters are sent and validated), read a run with its results, cancel a run, and list runs with filters and paging. Probe answers are canned; no model
    /// runs.
    /// </summary>
    public sealed class ClientContractModelProbeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.ModelProbes";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("evaluate", "Evaluate a captain and an endpoint, get and cancel a run, list runs; invalid Full parameters and unknown ids fail", async (c, fx) =>
            {
                string tenantId = Armada.Core.Constants.DefaultTenantId;
                Captain captain;
                ModelEndpoint endpoint;
                LoggingModule quiet = new LoggingModule();
                quiet.Settings.EnableConsole = false;
                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
                using (DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet))
                {
                    await db.InitializeAsync().ConfigureAwait(false);
                    captain = await db.Captains.CreateAsync(new Captain { Name = "probe-contract-" + ClientContract.Suffix(), Runtime = AgentRuntimeEnum.ClaudeCode, TenantId = tenantId }).ConfigureAwait(false);
                    endpoint = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "probe-contract-ep", BaseUrl = "http://127.0.0.1:9", Model = "canned-model", TenantId = tenantId }).ConfigureAwait(false);
                }

                ModelProbeRun queued = (await c.EvaluateCaptainAsync(captain.Id))!;
                AssertEqual(captain.Id, queued.CaptainId, "captain run");
                AssertEqual(ModelProbeDepthEnum.Quick, queued.Depth, "default depth sent");
                AssertTrue(queued.IsEvaluation, "evaluation run");

                ModelProbeRun endpointRun = (await c.EvaluateModelEndpointAsync(endpoint.Id, ModelProbeDepthEnum.Quick))!;
                AssertEqual(endpoint.Id, endpointRun.ModelEndpointId, "endpoint run");
                AssertEqual(ModelProbeTargetEnum.ModelEndpoint, endpointRun.TargetKind, "endpoint target");

                // Full parameters reach the server: an unknown pack is a 400 naming it, an unknown evaluator a 404.
                ArmadaApiException badPack = await ClientContract.ExpectErrorAsync(() => c.EvaluateCaptainWithRequestAsync(captain.Id,
                    new EvaluateCaptainRequest { Depth = ModelProbeDepthEnum.Full, Packs = new List<string> { "no-such-pack" } }), "Full unknown pack", 400, 400);
                AssertContains("no-such-pack", badPack.Message);
                await ClientContract.ExpectErrorAsync(() => c.EvaluateCaptainWithRequestAsync(captain.Id,
                    new EvaluateCaptainRequest { Depth = ModelProbeDepthEnum.Full, EvaluatorCaptainId = "cpt_missing_evaluator" }), "Full unknown evaluator", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.EvaluateCaptainAsync("cpt_missing"), "unknown captain", 404, 404);
                await ClientContract.ExpectErrorAsync(() => c.EvaluateModelEndpointAsync("mep_missing"), "unknown endpoint", 404, 404);

                ModelProbeRun done = await WaitAsync(c, queued.Id).ConfigureAwait(false);
                AssertEqual(ModelProbeRunStatusEnum.Completed, done.Status, "completed");
                AssertEqual(6, done.Results.Count, "one result per probe");
                await WaitAsync(c, endpointRun.Id).ConfigureAwait(false);
                await ClientContract.ExpectErrorAsync(() => c.GetModelProbeRunAsync("mpr_missing"), "unknown run", 404, 404);
                ModelProbeRun unchanged = (await c.CancelModelProbeRunAsync(queued.Id))!;
                AssertEqual(ModelProbeRunStatusEnum.Completed, unchanged.Status, "cancelling a finished run leaves it as it was");
                await ClientContract.ExpectErrorAsync(() => c.CancelModelProbeRunAsync("mpr_missing"), "cancel unknown run", 404, 404);

                EnumerationResult<ModelProbeRun> byCaptain = (await c.ListModelProbeRunsAsync(new ModelProbeRunQuery { CaptainId = captain.Id, PageSize = 5 }))!;
                AssertEqual(1, (int)byCaptain.TotalRecords, "captain filter sent");
                AssertEqual(queued.Id, byCaptain.Objects.Single().Id, "listed");
                AssertEqual(5, byCaptain.PageSize, "page size sent");
                EnumerationResult<ModelProbeRun> byEndpoint = (await c.ListModelProbeRunsAsync(new ModelProbeRunQuery { ModelEndpointId = endpoint.Id, Status = ModelProbeRunStatusEnum.Completed }))!;
                AssertEqual(endpointRun.Id, byEndpoint.Objects.Single().Id, "endpoint and status filters sent");
                EnumerationResult<ModelProbeRun> failed = (await c.ListModelProbeRunsAsync(new ModelProbeRunQuery { CaptainId = captain.Id, Status = ModelProbeRunStatusEnum.Failed }))!;
                AssertEqual(0, (int)failed.TotalRecords, "status filter sent");
                AssertTrue((await c.ListModelProbeRunsAsync())!.TotalRecords >= 2, "no query");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: Evaluate (model probe runs)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<ModelProbeRun> WaitAsync(ArmadaClient c, string runId)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (true)
            {
                ModelProbeRun run = (await c.GetModelProbeRunAsync(runId).ConfigureAwait(false))!;
                if (run.Status == ModelProbeRunStatusEnum.Completed || run.Status == ModelProbeRunStatusEnum.Failed) return run;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("run " + runId + " still " + run.Status);
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body, fx => fx.Server.ModelProbeExecutorOverride = new CannedModelProbeExecutor());
        }

        #endregion
    }
}
