namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ModelEndpointService.CheckHealthAllAsync"/> grouping. The sweep dedupes
    /// probes by (normalized base URL, API key): endpoints that share a URL AND key are probed once, but
    /// endpoints on the same URL with different keys are probed separately so each endpoint's health reflects
    /// its own configured credential rather than an arbitrary co-located endpoint's. The probes target a
    /// closed loopback port, so they fail fast; the assertion is on the number of distinct probes (the return
    /// value), which counts groups regardless of probe outcome.
    /// </summary>
    public sealed class ModelEndpointHealthSweepSuite : IArmadaTestSuite
    {
        #region Public-Members

        // A closed loopback port so each probe fails fast with connection-refused rather than hanging.
        private const string ClosedUrl = "http://127.0.0.1:9";
        private const string OtherClosedUrl = "http://127.0.0.1:19";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the model-endpoint health-sweep suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("same_url_same_key_probes_once", "Endpoints sharing a URL and key are probed once", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-a", ClosedUrl, "shared-key").ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-b", ClosedUrl, "shared-key").ConfigureAwait(false);

                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                int probed = await service.CheckHealthAllAsync().ConfigureAwait(false);

                AssertEqual(1, probed);
            }));

            cases.Add(CaseAsync("same_url_different_keys_probe_separately", "Endpoints on one URL with different keys are probed separately", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-a", ClosedUrl, "key-one").ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-b", ClosedUrl, "key-two").ConfigureAwait(false);

                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                int probed = await service.CheckHealthAllAsync().ConfigureAwait(false);

                AssertEqual(2, probed);
            }));

            cases.Add(CaseAsync("api_keys_are_case_sensitive_in_grouping", "Keys differing only by case are probed separately", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-a", ClosedUrl, "AbC").ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-b", ClosedUrl, "abc").ConfigureAwait(false);

                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                int probed = await service.CheckHealthAllAsync().ConfigureAwait(false);

                AssertEqual(2, probed);
            }));

            cases.Add(CaseAsync("different_urls_probe_separately", "Endpoints on different URLs are probed separately even with the same key", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-a", ClosedUrl, "shared-key").ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-b", OtherClosedUrl, "shared-key").ConfigureAwait(false);

                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                int probed = await service.CheckHealthAllAsync().ConfigureAwait(false);

                AssertEqual(2, probed);
            }));

            cases.Add(CaseAsync("same_url_key_different_kind_probe_separately", "Endpoints on one URL and key that differ in kind are probed separately", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-a", ClosedUrl, "shared-key", ModelProviderEnum.OpenAICompatible, ModelEndpointKindEnum.Inference).ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-b", ClosedUrl, "shared-key", ModelProviderEnum.OpenAICompatible, ModelEndpointKindEnum.Embedding).ConfigureAwait(false);

                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                int probed = await service.CheckHealthAllAsync().ConfigureAwait(false);

                AssertEqual(2, probed);
            }));

            cases.Add(CaseAsync("same_url_key_different_provider_probe_separately", "Endpoints on one URL and key that differ in provider are probed separately", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-a", ClosedUrl, "shared-key", ModelProviderEnum.OpenAICompatible, ModelEndpointKindEnum.Inference).ConfigureAwait(false);
                await SeedEndpointAsync(testDb, "ep-b", ClosedUrl, "shared-key", ModelProviderEnum.OpenAI, ModelEndpointKindEnum.Inference).ConfigureAwait(false);

                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                int probed = await service.CheckHealthAllAsync().ConfigureAwait(false);

                AssertEqual(2, probed);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ModelEndpointHealthSweep",
                displayName: "Model Endpoint Health Sweep",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task SeedEndpointAsync(
            TestDatabase testDb,
            string name,
            string baseUrl,
            string apiKey,
            ModelProviderEnum provider = ModelProviderEnum.OpenAICompatible,
            ModelEndpointKindEnum kind = ModelEndpointKindEnum.Inference)
        {
            ModelEndpoint endpoint = new ModelEndpoint();
            endpoint.TenantId = "default";
            endpoint.UserId = "default";
            endpoint.Name = name;
            endpoint.Provider = provider;
            endpoint.Kind = kind;
            endpoint.BaseUrl = baseUrl;
            endpoint.Model = "test-model";
            endpoint.ApiKey = apiKey;
            endpoint.TimeoutMs = 2000;
            endpoint.Enabled = true;
            await testDb.Driver.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ModelEndpointHealthSweep",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
