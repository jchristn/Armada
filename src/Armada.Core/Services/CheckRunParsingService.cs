namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Parses structured test-result and coverage summaries from check-run output and artifacts.
    /// </summary>
    public static class CheckRunParsingService
    {
        private static readonly Regex _DotNetSummaryRegex = new Regex(
            @"(?im)^(?:Passed|Failed)!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+),\s*Duration:\s*(?<duration>[^\r\n]+)$",
            RegexOptions.Compiled);

        private static readonly Regex _PytestSummaryRegex = new Regex(
            @"(?im)^=+\s*(?<body>.+?)\s+in\s+(?<duration>[0-9A-Za-z\.\:\s]+)\s*=+$",
            RegexOptions.Compiled);

        private static readonly Regex _PytestTokenRegex = new Regex(
            @"(?<count>\d+)\s+(?<kind>failed|passed|skipped|error|errors|xfailed|xpassed)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _JavascriptTestsLineRegex = new Regex(
            @"(?im)^Tests?\s+(?<body>.+)$",
            RegexOptions.Compiled);

        private static readonly Regex _JavascriptTokenRegex = new Regex(
            @"(?<count>\d+)\s+(?<kind>failed|passed|skipped|todo|total)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex _CargoSummaryRegex = new Regex(
            @"(?im)^test result:\s+(?<result>ok|failed)\.\s+(?<passed>\d+)\s+passed;\s+(?<failed>\d+)\s+failed;\s+(?<ignored>\d+)\s+ignored(?:;\s+(?<measured>\d+)\s+measured)?(?:;\s+(?<filtered>\d+)\s+filtered out)?(?:;\s+finished in\s+(?<duration>[^\r\n]+))?",
            RegexOptions.Compiled);

        private static readonly Regex _MavenSurefireSummaryRegex = new Regex(
            @"(?im)^Tests run:\s*(?<total>\d+),\s*Failures:\s*(?<failures>\d+),\s*Errors:\s*(?<errors>\d+),\s*Skipped:\s*(?<skipped>\d+)(?:,\s*Time elapsed:\s*(?<duration>[0-9A-Za-z\.\s:]+))?",
            RegexOptions.Compiled);

        private static readonly Regex _NUnitConsoleSummaryRegex = new Regex(
            @"(?im)^Test Count:\s*(?<total>\d+),\s*Passed:\s*(?<passed>\d+),\s*Failed:\s*(?<failed>\d+),\s*Warnings:\s*(?<warnings>\d+),\s*Inconclusive:\s*(?<inconclusive>\d+),\s*Skipped:\s*(?<skipped>\d+)",
            RegexOptions.Compiled);

        private static readonly Regex _DurationTokenRegex = new Regex(
            @"(?<value>\d+(?:\.\d+)?)\s*(?<unit>ms|milliseconds?|s|sec|secs|seconds?|m|min|mins|minutes?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Parse a test summary from command output when possible.
        /// </summary>
        public static CheckRunTestSummary? ParseTestSummary(string? output)
        {
            return ParseTestSummary(output, null, null);
        }

        /// <summary>
        /// Parse a test summary, preferring structured result artifacts (TRX, JUnit XML, NUnit XML, Jest/Vitest
        /// JSON) and falling back to console summary lines only when no artifact yields one.
        /// </summary>
        public static CheckRunTestSummary? ParseTestSummary(string? output, string? workingDirectory, IEnumerable<CheckRunArtifact>? artifacts)
        {
            CheckRunTestSummary? artifactSummary = ParseTestSummaryFromArtifacts(workingDirectory, artifacts);
            if (artifactSummary != null)
                return artifactSummary;

            CheckRunTestSummary? outputSummary = null;
            if (String.IsNullOrWhiteSpace(output))
            {
                outputSummary = null;
            }
            else
            {
                outputSummary = ParseDotNetSummary(output)
                ?? ParsePytestSummary(output)
                ?? ParseJavascriptSummary(output)
                ?? ParseCargoSummary(output)
                ?? ParseMavenSurefireSummary(output)
                ?? ParseNUnitConsoleSummary(output);
            }

            return outputSummary;
        }

        /// <summary>
        /// Parse a coverage summary from collected artifacts when possible.
        /// </summary>
        public static CheckRunCoverageSummary? ParseCoverageSummary(string? workingDirectory, IEnumerable<CheckRunArtifact>? artifacts)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory) || artifacts == null)
                return null;

            string root = Path.GetFullPath(workingDirectory);
            foreach (CheckRunArtifact artifact in artifacts)
            {
                if (String.IsNullOrWhiteSpace(artifact.Path))
                    continue;

                try
                {
                    string fullPath = Path.GetFullPath(Path.Combine(root, artifact.Path));
                    if (!PathContainment.IsInside(root, fullPath))
                        continue;
                    if (!File.Exists(fullPath))
                        continue;

                    CheckRunCoverageSummary? parsed = ParseCoverageArtifact(fullPath, artifact.Path.Replace('\\', '/'));
                    if (parsed != null)
                        return parsed;
                }
                catch
                {
                }
            }

            return null;
        }

        private static CheckRunTestSummary? ParseDotNetSummary(string output)
        {
            Match match = _DotNetSummaryRegex.Match(output);
            if (!match.Success)
                return null;

            return new CheckRunTestSummary
            {
                Format = "dotnet",
                Failed = ParseInt(match, "failed"),
                Passed = ParseInt(match, "passed"),
                Skipped = ParseInt(match, "skipped"),
                Total = ParseInt(match, "total"),
                DurationMs = ParseDurationMilliseconds(match.Groups["duration"].Value)
            };
        }

        private static CheckRunTestSummary? ParsePytestSummary(string output)
        {
            Match match = _PytestSummaryRegex.Matches(output).Cast<Match>().LastOrDefault(m => m.Success) ?? Match.Empty;
            if (!match.Success)
                return null;

            int passed = 0;
            int failed = 0;
            int skipped = 0;
            foreach (Match token in _PytestTokenRegex.Matches(match.Groups["body"].Value))
            {
                int count = Int32.Parse(token.Groups["count"].Value, CultureInfo.InvariantCulture);
                string kind = token.Groups["kind"].Value.ToLowerInvariant();
                switch (kind)
                {
                    case "passed":
                    case "xpassed":
                        passed += count;
                        break;
                    case "failed":
                    case "error":
                    case "errors":
                    case "xfailed":
                        failed += count;
                        break;
                    case "skipped":
                        skipped += count;
                        break;
                }
            }

            int total = passed + failed + skipped;
            if (total == 0)
                return null;

            return new CheckRunTestSummary
            {
                Format = "pytest",
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                Total = total,
                DurationMs = ParseDurationMilliseconds(match.Groups["duration"].Value)
            };
        }

        private static CheckRunTestSummary? ParseJavascriptSummary(string output)
        {
            Match testsLine = _JavascriptTestsLineRegex.Matches(output).Cast<Match>().LastOrDefault(m => m.Success) ?? Match.Empty;
            if (!testsLine.Success)
                return null;

            int? passed = null;
            int? failed = null;
            int? skipped = null;
            int? total = null;

            foreach (Match token in _JavascriptTokenRegex.Matches(testsLine.Groups["body"].Value))
            {
                int count = Int32.Parse(token.Groups["count"].Value, CultureInfo.InvariantCulture);
                switch (token.Groups["kind"].Value.ToLowerInvariant())
                {
                    case "passed":
                        passed = count;
                        break;
                    case "failed":
                        failed = count;
                        break;
                    case "skipped":
                    case "todo":
                        skipped = (skipped ?? 0) + count;
                        break;
                    case "total":
                        total = count;
                        break;
                }
            }

            if (!passed.HasValue && !failed.HasValue && !skipped.HasValue)
                return null;

            total ??= (passed ?? 0) + (failed ?? 0) + (skipped ?? 0);
            long? durationMs = null;
            string? durationLine = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Reverse()
                .FirstOrDefault(line => line.TrimStart().StartsWith("Duration", StringComparison.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(durationLine))
                durationMs = ParseDurationMilliseconds(durationLine);

            return new CheckRunTestSummary
            {
                Format = "javascript",
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                Total = total,
                DurationMs = durationMs
            };
        }

        private static CheckRunTestSummary? ParseCargoSummary(string output)
        {
            Match match = _CargoSummaryRegex.Matches(output).Cast<Match>().LastOrDefault(candidate => candidate.Success) ?? Match.Empty;
            if (!match.Success)
                return null;

            int passed = ParseInt(match, "passed");
            int failed = ParseInt(match, "failed");
            int ignored = ParseInt(match, "ignored");
            int total = passed + failed + ignored;

            return new CheckRunTestSummary
            {
                Format = "cargo",
                Passed = passed,
                Failed = failed,
                Skipped = ignored,
                Total = total,
                DurationMs = ParseDurationMilliseconds(match.Groups["duration"].Value)
            };
        }

        private static CheckRunTestSummary? ParseMavenSurefireSummary(string output)
        {
            Match match = _MavenSurefireSummaryRegex.Matches(output).Cast<Match>().LastOrDefault(candidate => candidate.Success) ?? Match.Empty;
            if (!match.Success)
                return null;

            int total = ParseInt(match, "total");
            int failures = ParseInt(match, "failures");
            int errors = ParseInt(match, "errors");
            int skipped = ParseInt(match, "skipped");
            int failed = failures + errors;
            int passed = Math.Max(0, total - failed - skipped);

            return new CheckRunTestSummary
            {
                Format = "maven-surefire",
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                Total = total,
                DurationMs = ParseDurationMilliseconds(match.Groups["duration"].Value)
            };
        }

        private static CheckRunTestSummary? ParseNUnitConsoleSummary(string output)
        {
            Match match = _NUnitConsoleSummaryRegex.Matches(output).Cast<Match>().LastOrDefault(candidate => candidate.Success) ?? Match.Empty;
            if (!match.Success)
                return null;

            int total = ParseInt(match, "total");
            int passed = ParseInt(match, "passed");
            int failed = ParseInt(match, "failed");
            int skipped = ParseInt(match, "skipped") + ParseInt(match, "inconclusive") + ParseInt(match, "warnings");

            return new CheckRunTestSummary
            {
                Format = "nunit-console",
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                Total = total
            };
        }

        private static CheckRunCoverageSummary? ParseCoverageArtifact(string fullPath, string relativePath)
        {
            string extension = Path.GetExtension(fullPath);
            if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCoverageXml(fullPath, relativePath);
            }

            if (extension.Equals(".info", StringComparison.OrdinalIgnoreCase))
            {
                return ParseLcov(fullPath, relativePath);
            }

            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCoverageJson(fullPath, relativePath);
            }

            return null;
        }

        private static CheckRunTestSummary? ParseTestSummaryFromArtifacts(string? workingDirectory, IEnumerable<CheckRunArtifact>? artifacts)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory) || artifacts == null)
                return null;

            string root = Path.GetFullPath(workingDirectory);
            foreach (CheckRunArtifact artifact in artifacts)
            {
                if (String.IsNullOrWhiteSpace(artifact.Path))
                    continue;

                try
                {
                    string fullPath = Path.GetFullPath(Path.Combine(root, artifact.Path));
                    if (!PathContainment.IsInside(root, fullPath))
                        continue;
                    if (!File.Exists(fullPath))
                        continue;

                    CheckRunTestSummary? summary = ParseTestArtifact(fullPath);
                    if (summary != null)
                        return summary;
                }
                catch
                {
                }
            }

            return null;
        }

        private static CheckRunTestSummary? ParseTestArtifact(string fullPath)
        {
            string extension = Path.GetExtension(fullPath);
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                return ParseJestJson(fullPath);

            if (!extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".trx", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            XDocument document = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace);
            string rootName = document.Root?.Name.LocalName ?? String.Empty;
            if (rootName.Equals("TestRun", StringComparison.OrdinalIgnoreCase))
                return ParseTrx(document);
            if (rootName.Equals("test-run", StringComparison.OrdinalIgnoreCase))
                return ParseNUnitXml(document);
            if (rootName.Equals("testsuite", StringComparison.OrdinalIgnoreCase)
                || rootName.Equals("testsuites", StringComparison.OrdinalIgnoreCase))
            {
                return ParseJUnit(document);
            }

            return null;
        }

        private static CheckRunTestSummary? ParseJestJson(string fullPath)
        {
            JestJsonReport? report;
            try
            {
                report = JsonSerializer.Deserialize<JestJsonReport>(File.ReadAllText(fullPath));
            }
            catch (JsonException)
            {
                return null;
            }

            if (report == null || !report.NumTotalTests.HasValue)
                return null;

            int total = report.NumTotalTests.Value;
            int failed = report.NumFailedTests ?? 0;
            int skipped = (report.NumPendingTests ?? 0) + (report.NumTodoTests ?? 0);
            int passed = report.NumPassedTests ?? Math.Max(0, total - failed - skipped);
            return new CheckRunTestSummary
            {
                Format = "jest-json",
                Total = total,
                Passed = passed,
                Failed = failed,
                Skipped = skipped
            };
        }

        private static CheckRunTestSummary? ParseTrx(XDocument document)
        {
            XElement? counters = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "Counters");
            if (counters == null)
                return null;

            int? total = ParseNullableInt(counters.Attribute("total")?.Value);
            int? passed = ParseNullableInt(counters.Attribute("passed")?.Value);
            int? failed = ParseNullableInt(counters.Attribute("failed")?.Value);
            int? notExecuted = ParseNullableInt(counters.Attribute("notExecuted")?.Value);
            if (!total.HasValue && !passed.HasValue && !failed.HasValue && !notExecuted.HasValue)
                return null;

            return new CheckRunTestSummary
            {
                Format = "trx",
                Total = total,
                Passed = passed,
                Failed = failed,
                Skipped = notExecuted
            };
        }

        private static CheckRunTestSummary? ParseJUnit(XDocument document)
        {
            IEnumerable<XElement> suites = document.Root?.Name.LocalName.Equals("testsuite", StringComparison.OrdinalIgnoreCase) == true
                ? new[] { document.Root! }
                : document.Descendants().Where(node => node.Name.LocalName == "testsuite");

            int total = 0;
            int failed = 0;
            int skipped = 0;
            double durationSeconds = 0;
            bool found = false;

            foreach (XElement suite in suites)
            {
                int? tests = ParseNullableInt(suite.Attribute("tests")?.Value);
                int? failures = ParseNullableInt(suite.Attribute("failures")?.Value);
                int? errors = ParseNullableInt(suite.Attribute("errors")?.Value);
                int? skippedCount = ParseNullableInt(suite.Attribute("skipped")?.Value);
                double? time = ParseNullableDouble(suite.Attribute("time")?.Value, scaleFromFraction: false);

                if (!tests.HasValue && !failures.HasValue && !errors.HasValue && !skippedCount.HasValue)
                    continue;

                found = true;
                total += tests ?? 0;
                failed += (failures ?? 0) + (errors ?? 0);
                skipped += skippedCount ?? 0;
                durationSeconds += time ?? 0;
            }

            if (!found)
                return null;

            int passed = Math.Max(0, total - failed - skipped);
            return new CheckRunTestSummary
            {
                Format = "junit",
                Total = total,
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                DurationMs = Convert.ToInt64(Math.Round(durationSeconds * 1000.0))
            };
        }

        private static CheckRunTestSummary? ParseNUnitXml(XDocument document)
        {
            XElement? root = document.Root;
            if (root == null)
                return null;

            int? total = ParseNullableInt(root.Attribute("total")?.Value);
            int? passed = ParseNullableInt(root.Attribute("passed")?.Value);
            int? failed = ParseNullableInt(root.Attribute("failed")?.Value);
            int? inconclusive = ParseNullableInt(root.Attribute("inconclusive")?.Value);
            int? skipped = ParseNullableInt(root.Attribute("skipped")?.Value);
            int? warnings = ParseNullableInt(root.Attribute("warnings")?.Value);

            if (!total.HasValue && !passed.HasValue && !failed.HasValue && !inconclusive.HasValue && !skipped.HasValue && !warnings.HasValue)
                return null;

            int aggregateSkipped = (skipped ?? 0) + (inconclusive ?? 0) + (warnings ?? 0);
            long? durationMs = null;
            double? durationSeconds = ParseNullableDouble(root.Attribute("duration")?.Value, scaleFromFraction: false);
            if (durationSeconds.HasValue)
                durationMs = Convert.ToInt64(Math.Round(durationSeconds.Value * 1000.0));

            return new CheckRunTestSummary
            {
                Format = "nunit",
                Total = total,
                Passed = passed,
                Failed = failed,
                Skipped = aggregateSkipped,
                DurationMs = durationMs
            };
        }

        private static CheckRunCoverageSummary? ParseCoverageXml(string fullPath, string relativePath)
        {
            XDocument document = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace);
            string rootName = document.Root?.Name.LocalName ?? String.Empty;

            if (rootName.Equals("coverage", StringComparison.OrdinalIgnoreCase))
                return ParseCobertura(document, relativePath);
            if (rootName.Equals("report", StringComparison.OrdinalIgnoreCase))
                return ParseJacoco(document, relativePath);
            if (rootName.Equals("CoverageSession", StringComparison.OrdinalIgnoreCase))
                return ParseOpenCover(document, relativePath);

            return null;
        }

        private static CheckRunCoverageSummary? ParseCobertura(XDocument document, string relativePath)
        {
            XElement? root = document.Root;
            if (root == null)
                return null;

            return new CheckRunCoverageSummary
            {
                Format = "cobertura",
                SourcePath = relativePath,
                Lines = CreateMetric(
                    ParseNullableInt(root.Attribute("lines-covered")?.Value),
                    ParseNullableInt(root.Attribute("lines-valid")?.Value),
                    ParseNullableDouble(root.Attribute("line-rate")?.Value, scaleFromFraction: true)),
                Branches = CreateMetric(
                    ParseNullableInt(root.Attribute("branches-covered")?.Value),
                    ParseNullableInt(root.Attribute("branches-valid")?.Value),
                    ParseNullableDouble(root.Attribute("branch-rate")?.Value, scaleFromFraction: true))
            };
        }

        private static CheckRunCoverageSummary? ParseJacoco(XDocument document, string relativePath)
        {
            XElement[] counters = document.Descendants().Where(node => node.Name.LocalName == "counter").ToArray();
            if (counters.Length == 0)
                return null;

            return new CheckRunCoverageSummary
            {
                Format = "jacoco",
                SourcePath = relativePath,
                Lines = CreateMetricFromJaCoCo(counters, "LINE"),
                Branches = CreateMetricFromJaCoCo(counters, "BRANCH"),
                Functions = CreateMetricFromJaCoCo(counters, "METHOD"),
                Statements = CreateMetricFromJaCoCo(counters, "INSTRUCTION")
            };
        }

        private static CheckRunCoverageSummary? ParseOpenCover(XDocument document, string relativePath)
        {
            XElement? summary = document.Descendants().FirstOrDefault(node => node.Name.LocalName == "Summary");
            if (summary == null)
                return null;

            return new CheckRunCoverageSummary
            {
                Format = "opencover",
                SourcePath = relativePath,
                Lines = CreateMetric(
                    ParseNullableInt(summary.Attribute("visitedSequencePoints")?.Value),
                    ParseNullableInt(summary.Attribute("numSequencePoints")?.Value),
                    null),
                Branches = CreateMetric(
                    ParseNullableInt(summary.Attribute("visitedBranchPoints")?.Value),
                    ParseNullableInt(summary.Attribute("numBranchPoints")?.Value),
                    null)
            };
        }

        private static CheckRunCoverageSummary? ParseLcov(string fullPath, string relativePath)
        {
            int? linesTotal = null;
            int? linesCovered = null;
            int? branchesTotal = null;
            int? branchesCovered = null;
            int? functionsTotal = null;
            int? functionsCovered = null;

            foreach (string rawLine in File.ReadAllLines(fullPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("LF:", StringComparison.OrdinalIgnoreCase))
                    linesTotal = ParseNullableInt(line.Substring(3));
                else if (line.StartsWith("LH:", StringComparison.OrdinalIgnoreCase))
                    linesCovered = ParseNullableInt(line.Substring(3));
                else if (line.StartsWith("BRF:", StringComparison.OrdinalIgnoreCase))
                    branchesTotal = ParseNullableInt(line.Substring(4));
                else if (line.StartsWith("BRH:", StringComparison.OrdinalIgnoreCase))
                    branchesCovered = ParseNullableInt(line.Substring(4));
                else if (line.StartsWith("FNF:", StringComparison.OrdinalIgnoreCase))
                    functionsTotal = ParseNullableInt(line.Substring(4));
                else if (line.StartsWith("FNH:", StringComparison.OrdinalIgnoreCase))
                    functionsCovered = ParseNullableInt(line.Substring(4));
            }

            if (!linesTotal.HasValue && !branchesTotal.HasValue && !functionsTotal.HasValue)
                return null;

            return new CheckRunCoverageSummary
            {
                Format = "lcov",
                SourcePath = relativePath,
                Lines = CreateMetric(linesCovered, linesTotal, null),
                Branches = CreateMetric(branchesCovered, branchesTotal, null),
                Functions = CreateMetric(functionsCovered, functionsTotal, null)
            };
        }

        private static CheckRunCoverageSummary? ParseCoverageJson(string fullPath, string relativePath)
        {
            IstanbulCoverageReport? report = JsonSerializer.Deserialize<IstanbulCoverageReport>(File.ReadAllText(fullPath));
            if (report == null || report.Total == null)
                return null;

            return new CheckRunCoverageSummary
            {
                Format = "istanbul-summary",
                SourcePath = relativePath,
                Lines = CreateMetricFromIstanbul(report.Total.Lines),
                Branches = CreateMetricFromIstanbul(report.Total.Branches),
                Functions = CreateMetricFromIstanbul(report.Total.Functions),
                Statements = CreateMetricFromIstanbul(report.Total.Statements)
            };
        }

        private static CheckRunCoverageMetric? CreateMetricFromJaCoCo(IEnumerable<XElement> counters, string type)
        {
            XElement? counter = counters.FirstOrDefault(node =>
                node.Attribute("type")?.Value.Equals(type, StringComparison.OrdinalIgnoreCase) == true);
            if (counter == null)
                return null;

            int? missed = ParseNullableInt(counter.Attribute("missed")?.Value);
            int? covered = ParseNullableInt(counter.Attribute("covered")?.Value);
            int? total = missed.HasValue || covered.HasValue ? (missed ?? 0) + (covered ?? 0) : null;
            return CreateMetric(covered, total, null);
        }

        private static CheckRunCoverageMetric? CreateMetricFromIstanbul(IstanbulCoverageMetric? metric)
        {
            if (metric == null)
                return null;

            return CreateMetric(metric.Covered, metric.Total, metric.Pct);
        }

        private static CheckRunCoverageMetric? CreateMetric(int? covered, int? total, double? percentage)
        {
            if (!covered.HasValue && !total.HasValue && !percentage.HasValue)
                return null;

            if (!percentage.HasValue && covered.HasValue && total.HasValue && total.Value > 0)
                percentage = Math.Round((double)covered.Value / total.Value * 100.0, 2);

            return new CheckRunCoverageMetric
            {
                Covered = covered,
                Total = total,
                Percentage = percentage
            };
        }

        private static int? ParseNullableInt(string? value)
        {
            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;
            return null;
        }

        private static double? ParseNullableDouble(string? value, bool scaleFromFraction)
        {
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                return null;

            if (scaleFromFraction)
                parsed *= 100.0;

            return Math.Round(parsed, 2);
        }

        private static int ParseInt(Match match, string groupName)
        {
            return Int32.Parse(match.Groups[groupName].Value, CultureInfo.InvariantCulture);
        }

        private static long? ParseDurationMilliseconds(string? value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return null;

            string trimmed = value.Trim();
            if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out TimeSpan duration))
                return Convert.ToInt64(Math.Round(duration.TotalMilliseconds));

            double totalMilliseconds = 0;
            bool matched = false;
            foreach (Match token in _DurationTokenRegex.Matches(trimmed))
            {
                matched = true;
                double amount = Double.Parse(token.Groups["value"].Value, CultureInfo.InvariantCulture);
                string unit = token.Groups["unit"].Value.ToLowerInvariant();
                if (unit.StartsWith("ms", StringComparison.Ordinal))
                    totalMilliseconds += amount;
                else if (unit.StartsWith("s", StringComparison.Ordinal) || unit.StartsWith("sec", StringComparison.Ordinal))
                    totalMilliseconds += amount * 1000.0;
                else if (unit.StartsWith("m", StringComparison.Ordinal))
                    totalMilliseconds += amount * 60_000.0;
            }

            return matched ? Convert.ToInt64(Math.Round(totalMilliseconds)) : null;
        }
    }
}
