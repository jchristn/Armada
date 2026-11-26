namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// The result of reading an Architect's output for a structured plan: a status, the plan when valid, and the ordered,
    /// capped list of contract errors when not.
    /// </summary>
    public class ArchitectPlanParseResult
    {
        #region Public-Members

        /// <summary>
        /// Most errors kept in <see cref="Errors"/>; the rest are counted in one final entry.
        /// </summary>
        public const int MaxErrors = 20;

        /// <summary>
        /// What the parser found.
        /// </summary>
        public ArchitectPlanParseStatusEnum Status { get; set; } = ArchitectPlanParseStatusEnum.NoPlanFound;

        /// <summary>
        /// The plan, when <see cref="Status"/> is Parsed.
        /// </summary>
        public ArchitectPlan? Plan { get; set; } = null;

        /// <summary>
        /// Contract errors in the order found: at most <see cref="MaxErrors"/>, plus one entry counting the rest.
        /// </summary>
        public List<string> Errors
        {
            get => _Errors;
            set => _Errors = value ?? new List<string>();
        }

        /// <summary>
        /// One message for the Architect's repair turn and the failure reason; null when the plan parsed or no block was found.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Errors = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Keep the first <see cref="MaxErrors"/> errors in order and count the rest.
        /// </summary>
        /// <param name="errors">All errors, in the order found.</param>
        /// <returns>The capped list.</returns>
        /// <exception cref="ArgumentNullException">Thrown when errors is null.</exception>
        public static List<string> Cap(List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (errors.Count <= MaxErrors) return new List<string>(errors);
            List<string> capped = errors.GetRange(0, MaxErrors);
            capped.Add("... and " + (errors.Count - MaxErrors) + " more");
            return capped;
        }

        #endregion
    }
}
