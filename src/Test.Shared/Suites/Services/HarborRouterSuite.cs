namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="HarborRouter"/>: dock-affinity pinning (including the offline-owner stall),
    /// capability filtering, capacity limits, preferred-Harbor honoring, and least-loaded selection. Pure,
    /// deterministic selection logic; positive cases assert the chosen Harbor, negative cases assert the
    /// typed no-eligible reasons.
    /// </summary>
    public sealed class HarborRouterSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the HarborRouter suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("affinity_pins_to_owning_harbor", "Dock affinity pins to the owning Harbor", TestTags.Positive, () =>
            {
                HarborRouter router = new HarborRouter();
                List<Harbor> harbors = new List<Harbor> { Cap("hbr_a", "A", 4, "claude"), Cap("hbr_b", "B", 4, "claude") };
                HarborRoutingRequest request = new HarborRoutingRequest { ExistingHarborId = "hbr_b", RequestedRuntime = "claude" };

                HarborRoutingDecision decision = router.Select(harbors, All, Zero, request);
                AssertTrue(decision.Success, "Expected an affinity pin to succeed.");
                AssertEqual("hbr_b", decision.HarborId);
                AssertTrue(decision.PinnedByAffinity, "Expected the decision to be pinned by affinity.");
            }));

            cases.Add(Case("affinity_offline_owner_stalls", "Dock affinity stalls when the owner is offline", TestTags.Negative, () =>
            {
                HarborRouter router = new HarborRouter();
                List<Harbor> harbors = new List<Harbor> { Cap("hbr_a", "A", 4, "claude"), Cap("hbr_b", "B", 4, "claude") };
                HarborRoutingRequest request = new HarborRoutingRequest { ExistingHarborId = "hbr_b" };

                // hbr_b is registered but not connected.
                HarborRoutingDecision decision = router.Select(harbors, id => id == "hbr_a", Zero, request);
                AssertTrue(!decision.Success, "Expected the stall to fail selection.");
                AssertTrue(decision.Reason != null && decision.Reason.Contains("hbr_b"), "Expected the reason to name the offline owner.");
            }));

            cases.Add(Case("capability_filter_excludes_incapable", "Capability filter excludes Harbors lacking the runtime", TestTags.Positive, () =>
            {
                HarborRouter router = new HarborRouter();
                List<Harbor> harbors = new List<Harbor> { Cap("hbr_a", "A", 4, "codex"), Cap("hbr_b", "B", 4, "claude") };
                HarborRoutingRequest request = new HarborRoutingRequest { RequestedRuntime = "claude" };

                HarborRoutingDecision decision = router.Select(harbors, All, Zero, request);
                AssertTrue(decision.Success, "Expected a capable Harbor to be selected.");
                AssertEqual("hbr_b", decision.HarborId);
            }));

            cases.Add(Case("preferred_harbor_honored_when_eligible", "Preferred Harbor is honored when eligible", TestTags.Positive, () =>
            {
                HarborRouter router = new HarborRouter();
                List<Harbor> harbors = new List<Harbor> { Cap("hbr_a", "A", 4, "claude"), Cap("hbr_b", "B", 4, "claude") };
                HarborRoutingRequest request = new HarborRoutingRequest { RequestedRuntime = "claude", PreferredHarborId = "hbr_a" };

                // hbr_a is busier, but preference wins among eligible.
                HarborRoutingDecision decision = router.Select(harbors, All, id => id == "hbr_a" ? 2 : 0, request);
                AssertTrue(decision.Success, "Expected selection to succeed.");
                AssertEqual("hbr_a", decision.HarborId);
            }));

            cases.Add(Case("least_loaded_selected", "Least-loaded eligible Harbor is selected", TestTags.Positive, () =>
            {
                HarborRouter router = new HarborRouter();
                List<Harbor> harbors = new List<Harbor> { Cap("hbr_a", "A", 4, "claude"), Cap("hbr_b", "B", 4, "claude") };
                HarborRoutingRequest request = new HarborRoutingRequest { RequestedRuntime = "claude" };

                HarborRoutingDecision decision = router.Select(harbors, All, id => id == "hbr_a" ? 3 : 1, request);
                AssertTrue(decision.Success, "Expected selection to succeed.");
                AssertEqual("hbr_b", decision.HarborId);
            }));

            cases.Add(Case("capacity_full_reports_reason", "All-at-capacity reports a clear reason", TestTags.Negative, () =>
            {
                HarborRouter router = new HarborRouter();
                List<Harbor> harbors = new List<Harbor> { Cap("hbr_a", "A", 2, "claude") };
                HarborRoutingRequest request = new HarborRoutingRequest { RequestedRuntime = "claude" };

                HarborRoutingDecision decision = router.Select(harbors, All, _ => 2, request);
                AssertTrue(!decision.Success, "Expected capacity-full to fail selection.");
                AssertTrue(decision.Reason != null && decision.Reason.Contains("capacity"), "Expected a capacity reason.");
            }));

            cases.Add(Case("no_connected_reports_reason", "No connected Harbor reports a clear reason", TestTags.Negative, () =>
            {
                HarborRouter router = new HarborRouter();
                List<Harbor> harbors = new List<Harbor> { Cap("hbr_a", "A", 4, "claude") };
                HarborRoutingRequest request = new HarborRoutingRequest { RequestedRuntime = "claude" };

                HarborRoutingDecision decision = router.Select(harbors, _ => false, Zero, request);
                AssertTrue(!decision.Success, "Expected no-connected to fail selection.");
                AssertTrue(decision.Reason != null && decision.Reason.Contains("connected"), "Expected a connectivity reason.");
            }));

            cases.Add(Case("disabled_harbor_excluded", "Disabled Harbor is excluded from routing", TestTags.Positive, () =>
            {
                HarborRouter router = new HarborRouter();
                Harbor disabled = Cap("hbr_a", "A", 4, "claude");
                disabled.Enabled = false;
                List<Harbor> harbors = new List<Harbor> { disabled, Cap("hbr_b", "B", 4, "claude") };
                HarborRoutingRequest request = new HarborRoutingRequest { RequestedRuntime = "claude" };

                HarborRoutingDecision decision = router.Select(harbors, All, Zero, request);
                AssertTrue(decision.Success, "Expected the enabled Harbor to be selected.");
                AssertEqual("hbr_b", decision.HarborId);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.HarborRouter",
                displayName: "Harbor Router",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Harbor Cap(string id, string name, int capacity, string runtime)
        {
            Harbor harbor = new Harbor { Id = id, Name = name, MaxConcurrentJobs = capacity, Enabled = true };
            harbor.Capabilities = new List<HarborCapability> { new HarborCapability { Name = runtime, Available = true } };
            return harbor;
        }

        private static bool All(string id) => true;

        private static int Zero(string id) => 0;

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.HarborRouter",
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
