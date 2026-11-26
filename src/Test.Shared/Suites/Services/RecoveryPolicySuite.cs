namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="RecoveryPolicyResolver"/> and <see cref="ModelProfileRecovery"/> (RS-5.5): the global
    /// settings are the defaults, a model profile overrides them, Judge rework rounds can be raised but never lowered,
    /// Unclassified resumes never exceed the resumes, and out-of-range overrides are refused.
    /// </summary>
    public sealed class RecoveryPolicySuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the recovery policy suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("global_settings_are_the_defaults", "Without a profile, or with no overrides, the global settings apply", TestTags.Positive, () =>
            {
                ArmadaSettings settings = Settings();
                foreach (ModelProfile? profile in new ModelProfile?[] { null, new ModelProfile() })
                {
                    RecoveryPolicy policy = RecoveryPolicyResolver.Resolve(settings, profile);
                    AssertEqual(3, policy.MaxResumes);
                    AssertEqual(1, policy.UnclassifiedResumes);
                    AssertEqual(10, policy.TransientGraceMinutes);
                    AssertEqual(2, policy.JudgeReworkRounds);
                    AssertEqual(1, policy.OutputRepairAttempts);
                    AssertEqual(profile?.Id, policy.ModelProfileId);
                }
            }));

            cases.Add(Case("profile_overrides_apply", "A profile's overrides replace the global values", TestTags.Positive, () =>
            {
                ModelProfile profile = new ModelProfile();
                profile.Recovery.MaxResumes = 6;
                profile.Recovery.UnclassifiedResumes = 2;
                profile.Recovery.TransientGraceMinutes = 0;
                profile.Recovery.JudgeReworkRounds = 4;
                profile.Recovery.OutputRepairAttempts = 3;
                RecoveryPolicy policy = RecoveryPolicyResolver.Resolve(Settings(), profile);
                AssertEqual(6, policy.MaxResumes);
                AssertEqual(2, policy.UnclassifiedResumes);
                AssertEqual(0, policy.TransientGraceMinutes, "0 is an override, not unset");
                AssertEqual(4, policy.JudgeReworkRounds);
                AssertEqual(3, policy.OutputRepairAttempts);
                AssertEqual(profile.Id, policy.ModelProfileId);
            }));

            cases.Add(Case("judge_rework_rounds_never_shortened", "A profile cannot lower JudgeReworkRounds below the global value", TestTags.Negative, () =>
            {
                ModelProfile profile = new ModelProfile();
                profile.Recovery.JudgeReworkRounds = 1;
                AssertEqual(2, RecoveryPolicyResolver.Resolve(Settings(), profile).JudgeReworkRounds);
            }));

            cases.Add(Case("unclassified_resumes_capped_by_resumes", "Unclassified resumes never exceed the resumes in effect", TestTags.Negative, () =>
            {
                ModelProfile profile = new ModelProfile();
                profile.Recovery.MaxResumes = 1;
                profile.Recovery.UnclassifiedResumes = 3;
                AssertEqual(1, RecoveryPolicyResolver.Resolve(Settings(), profile).UnclassifiedResumes);
                ArmadaSettings settings = Settings();
                settings.MaxRecoveryAttempts = 0;
                AssertEqual(0, RecoveryPolicyResolver.Resolve(settings, null).UnclassifiedResumes, "also for the global values");
            }));

            cases.Add(Case("out_of_range_overrides_refused", "Each override refuses a value outside its range and accepts null", TestTags.Negative, () =>
            {
                ModelProfileRecovery recovery = new ModelProfileRecovery();
                AssertThrows<ArgumentOutOfRangeException>(() => recovery.MaxResumes = 11);
                AssertThrows<ArgumentOutOfRangeException>(() => recovery.MaxResumes = -1);
                AssertThrows<ArgumentOutOfRangeException>(() => recovery.UnclassifiedResumes = 4);
                AssertThrows<ArgumentOutOfRangeException>(() => recovery.TransientGraceMinutes = 121);
                AssertThrows<ArgumentOutOfRangeException>(() => recovery.JudgeReworkRounds = 0);
                AssertThrows<ArgumentOutOfRangeException>(() => recovery.JudgeReworkRounds = 6);
                AssertThrows<ArgumentOutOfRangeException>(() => recovery.OutputRepairAttempts = 4);
                recovery.MaxResumes = 10;
                recovery.MaxResumes = null;
                AssertNull(recovery.MaxResumes);
            }));

            cases.Add(Case("global_settings_clamp", "UnclassifiedResumeLimit and JudgeReworkRounds clamp to their ranges", TestTags.Negative, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertEqual(1, settings.UnclassifiedResumeLimit, "default");
                AssertEqual(2, settings.JudgeReworkRounds, "default");
                settings.UnclassifiedResumeLimit = 9;
                settings.JudgeReworkRounds = 0;
                AssertEqual(3, settings.UnclassifiedResumeLimit);
                AssertEqual(1, settings.JudgeReworkRounds);
            }));

            cases.Add(Case("escalate_after_resumes", "EscalateAfterResumes defaults to the resumes, never exceeds them, and caps Unclassified resumes (RS-5.6)", TestTags.Positive, () =>
            {
                AssertEqual(3, RecoveryPolicyResolver.Resolve(Settings(), null).EscalateAfterResumes, "default: escalate once the resumes are spent");
                ModelProfile profile = new ModelProfile();
                profile.Recovery.EscalateAfterResumes = 0;
                RecoveryPolicy policy = RecoveryPolicyResolver.Resolve(Settings(), profile);
                AssertEqual(0, policy.EscalateAfterResumes);
                AssertEqual(0, policy.UnclassifiedResumes, "an Unclassified exit escalates at once too");
                AssertEqual(3, policy.MaxResumes, "transient outages keep the resumes");
                profile.Recovery.EscalateAfterResumes = 9;
                AssertEqual(3, RecoveryPolicyResolver.Resolve(Settings(), profile).EscalateAfterResumes, "never more than the resumes");
                AssertThrows<ArgumentOutOfRangeException>(() => profile.Recovery.EscalateAfterResumes = 11);
            }));

            cases.Add(Case("max_escalations_clamps", "MaxEscalations defaults to 2 (open, then two frontier rungs) and clamps to [0, 5]", TestTags.Negative, () =>
            {
                ArmadaSettings settings = new ArmadaSettings();
                AssertEqual(2, settings.MaxEscalations);
                AssertFalse(settings.EscalateOnOpenEndpointBreaker, "the breaker fallback is opt-in");
                settings.MaxEscalations = 9;
                AssertEqual(5, settings.MaxEscalations);
                settings.MaxEscalations = -1;
                AssertEqual(0, settings.MaxEscalations);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.RecoveryPolicy",
                displayName: "Recovery Policy",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaSettings Settings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.MaxRecoveryAttempts = 3;
            settings.UnclassifiedResumeLimit = 1;
            settings.TransientOutageGraceMinutes = 10;
            settings.JudgeReworkRounds = 2;
            settings.MaxOutputRepairAttempts = 1;
            return settings;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.RecoveryPolicy",
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
