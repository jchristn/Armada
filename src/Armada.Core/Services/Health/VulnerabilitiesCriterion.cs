namespace Armada.Core.Services.Health
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Grades known-vulnerable package dependencies (NuGet via dotnet list package --vulnerable, npm via npm audit).
    /// Pass when none, Warn when the highest severity is Low or Moderate, Fail when High or Critical. Tool failures grade
    /// Unknown with a detail code and are never Pass. Not applicable to a bare clone or to a repository without NuGet
    /// projects or locked npm packages. Writes VulnerableCount and MaxVulnerabilitySeverity, and fills
    /// <see cref="VesselHealthContext.VulnerableDependencies"/>.
    /// </summary>
    public class VulnerabilitiesCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.Vulnerabilities;

        /// <inheritdoc />
        public bool RequiresRepository => true;

        #endregion

        #region Private-Members

        private readonly DependencyScanner _Scanner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="scanner">Dependency scanner.</param>
        /// <exception cref="ArgumentNullException">Thrown when scanner is null.</exception>
        public VulnerabilitiesCriterion(DependencyScanner scanner)
        {
            _Scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<bool> AppliesAsync(VesselHealthContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Task.FromResult(!context.IsBare);
        }

        /// <inheritdoc />
        public async Task<VesselHealthCriterionResult> EvaluateAsync(VesselHealthContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            DependencyScanResult scan = await _Scanner.ScanAsync(context, DependencyScanModeEnum.Vulnerable, token).ConfigureAwait(false);
            context.VulnerableDependencies.AddRange(scan.Dependencies);
            return Grade(scan, context.Health);
        }

        /// <summary>
        /// Grade a vulnerability scan and write the counts onto the health row (counts stay unset on failure).
        /// </summary>
        /// <param name="scan">Scan result.</param>
        /// <param name="health">Health row to update, or null.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when scan is null.</exception>
        public static VesselHealthCriterionResult Grade(DependencyScanResult scan, VesselHealth? health)
        {
            if (scan == null) throw new ArgumentNullException(nameof(scan));
            if (scan.ErrorCode != null)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Unknown, scan.ErrorCode, scan.ErrorValue, null);
            if (!scan.HasTargets)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.NotApplicable, VesselHealthDetailCodes.NoPackageManifests);

            int vulnerable = scan.Dependencies
                .Where(d => d.IsVulnerable)
                .Select(d => d.Ecosystem.ToLowerInvariant() + "|" + d.PackageName.ToLowerInvariant())
                .Distinct()
                .Count();
            VulnerabilitySeverityEnum max = scan.Dependencies.Where(d => d.IsVulnerable)
                .Select(d => d.Severity)
                .DefaultIfEmpty(VulnerabilitySeverityEnum.None)
                .Max();
            if (health != null)
            {
                health.VulnerableCount = vulnerable;
                health.MaxVulnerabilitySeverity = max;
            }

            if (vulnerable == 0) return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.NoVulnerabilities, 0, 0);
            VesselHealthStatusEnum status = max >= VulnerabilitySeverityEnum.High ? VesselHealthStatusEnum.Fail : VesselHealthStatusEnum.Warn;
            return new VesselHealthCriterionResult(status, VesselHealthDetailCodes.VulnerablePackages, vulnerable, (long)max);
        }

        #endregion
    }
}
