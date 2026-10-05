namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Authorization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="UrlPathCanonicalizer"/>: equivalent spellings canonicalize to one path, and
    /// ambiguous spellings (encoded separators or dots, double encoding, dot segments, backslashes, control
    /// characters) are rejected with a typed reason.
    /// </summary>
    public sealed class UrlPathCanonicalizerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.UrlPathCanonicalizer";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the URL path canonicalizer suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("equivalent_spellings_share_one_canonical_path", "Equivalent spellings canonicalize to one path", TestTags.Positive, () =>
            {
                string[] spellings = new string[]
                {
                    "/api/v1/settings",
                    "/api/v1/settings/",
                    "//api/v1/settings",
                    "/api//v1///settings//",
                    "/api/v1/%73ettings",
                    "/api/v1/%73%65ttings",
                    "api/v1/settings"
                };

                foreach (string spelling in spellings)
                {
                    UrlPathCanonicalizationResult result = UrlPathCanonicalizer.Canonicalize(spelling);
                    AssertTrue(result.Success, "'" + spelling + "' should canonicalize, got " + result.Rejection);
                    AssertEqual("/api/v1/settings", result.Path, "canonical path for '" + spelling + "'");
                    AssertTrue(result.MatchesSegments("api", "v1", "settings"), "segments for '" + spelling + "'");
                }

                UrlPathCanonicalizationResult root = UrlPathCanonicalizer.Canonicalize("///");
                AssertTrue(root.Success, "root should canonicalize");
                AssertEqual("/", root.Path);
                AssertEqual(0, root.Segments.Count);

                UrlPathCanonicalizationResult reencoded = UrlPathCanonicalizer.Canonicalize("/files/a%20b/%C3%A9");
                AssertTrue(reencoded.Success, "encoded space and UTF-8 should canonicalize");
                AssertEqual("a b", reencoded.Segments[1]);
                AssertEqual("\u00e9", reencoded.Segments[2]);
                AssertEqual("/files/a%20b/%C3%A9", reencoded.Path);
            }));

            cases.Add(Case("ambiguous_spellings_are_rejected_with_reason", "Ambiguous spellings are rejected with a typed reason", TestTags.Negative, () =>
            {
                Dictionary<string, UrlPathRejectionEnum> expected = new Dictionary<string, UrlPathRejectionEnum>(StringComparer.Ordinal)
                {
                    [""] = UrlPathRejectionEnum.Empty,
                    ["   "] = UrlPathRejectionEnum.Empty,
                    ["/api/v1/./status"] = UrlPathRejectionEnum.DotSegment,
                    ["/api/v1/../v1/status"] = UrlPathRejectionEnum.DotSegment,
                    ["/api/v1/%2e/status"] = UrlPathRejectionEnum.AmbiguousEncoding,
                    ["/api/v1/%2E%2E/status"] = UrlPathRejectionEnum.AmbiguousEncoding,
                    ["/api/v1/status%2fshutdown"] = UrlPathRejectionEnum.AmbiguousEncoding,
                    ["/api/v1/status%5Cshutdown"] = UrlPathRejectionEnum.AmbiguousEncoding,
                    ["/api/v1/%2573ettings"] = UrlPathRejectionEnum.AmbiguousEncoding,
                    ["/api/v1/restore%00"] = UrlPathRejectionEnum.AmbiguousEncoding,
                    ["/api/v1/restore%3bx"] = UrlPathRejectionEnum.AmbiguousEncoding,
                    ["/api/v1/status\\shutdown"] = UrlPathRejectionEnum.InvalidCharacter,
                    ["/api/v1/restore;x=1"] = UrlPathRejectionEnum.InvalidCharacter,
                    ["/api/v1/restore?x=1"] = UrlPathRejectionEnum.InvalidCharacter,
                    ["/api/v1/re store"] = UrlPathRejectionEnum.InvalidCharacter,
                    ["/api/v1/restore%"] = UrlPathRejectionEnum.MalformedEncoding,
                    ["/api/v1/restore%4"] = UrlPathRejectionEnum.MalformedEncoding,
                    ["/api/v1/restore%zz"] = UrlPathRejectionEnum.MalformedEncoding,
                    ["/api/v1/%C3"] = UrlPathRejectionEnum.MalformedEncoding
                };

                foreach (KeyValuePair<string, UrlPathRejectionEnum> pair in expected)
                {
                    UrlPathCanonicalizationResult result = UrlPathCanonicalizer.Canonicalize(pair.Key);
                    AssertFalse(result.Success, "'" + pair.Key + "' must be rejected");
                    AssertEqual(pair.Value, result.Rejection, "rejection for '" + pair.Key + "'");
                    AssertEqual(String.Empty, result.Path, "no path on rejection");
                    AssertFalse(result.StartsWithSegments("api"), "a rejected path matches no prefix");
                }

                UrlPathCanonicalizationResult nullResult = UrlPathCanonicalizer.Canonicalize(null);
                AssertEqual(UrlPathRejectionEnum.Empty, nullResult.Rejection);
            }));

            cases.Add(Case("segment_matching_respects_boundaries", "Segment matching respects segment boundaries", TestTags.Negative, () =>
            {
                UrlPathCanonicalizationResult lookalike = UrlPathCanonicalizer.Canonicalize("/dashboardx/index.html");
                AssertTrue(lookalike.Success, "lookalike path is well formed");
                AssertFalse(lookalike.StartsWithSegments("dashboard"), "'dashboardx' is not under 'dashboard'");

                UrlPathCanonicalizationResult real = UrlPathCanonicalizer.Canonicalize("/Dashboard/index.html");
                AssertTrue(real.StartsWithSegments("dashboard"), "segment match is case-insensitive");
                AssertFalse(real.MatchesSegments("dashboard"), "exact match needs the same segment count");
            }));

            cases.Add(Case("authorization_dashboard_exemption_requires_segment_boundary", "Anonymous dashboard exemption matches whole segments only", TestTags.Negative, () =>
            {
                AssertEqual(PermissionLevel.NoAuthRequired, AuthorizationConfig.GetRequirement("GET", "/dashboard").Level);
                AssertEqual(PermissionLevel.NoAuthRequired, AuthorizationConfig.GetRequirement("GET", "/dashboard/missions/msn_1").Level);
                AssertEqual(PermissionLevel.NoAuthRequired, AuthorizationConfig.GetRequirement("GET", "/assets/index.js").Level);
                AssertEqual(PermissionLevel.AdminOnly, AuthorizationConfig.GetRequirement("GET", "/dashboardx").Level, "'/dashboardx' is not the dashboard");
                AssertEqual(PermissionLevel.AdminOnly, AuthorizationConfig.GetRequirement("GET", "/dashboard-admin/x").Level, "'/dashboard-admin' is not the dashboard");
                AssertEqual(PermissionLevel.AdminOnly, AuthorizationConfig.GetRequirement("GET", "/dashboard/%2e%2e/undeclared").Level, "ambiguous spelling is not exempt");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "URL Path Canonicalizer",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
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
