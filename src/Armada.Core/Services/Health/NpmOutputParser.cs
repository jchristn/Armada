namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Health.Json;

    /// <summary>
    /// Interprets npm outdated --json and npm audit --json output. npm outdated exits 1 when packages are outdated and
    /// npm audit exits non-zero when vulnerabilities exist; both are normal when valid JSON is written. Any failure
    /// yields a result with an error code, never an empty success. Stateless and thread-safe.
    /// </summary>
    public static class NpmOutputParser
    {
        #region Public-Members

        /// <summary>
        /// Ecosystem label written on npm dependency rows.
        /// </summary>
        public const string Ecosystem = "npm";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Interpret one npm outdated --json invocation.
        /// </summary>
        /// <param name="result">Tool result.</param>
        /// <param name="manifestPath">Repository-relative path of the package.json, written as ProjectPath.</param>
        /// <param name="timeoutSeconds">Configured timeout, reported with a Timeout error.</param>
        /// <returns>The scan result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        public static DependencyScanResult InterpretOutdated(DependencyToolResult result, string? manifestPath, int timeoutSeconds)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            DependencyScanResult? early = CheckOutcome(result, timeoutSeconds);
            if (early != null) return early;

            string? json = DependencyJson.ExtractObject(result.StandardOutput);
            if (json == null)
            {
                if (result.ExitCode == 0 && String.IsNullOrWhiteSpace(result.StandardOutput))
                    return new DependencyScanResult { HasTargets = true };
                return Unparsable(result);
            }

            DependencyScanResult? error = CheckErrorEnvelope(json);
            if (error != null) return error;

            Dictionary<string, NpmOutdatedEntry>? entries = null;
            try
            {
                entries = JsonSerializer.Deserialize<Dictionary<string, NpmOutdatedEntry>>(json, DependencyJson.Options);
            }
            catch (JsonException)
            {
                try
                {
                    Dictionary<string, List<NpmOutdatedEntry>>? multi = JsonSerializer.Deserialize<Dictionary<string, List<NpmOutdatedEntry>>>(json, DependencyJson.Options);
                    if (multi != null)
                    {
                        entries = new Dictionary<string, NpmOutdatedEntry>(StringComparer.Ordinal);
                        foreach (KeyValuePair<string, List<NpmOutdatedEntry>> pair in multi)
                        {
                            if (pair.Value != null && pair.Value.Count > 0) entries[pair.Key] = pair.Value[0];
                        }
                    }
                }
                catch (JsonException)
                {
                    entries = null;
                }
            }

            if (entries == null) return DependencyScanResult.Failed(VesselHealthDetailCodes.ParseError);

            DependencyScanResult scan = new DependencyScanResult();
            scan.HasTargets = true;
            foreach (KeyValuePair<string, NpmOutdatedEntry> pair in entries)
            {
                if (pair.Value == null || String.IsNullOrWhiteSpace(pair.Key)) continue;
                string? current = pair.Value.Current ?? pair.Value.Wanted;
                DependencyDriftEnum drift = VersionDriftCalculator.Compute(current, pair.Value.Latest);
                if (drift == DependencyDriftEnum.None) continue;
                VesselDependency dependency = new VesselDependency();
                dependency.Ecosystem = Ecosystem;
                dependency.ProjectPath = manifestPath;
                dependency.PackageName = pair.Key;
                dependency.CurrentVersion = current;
                dependency.LatestVersion = pair.Value.Latest;
                dependency.Drift = drift;
                DotnetListParser.Merge(scan.Dependencies, dependency);
            }

            return scan;
        }

        /// <summary>
        /// Interpret one npm audit --json invocation (audit report version 2).
        /// </summary>
        /// <param name="result">Tool result.</param>
        /// <param name="manifestPath">Repository-relative path of the package.json, written as ProjectPath.</param>
        /// <param name="timeoutSeconds">Configured timeout, reported with a Timeout error.</param>
        /// <returns>The scan result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        public static DependencyScanResult InterpretAudit(DependencyToolResult result, string? manifestPath, int timeoutSeconds)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            DependencyScanResult? early = CheckOutcome(result, timeoutSeconds);
            if (early != null) return early;

            string? json = DependencyJson.ExtractObject(result.StandardOutput);
            if (json == null) return Unparsable(result);

            NpmAuditReport? report;
            try
            {
                report = JsonSerializer.Deserialize<NpmAuditReport>(json, DependencyJson.Options);
            }
            catch (JsonException)
            {
                report = null;
            }

            if (report == null) return DependencyScanResult.Failed(VesselHealthDetailCodes.ParseError);
            if (report.Error != null)
            {
                return IsRestoreErrorCode(report.Error.Code)
                    ? DependencyScanResult.Failed(VesselHealthDetailCodes.RestoreRequired)
                    : DependencyScanResult.Failed(VesselHealthDetailCodes.ToolFailed, result.ExitCode);
            }

            if (report.Vulnerabilities == null) return DependencyScanResult.Failed(VesselHealthDetailCodes.ParseError);

            DependencyScanResult scan = new DependencyScanResult();
            scan.HasTargets = true;
            foreach (KeyValuePair<string, NpmAuditVulnerability> pair in report.Vulnerabilities)
            {
                if (pair.Value == null) continue;
                string name = !String.IsNullOrWhiteSpace(pair.Value.Name) ? pair.Value.Name! : pair.Key;
                if (String.IsNullOrWhiteSpace(name)) continue;
                VesselDependency dependency = new VesselDependency();
                dependency.Ecosystem = Ecosystem;
                dependency.ProjectPath = manifestPath;
                dependency.PackageName = name;
                dependency.CurrentVersion = pair.Value.Range;
                dependency.IsVulnerable = true;
                dependency.Severity = DependencyJson.ParseSeverity(pair.Value.Severity);
                DotnetListParser.Merge(scan.Dependencies, dependency);
            }

            return scan;
        }

        #endregion

        #region Private-Methods

        private static DependencyScanResult? CheckOutcome(DependencyToolResult result, int timeoutSeconds)
        {
            if (result.Outcome == DependencyToolOutcomeEnum.ToolMissing) return DependencyScanResult.Failed(VesselHealthDetailCodes.ToolMissing);
            if (result.Outcome == DependencyToolOutcomeEnum.TimedOut) return DependencyScanResult.Failed(VesselHealthDetailCodes.Timeout, timeoutSeconds);
            return null;
        }

        private static DependencyScanResult? CheckErrorEnvelope(string json)
        {
            NpmErrorEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<NpmErrorEnvelope>(json, DependencyJson.Options);
            }
            catch (JsonException)
            {
                return null;
            }

            if (envelope?.Error == null || String.IsNullOrWhiteSpace(envelope.Error.Code)) return null;
            return IsRestoreErrorCode(envelope.Error.Code)
                ? DependencyScanResult.Failed(VesselHealthDetailCodes.RestoreRequired)
                : DependencyScanResult.Failed(VesselHealthDetailCodes.ToolFailed);
        }

        /// <summary>
        /// Whether an npm error code means the package tree must be installed or locked first. Decided from npm's
        /// machine-readable <c>error.code</c>, never from the summary or detail text.
        /// </summary>
        private static bool IsRestoreErrorCode(string? code)
        {
            switch (code)
            {
                case "ENOLOCK":
                    return true;
                default:
                    return false;
            }
        }

        private static DependencyScanResult Unparsable(DependencyToolResult result)
        {
            if (result.ExitCode != 0) return DependencyScanResult.Failed(VesselHealthDetailCodes.ToolFailed, result.ExitCode);
            return DependencyScanResult.Failed(VesselHealthDetailCodes.ParseError);
        }

        #endregion
    }
}
