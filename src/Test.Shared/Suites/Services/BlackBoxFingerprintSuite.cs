namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// BlackBoxFingerprint: reason templates replace volatile tokens; the same failure mode hashes the same; different modes differ.
    /// </summary>
    public sealed class BlackBoxFingerprintSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxFingerprint";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("template_normalization", "Ids, SHAs, paths, URLs, numbers and timestamps become placeholders", TestTags.Positive, () =>
            {
                string template = BlackBoxFingerprint.NormalizeReason("Mission msn_mvsqzal6_b1BfR4tnZ29 conflict at 1a2b3c4d5e6f7a8b in src/Armada.Core/X.cs after 3 tries on 2026-10-29T02:26:00Z via https://github.com/a/b  (id 0f8fad5b-d9cb-469f-a165-70867728950e)");
                AssertEqual("Mission {id} conflict at {sha} in {path} after {n} tries on {time} via {url} (id {id})", template);
                AssertEqual("", BlackBoxFingerprint.NormalizeReason(null), "null");
                AssertEqual(BlackBoxFingerprint.MaxTemplateChars, BlackBoxFingerprint.NormalizeReason(new string('a', 5000) + " b").Length, "capped");
                return Task.CompletedTask;
            }));

            cases.Add(Case("same_mode_same_hash", "Two failures that differ only in volatile tokens share a fingerprint; a different step, kind or method does not", TestTags.Positive, () =>
            {
                string a = BlackBoxFingerprint.Compute(BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum.LandingConflict, "LandAsync", "Checkout /Users/a/repo has 52 dirty paths at abc1234def");
                string b = BlackBoxFingerprint.Compute(BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum.LandingConflict, "LandAsync", "Checkout /home/b/other has 3 dirty paths at 9999999aaa");
                AssertEqual(64, a.Length, "hex sha-256");
                AssertEqual(a, b, "same failure mode");
                AssertNotEqual(a, BlackBoxFingerprint.Compute(BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.Push, MissionFailureKindEnum.LandingConflict, "LandAsync", "Checkout /Users/a/repo has 52 dirty paths at abc1234def"), "step");
                AssertNotEqual(a, BlackBoxFingerprint.Compute(BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.DirtyCheckout, null, "LandAsync", "Checkout /Users/a/repo has 52 dirty paths at abc1234def"), "kind");
                AssertNotEqual(a, BlackBoxFingerprint.Compute(BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum.LandingConflict, "Other", "Checkout /Users/a/repo has 52 dirty paths at abc1234def"), "method");
                AssertNotEqual(a, BlackBoxFingerprint.Compute(BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum.LandingConflict, "LandAsync", "Push rejected"), "reason text");
                return Task.CompletedTask;
            }));

            cases.Add(Case("taxonomy_maps_every_step", "Every step except Unknown maps to a known phase and the phase lists contain it", TestTags.Positive, () =>
            {
                foreach (BlackBoxStepEnum step in Enum.GetValues(typeof(BlackBoxStepEnum)))
                {
                    BlackBoxPhaseEnum phase = BlackBoxTaxonomy.PhaseOf(step);
                    if (step == BlackBoxStepEnum.Unknown) { AssertEqual(BlackBoxPhaseEnum.Unknown, phase, "unknown"); continue; }
                    AssertNotEqual(BlackBoxPhaseEnum.Unknown, phase, step + " mapped");
                    AssertTrue(BlackBoxTaxonomy.StepsOf(phase).Contains(step), step + " listed under " + phase);
                }
                AssertEqual(BlackBoxPhaseEnum.Completion, BlackBoxTaxonomy.PhaseOf(BlackBoxStepEnum.ScopeViolation));
                AssertEqual(BlackBoxPhaseEnum.MergeQueue, BlackBoxTaxonomy.PhaseOf(BlackBoxStepEnum.QueueTests));
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box fingerprint", cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
