namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// What the Harbor app shows (RS-12.19): its capability flags, the update prompt when the Admiral expects a flag it
    /// lacks, and the context windows discovered for local endpoints.
    /// </summary>
    public sealed class HarborAppStatusSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborAppStatus";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("current_harbor_has_no_update_prompt", "A Harbor advertising every expected flag lists them and shows no update prompt", () =>
            {
                List<HarborCapability> capabilities = new List<HarborCapability> { new HarborCapability { Name = "git", Available = true } };
                HarborCapabilityFlags.AddAdvertised(capabilities);
                HarborCapabilityReport report = HarborCapabilityReport.Build(capabilities, null);
                AssertEqual(HarborCapabilityFlags.Advertised.Count, report.Advertised.Count, "every advertised flag listed");
                AssertFalse(report.Advertised.Contains("git"), "tools are not capability flags");
                AssertFalse(report.UpdateRecommended);
                AssertNull(report.UpdatePrompt);
                AssertEqual("Harbor features: " + HarborCapabilityFlags.Advertised.Count + " advertised.", report.Summary);
                AssertEqual(HarborCapabilityFlags.All.Count, report.Details.Count, "one detail line per declared flag");
                AssertTrue(report.Details.All(l => !l.Contains("]: missing", StringComparison.Ordinal)), "nothing missing");
                return Task.CompletedTask;
            }));

            cases.Add(Case("missing_flags_raise_the_update_prompt", "Flags the Admiral lists as missing produce the update prompt in plain language", () =>
            {
                List<HarborCapability> capabilities = new List<HarborCapability> { new HarborCapability { Name = HarborCapabilityFlags.InferenceStreaming, Available = true } };
                HarborCapabilityReport report = HarborCapabilityReport.Build(capabilities,
                    new List<string> { HarborCapabilityFlags.SubmitTool, HarborCapabilityFlags.ContextDiscovery, HarborCapabilityFlags.SubmitTool, "api-future-v9" });
                AssertTrue(report.UpdateRecommended);
                AssertEqual(3, report.Missing.Count, "duplicates dropped");
                string prompt = report.UpdatePrompt!;
                AssertTrue(prompt.StartsWith("Harbor update recommended.", StringComparison.Ordinal), prompt);
                AssertTrue(prompt.Contains(HarborCapabilityCatalog.Describe(HarborCapabilityFlags.SubmitTool), StringComparison.Ordinal), "plain-language flag name");
                AssertTrue(prompt.Contains("api-future-v9", StringComparison.Ordinal), "an unknown flag shows by name");
                AssertTrue(report.Summary.EndsWith("3 the Admiral expects are missing.", StringComparison.Ordinal), report.Summary);
                AssertTrue(report.Details.Any(l => l.Contains("[" + HarborCapabilityFlags.ContextDiscovery + "]: missing", StringComparison.Ordinal)), "missing flag detail");
                AssertTrue(report.Details.Any(l => l.Contains("[" + HarborCapabilityFlags.InferenceStreaming + "]: yes", StringComparison.Ordinal)), "advertised flag detail");
                AssertTrue(report.Details.Any(l => l.StartsWith("api-future-v9: missing", StringComparison.Ordinal)), "unknown missing flag detail");
                AssertFalse(prompt.Contains('\u2014'), "no em-dashes");
                return Task.CompletedTask;
            }));

            cases.Add(Case("every_declared_flag_has_a_description", "Each declared flag has a plain-language description", () =>
            {
                foreach (string flag in HarborCapabilityFlags.All)
                    AssertFalse(String.Equals(HarborCapabilityCatalog.Describe(flag), flag, StringComparison.Ordinal), "described: " + flag);
                AssertEqual("", HarborCapabilityCatalog.Describe(null));
                return Task.CompletedTask;
            }));

            cases.Add(Case("discovered_contexts_keep_the_latest_per_endpoint_and_model", "The context log keeps the latest entry per endpoint and model, is bounded, and describes each", () =>
            {
                HarborDiscoveredContextLog log = new HarborDiscoveredContextLog();
                int changes = 0;
                log.Changed += () => changes++;
                DateTime start = DateTime.UtcNow.AddHours(-1);
                log.Record(new HarborDiscoveredContext { EndpointName = "local", Model = "qwen3.8", Reason = "server unreachable", DiscoveredUtc = start });
                log.Record(new HarborDiscoveredContext { EndpointName = "local", Model = "qwen3.8", ContextLength = 131072, Source = "ollama /api/show", DiscoveredUtc = start.AddMinutes(1) });
                log.Record(new HarborDiscoveredContext { EndpointName = "lab", Model = null, Reason = "no window reported", DiscoveredUtc = start.AddMinutes(2) });
                AssertEqual(3, changes);
                List<HarborDiscoveredContext> entries = log.Snapshot();
                AssertEqual(2, entries.Count, "one entry per endpoint and model");
                List<string> lines = HarborDiscoveredContextLog.Describe(entries);
                AssertEqual("lab: not reported (no window reported)", lines[0]);
                AssertEqual("local (qwen3.8): 131,072 tokens, from ollama /api/show", lines[1]);

                for (int i = 0; i < HarborDiscoveredContextLog.MaxEntries + 5; i++)
                    log.Record(new HarborDiscoveredContext { EndpointName = "e" + i, ContextLength = 4096, DiscoveredUtc = start.AddMinutes(10 + i) });
                entries = log.Snapshot();
                AssertEqual(HarborDiscoveredContextLog.MaxEntries, entries.Count, "bounded");
                AssertFalse(entries.Any(e => e.EndpointName == "local"), "oldest dropped first");
                AssertThrows<ArgumentNullException>(() => log.Record(null!));
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Harbor app status", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
            => new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: token => body(), tags: new List<string> { TestTags.Positive });

        #endregion
    }
}
