namespace Armada.Core.Services.Health
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Grades outdated package dependencies (NuGet via dotnet list package --outdated, npm via npm outdated). Pass when
    /// nothing is outdated, Warn when only minor or patch drift exists, Fail when any package has major drift. A missing
    /// tool, a required restore, a timeout, a non-zero exit, or unparsable output grades Unknown with a detail code and is
    /// never Pass. Not applicable to a bare clone or to a repository without NuGet projects or locked npm packages.
    /// Writes OutdatedCount and OutdatedMajorCount, and fills <see cref="VesselHealthContext.OutdatedDependencies"/>.
    /// </summary>
    public class DependenciesCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.Dependencies;

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
        public DependenciesCriterion(DependencyScanner scanner)
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
            DependencyScanResult scan = await _Scanner.ScanAsync(context, DependencyScanModeEnum.Outdated, token).ConfigureAwait(false);
            context.OutdatedDependencies.AddRange(scan.Dependencies);
            return Grade(scan, context.Health);
        }

        /// <summary>
        /// Grade an outdated-dependency scan and write the counts onto the health row (counts stay null on failure).
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

            int outdated = scan.Dependencies
                .Where(d => d.Drift != DependencyDriftEnum.None)
                .Select(d => d.Ecosystem.ToLowerInvariant() + "|" + d.PackageName.ToLowerInvariant())
                .Distinct()
                .Count();
            int major = scan.Dependencies
                .Where(d => d.Drift == DependencyDriftEnum.Major)
                .Select(d => d.Ecosystem.ToLowerInvariant() + "|" + d.PackageName.ToLowerInvariant())
                .Distinct()
                .Count();
            if (health != null)
            {
                health.OutdatedCount = outdated;
                health.OutdatedMajorCount = major;
            }

            if (outdated == 0) return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.NoOutdatedPackages, 0, 0);
            VesselHealthStatusEnum status = major > 0 ? VesselHealthStatusEnum.Fail : VesselHealthStatusEnum.Warn;
            return new VesselHealthCriterionResult(status, VesselHealthDetailCodes.OutdatedPackages, outdated, major);
        }

        #endregion
    }
}
