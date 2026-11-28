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
    /// Descriptors for <see cref="TimeoutPolicyResolver"/>, <see cref="ModelProfileTimeouts"/> and
    /// <see cref="ModelProfileService.SpeedFrom"/> (RS-8.8): the global settings are the defaults, a profile override
    /// replaces them, a slow measured speed scales them up to four times, nothing exceeds four times its global setting,
    /// and out-of-range overrides are refused.
    /// </summary>
    public sealed class TimeoutPolicySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.TimeoutPolicy";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the timeout policy suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("global_settings_are_the_defaults", "Without a profile, overrides or a speed measurement, the global settings apply", TestTags.Positive, () =>
            {
                foreach (ModelProfile? profile in new ModelProfile?[] { null, new ModelProfile() })
                {
                    TimeoutPolicy policy = TimeoutPolicyResolver.Resolve(Settings(), profile);
                    AssertEqual(10, policy.StallThresholdMinutes);
                    AssertEqual(60, policy.ToolWaitHardLimitMinutes);
                    AssertEqual(240, policy.MaxRuntimeMinutes);
                    AssertEqual(10, policy.FirstOutputMinutes, "the first-output time defaults to the stall threshold");
                    AssertEqual(1.0, policy.SpeedScale);
                    AssertEqual(TimeoutSourceEnum.Global, policy.StallThresholdSource);
                    AssertEqual(TimeoutSourceEnum.Global, policy.FirstOutputSource);
                    AssertEqual(profile?.Id, policy.ModelProfileId);
                }
            }));

            cases.Add(Case("profile_overrides_apply", "A profile's overrides replace the global values, within four times each global value", TestTags.Positive, () =>
            {
                ModelProfile profile = new ModelProfile();
                profile.Timeouts.StallThresholdMinutes = 5;
                profile.Timeouts.ToolWaitHardLimitMinutes = 1000;
                profile.Timeouts.MaxRuntimeMinutes = 480;
                profile.Timeouts.FirstOutputMinutes = 30;
                TimeoutPolicy policy = TimeoutPolicyResolver.Resolve(Settings(), profile);
                AssertEqual(5, policy.StallThresholdMinutes, "a profile may shorten a timeout");
                AssertEqual(240, policy.ToolWaitHardLimitMinutes, "1000 is held to four times the global 60");
                AssertEqual(480, policy.MaxRuntimeMinutes);
                AssertEqual(30, policy.FirstOutputMinutes);
                AssertEqual(TimeoutSourceEnum.Profile, policy.StallThresholdSource);
                AssertEqual(TimeoutSourceEnum.Profile, policy.ToolWaitHardLimitSource);
                AssertEqual(TimeoutSourceEnum.Profile, policy.MaxRuntimeSource);
                AssertEqual(TimeoutSourceEnum.Profile, policy.FirstOutputSource);
            }));

            cases.Add(Case("slow_speed_scales_unset_values", "A model slower than the reference rate gets the unset values scaled, at most four times", TestTags.Positive, () =>
            {
                ModelProfile profile = new ModelProfile();
                profile.Speed.TokensPerSecond = 12;
                profile.Timeouts.StallThresholdMinutes = 15;
                TimeoutPolicy policy = TimeoutPolicyResolver.Resolve(Settings(), profile);
                AssertEqual(2.5, policy.SpeedScale, "30 / 12");
                AssertEqual(15, policy.StallThresholdMinutes, "an override wins over the measurement");
                AssertEqual(150, policy.ToolWaitHardLimitMinutes, "60 x 2.5");
                AssertEqual(600, policy.MaxRuntimeMinutes, "240 x 2.5");
                AssertEqual(TimeoutSourceEnum.Measured, policy.ToolWaitHardLimitSource);
                AssertEqual(15, policy.FirstOutputMinutes, "the first-output time follows the stall threshold");

                profile.Speed.TokensPerSecond = 1;
                policy = TimeoutPolicyResolver.Resolve(Settings(), profile);
                AssertEqual(4.0, policy.SpeedScale, "the scale is capped at four");
                AssertEqual(240, policy.ToolWaitHardLimitMinutes);

                profile.Speed.TokensPerSecond = 90;
                policy = TimeoutPolicyResolver.Resolve(Settings(), profile);
                AssertEqual(1.0, policy.SpeedScale, "a fast model keeps the global values");
                AssertEqual(TimeoutSourceEnum.Global, policy.ToolWaitHardLimitSource);

                ArmadaSettings off = Settings();
                off.TimeoutScaleReferenceTokensPerSecond = 0;
                profile.Speed.TokensPerSecond = 1;
                AssertEqual(60, TimeoutPolicyResolver.Resolve(off, profile).ToolWaitHardLimitMinutes, "a reference of 0 turns scaling off");
            }));

            cases.Add(Case("measured_first_output_extends_the_first_output_time", "A slow measured first output lengthens the first-output time, never past four stall thresholds", TestTags.Positive, () =>
            {
                ModelProfile profile = new ModelProfile();
                profile.Speed.FirstOutputMs = 270000;
                TimeoutPolicy policy = TimeoutPolicyResolver.Resolve(Settings(), profile);
                AssertEqual(10, policy.StallThresholdMinutes, "no rate measured, so no scaling");
                AssertEqual(18, policy.FirstOutputMinutes, "4.5 minutes with a margin of four");
                AssertEqual(TimeoutSourceEnum.Measured, policy.FirstOutputSource);

                profile.Speed.FirstOutputMs = 3600000;
                AssertEqual(40, TimeoutPolicyResolver.Resolve(Settings(), profile).FirstOutputMinutes, "held to four times the global stall threshold");

                profile.Speed.FirstOutputMs = 1000;
                policy = TimeoutPolicyResolver.Resolve(Settings(), profile);
                AssertEqual(10, policy.FirstOutputMinutes, "a fast first output keeps the stall threshold");
                AssertEqual(TimeoutSourceEnum.Global, policy.FirstOutputSource);
            }));

            cases.Add(Case("no_runtime_cap_stays_off", "MaxMissionRuntimeMinutes 0 (no cap) is never turned on by a profile", TestTags.Negative, () =>
            {
                ArmadaSettings settings = Settings();
                settings.MaxMissionRuntimeMinutes = 0;
                ModelProfile profile = new ModelProfile();
                profile.Timeouts.MaxRuntimeMinutes = 60;
                profile.Speed.TokensPerSecond = 5;
                AssertEqual(0, TimeoutPolicyResolver.Resolve(settings, profile).MaxRuntimeMinutes);
            }));

            cases.Add(Case("overrides_validate", "Out-of-range overrides and settings are refused", TestTags.Negative, () =>
            {
                ModelProfileTimeouts timeouts = new ModelProfileTimeouts();
                AssertThrows<ArgumentOutOfRangeException>(() => timeouts.StallThresholdMinutes = 0);
                AssertThrows<ArgumentOutOfRangeException>(() => timeouts.StallThresholdMinutes = 241);
                AssertThrows<ArgumentOutOfRangeException>(() => timeouts.ToolWaitHardLimitMinutes = 1441);
                AssertThrows<ArgumentOutOfRangeException>(() => timeouts.MaxRuntimeMinutes = 0);
                AssertThrows<ArgumentOutOfRangeException>(() => timeouts.MaxRuntimeMinutes = 2881);
                AssertThrows<ArgumentOutOfRangeException>(() => timeouts.FirstOutputMinutes = 0);
                timeouts.StallThresholdMinutes = null;
                AssertNull(timeouts.StallThresholdMinutes, "null clears an override");
                ArmadaSettings settings = new ArmadaSettings();
                AssertEqual(30, settings.TimeoutScaleReferenceTokensPerSecond);
                AssertThrows<ArgumentOutOfRangeException>(() => settings.TimeoutScaleReferenceTokensPerSecond = -1);
                AssertThrows<ArgumentOutOfRangeException>(() => settings.TimeoutScaleReferenceTokensPerSecond = 1001);
                ModelProfileSpeed speed = new ModelProfileSpeed { TokensPerSecond = Double.NaN, FirstOutputMs = -1 };
                AssertNull(speed.TokensPerSecond, "a rate that is not a positive number reads as null");
                AssertNull(speed.FirstOutputMs, "a negative first output reads as null");
            }));

            cases.Add(Case("speed_from_probe_results", "The recorded speed is the slowest rate and the slowest first output of the Speed probe", TestTags.Positive, () =>
            {
                ModelProbeResult result = new ModelProbeResult { Probe = ModelProbeKindEnum.Speed };
                result.Metrics.OutputTokensPerSecond4k = 40;
                result.Metrics.OutputTokensPerSecond32k = 18;
                result.Metrics.FirstOutputMs4k = 1200;
                result.Metrics.FirstOutputMs32k = 61000;
                ModelProfileSpeed? speed = ModelProfileService.SpeedFrom(new List<ModelProbeResult> { new ModelProbeResult { Probe = ModelProbeKindEnum.ServedContext }, result });
                AssertNotNull(speed);
                AssertEqual((double?)18, speed!.TokensPerSecond);
                AssertEqual((long?)61000, speed.FirstOutputMs);

                ModelProbeResult unmeasured = new ModelProbeResult { Probe = ModelProbeKindEnum.Speed };
                AssertNull(ModelProfileService.SpeedFrom(new List<ModelProbeResult> { unmeasured }), "no rate, nothing to record");
                AssertNull(ModelProfileService.SpeedFrom(null));
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Timeout Policy", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaSettings Settings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.StallThresholdMinutes = 10;
            settings.ToolWaitHardLimitMinutes = 60;
            settings.MaxMissionRuntimeMinutes = 240;
            settings.TimeoutScaleReferenceTokensPerSecond = 30;
            return settings;
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
