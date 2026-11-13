namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// The result of one alert rule for one profile, revision group, persona and vessel scope.
    /// </summary>
    public class ModelAlertRuleResult
    {
        /// <summary>
        /// Rule.
        /// </summary>
        public ModelAlertRuleEnum Rule { get; set; } = ModelAlertRuleEnum.FirstPassSuccess;

        /// <summary>
        /// Crossed, Clear, MinimumNotMet or Unavailable (inactive).
        /// </summary>
        public ModelAlertRuleStateEnum State { get; set; } = ModelAlertRuleStateEnum.Clear;

        /// <summary>
        /// The metric count, or null.
        /// </summary>
        public int? Count { get; set; } = null;

        /// <summary>
        /// The metric denominator, or null.
        /// </summary>
        public int? Of { get; set; } = null;

        /// <summary>
        /// The metric value (rate, streak, or tokens per accepted result), or null.
        /// </summary>
        public double? Value { get; set; } = null;

        /// <summary>
        /// Lower bound of the 95% Wilson interval for rate rules, or null.
        /// </summary>
        public double? Lower { get; set; } = null;

        /// <summary>
        /// Upper bound of the 95% Wilson interval for rate rules, or null.
        /// </summary>
        public double? Upper { get; set; } = null;

        /// <summary>
        /// The effective threshold (for the cost rule, the token limit computed from the median), or null.
        /// </summary>
        public double? Threshold { get; set; } = null;

        /// <summary>
        /// The minimum count the rule needs.
        /// </summary>
        public int Minimum { get; set; } = 0;

        /// <summary>
        /// For the cost rule: the persona median of tokens per accepted result across the other profiles, or null.
        /// </summary>
        public double? Median { get; set; } = null;

        /// <summary>
        /// For the cost rule: how many other profile groups the median is taken over.
        /// </summary>
        public int? MedianOf { get; set; } = null;

        /// <summary>
        /// Why the rule is inactive or not judged, or null.
        /// </summary>
        public string? Reason { get; set; } = null;
    }
}
