namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="GitAnchorInputs"/>. Positive cases confirm path-like tokens and notable
    /// subject terms are extracted from mission text; negative cases confirm stopwords/short words are
    /// dropped and empty text yields nothing.
    /// </summary>
    public sealed class GitAnchorInputsSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the git-anchor-inputs suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("extracts_named_paths", "Named repository paths are extracted", TestTags.Positive, () =>
            {
                IReadOnlyList<string> paths = GitAnchorInputs.ExtractPaths("Refactor src/Armada.Core/Services/MissionService.cs and update models.ts");
                AssertTrue(paths.Contains("src/Armada.Core/Services/MissionService.cs"), "the source path should be extracted");
                AssertTrue(paths.Contains("models.ts"), "the bare filename should be extracted");
            }));

            cases.Add(Case("paths_are_capped_and_distinct", "Extracted paths are distinct and capped", TestTags.Positive, () =>
            {
                IReadOnlyList<string> paths = GitAnchorInputs.ExtractPaths("a/b.cs a/b.cs c/d.cs e/f.cs g/h.cs i/j.cs k/l.cs m/n.cs", max: 3);
                AssertEqual(3, paths.Count, "paths should cap at the requested max");
                AssertEqual(paths.Count, paths.Distinct().Count(), "paths should be distinct");
            }));

            cases.Add(Case("extracts_subject_terms", "Notable subject terms are extracted", TestTags.Positive, () =>
            {
                IReadOnlyList<string> terms = GitAnchorInputs.ExtractSubjectTerms("Add QuarantineReason to CaptainState", null);
                AssertTrue(terms.Contains("QuarantineReason"), "a PascalCase identifier should be a subject term");
                AssertTrue(terms.Contains("CaptainState"), "a second PascalCase identifier should be a subject term");
            }));

            cases.Add(Case("stopwords_and_short_words_dropped", "Stopwords and short words are not subject terms", TestTags.Negative, () =>
            {
                IReadOnlyList<string> terms = GitAnchorInputs.ExtractSubjectTerms("update the mission with a fix", null);
                AssertFalse(terms.Contains("update", StringComparer.OrdinalIgnoreCase), "a stopword should be dropped");
                AssertFalse(terms.Contains("mission", StringComparer.OrdinalIgnoreCase), "a domain stopword should be dropped");
                AssertFalse(terms.Contains("fix", StringComparer.OrdinalIgnoreCase), "a short word should be dropped");
            }));

            cases.Add(Case("empty_text_yields_nothing", "Empty text yields no paths or terms", TestTags.Negative, () =>
            {
                AssertEqual(0, GitAnchorInputs.ExtractPaths(null).Count, "no paths from null");
                AssertEqual(0, GitAnchorInputs.ExtractSubjectTerms(null, null).Count, "no terms from null");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.GitAnchorInputs",
                displayName: "Git Anchor Inputs",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.GitAnchorInputs",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
