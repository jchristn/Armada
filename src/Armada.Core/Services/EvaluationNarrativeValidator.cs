namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Checks the evaluator's reply to a Full Evaluate (docs/MODEL_HETEROGENEITY.md, Evaluate (Full)). The reply must be one
    /// JSON object; every strength and weakness must name a count of the report and repeat its numbers exactly; every
    /// "N of M" anywhere in the narrative must be a count of the report; subjective grades may only name attempts the
    /// evaluator was shown. A reply that fails any check is rejected with the reason; the mechanical report is kept.
    /// </summary>
    public static class EvaluationNarrativeValidator
    {
        #region Public-Members

        /// <summary>
        /// Longest rejected reply kept in the report.
        /// </summary>
        public const int MaxRejectedOutputChars = 2000;

        #endregion

        #region Private-Members

        private static readonly Regex _Cited = new Regex(@"(\d+)\s+(?:of|out of)\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate the evaluator's reply against the report's counts.
        /// </summary>
        /// <param name="output">The evaluator's reply.</param>
        /// <param name="report">Report with its counts filled.</param>
        /// <param name="shownResultIds">Ids of the attempts the evaluator was asked to grade.</param>
        /// <returns>The narrative, Accepted or Rejected with the reason.</returns>
        /// <exception cref="ArgumentNullException">Thrown when report or shownResultIds is null.</exception>
        public static ModelEvaluationNarrative Validate(string? output, ModelEvaluationReport report, IReadOnlyCollection<string> shownResultIds)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (shownResultIds == null) throw new ArgumentNullException(nameof(shownResultIds));

            EvaluatorReply? reply = Parse(output, out string? parseError);
            if (reply == null) return Rejected(parseError ?? "The evaluator's reply did not parse.", output);

            Dictionary<string, ModelEvaluationCount> counts = ModelEvaluationReportBuilder.CountsByKey(report);
            List<string> errors = new List<string>();
            List<ModelEvaluationFinding> strengths = CheckFindings("strength", reply.Strengths, counts, errors);
            List<ModelEvaluationFinding> weaknesses = CheckFindings("weakness", reply.Weaknesses, counts, errors);

            List<string> problems = (reply.ObservedProblems ?? new List<string>()).Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            if (problems.Count > EvaluationLimits.MaxFindings) errors.Add("More than " + EvaluationLimits.MaxFindings + " observed problems.");
            foreach (string problem in problems) CheckText("observed problem", problem, EvaluationLimits.MaxSentenceChars, counts, null, errors);

            string? howToWork = String.IsNullOrWhiteSpace(reply.HowToWork) ? null : reply.HowToWork.Trim();
            if (howToWork != null) CheckText("how-to-work note", howToWork, EvaluationLimits.MaxHowToWorkChars, counts, null, errors);

            List<ModelEvaluationSubjectiveGrade> grades = CheckGrades(reply.Grades, shownResultIds, errors);

            if (errors.Count > 0) return Rejected(String.Join(" ", errors.Take(5)) + (errors.Count > 5 ? " (" + (errors.Count - 5) + " more)" : String.Empty), output);
            if (strengths.Count == 0 && weaknesses.Count == 0 && counts.Values.Any(c => c.Graded > 0))
                return Rejected("The reply has no strengths or weaknesses although graded counts exist.", output);

            return new ModelEvaluationNarrative
            {
                Status = ModelEvaluationNarrativeStatusEnum.Accepted,
                Strengths = strengths,
                Weaknesses = weaknesses,
                ObservedProblems = problems,
                HowToWork = howToWork,
                SubjectiveGrades = grades
            };
        }

        /// <summary>
        /// A rejected narrative carrying the reason and an excerpt of the reply.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <param name="output">Reply, or null.</param>
        /// <returns>The narrative.</returns>
        public static ModelEvaluationNarrative Rejected(string reason, string? output)
        {
            string? excerpt = output;
            if (excerpt != null && excerpt.Length > MaxRejectedOutputChars) excerpt = excerpt.Substring(0, MaxRejectedOutputChars);
            return new ModelEvaluationNarrative
            {
                Status = ModelEvaluationNarrativeStatusEnum.Rejected,
                Reason = reason,
                RejectedOutput = excerpt
            };
        }

        #endregion

        #region Private-Methods

        private static EvaluatorReply? Parse(string? output, out string? error)
        {
            error = null;
            if (String.IsNullOrWhiteSpace(output))
            {
                error = "The evaluator returned no output.";
                return null;
            }

            int start = output.IndexOf('{');
            int end = output.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                error = "The evaluator's reply has no JSON object.";
                return null;
            }

            try
            {
                EvaluatorReply? reply = JsonSerializer.Deserialize<EvaluatorReply>(output.Substring(start, end - start + 1), _Json);
                if (reply == null) error = "The evaluator's reply is an empty JSON value.";
                return reply;
            }
            catch (JsonException ex)
            {
                error = "The evaluator's reply is not valid JSON in the requested shape: " + ex.Message;
                return null;
            }
        }

        private static List<ModelEvaluationFinding> CheckFindings(string kind, List<EvaluatorFinding>? findings, Dictionary<string, ModelEvaluationCount> counts, List<string> errors)
        {
            List<ModelEvaluationFinding> checkedFindings = new List<ModelEvaluationFinding>();
            if (findings == null) return checkedFindings;
            if (findings.Count > EvaluationLimits.MaxFindings) errors.Add("More than " + EvaluationLimits.MaxFindings + " " + kind + " entries.");

            foreach (EvaluatorFinding finding in findings.Take(EvaluationLimits.MaxFindings))
            {
                if (finding == null) continue;
                string key = (finding.Key ?? String.Empty).Trim();
                string text = (finding.Text ?? String.Empty).Trim();
                if (!counts.TryGetValue(key, out ModelEvaluationCount? count))
                {
                    errors.Add("A " + kind + " names the unknown count '" + key + "'.");
                    continue;
                }

                if (finding.Passed != count.Passed || finding.Total != count.Graded)
                {
                    errors.Add("A " + kind + " cites " + key + " as " + finding.Passed + " of " + finding.Total + " but the count is " + count.Passed + " of " + count.Graded + ".");
                    continue;
                }

                if (count.Graded == 0)
                {
                    errors.Add("A " + kind + " cites " + key + ", which has no graded attempts.");
                    continue;
                }

                if (text.Length == 0)
                {
                    errors.Add("A " + kind + " for " + key + " has no text.");
                    continue;
                }

                CheckText(kind, text, EvaluationLimits.MaxSentenceChars, counts, count, errors);
                checkedFindings.Add(new ModelEvaluationFinding { Key = key, Text = text, Passed = count.Passed, Total = count.Graded });
            }

            return checkedFindings;
        }

        private static void CheckText(string kind, string text, int maxChars, Dictionary<string, ModelEvaluationCount> counts, ModelEvaluationCount? own, List<string> errors)
        {
            if (text.Length > maxChars) errors.Add("A " + kind + " is longer than " + maxChars + " characters.");
            if (text.Any(c => c > 127)) errors.Add("A " + kind + " contains non-ASCII characters.");
            foreach (Match match in _Cited.Matches(text))
            {
                if (!Int32.TryParse(match.Groups[1].Value, out int passed) || !Int32.TryParse(match.Groups[2].Value, out int total))
                {
                    errors.Add("A " + kind + " cites an unreadable count '" + match.Value + "'.");
                    continue;
                }

                // A finding may mention a second count in passing ("... (0 of 3); reports often need repair (2 of 3)"), so
                // a cited pair must be the finding's own count or another count of the report.
                bool matches = (own != null && passed == own.Passed && total == own.Graded)
                    || counts.Values.Any(c => c.Passed == passed && c.Graded == total);
                if (!matches)
                    errors.Add("A " + kind + " cites '" + match.Value + "', which does not match " + (own != null ? own.Key + " (" + own.Passed + " of " + own.Graded + ") or " : String.Empty) + "any other count.");
            }
        }

        private static List<ModelEvaluationSubjectiveGrade> CheckGrades(List<EvaluatorGrade>? grades, IReadOnlyCollection<string> shown, List<string> errors)
        {
            List<ModelEvaluationSubjectiveGrade> checkedGrades = new List<ModelEvaluationSubjectiveGrade>();
            if (grades == null) return checkedGrades;
            if (grades.Count > EvaluationLimits.MaxSubjectiveGrades) errors.Add("More than " + EvaluationLimits.MaxSubjectiveGrades + " grades.");
            HashSet<string> allowed = new HashSet<string>(shown, StringComparer.Ordinal);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (EvaluatorGrade grade in grades.Take(EvaluationLimits.MaxSubjectiveGrades))
            {
                if (grade == null) continue;
                string id = (grade.ResultId ?? String.Empty).Trim();
                if (!allowed.Contains(id))
                {
                    errors.Add("A grade names the attempt '" + id + "', which was not given to the evaluator.");
                    continue;
                }

                if (!seen.Add(id))
                {
                    errors.Add("The attempt '" + id + "' is graded twice.");
                    continue;
                }

                if (!grade.Passed.HasValue)
                {
                    errors.Add("The grade of '" + id + "' has no passed value.");
                    continue;
                }

                string? note = String.IsNullOrWhiteSpace(grade.Note) ? null : grade.Note.Trim();
                if (note != null && note.Length > EvaluationLimits.MaxSentenceChars) note = note.Substring(0, EvaluationLimits.MaxSentenceChars);
                checkedGrades.Add(new ModelEvaluationSubjectiveGrade { ResultId = id, Passed = grade.Passed.Value, Note = note });
            }

            return checkedGrades;
        }

        #endregion

        #region Private-Classes

        private class EvaluatorReply
        {
            public List<EvaluatorGrade>? Grades { get; set; } = null;
            public List<EvaluatorFinding>? Strengths { get; set; } = null;
            public List<EvaluatorFinding>? Weaknesses { get; set; } = null;
            public List<string>? ObservedProblems { get; set; } = null;
            public string? HowToWork { get; set; } = null;
        }

        private class EvaluatorFinding
        {
            public string? Key { get; set; } = null;
            public int Passed { get; set; } = 0;
            public int Total { get; set; } = 0;
            public string? Text { get; set; } = null;
        }

        private class EvaluatorGrade
        {
            public string? ResultId { get; set; } = null;
            public bool? Passed { get; set; } = null;
            public string? Note { get; set; } = null;
        }

        #endregion
    }
}
