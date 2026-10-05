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
    /// Descriptors for <see cref="DockBoundaryScanner"/>. Positive cases confirm secrets, protected paths,
    /// and private identifiers are flagged (without echoing secret bytes); negative cases confirm clean
    /// diffs, removed lines, disabled scanning, and a null policy produce nothing.
    /// </summary>
    public sealed class DockBoundarySuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the dock-boundary scanner suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("secret_in_added_line_flagged_without_bytes", "Secret in an added line is flagged, no secret bytes", TestTags.Positive, () =>
            {
                string diff = FileDiff("config.py", "+AWS_KEY = \"AKIAIOSFODNN7EXAMPLE\"", " physical = 1");
                DockBoundaryPolicy policy = new DockBoundaryPolicy { SecretScanEnabled = true };
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, policy);
                AssertTrue(findings.Count >= 1, "expected a secret finding");
                BoundaryFinding f = findings[0];
                AssertEqual("secret", f.Kind);
                AssertEqual("config.py", f.Path);
                // The finding must NOT carry the secret value anywhere.
                AssertFalse(f.RuleId.Contains("AKIA"), "rule id leaked secret");
                AssertFalse(DockBoundaryScanner.Summarize(findings).Contains("AKIAIOSFODNN7EXAMPLE"), "summary leaked secret");
            }));

            cases.Add(Case("private_key_block_flagged", "PEM private-key block is flagged", TestTags.Positive, () =>
            {
                string diff = FileDiff("id_rsa", "+-----BEGIN RSA PRIVATE KEY-----");
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, new DockBoundaryPolicy { SecretScanEnabled = true });
                AssertTrue(findings.Count >= 1);
                AssertEqual("secret", findings[0].Kind);
            }));

            cases.Add(Case("protected_path_flagged", "A changed protected path is flagged", TestTags.Positive, () =>
            {
                DockBoundaryPolicy policy = new DockBoundaryPolicy { ProtectedPathGlobs = new List<string> { ".github/**", "LICENSE" } };
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(null, new List<string> { ".github/workflows/ci.yml", "src/app.py" }, policy);
                AssertEqual(1, findings.Count);
                AssertEqual("protected-path", findings[0].Kind);
                AssertEqual(".github/workflows/ci.yml", findings[0].Path);
            }));

            cases.Add(Case("private_identifier_flagged", "A private identifier in an added line is flagged", TestTags.Positive, () =>
            {
                string diff = FileDiff("readme.md", "+Deployed for AcmeCorp internal use");
                DockBoundaryPolicy policy = new DockBoundaryPolicy { PrivateIdentifiers = new List<string> { "AcmeCorp" } };
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, policy);
                AssertTrue(findings.Count >= 1);
                AssertEqual("private-identifier", findings[0].Kind);
            }));

            cases.Add(Case("clean_diff_no_findings", "A clean diff produces no findings", TestTags.Negative, () =>
            {
                string diff = FileDiff("app.py", "+x = compute(y)", "+return x");
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, new List<string> { "app.py" }, new DockBoundaryPolicy { SecretScanEnabled = true, ProtectedPathGlobs = new List<string> { ".github/**" } });
                AssertEqual(0, findings.Count);
            }));

            cases.Add(Case("secret_in_removed_line_not_flagged", "A secret on a removed line is not flagged", TestTags.Negative, () =>
            {
                string diff = FileDiff("config.py", "-AWS_KEY = \"AKIAIOSFODNN7EXAMPLE\"");
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, new DockBoundaryPolicy { SecretScanEnabled = true });
                AssertEqual(0, findings.Count);
            }));

            cases.Add(Case("disabled_scanning_no_findings", "Secret scanning disabled -> no findings", TestTags.Negative, () =>
            {
                string diff = FileDiff("config.py", "+token = \"ghp_abcdefghijklmnopqrstuvwxyz0123456789\"");
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, new DockBoundaryPolicy { SecretScanEnabled = false });
                AssertEqual(0, findings.Count);
            }));

            cases.Add(Case("null_policy_no_findings", "A null policy produces no findings", TestTags.Negative, () =>
            {
                string diff = FileDiff("config.py", "+AWS_KEY = \"AKIAIOSFODNN7EXAMPLE\"");
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, new List<string> { "config.py" }, null);
                AssertEqual(0, findings.Count);
            }));


            cases.Add(Case("secret_on_added_line_rendering_as_plus_plus_plus_flagged", "An added line whose text starts with \"++ \" (rendered \"+++ \") is still scanned", TestTags.Positive, () =>
            {
                // The added content is "++ AWS_KEY=...", which git renders as "+++ AWS_KEY=...". A header-text
                // parser treats that as a file header and skips it; the hunk-range parser treats it as content.
                string diff = FileDiff("deploy.sh", " echo start", "+++ AWS_KEY=AKIAIOSFODNN7EXAMPLE");
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, new DockBoundaryPolicy { SecretScanEnabled = true });
                AssertEqual(1, findings.Count);
                AssertEqual("secret", findings[0].Kind);
                AssertEqual("aws-access-key-id", findings[0].RuleId);
                AssertEqual("deploy.sh", findings[0].Path);
                AssertEqual(2, findings[0].Line);
            }));

            cases.Add(Case("deleted_protected_file_flagged_from_diff", "Deleting a protected file is flagged from the diff alone", TestTags.Positive, () =>
            {
                string diff =
                    "diff --git a/.github/workflows/ci.yml b/.github/workflows/ci.yml\n" +
                    "deleted file mode 100644\n" +
                    "index 1111111..0000000\n" +
                    "--- a/.github/workflows/ci.yml\n" +
                    "+++ /dev/null\n" +
                    "@@ -1,2 +0,0 @@\n" +
                    "-name: ci\n" +
                    "-on: push\n";
                DockBoundaryPolicy policy = new DockBoundaryPolicy { ProtectedPathGlobs = new List<string> { ".github/**" } };
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, policy);
                AssertEqual(1, findings.Count);
                AssertEqual("protected-path", findings[0].Kind);
                AssertEqual(".github/workflows/ci.yml", findings[0].Path);
            }));

            cases.Add(Case("pure_rename_out_of_protected_path_flagged", "A pure rename out of a protected path is flagged on its old path", TestTags.Positive, () =>
            {
                string diff =
                    "diff --git a/.github/CODEOWNERS b/docs/CODEOWNERS\n" +
                    "similarity index 100%\n" +
                    "rename from .github/CODEOWNERS\n" +
                    "rename to docs/CODEOWNERS\n";
                DockBoundaryPolicy policy = new DockBoundaryPolicy { ProtectedPathGlobs = new List<string> { ".github/**" } };
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, policy);
                AssertEqual(1, findings.Count);
                AssertEqual(".github/CODEOWNERS", findings[0].Path);
            }));

            cases.Add(Case("c_quoted_protected_path_flagged", "A C-quoted (non-ASCII) protected path is decoded and flagged", TestTags.Positive, () =>
            {
                string diff =
                    "diff --git \"a/.github/caf\\303\\251.yml\" \"b/.github/caf\\303\\251.yml\"\n" +
                    "new file mode 100644\n" +
                    "index 0000000..1111111\n" +
                    "--- /dev/null\n" +
                    "+++ \"b/.github/caf\\303\\251.yml\"\n" +
                    "@@ -0,0 +1 @@\n" +
                    "+on: push\n";
                DockBoundaryPolicy policy = new DockBoundaryPolicy { ProtectedPathGlobs = new List<string> { ".github/**" } };
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, policy);
                AssertEqual(1, findings.Count);
                AssertEqual(".github/caf\u00e9.yml", findings[0].Path);
            }));

            cases.Add(Case("header_like_context_does_not_switch_file", "A content line that looks like a file header does not change the reported file", TestTags.Negative, () =>
            {
                // Removed line "-- b/other.py" renders as "--- b/other.py"; added line "++ b/other.py" as "+++ b/other.py".
                string diff = FileDiff("notes.md", "--- b/other.py", "+++ b/other.py", "+Deployed for AcmeCorp");
                List<BoundaryFinding> findings = DockBoundaryScanner.Scan(diff, null, new DockBoundaryPolicy { PrivateIdentifiers = new List<string> { "AcmeCorp" } });
                AssertEqual(1, findings.Count);
                AssertEqual("notes.md", findings[0].Path);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.DockBoundary",
                displayName: "Dock Boundary Scanner",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Build a well-formed single-hunk git diff for one modified file from hunk body lines
        /// (each starting with '+', '-', or ' ').
        /// </summary>
        private static string FileDiff(string path, params string[] bodyLines)
        {
            int oldCount = 0;
            int newCount = 0;
            foreach (string line in bodyLines)
            {
                if (line.StartsWith("+", StringComparison.Ordinal)) newCount++;
                else if (line.StartsWith("-", StringComparison.Ordinal)) oldCount++;
                else { oldCount++; newCount++; }
            }

            return "diff --git a/" + path + " b/" + path + "\n" +
                "index 1111111..2222222 100644\n" +
                "--- a/" + path + "\n" +
                "+++ b/" + path + "\n" +
                "@@ -1," + oldCount + " +1," + newCount + " @@\n" +
                String.Join("\n", bodyLines) + "\n";
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.DockBoundary",
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
