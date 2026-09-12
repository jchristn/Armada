namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ResourceAdmission"/>. Positive cases confirm ample memory admits and low
    /// memory defers with a reason; negative cases confirm the check fails open on an unmeasurable probe
    /// and never divides by zero, so a bad reading can never wedge all dispatch.
    /// </summary>
    public sealed class ResourceAdmissionSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the resource-admission suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            const long gb = 1024L * 1024L * 1024L;

            cases.Add(Case("ample_memory_admits", "Ample available memory admits", TestTags.Positive, () =>
            {
                AdmissionDecision d = ResourceAdmission.Evaluate(8 * gb, 16 * gb, 2 * gb);
                AssertTrue(d.Admit, "expected admit");
                AssertNull(d.DeferReason);
            }));

            cases.Add(Case("low_memory_defers", "Available below the floor defers with a reason", TestTags.Positive, () =>
            {
                AdmissionDecision d = ResourceAdmission.Evaluate(1 * gb, 16 * gb, 2 * gb);
                AssertFalse(d.Admit, "expected defer");
                AssertNotNull(d.DeferReason);
            }));

            cases.Add(Case("disabled_gate_always_admits", "Zero/negative floor disables the gate", TestTags.Positive, () =>
            {
                AssertTrue(ResourceAdmission.Evaluate(1, 16 * gb, 0).Admit, "floor 0 should admit");
                AssertTrue(ResourceAdmission.Evaluate(1, 16 * gb, -1).Admit, "negative floor should admit");
            }));

            cases.Add(Case("unmeasurable_probe_fails_open", "Non-positive available memory fails open (admit)", TestTags.Negative, () =>
            {
                AssertTrue(ResourceAdmission.Evaluate(0, 16 * gb, 2 * gb).Admit, "unmeasurable available should admit");
                AssertTrue(ResourceAdmission.Evaluate(-5, 16 * gb, 2 * gb).Admit, "negative available should admit");
            }));

            cases.Add(Case("zero_total_does_not_throw", "Zero total memory does not throw", TestTags.Negative, () =>
            {
                AdmissionDecision d = ResourceAdmission.Evaluate(1 * gb, 0, 2 * gb);
                // available (1GB) < floor (2GB) -> defer, and no divide-by-zero on the informational total.
                AssertFalse(d.Admit, "expected defer");
            }));

            cases.Add(Case("settings_floor_clamps_negative_to_zero", "The settings memory floor clamps a negative value to zero", TestTags.Negative, () =>
            {
                Armada.Core.Settings.ArmadaSettings settings = new Armada.Core.Settings.ArmadaSettings();
                settings.MinAvailableMemoryBytesForLaunch = -5;
                AssertEqual(0L, settings.MinAvailableMemoryBytesForLaunch, "a negative floor clamps to 0 (gate disabled)");
                AssertEqual(Armada.Core.Constants.DefaultMinAvailableMemoryBytesForLaunch, new Armada.Core.Settings.ArmadaSettings().MinAvailableMemoryBytesForLaunch, "the default floor comes from Constants");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ResourceAdmission",
                displayName: "Resource Admission",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ResourceAdmission",
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
