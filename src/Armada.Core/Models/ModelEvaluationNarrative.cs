namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// The evaluator's part of a Full Evaluate report: rubric grades of the pending battery attempts, strengths and
    /// weaknesses written from the mechanical counts only, observed problems and a short "how to work with this model"
    /// note. Kept only when every count it cites matches the mechanical counts; otherwise Status is Rejected with the
    /// reason and the lists are empty.
    /// </summary>
    public class ModelEvaluationNarrative
    {
        #region Public-Members

        /// <summary>
        /// Skipped, Accepted or Rejected.
        /// </summary>
        public ModelEvaluationNarrativeStatusEnum Status { get; set; } = ModelEvaluationNarrativeStatusEnum.Skipped;

        /// <summary>
        /// Why the narrative was skipped or rejected, or null when accepted.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Strengths, each citing one count.
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxFindings, Reason = "EvaluationNarrativeValidator rejects more")]
        public List<ModelEvaluationFinding> Strengths
        {
            get => _Strengths;
            set => _Strengths = value ?? new List<ModelEvaluationFinding>();
        }

        /// <summary>
        /// Weaknesses, each citing one count.
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxFindings, Reason = "EvaluationNarrativeValidator rejects more")]
        public List<ModelEvaluationFinding> Weaknesses
        {
            get => _Weaknesses;
            set => _Weaknesses = value ?? new List<ModelEvaluationFinding>();
        }

        /// <summary>
        /// Observed problems, one sentence each.
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxFindings, Reason = "EvaluationNarrativeValidator rejects more")]
        public List<string> ObservedProblems
        {
            get => _ObservedProblems;
            set => _ObservedProblems = value ?? new List<string>();
        }

        /// <summary>
        /// Short "how to work with this model" note, or null.
        /// </summary>
        public string? HowToWork { get; set; } = null;

        /// <summary>
        /// Rubric grades of the battery attempts the mechanical graders left pending.
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxSubjectiveGrades, Reason = "One per pending battery attempt the evaluator was shown")]
        public List<ModelEvaluationSubjectiveGrade> SubjectiveGrades
        {
            get => _SubjectiveGrades;
            set => _SubjectiveGrades = value ?? new List<ModelEvaluationSubjectiveGrade>();
        }

        /// <summary>
        /// The evaluator's raw output (at most 8000 characters) when the narrative was rejected, for diagnosis; null
        /// otherwise.
        /// </summary>
        public string? RejectedOutput { get; set; } = null;

        #endregion

        #region Private-Members

        private List<ModelEvaluationFinding> _Strengths = new List<ModelEvaluationFinding>();
        private List<ModelEvaluationFinding> _Weaknesses = new List<ModelEvaluationFinding>();
        private List<string> _ObservedProblems = new List<string>();
        private List<ModelEvaluationSubjectiveGrade> _SubjectiveGrades = new List<ModelEvaluationSubjectiveGrade>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEvaluationNarrative()
        {
        }

        /// <summary>
        /// A skipped narrative with its reason.
        /// </summary>
        /// <param name="reason">Why no evaluator graded the run.</param>
        /// <returns>The narrative.</returns>
        public static ModelEvaluationNarrative Skipped(string reason)
        {
            return new ModelEvaluationNarrative { Status = ModelEvaluationNarrativeStatusEnum.Skipped, Reason = reason };
        }

        #endregion
    }
}
