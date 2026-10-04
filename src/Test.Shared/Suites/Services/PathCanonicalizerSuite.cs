namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="PathCanonicalizer"/>. Positive cases assert that trailing separators and
    /// relative segments are normalized, that a directory reached through a symbolic link compares equal
    /// to its target (the macOS <c>/var</c> vs <c>/private/var</c> case git exposes), and that
    /// not-yet-existing segments are preserved; negative cases assert distinct paths stay distinct and
    /// null input is rejected.
    /// </summary>
    public sealed class PathCanonicalizerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PathCanonicalizer";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the path canonicalizer suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("canonicalize_normalizes_separators_and_dot_segments", "Canonicalize normalizes trailing separators and dot segments", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("pathcanon");
                string child = Path.Combine(root, "child");
                Directory.CreateDirectory(child);

                string withTrailing = child + Path.DirectorySeparatorChar;
                string withDots = Path.Combine(root, "child", "..", "child");

                AssertEqual(PathCanonicalizer.Canonicalize(child), PathCanonicalizer.Canonicalize(withTrailing));
                AssertEqual(PathCanonicalizer.Canonicalize(child), PathCanonicalizer.Canonicalize(withDots));
                AssertTrue(PathCanonicalizer.AreEquivalent(withTrailing, withDots), "Expected equivalent spellings to compare equal");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("canonicalize_resolves_symlinked_directory", "Canonicalize resolves a directory reached through a symbolic link", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("pathcanon");
                string target = Path.Combine(root, "target");
                string link = Path.Combine(root, "link");
                Directory.CreateDirectory(Path.Combine(target, "inner"));

                try
                {
                    Directory.CreateSymbolicLink(link, target);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // Creating symbolic links can require elevation on Windows; nothing to verify then.
                    return Task.CompletedTask;
                }

                AssertTrue(PathCanonicalizer.AreEquivalent(Path.Combine(link, "inner"), Path.Combine(target, "inner")),
                    "Expected a path through a symbolic link to match its target");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("canonicalize_preserves_missing_segments", "Canonicalize keeps segments that do not exist yet", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("pathcanon");
                string missing = Path.Combine(root, "not-created", "worktree");

                string canonical = PathCanonicalizer.Canonicalize(missing);
                AssertTrue(canonical.EndsWith(Path.Combine("not-created", "worktree"), StringComparison.Ordinal),
                    "Expected the missing segments to be preserved, got " + canonical);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("are_equivalent_distinguishes_different_paths", "AreEquivalent returns false for different directories", TestTags.Negative, () =>
            {
                string root = TestTemp.NewDirectory("pathcanon");
                string first = Path.Combine(root, "first");
                string second = Path.Combine(root, "second");
                Directory.CreateDirectory(first);
                Directory.CreateDirectory(second);

                AssertFalse(PathCanonicalizer.AreEquivalent(first, second), "Expected different directories to differ");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("canonicalize_rejects_null", "Canonicalize rejects a null path", TestTags.Negative, async () =>
            {
                await AssertThrowsAsync<ArgumentNullException>(() =>
                {
                    PathCanonicalizer.Canonicalize(null!);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Path Canonicalizer",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
