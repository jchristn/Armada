namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Timeout overrides for a model profile, set by an administrator (RS-8.8). Every value is null unless set; a null
    /// value takes the measured-speed default (the global setting scaled for a slow model), or the global setting when
    /// the profile has no speed measurement. The values apply to missions run by captains linked to the profile. A
    /// resolved value is never more than four times its global setting.
    /// </summary>
    public class ModelProfileTimeouts
    {
        #region Public-Members

        /// <summary>
        /// Minutes without output before a captain counts as stalled (overrides StallThresholdMinutes), 1 to 240, or null.
        /// </summary>
        public int? StallThresholdMinutes
        {
            get => _StallThresholdMinutes;
            set => _StallThresholdMinutes = Check(value, 1, 240, nameof(StallThresholdMinutes));
        }

        /// <summary>
        /// Minutes a quiet runtime tool may run before the stall check applies anyway (overrides ToolWaitHardLimitMinutes),
        /// 1 to 1440, or null.
        /// </summary>
        public int? ToolWaitHardLimitMinutes
        {
            get => _ToolWaitHardLimitMinutes;
            set => _ToolWaitHardLimitMinutes = Check(value, 1, 1440, nameof(ToolWaitHardLimitMinutes));
        }

        /// <summary>
        /// Minutes a mission may run before it is failed as a runaway (overrides MaxMissionRuntimeMinutes), 1 to 2880, or
        /// null. Ignored while MaxMissionRuntimeMinutes is 0 (no cap).
        /// </summary>
        public int? MaxRuntimeMinutes
        {
            get => _MaxRuntimeMinutes;
            set => _MaxRuntimeMinutes = Check(value, 1, 2880, nameof(MaxRuntimeMinutes));
        }

        /// <summary>
        /// Minutes a launched mission may wait for its first output before it counts as stalled, 1 to 240, or null. Until
        /// the first output this replaces the stall threshold, so a slow model reading a long prompt is not stopped early.
        /// </summary>
        public int? FirstOutputMinutes
        {
            get => _FirstOutputMinutes;
            set => _FirstOutputMinutes = Check(value, 1, 240, nameof(FirstOutputMinutes));
        }

        #endregion

        #region Private-Members

        private int? _StallThresholdMinutes = null;
        private int? _ToolWaitHardLimitMinutes = null;
        private int? _MaxRuntimeMinutes = null;
        private int? _FirstOutputMinutes = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileTimeouts()
        {
        }

        #endregion

        #region Private-Methods

        private static int? Check(int? value, int min, int max, string name)
        {
            if (value.HasValue && (value.Value < min || value.Value > max))
                throw new ArgumentOutOfRangeException(name, name + " must be between " + min + " and " + max + ".");
            return value;
        }

        #endregion
    }
}
