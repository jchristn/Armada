namespace Test.Shared.Infrastructure
{
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Services.Ask;

    /// <summary>
    /// The Ask black box answer check (plan 16.8 cases 2 and 7, mirrored by the grounding check in
    /// scripts/common/evaluate-prompt-handoffs.py): the answer has the required headings, labels the opinion, separates
    /// user changes from Armada changes, and names only files, commits and missions that are in the entry.
    /// </summary>
    public static class AskBlackBoxGrounding
    {
        private static readonly Regex _Missions = new Regex(@"(?<![A-Za-z0-9_])msn_[A-Za-z0-9][A-Za-z0-9_]*[A-Za-z0-9]", RegexOptions.CultureInvariant);
        private static readonly Regex _Commits = new Regex(@"(?<![A-Za-z0-9_])(?=[0-9a-f]*[0-9])(?=[0-9a-f]*[a-f])[0-9a-f]{7,40}(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);
        private static readonly Regex _Files = new Regex(@"(?<![A-Za-z0-9_./-])(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*\.(?:cs|csproj|sln|ts|tsx|js|jsx|json|md|py|sh|yml|yaml|xml|txt|config|props|targets)(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);

        /// <summary>
        /// Check an answer against an entry's facts.
        /// </summary>
        /// <param name="answer">Answer text.</param>
        /// <param name="facts">Entry facts.</param>
        /// <returns>The report.</returns>
        public static AskBlackBoxGroundingReport Check(string answer, AskBlackBoxFacts facts)
        {
            if (answer == null) throw new ArgumentNullException(nameof(answer));
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            AskBlackBoxGroundingReport report = new AskBlackBoxGroundingReport();
            foreach (string heading in AskBlackBoxGuidance.AnswerHeadings)
            {
                if (!Regex.IsMatch(answer, @"(?im)^\s*(?:#+\s*|\*\*|- \*\*)?" + Regex.Escape(heading) + @"\b")) report.MissingHeadings.Add(heading);
            }

            report.OpinionLabeled = Regex.IsMatch(answer, @"(?i)\b(my opinion|in my opinion|i think|i believe)\b");
            report.ChangesSplit = Regex.IsMatch(answer, @"(?i)\b(you can|user can|you could)\b") && Regex.IsMatch(answer, @"(?i)\barmada (should|could|can)\b");

            foreach (string mission in _Missions.Matches(answer).Select(m => m.Value).Distinct(StringComparer.Ordinal))
                if (!facts.Missions.Contains(mission)) report.UnknownMissions.Add(mission);

            foreach (string commit in _Commits.Matches(answer).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!facts.Commits.Any(c => c.StartsWith(commit, StringComparison.OrdinalIgnoreCase) || commit.StartsWith(c, StringComparison.OrdinalIgnoreCase))) report.UnknownCommits.Add(commit);

            foreach (string file in _Files.Matches(answer).Select(m => m.Value).Distinct(StringComparer.Ordinal))
                if (!facts.Files.Any(f => String.Equals(f, file, StringComparison.Ordinal) || f.EndsWith("/" + file, StringComparison.Ordinal))) report.UnknownFiles.Add(file);

            return report;
        }

        /// <summary>
        /// Whether an answer about a mission without an entry says so and offers the mission's FailureReason (plan 16.8
        /// case 5).
        /// </summary>
        /// <param name="answer">Answer text.</param>
        /// <returns>True when it does.</returns>
        public static bool SaysNoEntryAndOffersFailureReason(string answer)
        {
            if (String.IsNullOrEmpty(answer)) return false;
            return Regex.IsMatch(answer, @"(?i)\bno black box (entry|record)\b|\bhas no black box\b") && answer.Contains("FailureReason", StringComparison.Ordinal);
        }
    }
}
