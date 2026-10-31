namespace Armada.Core.Services
{
    /// <summary>
    /// Bounds of the Full Evaluate report (collection sizes the server produces or accepts from the evaluator).
    /// </summary>
    public static class EvaluationLimits
    {
        /// <summary>
        /// Most strengths, weaknesses or observed problems the evaluator may write (each list).
        /// </summary>
        public const int MaxFindings = 20;

        /// <summary>
        /// Most subjective grades in one report.
        /// </summary>
        public const int MaxSubjectiveGrades = 500;

        /// <summary>
        /// Most counts in one list of a report (probes, tasks, personas).
        /// </summary>
        public const int MaxCounts = 1000;

        /// <summary>
        /// Most warnings or notes in one report.
        /// </summary>
        public const int MaxNotes = 50;

        /// <summary>
        /// Longest sentence the evaluator may write in one finding, problem or grade note.
        /// </summary>
        public const int MaxSentenceChars = 300;

        /// <summary>
        /// Longest "how to work with this model" note.
        /// </summary>
        public const int MaxHowToWorkChars = 1000;
    }
}
