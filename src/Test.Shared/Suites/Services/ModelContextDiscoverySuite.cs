namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Context discovery (RS-7.8): an endpoint that configures no window gets the one its model server reports (Ollama
    /// /api/ps, then the Modelfile num_ctx from /api/show, or max_model_len from an OpenAI-compatible /v1/models), never the
    /// model's trained maximum. Validation and first use store it on the endpoint as discovered, a Harbor's report is stored
    /// only from a Harbor that advertises it, a configured window always wins, and a model profile records the source.
    /// </summary>
    public sealed class ModelContextDiscoverySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ModelContextDiscovery";
        private const string PsLoaded = "{\"models\":[{\"name\":\"qwen3:8b\",\"model\":\"qwen3:8b\",\"context_length\":32768}]}";
        private const string PsEmpty = "{\"models\":[]}";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("ollama_ps_reports_served_window", "A loaded Ollama model's served window comes from /api/ps and /api/show is not asked", TestTags.Positive, async () =>
            {
                using FakeModelServer server = new FakeModelServer();
                server.Routes["GET /api/ps"] = PsLoaded;
                ModelContextDiscoveryResult found = await ModelContextDiscovery.DiscoverAsync(Ollama(server)).ConfigureAwait(false);
                AssertEqual(32768, found.ContextLength);
                AssertEqual(ModelContextDiscovery.OllamaPsSource, found.Source);
                AssertFalse(server.Requests.Contains("POST /api/show"), "a loaded model needs no /api/show");
            }));

            cases.Add(Case("ollama_show_num_ctx_when_not_loaded", "A model that is not loaded gets the Modelfile num_ctx from /api/show", TestTags.Positive, async () =>
            {
                using FakeModelServer server = new FakeModelServer();
                server.Routes["GET /api/ps"] = PsEmpty;
                server.Routes["POST /api/show"] = "{\"parameters\":\"stop                           \\\"<|im_end|>\\\"\\nnum_ctx                        16384\\ntemperature 0.6\"}";
                ModelContextDiscoveryResult found = await ModelContextDiscovery.DiscoverAsync(Ollama(server)).ConfigureAwait(false);
                AssertEqual(16384, found.ContextLength);
                AssertEqual(ModelContextDiscovery.OllamaShowSource, found.Source);
            }));

            cases.Add(Case("ollama_trained_maximum_not_used", "With no served window and no num_ctx, the trained maximum in model_info is not taken as the window", TestTags.Negative, async () =>
            {
                using FakeModelServer server = new FakeModelServer();
                server.Routes["GET /api/ps"] = PsEmpty;
                server.Routes["POST /api/show"] = "{\"parameters\":\"temperature 0.6\",\"model_info\":{\"qwen3.context_length\":262144}}";
                ModelContextDiscoveryResult found = await ModelContextDiscovery.DiscoverAsync(Ollama(server)).ConfigureAwait(false);
                AssertFalse(found.Found, "Ollama serves its default, not the trained maximum");
                AssertContains("num_ctx", found.Reason ?? String.Empty);
            }));

            cases.Add(Case("vllm_models_max_model_len", "An OpenAI-compatible server's max_model_len for the endpoint's model is the window, under a /v1 base URL or not", TestTags.Positive, async () =>
            {
                using FakeModelServer server = new FakeModelServer();
                server.Routes["GET /v1/models"] = "{\"object\":\"list\",\"data\":[{\"id\":\"other\",\"max_model_len\":8192},{\"id\":\"Qwen/Qwen3-8B\",\"max_model_len\":131072}]}";
                foreach (string baseUrl in new[] { server.BaseUrl + "/v1", server.BaseUrl })
                {
                    ModelEndpoint endpoint = new ModelEndpoint { Kind = ModelEndpointKindEnum.Inference, Provider = ModelProviderEnum.OpenAICompatible, BaseUrl = baseUrl, Model = "Qwen/Qwen3-8B" };
                    ModelContextDiscoveryResult found = await ModelContextDiscovery.DiscoverAsync(endpoint).ConfigureAwait(false);
                    AssertEqual(131072, found.ContextLength, baseUrl);
                    AssertEqual(ModelContextDiscovery.ModelListSource, found.Source);
                }
            }));

            cases.Add(Case("out_of_range_and_unsupported_ignored", "A window outside 512 to 2097152, a missing model, and a provider that reports nothing give no value", TestTags.Negative, async () =>
            {
                AssertNull(ModelContextDiscovery.FindMaxModelLen("{\"data\":[{\"id\":\"m\",\"max_model_len\":100}]}", "m"));
                AssertNull(ModelContextDiscovery.FindMaxModelLen("{\"data\":[{\"id\":\"m\",\"max_model_len\":4096}]}", "other"));
                AssertNull(ModelContextDiscovery.FindMaxModelLen("not json", "m"));
                AssertNull(ModelContextDiscovery.ParseShowNumCtx("{\"parameters\":\"num_ctx 99999999\"}"));
                ModelEndpoint openAi = new ModelEndpoint { Kind = ModelEndpointKindEnum.Inference, Provider = ModelProviderEnum.OpenAI, BaseUrl = "https://api.openai.com/v1", Model = "gpt" };
                ModelContextDiscoveryResult found = await ModelContextDiscovery.DiscoverAsync(openAi).ConfigureAwait(false);
                AssertFalse(found.Found);
                AssertContains("does not report", found.Reason ?? String.Empty);
                ModelEndpoint unreachable = new ModelEndpoint { Kind = ModelEndpointKindEnum.Inference, Provider = ModelProviderEnum.Ollama, BaseUrl = "http://127.0.0.1:1", Model = "qwen3:8b" };
                ModelContextDiscoveryResult down = await ModelContextDiscovery.DiscoverAsync(unreachable).ConfigureAwait(false);
                AssertFalse(down.Found, "an unreachable server is a reason, not a throw");
                AssertNotNull(down.Reason);
            }));

            cases.Add(Case("apply_window_only_when_unconfigured", "The discovered window fills a run snapshot only when it configures none", TestTags.Positive, () =>
            {
                ModelEndpoint discovered = new ModelEndpoint { DiscoveredContextLength = 32768, DiscoveredContextSource = ModelContextDiscovery.OllamaPsSource };
                AssertTrue(ModelContextDiscovery.ApplyWindow(discovered));
                AssertEqual(32768, discovered.InferenceOptions.ContextLength);
                ModelEndpoint configured = new ModelEndpoint { DiscoveredContextLength = 32768, InferenceOptions = new InferenceOptions { ContextLength = 8192 } };
                AssertFalse(ModelContextDiscovery.ApplyWindow(configured), "configured wins");
                AssertEqual(8192, configured.InferenceOptions.ContextLength);
                AssertEqual(ContextLengthSourceEnum.Configured, ModelContextDiscovery.SourceOf(configured));
                AssertEqual(8192, ModelContextDiscovery.WindowOf(configured));
                AssertEqual(ContextLengthSourceEnum.Discovered, ModelContextDiscovery.SourceOf(new ModelEndpoint { DiscoveredContextLength = 4096 }));
                AssertNull(ModelContextDiscovery.SourceOf(new ModelEndpoint()));
                return Task.CompletedTask;
            }));

            cases.Add(Case("validate_stores_discovered_window", "Validating an Ollama endpoint with no window stores the served one; an edit of the model clears it", TestTags.Positive, async () =>
            {
                using FakeModelServer server = new FakeModelServer();
                server.Routes["GET /api/ps"] = PsLoaded;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_ctx", "usr_ctx", false, true, "UnitTest");
                ModelEndpoint request = Ollama(server);
                request.Name = "Qwen";
                request.DiscoveredContextLength = 999999;
                ModelEndpoint created = await service.CreateAsync(auth, request).ConfigureAwait(false);
                AssertNull(created.DiscoveredContextLength, "a create cannot set the discovered window");

                ModelEndpointProbeResult result = await service.ValidateAsync(auth, created.Id).ConfigureAwait(false);
                AssertTrue(result.Success, result.Error ?? "");
                AssertEqual(32768, result.DiscoveredContextLength);
                ModelEndpoint stored = (await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false))!;
                AssertEqual(32768, stored.DiscoveredContextLength);
                AssertEqual(ModelContextDiscovery.OllamaPsSource, stored.DiscoveredContextSource);
                AssertNotNull(stored.ContextDiscoveredUtc);
                AssertNull(stored.InferenceOptions.ContextLength, "discovery never writes the configured window");

                ModelEndpoint edit = Ollama(server);
                edit.Id = created.Id;
                edit.Name = "Qwen";
                edit.Model = "qwen3:14b";
                ModelEndpoint updated = await service.UpdateAsync(auth, edit).ConfigureAwait(false);
                AssertNull(updated.DiscoveredContextLength, "a different model may serve a different window");
            }));

            cases.Add(Case("validate_skips_configured_and_harbor_only", "Validation does not ask for a window the endpoint configures, or for an endpoint that runs only on a Harbor", TestTags.Negative, async () =>
            {
                using FakeModelServer server = new FakeModelServer();
                server.Routes["GET /api/ps"] = PsLoaded;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_ctx2", "usr_ctx2", false, true, "UnitTest");
                foreach (InferenceOptions options in new[] { new InferenceOptions { ContextLength = 8192 }, new InferenceOptions { RequireHarbor = true } })
                {
                    ModelEndpoint request = Ollama(server);
                    request.Name = "Qwen " + Guid.NewGuid().ToString("N");
                    request.InferenceOptions = options;
                    ModelEndpoint created = await service.CreateAsync(auth, request).ConfigureAwait(false);
                    ModelEndpointProbeResult result = await service.ValidateAsync(auth, created.Id).ConfigureAwait(false);
                    AssertNull(result.DiscoveredContextLength);
                    ModelEndpoint stored = (await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false))!;
                    AssertNull(stored.DiscoveredContextLength);
                }

                AssertFalse(server.Requests.Contains("GET /api/ps"), "no discovery request was made");
            }));

            cases.Add(Case("first_use_stores_and_reuses", "First use discovers and stores the window, reuses it within the refresh interval, and does not retry a miss", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpoint stored = await CreateEndpointAsync(testDb.Driver, "http://127.0.0.1:1").ConfigureAwait(false);
                int calls = 0;
                ModelContextDiscoveryResult answer = new ModelContextDiscoveryResult { ContextLength = 40960, Source = ModelContextDiscovery.OllamaPsSource };
                ModelContextDiscoveryService service = new ModelContextDiscoveryService(testDb.Driver, CreateLogging(), (ep, model, ct) => { calls++; return Task.FromResult(answer); });

                ModelEndpoint snapshot = (await testDb.Driver.ModelEndpoints.ReadAsync(stored.Id).ConfigureAwait(false))!;
                await service.EnsureAsync(snapshot, null).ConfigureAwait(false);
                AssertEqual(40960, snapshot.DiscoveredContextLength);
                ModelEndpoint reread = (await testDb.Driver.ModelEndpoints.ReadAsync(stored.Id).ConfigureAwait(false))!;
                AssertEqual(40960, reread.DiscoveredContextLength, "stored on the endpoint");
                AssertEqual(stored.LastUpdateUtc, reread.LastUpdateUtc, "the narrow write leaves the update time alone");

                await service.EnsureAsync(reread, null).ConfigureAwait(false);
                AssertEqual(1, calls, "a fresh stored window is reused");

                ModelContextDiscoveryService missing = new ModelContextDiscoveryService(testDb.Driver, CreateLogging(), (ep, model, ct) => { calls++; return Task.FromResult(new ModelContextDiscoveryResult { Reason = "none" }); });
                ModelEndpoint fresh = await CreateEndpointAsync(testDb.Driver, "http://127.0.0.1:2").ConfigureAwait(false);
                await missing.EnsureAsync(fresh, null).ConfigureAwait(false);
                await missing.EnsureAsync(fresh, null).ConfigureAwait(false);
                AssertEqual(2, calls, "a miss is not retried within the interval");

                ModelEndpoint configured = await CreateEndpointAsync(testDb.Driver, "http://127.0.0.1:3").ConfigureAwait(false);
                configured.InferenceOptions.ContextLength = 8192;
                await service.EnsureAsync(configured, null).ConfigureAwait(false);
                AssertEqual(2, calls, "a configured window is never discovered");
            }));

            cases.Add(Case("harbor_report_stored_from_capable_harbor_only", "A Harbor's exit report is stored for the captain's endpoint, never over a configured window", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelEndpoint endpoint = await CreateEndpointAsync(db, "http://127.0.0.1:4").ConfigureAwait(false);
                Captain captain = new Captain("qwen-harbor", AgentRuntimeEnum.ApiEndpoint) { ModelEndpointId = endpoint.Id };
                captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                await db.HarborJobs.CreateAsync(new HarborJobRecord { JobId = "job_ctx", HarborId = "hbr_ctx", Kind = HarborJobKindEnum.Mission, Runtime = "ApiEndpoint", CaptainId = captain.Id }).ConfigureAwait(false);

                HarborMetricsRecorder recorder = new HarborMetricsRecorder(db, CreateLogging(), new HarborServerSettings());
                recorder.ContextDiscovery = new ModelContextDiscoveryService(db, CreateLogging());
                await recorder.OnExitedAsync(new HarborExited { JobId = "job_ctx", ExitCode = 0, DiscoveredContextLength = 65536, DiscoveredContextSource = ModelContextDiscovery.OllamaPsSource }).ConfigureAwait(false);
                ModelEndpoint stored = (await db.ModelEndpoints.ReadAsync(endpoint.Id).ConfigureAwait(false))!;
                AssertEqual(65536, stored.DiscoveredContextLength);

                stored.InferenceOptions.ContextLength = 8192;
                await db.ModelEndpoints.UpdateAsync(stored).ConfigureAwait(false);
                AssertFalse(await recorder.ContextDiscovery.RecordReportedAsync(endpoint.Id, 16384, ModelContextDiscovery.OllamaPsSource).ConfigureAwait(false), "configured wins");

                HarborInferenceEndpoint shipped = HarborInferenceEndpoint.FromEndpoint(stored);
                AssertEqual(65536, shipped.DiscoveredContextLength, "the Admiral ships the discovered window");
                AssertEqual(65536, shipped.ToEndpoint().DiscoveredContextLength);
                HarborExited wire = (HarborExited)HarborProtocol.Deserialize(HarborProtocol.Serialize(new HarborExited { JobId = "j", DiscoveredContextLength = 4096, DiscoveredContextSource = "s" }));
                AssertEqual(4096, wire.DiscoveredContextLength);
                AssertEqual("api-context-discovery-v1", HarborContextDiscovery.Capability);
            }));

            cases.Add(Case("profile_records_context_source", "A model profile created from a discovered window records Discovered as its source; the source is not part of identity", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelProfileService profiles = new ModelProfileService(testDb.Driver, CreateLogging());
                ModelProfileObservation observation = new ModelProfileObservation
                {
                    TenantId = "ten_prof", Runtime = "ApiEndpoint", Provider = "Ollama", Model = "qwen3:8b",
                    ServedContextTokens = 32768, ServedContextSource = ContextLengthSourceEnum.Discovered
                };
                ModelProfileResolution first = await profiles.ResolveAsync(observation).ConfigureAwait(false);
                ModelProfile? read = await testDb.Driver.ModelProfiles.ReadAsync(first.Profile.Id).ConfigureAwait(false);
                AssertEqual(ContextLengthSourceEnum.Discovered, read!.Identity.ServedContextSource);

                observation.ServedContextSource = ContextLengthSourceEnum.Configured;
                ModelProfileResolution second = await profiles.ResolveAsync(observation).ConfigureAwait(false);
                AssertEqual(first.Profile.Id, second.Profile.Id, "the same window from another source is the same profile");

                observation.ServedContextTokens = 65536;
                ModelProfileResolution third = await profiles.ResolveAsync(observation).ConfigureAwait(false);
                AssertTrue(first.Profile.Id != third.Profile.Id, "a different served window is a different profile");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Model context discovery (RS-7.8)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ModelEndpoint Ollama(FakeModelServer server)
        {
            return new ModelEndpoint { Kind = ModelEndpointKindEnum.Inference, Provider = ModelProviderEnum.Ollama, BaseUrl = server.BaseUrl, Model = "qwen3:8b", TimeoutMs = 5000 };
        }

        private static async Task<ModelEndpoint> CreateEndpointAsync(DatabaseDriver db, string baseUrl)
        {
            ModelEndpoint endpoint = new ModelEndpoint
            {
                Name = "ctx " + Guid.NewGuid().ToString("N"),
                Kind = ModelEndpointKindEnum.Inference,
                Provider = ModelProviderEnum.Ollama,
                BaseUrl = baseUrl,
                Model = "qwen3:8b"
            };
            return await db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
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
