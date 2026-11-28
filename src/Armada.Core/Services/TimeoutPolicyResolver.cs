namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Resolves the timeouts in effect for a captain (RS-8.8). For each of the stall threshold, the tool-wait hard limit,
    /// the maximum runtime and the first-output time: the model profile's override when set; otherwise the global setting
    /// scaled by the profile's measured speed (a model slower than TimeoutScaleReferenceTokensPerSecond gets
    /// proportionally longer, at most four times); otherwise the global setting. No value exceeds four times its global
    /// setting, so the global settings stay both the defaults and the bounds. Thread safe.
    /// </summary>
    public static class TimeoutPolicyResolver
    {
        #region Public-Members

        /// <summary>
        /// The largest factor a profile or measurement can apply to a global setting.
        /// </summary>
        public const double MaxScale = 4.0;

        /// <summary>
        /// Margin applied to a measured first output when deriving the first-output time.
        /// </summary>
        public const double FirstOutputMargin = 4.0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply a profile's overrides and measured speed to the global settings.
        /// </summary>
        /// <param name="settings">Global settings.</param>
        /// <param name="profile">The captain's model profile, or null.</param>
        /// <returns>The timeouts in effect.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static TimeoutPolicy Resolve(ArmadaSettings settings, ModelProfile? profile)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ModelProfileTimeouts overrides = profile?.Timeouts ?? new ModelProfileTimeouts();
            ModelProfileSpeed? speed = profile?.Speed;
            TimeoutPolicy policy = new TimeoutPolicy();
            policy.ModelProfileId = profile?.Id;
            policy.SpeedScale = Scale(settings.TimeoutScaleReferenceTokensPerSecond, speed?.TokensPerSecond);
            bool measured = policy.SpeedScale > 1.0;

            int stall = settings.StallThresholdMinutes;
            policy.StallThresholdMinutes = Pick(stall, overrides.StallThresholdMinutes, measured, policy.SpeedScale, out TimeoutSourceEnum stallSource);
            policy.StallThresholdSource = stallSource;

            policy.ToolWaitHardLimitMinutes = Pick(settings.ToolWaitHardLimitMinutes, overrides.ToolWaitHardLimitMinutes, measured, policy.SpeedScale, out TimeoutSourceEnum toolSource);
            policy.ToolWaitHardLimitSource = toolSource;

            if (settings.MaxMissionRuntimeMinutes <= 0)
            {
                policy.MaxRuntimeMinutes = 0;
                policy.MaxRuntimeSource = TimeoutSourceEnum.Global;
            }
            else
            {
                policy.MaxRuntimeMinutes = Pick(settings.MaxMissionRuntimeMinutes, overrides.MaxRuntimeMinutes, measured, policy.SpeedScale, out TimeoutSourceEnum runtimeSource);
                policy.MaxRuntimeSource = runtimeSource;
            }

            int firstOutputBound = Bound(stall);
            if (overrides.FirstOutputMinutes.HasValue)
            {
                policy.FirstOutputMinutes = Math.Min(overrides.FirstOutputMinutes.Value, firstOutputBound);
                policy.FirstOutputSource = TimeoutSourceEnum.Profile;
            }
            else
            {
                policy.FirstOutputMinutes = policy.StallThresholdMinutes;
                policy.FirstOutputSource = policy.StallThresholdSource;
                if (speed?.FirstOutputMs != null)
                {
                    int needed = (int)Math.Ceiling(speed.FirstOutputMs.Value * FirstOutputMargin / 60000.0);
                    if (needed > policy.FirstOutputMinutes)
                    {
                        policy.FirstOutputMinutes = Math.Min(needed, firstOutputBound);
                        policy.FirstOutputSource = TimeoutSourceEnum.Measured;
                    }
                }
            }

            return policy;
        }

        /// <summary>
        /// The factor for a measured output rate: 1 when scaling is off, the rate is unknown, or the model is at least as
        /// fast as the reference; otherwise the reference divided by the rate, at most <see cref="MaxScale"/>.
        /// </summary>
        /// <param name="referenceTokensPerSecond">TimeoutScaleReferenceTokensPerSecond; 0 turns scaling off.</param>
        /// <param name="tokensPerSecond">The measured output rate, or null.</param>
        /// <returns>The factor, from 1 to <see cref="MaxScale"/>.</returns>
        public static double Scale(int referenceTokensPerSecond, double? tokensPerSecond)
        {
            if (referenceTokensPerSecond <= 0 || !tokensPerSecond.HasValue || tokensPerSecond.Value <= 0) return 1.0;
            double scale = referenceTokensPerSecond / tokensPerSecond.Value;
            if (scale <= 1.0) return 1.0;
            return Math.Min(scale, MaxScale);
        }

        #endregion

        #region Private-Methods

        private static int Pick(int global, int? overrideValue, bool measured, double scale, out TimeoutSourceEnum source)
        {
            int bound = Bound(global);
            if (overrideValue.HasValue)
            {
                source = TimeoutSourceEnum.Profile;
                return Math.Min(overrideValue.Value, bound);
            }

            if (measured)
            {
                source = TimeoutSourceEnum.Measured;
                return Math.Min((int)Math.Ceiling(global * scale), bound);
            }

            source = TimeoutSourceEnum.Global;
            return global;
        }

        private static int Bound(int global)
        {
            return (int)Math.Min(Int32.MaxValue, Math.Ceiling(global * MaxScale));
        }

        #endregion
    }
}
