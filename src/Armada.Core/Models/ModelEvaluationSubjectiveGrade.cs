namespace Armada.Core.Models
{
    /// <summary>
    /// The evaluator's rubric grade of one battery attempt the mechanical graders left pending (an Architect plan, a
    /// Recorder summary). Kept apart from the mechanical counts, which stay the source of truth.
    /// </summary>
    public class ModelEvaluationSubjectiveGrade
    {
        #region Public-Members

        /// <summary>
        /// Battery task result graded (btr_ prefix).
        /// </summary>
        public string ResultId { get; set; } = "";

        /// <summary>
        /// Task key ("task:pack/task").
        /// </summary>
        public string? TaskKey { get; set; } = null;

        /// <summary>
        /// Whether the attempt meets the task's rubric.
        /// </summary>
        public bool Passed { get; set; } = false;

        /// <summary>
        /// Short reason, at most 300 characters.
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEvaluationSubjectiveGrade()
        {
        }

        #endregion
    }
}
