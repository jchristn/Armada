namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The endpoint concurrency key (RS-8.9): scope (Harbor or Admiral for a loopback URL, shared otherwise), normalized
    /// URL and model. Loopback spellings, default ports, trailing slashes and a trailing /v1 are one server; localhost on
    /// two Harbors is two budgets; two models on one server are two budgets.
    /// </summary>
    public sealed class EndpointConcurrencyKeySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.EndpointConcurrencyKey";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("loopback_spellings_are_one_server", "localhost, 127.0.0.1 and ::1 with or without a trailing slash or /v1 share one key", TestTags.Positive, () =>
            {
                string expected = ConcurrencyCapacityPolicy.EndpointKey(Endpoint("http://localhost:11434", "qwen3.8:27b"));
                AssertEqual("admiral|http://localhost:11434|qwen3.8:27b", expected);
                foreach (string url in new[] { "http://127.0.0.1:11434/", "HTTP://LOCALHOST:11434/v1", "http://[::1]:11434/v1/", "http://127.0.0.2:11434" })
                    AssertEqual(expected, ConcurrencyCapacityPolicy.EndpointKey(Endpoint(url, "Qwen3.8:27B")), url);
            }));

            cases.Add(Case("default_ports_and_paths_normalize", "A default port is dropped and a non-loopback host has the shared scope", TestTags.Positive, () =>
            {
                AssertEqual("any|https://api.example.com|gpt-6.1", ConcurrencyCapacityPolicy.EndpointKey(Endpoint("https://API.example.com:443/v1/", "gpt-6.1"), "hbr_a"));
                AssertEqual("any|http://gpu.example.lan:8080/ollama|qwen", ConcurrencyCapacityPolicy.EndpointKey(Endpoint("http://gpu.example.lan:8080/ollama/", "qwen")));
                AssertEqual("endpoint:mep_x", ConcurrencyCapacityPolicy.EndpointKey(new ModelEndpoint { Id = "mep_x", BaseUrl = " " }));
            }));

            cases.Add(Case("loopback_scope_follows_the_host", "localhost on two Harbors is two keys; a shared server is one key from any host", TestTags.Positive, () =>
            {
                ModelEndpoint local = Endpoint("http://localhost:11434", "qwen");
                string a = ConcurrencyCapacityPolicy.EndpointKey(local, "hbr_a");
                string b = ConcurrencyCapacityPolicy.EndpointKey(local, "hbr_b");
                AssertNotEqual(a, b, "two Harbors' localhost are two servers");
                AssertNotEqual(a, ConcurrencyCapacityPolicy.EndpointKey(local), "the Admiral's localhost is a third");
                ModelEndpoint lan = Endpoint("http://10.0.0.5:11434", "qwen");
                AssertEqual(ConcurrencyCapacityPolicy.EndpointKey(lan, "hbr_a"), ConcurrencyCapacityPolicy.EndpointKey(lan, "hbr_b"));
                AssertEqual(ConcurrencyCapacityPolicy.ServerKey(local), ConcurrencyCapacityPolicy.ServerKey(Endpoint("http://127.0.0.1:11434/v1", "QWEN")));
            }));

            cases.Add(Case("models_are_separate_budgets", "Two models on one server are two keys", TestTags.Positive, () =>
            {
                AssertNotEqual(ConcurrencyCapacityPolicy.EndpointKey(Endpoint("http://localhost:11434", "qwen")),
                    ConcurrencyCapacityPolicy.EndpointKey(Endpoint("http://localhost:11434", "glm")));
            }));

            cases.Add(Case("capacity_counts_loopback_usage_per_harbor", "A busy localhost captain on Harbor A fills A's budget, not Harbor B's or the Admiral's", TestTags.Reliability, () =>
            {
                ModelEndpoint first = Endpoint("http://localhost:11434", "qwen", 1);
                ModelEndpoint second = Endpoint("http://127.0.0.1:11434/", "qwen", 1);
                Captain busy = new Captain("busy") { ModelEndpointId = first.Id, ActiveMissionCount = 1, State = CaptainStateEnum.Working };
                Captain idle = new Captain("idle") { ModelEndpointId = second.Id };
                Dictionary<string, ModelEndpoint?> endpoints = new Dictionary<string, ModelEndpoint?> { [first.Id] = first, [second.Id] = second };
                Dictionary<string, List<string?>> hosts = ConcurrencyCapacityPolicy.HostsByCaptain(new List<Dock>
                {
                    new Dock("vsl_x") { CaptainId = busy.Id, HarborId = "hbr_a", State = DockStateEnum.Leased, Active = true },
                    new Dock("vsl_x") { CaptainId = busy.Id, HarborId = "hbr_old", State = DockStateEnum.Available, Active = true }
                });
                CapacityModel model = new CapacityModel(new List<Captain> { busy, idle }, 1, endpoints, new ArmadaSettings(), hosts);
                AssertNotNull(model.EndpointBlocker(idle, "hbr_a"), "Harbor A's localhost is at its cap of one");
                AssertNull(model.EndpointBlocker(idle, "hbr_b"), "Harbor B's localhost has room");
                AssertNull(model.EndpointBlocker(idle), "the Admiral's localhost has room");

                CapacityModel withoutHosts = new CapacityModel(new List<Captain> { busy, idle }, 1, endpoints, new ArmadaSettings());
                AssertNotNull(withoutHosts.EndpointBlocker(idle), "without dock hosts every slot counts on the Admiral's host, as before");
            }));

            cases.Add(Case("dock_hosts_needed_only_for_capped_loopback", "Docks are read only when a busy captain drives a capped loopback endpoint", TestTags.Positive, () =>
            {
                ModelEndpoint capped = Endpoint("http://localhost:11434", "qwen", 2);
                ModelEndpoint remote = Endpoint("https://api.example.com", "gpt", 2);
                Captain busy = new Captain("busy") { ModelEndpointId = capped.Id, ActiveMissionCount = 1 };
                AssertTrue(ConcurrencyCapacityPolicy.NeedsDockHosts(new List<Captain> { busy }, c => capped));
                AssertFalse(ConcurrencyCapacityPolicy.NeedsDockHosts(new List<Captain> { busy }, c => remote));
                AssertFalse(ConcurrencyCapacityPolicy.NeedsDockHosts(new List<Captain> { new Captain("idle") }, c => capped));
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Endpoint Concurrency Key", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ModelEndpoint Endpoint(string url, string? model, int? cap = null)
        {
            return new ModelEndpoint
            {
                Id = "mep_" + Guid.NewGuid().ToString("N"),
                BaseUrl = url,
                Model = model,
                InferenceOptions = cap.HasValue ? new InferenceOptions { MaxConcurrentMissions = cap } : new InferenceOptions()
            };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
