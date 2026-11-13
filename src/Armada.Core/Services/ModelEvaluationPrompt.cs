namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// The prompt a Full Evaluate sends its evaluator: the mechanical counts (with their keys), the battery attempts that
    /// wait for subjective grading with their rubric and output, and the exact JSON reply format. The evaluator grades only
    /// those attempts and writes strengths, weaknesses, observed problems and a short note from the counts only.
    /// </summary>
    public static class ModelEvaluationPrompt
    {
        #region Public-Members

        /// <summary>
        /// Most pending attempts shown to the evaluator.
        /// </summary>
        public const int MaxPendingShown = 30;

        /// <summary>
        /// Longest attempt output shown to the evaluator.
        /// </summary>
        public const int MaxOutputShownChars = 4000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the prompt.
        /// </summary>
        /// <param name="report">Report with its counts filled.</param>
        /// <param name="candidate">How the candidate is named in the prompt (runtime and model).</param>
        /// <param name="pending">Attempts waiting for subjective grading (at most <see cref="MaxPendingShown"/> are shown).</param>
        /// <param name="rubricOf">Reads an attempt's rubric, or null.</param>
        /// <returns>Prompt.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string Build(ModelEvaluationReport report, string candidate, IReadOnlyList<BatteryTaskResult> pending, Func<BatteryTaskResult, string?> rubricOf)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (pending == null) throw new ArgumentNullException(nameof(pending));
            if (rubricOf == null) throw new ArgumentNullException(nameof(rubricOf));

            StringBuilder sb = new StringBuilder();
            sb.Append("You are the evaluator of an Armada model evaluation of ").Append(candidate).Append(".\n");
            sb.Append("Armada already graded everything it can grade mechanically. Those counts are the source of truth.\n");
            sb.Append("Your job: (1) grade only the attempts listed under PENDING against their rubric; (2) write a short strengths and\n");
            sb.Append("weaknesses section from the COUNTS only; (3) list observed problems; (4) write a short note on how to work with this model.\n\n");
            sb.Append("Rules:\n");
            sb.Append("- Every strength and weakness names exactly one count by its key and repeats its passed and total numbers exactly.\n");
            sb.Append("- Write counts in text as \"N of M\" with the same numbers, for example \"Strong: single-file Worker edits (3 of 3).\"\n");
            sb.Append("- Do not cite any number of the form \"N of M\" that is not one of the counts below.\n");
            sb.Append("- Use plain ASCII only: no em dashes, smart quotes or other non-ASCII characters.\n");
            sb.Append("- At most ").Append(EvaluationLimits.MaxFindings).Append(" strengths, ").Append(EvaluationLimits.MaxFindings)
                .Append(" weaknesses and ").Append(EvaluationLimits.MaxFindings).Append(" observed problems; each at most ")
                .Append(EvaluationLimits.MaxSentenceChars).Append(" characters. The note is at most ").Append(EvaluationLimits.MaxHowToWorkChars).Append(" characters.\n\n");

            sb.Append("COUNTS (key | label | passed | total graded | could not run | pending):\n");
            foreach (ModelEvaluationCount count in ModelEvaluationReportBuilder.CountsByKey(report).Values)
            {
                sb.Append(count.Key).Append(" | ").Append(count.Label).Append(" | ").Append(count.Passed).Append(" | ").Append(count.Graded)
                    .Append(" | ").Append(count.CouldNotRun).Append(" | ").Append(count.PendingEvaluation).Append('\n');
            }

            sb.Append("\nPENDING (attempts for you to grade):\n");
            if (pending.Count == 0) sb.Append("(none)\n");
            foreach (BatteryTaskResult result in pending.Take(MaxPendingShown))
            {
                sb.Append("--- resultId: ").Append(result.Id).Append('\n');
                sb.Append("task: ").Append(ModelEvaluationReportBuilder.TaskKey(result.PackId, result.TaskId)).Append(" (").Append(result.TaskTitle).Append(")\n");
                sb.Append("persona: ").Append(result.Persona).Append('\n');
                string? rubric = rubricOf(result);
                sb.Append("rubric: ").Append(String.IsNullOrWhiteSpace(rubric) ? "(none given; judge whether the output does what the task asked)" : rubric!.Trim()).Append('\n');
                string output = result.Output ?? String.Empty;
                if (output.Length > MaxOutputShownChars) output = output.Substring(0, MaxOutputShownChars) + "\n[output truncated]";
                sb.Append("output:\n").Append(output).Append('\n');
            }

            sb.Append("\nReply with one JSON object and nothing else, in this shape:\n");
            sb.Append("{\"grades\":[{\"resultId\":\"...\",\"passed\":true,\"note\":\"...\"}],\n");
            sb.Append(" \"strengths\":[{\"key\":\"...\",\"passed\":3,\"total\":3,\"text\":\"Strong: ... (3 of 3).\"}],\n");
            sb.Append(" \"weaknesses\":[{\"key\":\"...\",\"passed\":0,\"total\":3,\"text\":\"Weak: ... (0 of 3).\"}],\n");
            sb.Append(" \"observedProblems\":[\"...\"],\n");
            sb.Append(" \"howToWork\":\"...\"}\n");
            return sb.ToString();
        }

        #endregion
    }
}
