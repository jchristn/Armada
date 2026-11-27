namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// The result of checking submit tool arguments: the validated submission, or the ordered errors to return to the
    /// model in the same call.
    /// </summary>
    public class StageSubmissionCheck
    {
        /// <summary>
        /// Most errors returned for one call.
        /// </summary>
        public const int MaxErrors = 20;

        /// <summary>
        /// The validated submission, or null when there are errors.
        /// </summary>
        public StageSubmissionEnvelope? Submission { get; set; } = null;

        /// <summary>
        /// Errors in the order found, at most <see cref="MaxErrors"/>.
        /// </summary>
        [CollectionBound(MaxErrors + 1, Reason = "At most MaxErrors, plus one line counting the rest")]
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// True when the submission is valid.
        /// </summary>
        public bool Accepted => Submission != null && Errors.Count == 0;
    }
}
