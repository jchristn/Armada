namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// The timeouts in effect for a captain's missions (RS-8.8): each global setting, replaced by the model profile's
    /// override or, without one, scaled for a slow model from the profile's measured speed. Produced by
    /// <see cref="Armada.Core.Services.TimeoutPolicyResolver"/>.
    /// </summary>
    public class TimeoutPolicy
    {
        /// <summary>
        /// The model profile that was applied, or null when only the global settings apply.
        /// </summary>
        public string? ModelProfileId { get; set; } = null;

        /// <summary>
        /// The factor applied to global settings for values the profile does not set: 1 for a fast or unmeasured model,
        /// up to 4 for a slow one.
        /// </summary>
        public double SpeedScale { get; set; } = 1;

        /// <summary>
        /// Minutes without output before the captain counts as stalled.
        /// </summary>
        public int StallThresholdMinutes { get; set; } = 10;

        /// <summary>
        /// Where <see cref="StallThresholdMinutes"/> came from.
        /// </summary>
        public TimeoutSourceEnum StallThresholdSource { get; set; } = TimeoutSourceEnum.Global;

        /// <summary>
        /// Minutes a quiet runtime tool may run before the stall check applies anyway.
        /// </summary>
        public int ToolWaitHardLimitMinutes { get; set; } = 60;

        /// <summary>
        /// Where <see cref="ToolWaitHardLimitMinutes"/> came from.
        /// </summary>
        public TimeoutSourceEnum ToolWaitHardLimitSource { get; set; } = TimeoutSourceEnum.Global;

        /// <summary>
        /// Minutes a mission may run before it is failed as a runaway; 0 means no cap.
        /// </summary>
        public int MaxRuntimeMinutes { get; set; } = 240;

        /// <summary>
        /// Where <see cref="MaxRuntimeMinutes"/> came from.
        /// </summary>
        public TimeoutSourceEnum MaxRuntimeSource { get; set; } = TimeoutSourceEnum.Global;

        /// <summary>
        /// Minutes a launched mission may wait for its first output before it counts as stalled. Equals
        /// <see cref="StallThresholdMinutes"/> unless the profile sets it or a measured first output needs longer.
        /// </summary>
        public int FirstOutputMinutes { get; set; } = 10;

        /// <summary>
        /// Where <see cref="FirstOutputMinutes"/> came from.
        /// </summary>
        public TimeoutSourceEnum FirstOutputSource { get; set; } = TimeoutSourceEnum.Global;
    }
}
