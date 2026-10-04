namespace Armada.Core.Services.Health
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Grades test infrastructure using the per-ecosystem detectors in <see cref="TestInfrastructureDetector"/> plus
    /// the vessel's latest completed test check run (UnitTest, IntegrationTest, E2ETest, or SmokeTest). Pass when tests
    /// are found and the latest test run passed; Warn when tests are found but there is no passing latest run; Fail when
    /// a recognized project has no tests; NotApplicable when no project is recognized or the path is a bare clone.
    /// Writes LastCheckRunStatus.
    /// </summary>
    public class TestInfrastructureCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.TestInfrastructure;

        /// <inheritdoc />
        public bool RequiresRepository => true;

        #endregion

        #region Private-Members

        private readonly DatabaseDriver? _Database;

        private static readonly CheckRunTypeEnum[] _TestTypes = new CheckRunTypeEnum[]
        {
            CheckRunTypeEnum.UnitTest, CheckRunTypeEnum.IntegrationTest, CheckRunTypeEnum.E2ETest, CheckRunTypeEnum.SmokeTest
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database used to read check runs, or null to ignore check runs (every vessel with
        /// tests then grades Warn with NoTestRun).</param>
        public TestInfrastructureCriterion(DatabaseDriver? database)
        {
            _Database = database;
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
            RepositoryFileInventory inventory = await context.Cache.GetInventoryAsync(context, token).ConfigureAwait(false);
            TestDetectionResult detection = await Task.Run(() => TestInfrastructureDetector.Detect(inventory), token).ConfigureAwait(false);
            CheckRun? latest = await ReadLatestTestRunAsync(context, token).ConfigureAwait(false);
            context.Health.LastCheckRunStatus = latest?.Status.ToString();
            return Grade(detection, latest?.Status);
        }

        /// <summary>
        /// Grade a detection result against the latest test run status.
        /// </summary>
        /// <param name="detection">Detection result.</param>
        /// <param name="latestRunStatus">Status of the latest completed test check run, or null when none.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when detection is null.</exception>
        public static VesselHealthCriterionResult Grade(TestDetectionResult detection, CheckRunStatusEnum? latestRunStatus)
        {
            if (detection == null) throw new ArgumentNullException(nameof(detection));
            if (detection.RecognizedProjects == 0)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.NotApplicable, VesselHealthDetailCodes.NoRecognizedProject);
            if (detection.TestIndicators == 0)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Fail, VesselHealthDetailCodes.NoTestsFound, detection.RecognizedProjects, null);
            if (latestRunStatus == null)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Warn, VesselHealthDetailCodes.NoTestRun, detection.TestIndicators, null);
            if (latestRunStatus.Value == CheckRunStatusEnum.Passed)
                return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.TestsPassing, detection.TestIndicators, null);
            return new VesselHealthCriterionResult(VesselHealthStatusEnum.Warn, VesselHealthDetailCodes.LastTestRunFailed, detection.TestIndicators, null);
        }

        #endregion

        #region Private-Methods

        private async Task<CheckRun?> ReadLatestTestRunAsync(VesselHealthContext context, CancellationToken token)
        {
            if (_Database == null) return null;
            CheckRunQuery query = new CheckRunQuery();
            query.TenantId = context.TenantId;
            query.VesselId = context.Vessel.Id;
            query.PageNumber = 1;
            query.PageSize = 50;
            EnumerationResult<CheckRun> runs = await _Database.CheckRuns.EnumerateAsync(query, token).ConfigureAwait(false);
            return runs.Objects
                .Where(r => _TestTypes.Contains(r.Type))
                .Where(r => r.Status == CheckRunStatusEnum.Passed || r.Status == CheckRunStatusEnum.Failed || r.Status == CheckRunStatusEnum.Canceled)
                .OrderByDescending(r => r.CompletedUtc ?? r.CreatedUtc)
                .FirstOrDefault();
        }

        #endregion
    }
}
