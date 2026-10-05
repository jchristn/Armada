namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Services;

    /// <summary>
    /// Vessel health wording, ported from the dashboard's <c>lib/health/healthText.ts</c>: status, criterion,
    /// severity, and drift labels, the criterion display order, and one localized sentence per backend detail code
    /// (with the same singular and plural catalog keys), so findings read exactly as they do in the browser.
    /// </summary>
    public static class HealthText
    {
        #region Public-Members

        /// <summary>
        /// Health statuses in the dashboard's order.
        /// </summary>
        public static readonly VesselHealthStatusEnum[] Statuses = new VesselHealthStatusEnum[]
        {
            VesselHealthStatusEnum.Pass, VesselHealthStatusEnum.Warn, VesselHealthStatusEnum.Fail, VesselHealthStatusEnum.NotApplicable, VesselHealthStatusEnum.Unknown,
        };

        /// <summary>
        /// Criteria that have findings, in display order.
        /// </summary>
        public static readonly VesselHealthCriterionEnum[] Criteria = new VesselHealthCriterionEnum[]
        {
            VesselHealthCriterionEnum.GitDivergence, VesselHealthCriterionEnum.WorkingTree, VesselHealthCriterionEnum.Branches,
            VesselHealthCriterionEnum.CommitRecency, VesselHealthCriterionEnum.Dependencies, VesselHealthCriterionEnum.Vulnerabilities,
            VesselHealthCriterionEnum.TestInfrastructure, VesselHealthCriterionEnum.ContinuousIntegration, VesselHealthCriterionEnum.ArmadaReadiness,
            VesselHealthCriterionEnum.MissionOutcomes,
        };

        /// <summary>
        /// Override targets: Overall and every criterion.
        /// </summary>
        public static readonly VesselHealthCriterionEnum[] OverrideCriteria = new VesselHealthCriterionEnum[]
        {
            VesselHealthCriterionEnum.Overall,
            VesselHealthCriterionEnum.GitDivergence, VesselHealthCriterionEnum.WorkingTree, VesselHealthCriterionEnum.Branches,
            VesselHealthCriterionEnum.CommitRecency, VesselHealthCriterionEnum.Dependencies, VesselHealthCriterionEnum.Vulnerabilities,
            VesselHealthCriterionEnum.TestInfrastructure, VesselHealthCriterionEnum.ContinuousIntegration, VesselHealthCriterionEnum.ArmadaReadiness,
            VesselHealthCriterionEnum.MissionOutcomes,
        };

        #endregion

        #region Private-Members

        private static readonly VulnerabilitySeverityEnum[] _SeverityByRank = new VulnerabilitySeverityEnum[]
        {
            VulnerabilitySeverityEnum.None, VulnerabilitySeverityEnum.Low, VulnerabilitySeverityEnum.Moderate, VulnerabilitySeverityEnum.High, VulnerabilitySeverityEnum.Critical,
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// English status label.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string StatusLabel(VesselHealthStatusEnum status)
        {
            switch (status)
            {
                case VesselHealthStatusEnum.Pass: return "Pass";
                case VesselHealthStatusEnum.Warn: return "Warn";
                case VesselHealthStatusEnum.Fail: return "Fail";
                case VesselHealthStatusEnum.NotApplicable: return "Not applicable";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Status badge text with an ASCII marker so the state never depends on color (for example <c>x Fail</c>).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="loc">Localizer.</param>
        /// <param name="overridden">True when an override set the status (adds a trailing <c>*</c>).</param>
        /// <returns>Badge text.</returns>
        public static string Badge(VesselHealthStatusEnum status, ITextLocalizer loc, bool overridden = false)
        {
            string marker;
            switch (status)
            {
                case VesselHealthStatusEnum.Pass: marker = "+"; break;
                case VesselHealthStatusEnum.Warn: marker = "!"; break;
                case VesselHealthStatusEnum.Fail: marker = "x"; break;
                case VesselHealthStatusEnum.NotApplicable: marker = "-"; break;
                default: marker = "?"; break;
            }

            return marker + " " + loc.T(StatusLabel(status)) + (overridden ? " *" : "");
        }

        /// <summary>
        /// Status name for the shared badge styles (Pass is green, Warn amber, Fail red).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Style key.</returns>
        public static string StyleKey(VesselHealthStatusEnum status)
        {
            switch (status)
            {
                case VesselHealthStatusEnum.Pass: return "Complete";
                case VesselHealthStatusEnum.Warn: return "Warning";
                case VesselHealthStatusEnum.Fail: return "Failed";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// English criterion label.
        /// </summary>
        /// <param name="criterion">Criterion.</param>
        /// <returns>Label.</returns>
        public static string CriterionLabel(VesselHealthCriterionEnum criterion)
        {
            switch (criterion)
            {
                case VesselHealthCriterionEnum.GitDivergence: return "Git divergence";
                case VesselHealthCriterionEnum.WorkingTree: return "Working tree";
                case VesselHealthCriterionEnum.Branches: return "Branches";
                case VesselHealthCriterionEnum.CommitRecency: return "Commit recency";
                case VesselHealthCriterionEnum.Dependencies: return "Dependencies";
                case VesselHealthCriterionEnum.Vulnerabilities: return "Vulnerabilities";
                case VesselHealthCriterionEnum.TestInfrastructure: return "Test infrastructure";
                case VesselHealthCriterionEnum.ContinuousIntegration: return "Continuous integration";
                case VesselHealthCriterionEnum.ArmadaReadiness: return "Armada readiness";
                case VesselHealthCriterionEnum.MissionOutcomes: return "Mission outcomes";
                default: return "Overall";
            }
        }

        /// <summary>
        /// English severity label.
        /// </summary>
        /// <param name="severity">Severity.</param>
        /// <returns>Label.</returns>
        public static string SeverityLabel(VulnerabilitySeverityEnum severity)
        {
            return severity.ToString();
        }

        /// <summary>
        /// English drift label.
        /// </summary>
        /// <param name="drift">Drift.</param>
        /// <returns>Label.</returns>
        public static string DriftLabel(DependencyDriftEnum drift)
        {
            return drift.ToString();
        }

        /// <summary>
        /// Locale-formatted count, or "-" for null.
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="value">Value.</param>
        /// <returns>Text.</returns>
        public static string Count(ITextLocalizer loc, long? value)
        {
            return value.HasValue ? loc.FormatNumber(value.Value) : "-";
        }

        /// <summary>
        /// A localized one-sentence description of a finding (for example "Behind by 7 commits (ahead 2)."); unknown
        /// codes fall back to the raw code so nothing is hidden.
        /// </summary>
        /// <param name="detailCode">Detail code.</param>
        /// <param name="valueA">Value A.</param>
        /// <param name="valueB">Value B.</param>
        /// <param name="loc">Localizer.</param>
        /// <returns>Sentence.</returns>
        public static string Describe(string? detailCode, long? valueA, long? valueB, ITextLocalizer loc)
        {
            string code = detailCode ?? "";
            if (code.Length == 0) return loc.T("No details recorded.");
            long a = valueA ?? 0;
            long b = valueB ?? 0;
            switch (code)
            {
                case "EvaluationError": return loc.T("The check failed while evaluating.");
                case "RepositoryUnavailable": return loc.T("The repository was not found on disk.");
                case "BareRepository": return loc.T("Only a bare clone is available, so there is no checkout to inspect.");
                case "NotApplicable": return loc.T("Does not apply to this vessel.");
                case "Even": return loc.T("Even with the default branch.");
                case "Ahead": return Plural(loc, a, "Ahead by {{count}} commit.", "Ahead by {{count}} commits.");
                case "Behind":
                    return a > 0
                        ? Plural(loc, b, "Behind by {{count}} commit (ahead {{ahead}}).", "Behind by {{count}} commits (ahead {{ahead}}).", "ahead", loc.FormatNumber(a))
                        : Plural(loc, b, "Behind by {{count}} commit.", "Behind by {{count}} commits.");
                case "Diverged": return loc.T("Diverged: {{ahead}} ahead and {{behind}} behind.", LocalizationArgs.Of("ahead", loc.FormatNumber(a), "behind", loc.FormatNumber(b)));
                case "FetchFailed": return loc.T("git fetch failed, so divergence could not be measured.");
                case "DefaultBranchMissing": return loc.T("The default branch could not be found.");
                case "NoCommits": return loc.T("The repository has no commits.");
                case "Clean": return loc.T("The working tree is clean.");
                case "UntrackedOnly": return Plural(loc, b, "{{count}} untracked file.", "{{count}} untracked files.");
                case "Modified": return Plural(loc, a, "{{count}} modified tracked file ({{untracked}} untracked).", "{{count}} modified tracked files ({{untracked}} untracked).", "untracked", loc.FormatNumber(b));
                case "BranchesOk": return a == 0 ? loc.T("No stale branches.") : Plural(loc, a, "{{count}} stale branch.", "{{count}} stale branches.");
                case "StaleBranches": return Plural(loc, a, "{{count}} stale branch ({{armada}} leftover armada/*).", "{{count}} stale branches ({{armada}} leftover armada/*).", "armada", loc.FormatNumber(b));
                case "ArmadaBranches": return Plural(loc, b, "{{count}} leftover armada/* branch ({{stale}} stale).", "{{count}} leftover armada/* branches ({{stale}} stale).", "stale", loc.FormatNumber(a));
                case "LastCommitAge": return loc.T("Last commit {{when}}.", LocalizationArgs.Of("when", DaysAgo(loc, a)));
                case "NoOutdatedPackages": return loc.T("All packages are up to date.");
                case "OutdatedPackages": return Plural(loc, a, "{{count}} outdated package ({{major}} major).", "{{count}} outdated packages ({{major}} major).", "major", loc.FormatNumber(b));
                case "NoVulnerabilities": return loc.T("No known vulnerabilities.");
                case "VulnerablePackages":
                    VulnerabilitySeverityEnum sev = b >= 0 && b < _SeverityByRank.Length ? _SeverityByRank[b] : VulnerabilitySeverityEnum.None;
                    return Plural(loc, a, "{{count}} vulnerable package (highest severity {{severity}}).", "{{count}} vulnerable packages (highest severity {{severity}}).", "severity", loc.T(SeverityLabel(sev)));
                case "NoPackageManifests": return loc.T("No NuGet project or npm package with a lockfile was found.");
                case "ToolMissing": return loc.T("dotnet or npm is not installed on the Admiral host.");
                case "RestoreRequired": return loc.T("A package restore is required before packages can be checked.");
                case "Timeout": return valueA == null ? loc.T("The package tool timed out.") : Plural(loc, a, "The package tool timed out after {{count}} second.", "The package tool timed out after {{count}} seconds.");
                case "ParseError": return loc.T("The package tool output could not be parsed.");
                case "ToolFailed": return valueA == null ? loc.T("The package tool failed.") : loc.T("The package tool exited with code {{code}}.", LocalizationArgs.Of("code", a.ToString(CultureInfo.InvariantCulture)));
                case "TestsPassing": return Plural(loc, a, "{{count}} test indicator found and the last test run passed.", "{{count}} test indicators found and the last test run passed.");
                case "NoTestRun": return Plural(loc, a, "{{count}} test indicator found but no test run is recorded.", "{{count}} test indicators found but no test run is recorded.");
                case "LastTestRunFailed": return Plural(loc, a, "{{count}} test indicator found but the last test run did not pass.", "{{count}} test indicators found but the last test run did not pass.");
                case "NoTestsFound": return Plural(loc, a, "No tests found in {{count}} recognized project.", "No tests found in {{count}} recognized projects.");
                case "NoRecognizedProject": return loc.T("No .NET, Node, Python, Go, or Rust project was recognized.");
                case "CiConfigured": return Plural(loc, a, "{{count}} CI configuration file found.", "{{count}} CI configuration files found.");
                case "NoCiConfig": return loc.T("No CI configuration was found.");
                case "ReadinessOk": return loc.T("No readiness issues.");
                case "ReadinessWarnings": return Plural(loc, b, "{{count}} readiness warning.", "{{count}} readiness warnings.");
                case "ReadinessErrors": return Plural(loc, a, "{{count}} readiness error ({{warnings}} warnings).", "{{count}} readiness errors ({{warnings}} warnings).", "warnings", loc.FormatNumber(b));
                case "NoRecentFailures":
                    return b == 1 ? loc.T("No failed missions in the last day.") : loc.T("No failed missions in the last {{days}} days.", LocalizationArgs.Of("days", loc.FormatNumber(b)));
                case "RecentFailures":
                    return b == 1
                        ? Plural(loc, a, "{{count}} failed mission in the last day.", "{{count}} failed missions in the last day.")
                        : Plural(loc, a, "{{count}} failed mission in the last {{days}} days.", "{{count}} failed missions in the last {{days}} days.", "days", loc.FormatNumber(b));
                default: return code;
            }
        }

        /// <summary>
        /// Describe a finding.
        /// </summary>
        /// <param name="finding">Finding.</param>
        /// <param name="loc">Localizer.</param>
        /// <returns>Sentence.</returns>
        public static string Describe(VesselHealthFinding finding, ITextLocalizer loc)
        {
            if (finding == null) return "";
            return Describe(finding.DetailCode, finding.ValueA, finding.ValueB, loc);
        }

        /// <summary>
        /// "N ahead, M behind", or "-" when neither is measured.
        /// </summary>
        /// <param name="ahead">Ahead.</param>
        /// <param name="behind">Behind.</param>
        /// <param name="loc">Localizer.</param>
        /// <returns>Text.</returns>
        public static string AheadBehind(int? ahead, int? behind, ITextLocalizer loc)
        {
            if (!ahead.HasValue && !behind.HasValue) return "-";
            return loc.T("{{ahead}} ahead, {{behind}} behind", LocalizationArgs.Of("ahead", loc.FormatNumber(ahead ?? 0), "behind", loc.FormatNumber(behind ?? 0)));
        }

        #endregion

        #region Private-Methods

        private static string Plural(ITextLocalizer loc, long count, string one, string other, string? extraKey = null, string? extraValue = null)
        {
            Dictionary<string, object?> args = new Dictionary<string, object?>(StringComparer.Ordinal);
            args["count"] = loc.FormatNumber(count);
            if (extraKey != null) args[extraKey] = extraValue;
            return loc.T(count == 1 ? one : other, args);
        }

        private static string DaysAgo(ITextLocalizer loc, long days)
        {
            if (days <= 0) return loc.T("today");
            if (days == 1) return loc.T("yesterday");
            return loc.T("{{count}} days ago", LocalizationArgs.Of("count", loc.FormatNumber(days)));
        }

        #endregion
    }
}
