namespace Armada.Core.Services
{
    /// <summary>
    /// Where one submission block sits in a stage output: its line range, the id its boundary line names, and its JSON.
    /// </summary>
    internal class StageSubmissionBlock
    {
        /// <summary>
        /// Line of the boundary marker (0-based).
        /// </summary>
        public int StartLine { get; set; } = 0;

        /// <summary>
        /// Line of the closing fence (0-based).
        /// </summary>
        public int EndLine { get; set; } = 0;

        /// <summary>
        /// Submission id named on the boundary line, or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// The submission JSON inside the fence.
        /// </summary>
        public string Json { get; set; } = "";
    }
}
