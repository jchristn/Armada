namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the mission scope validator: the "Touch only" directive parser and the out-of-scope filter.
    /// Covers the phantom empty path a transport newline used to produce, bare file names in the directive, and
    /// directory paths that still require an exact match.
    /// </summary>
    public sealed class MissionScopeValidationSuite : IArmadaTestSuite
    {
        private const string _SuiteId = "Services.MissionScopeValidation";

        /// <summary>
        /// Build the descriptor for the mission scope validation suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("trailing_newline_after_last_nul_is_not_a_violation", "A newline after the last NUL yields no empty path and no violation", TestTags.Negative, () =>
            {
                List<string> changed = GitMachineOutputParser.ParsePathListZ("src/Garrison.Server/Protocols/GarrisonMcpServer.cs\0\n");
                HashSet<string> allowed = MissionService.ParseMissionScopedFiles("Touch only src/Garrison.Server/Protocols/GarrisonMcpServer.cs.");
                List<string> outOfScope = MissionService.FindOutOfScopeFiles(changed, allowed);
                AssertEqual(0, outOfScope.Count, "no violation: " + String.Join("|", outOfScope));
            }));

            cases.Add(Case("empty_and_whitespace_paths_are_skipped", "Empty or line-ending-only changed paths are never reported", TestTags.Negative, () =>
            {
                HashSet<string> allowed = MissionService.ParseMissionScopedFiles("Touch only src/A.cs");
                List<string> outOfScope = MissionService.FindOutOfScopeFiles(new List<string> { "", "\n", "\r\n", " ", "src/A.cs" }, allowed);
                AssertEqual(0, outOfScope.Count, "no empty entries reported");
            }));

            cases.Add(Case("bare_name_directive_matches_nested_path", "A bare file name in the directive matches the file in any directory, case-insensitively", TestTags.Positive, () =>
            {
                HashSet<string> allowed = MissionService.ParseMissionScopedFiles("Touch only GarrisonMcpServer.cs");
                AssertTrue(allowed.Contains("GarrisonMcpServer.cs"), "bare token parsed");
                List<string> outOfScope = MissionService.FindOutOfScopeFiles(
                    new List<string> { "src/Garrison.Server/Protocols/GarrisonMcpServer.cs", "src/other/garrisonmcpserver.CS" },
                    allowed);
                AssertEqual(0, outOfScope.Count, "nested paths with the allowed file name are in scope");
            }));

            cases.Add(Case("real_out_of_scope_file_reported_by_name", "A real out-of-scope file is reported by name with no empty entries", TestTags.Negative, () =>
            {
                HashSet<string> allowed = MissionService.ParseMissionScopedFiles("Touch only GarrisonMcpServer.cs");
                List<string> outOfScope = MissionService.FindOutOfScopeFiles(
                    GitMachineOutputParser.ParsePathListZ("src/Garrison.Server/Protocols/GarrisonMcpServer.cs\0src/Garrison.Server/Other.cs\0\n"),
                    allowed);
                AssertEqual(1, outOfScope.Count, "exactly one out-of-scope file");
                AssertEqual("src/Garrison.Server/Other.cs", outOfScope[0]);
            }));

            cases.Add(Case("directory_directive_requires_exact_match", "A directive path with a directory still requires an exact repo-relative match", TestTags.Negative, () =>
            {
                HashSet<string> allowed = MissionService.ParseMissionScopedFiles("Touch only src/Server/Routes.cs");
                List<string> outOfScope = MissionService.FindOutOfScopeFiles(
                    new List<string> { "src/Server/Routes.cs", "src/Other/Routes.cs", "Routes.cs" },
                    allowed);
                AssertEqual(2, outOfScope.Count, "same file name in another directory is out of scope");
                AssertTrue(outOfScope.Contains("src/Other/Routes.cs"), "other directory reported");
                AssertTrue(outOfScope.Contains("Routes.cs"), "root-level file reported");
            }));

            cases.Add(Case("instruction_artifacts_are_ignored", "CLAUDE.md, CODEX.md and similar runtime instruction files are never violations", TestTags.Positive, () =>
            {
                HashSet<string> allowed = MissionService.ParseMissionScopedFiles("Edit only src/A.cs");
                List<string> outOfScope = MissionService.FindOutOfScopeFiles(new List<string> { "CLAUDE.md", "CODEX.md", "src/A.cs" }, allowed);
                AssertEqual(0, outOfScope.Count, "instruction artifacts ignored");
            }));

            cases.Add(Case("dot_slash_prefix_normalized", "Leading ./ on a changed or allowed path does not defeat the match", TestTags.Positive, () =>
            {
                HashSet<string> allowed = MissionService.ParseMissionScopedFiles("Modify only ./src/A.cs and docs\\B.md");
                List<string> outOfScope = MissionService.FindOutOfScopeFiles(new List<string> { "src/A.cs", "./docs/B.md" }, allowed);
                AssertEqual(0, outOfScope.Count, "normalized paths match: " + String.Join("|", outOfScope));
            }));

            return new TestSuiteDescriptor(
                suiteId: _SuiteId,
                displayName: "Mission Scope Validation",
                cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }
    }
}
