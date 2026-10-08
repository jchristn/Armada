namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Evaluates one vessel against every registered criterion and persists the result. The evaluator picks the path
    /// (the working directory when it exists and is a git checkout, otherwise the bare clone, otherwise a checkout on a
    /// connected Harbor, where git and the dependency tools then run), optionally fetches, runs
    /// each criterion with timing and exception isolation, skips the slow dependency and vulnerability checks while the
    /// manifest hash is unchanged and the previous results are younger than DependencyMaxAgeHours (unless forced),
    /// applies manual overrides to produce the effective status columns, rolls them up into the overall status, and
    /// writes the findings, dependency rows, and health row. Settings are read live on every evaluation. Thread-safe:
    /// distinct vessels may be evaluated concurrently.
    /// </summary>
    public class VesselHealthEvaluator
    {
        #region Public-Members

        /// <summary>
        /// Clock used for timestamps and age calculations. Defaults to DateTime.UtcNow; replaceable in tests.
        /// </summary>
        public Func<DateTime> Clock
        {
            get => _Clock;
            set => _Clock = value ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// The criteria this evaluator runs, in order. Never null.
        /// </summary>
        public IReadOnlyList<IVesselHealthCriterion> Criteria => _Criteria;

        /// <summary>
        /// Finds a checkout on a connected Harbor when the vessel has neither a working directory nor a bare clone on the
        /// Admiral host. When null, only the Admiral host is considered.
        /// </summary>
        public VesselHostResolver? Hosts { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[VesselHealthEvaluator] ";
        private readonly DatabaseDriver _Database;
        private readonly IGitService _Git;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly List<IVesselHealthCriterion> _Criteria;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="git">Git service.</param>
        /// <param name="settings">Live settings (RepositoryHealth and Import.ExcludedDirectoryNames are read per evaluation).</param>
        /// <param name="criteria">Criteria to run, in order.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public VesselHealthEvaluator(DatabaseDriver database, IGitService git, ArmadaSettings settings, IEnumerable<IVesselHealthCriterion> criteria, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            _Criteria = criteria.Where(c => c != null).ToList();
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Create the default criteria set, in evaluation order.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="readiness">Vessel readiness service.</param>
        /// <param name="scanner">Dependency scanner.</param>
        /// <returns>The criteria.</returns>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public static List<IVesselHealthCriterion> CreateDefaultCriteria(DatabaseDriver database, VesselReadinessService readiness, DependencyScanner scanner)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (readiness == null) throw new ArgumentNullException(nameof(readiness));
            if (scanner == null) throw new ArgumentNullException(nameof(scanner));
            return new List<IVesselHealthCriterion>
            {
                new GitDivergenceCriterion(),
                new WorkingTreeCriterion(),
                new BranchesCriterion(),
                new CommitRecencyCriterion(),
                new DependenciesCriterion(scanner),
                new VulnerabilitiesCriterion(scanner),
                new TestInfrastructureCriterion(database),
                new ContinuousIntegrationCriterion(),
                new ArmadaReadinessCriterion(readiness),
                new MissionOutcomesCriterion(database)
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Evaluate one vessel and persist the result.
        /// </summary>
        /// <param name="vessel">Vessel to evaluate.</param>
        /// <param name="forceDependencies">Run dependency and vulnerability checks even when fresh.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The persisted health row (effective statuses).</returns>
        /// <exception cref="ArgumentNullException">Thrown when vessel is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public async Task<VesselHealth> EvaluateAsync(Vessel vessel, bool forceDependencies, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            string tenantId = String.IsNullOrEmpty(vessel.TenantId) ? Constants.DefaultTenantId : vessel.TenantId!;
            RepositoryHealthSettings settings = _Settings.RepositoryHealth;
            Stopwatch total = Stopwatch.StartNew();

            using Activity? root = VesselHealthTelemetry.StartSpan("vessel_health.evaluate_vessel");
            root?.SetTag("armada.vessel_id", vessel.Id);

            VesselHealthContext context = new VesselHealthContext(vessel, tenantId, settings, _Settings.Import.ExcludedDirectoryNames, _Git);
            context.NowUtc = _Clock();
            context.ForceDependencies = forceDependencies;
            context.Previous = await _Database.VesselHealth.ReadByVesselAsync(tenantId, vessel.Id, token).ConfigureAwait(false);

            await SelectPathAsync(context, token).ConfigureAwait(false);
            if (!context.RepositoryAvailable) context.Health.ErrorCode = VesselHealthDetailCodes.RepositoryUnavailable;
            context.Health.EvaluatedPath = context.EvaluatedPath;

            if (context.RepositoryAvailable && settings.FetchBeforeEvaluate)
                await FetchAsync(context, token).ConfigureAwait(false);

            string? manifestHash = null;
            if (context.RepositoryAvailable)
            {
                RepositoryFileInventory inventory = await context.Cache.GetInventoryAsync(context, token).ConfigureAwait(false);
                manifestHash = ManifestHasher.Compute(inventory);
                context.Health.PrimaryLanguage = PrimaryLanguageDetector.Detect(inventory);
                context.Health.ProjectCount = PrimaryLanguageDetector.CountProjects(inventory);
            }

            List<VesselHealthFinding> previousFindings = context.Previous == null
                ? new List<VesselHealthFinding>()
                : await _Database.VesselHealthFindings.ReadByVesselAsync(tenantId, vessel.Id, token).ConfigureAwait(false);
            bool runDependencies = ShouldRunDependencies(context, manifestHash, previousFindings);

            List<VesselHealthFinding> findings = new List<VesselHealthFinding>();
            foreach (IVesselHealthCriterion criterion in _Criteria)
            {
                token.ThrowIfCancellationRequested();
                if (!runDependencies && IsDependencyCriterion(criterion.Code))
                {
                    VesselHealthFinding? kept = previousFindings.FirstOrDefault(f => f.Criterion == criterion.Code);
                    if (kept != null) findings.Add(kept);
                    continue;
                }

                VesselHealthCriterionResult result = await RunCriterionAsync(criterion, context, token).ConfigureAwait(false);
                VesselHealthFinding finding = new VesselHealthFinding();
                finding.TenantId = tenantId;
                finding.VesselId = vessel.Id;
                finding.Criterion = criterion.Code;
                finding.Status = result.Status;
                finding.DetailCode = result.DetailCode;
                finding.ValueA = result.ValueA;
                finding.ValueB = result.ValueB;
                finding.EvaluatedUtc = context.NowUtc;
                findings.Add(finding);
            }

            List<VesselDependency>? dependencies = null;
            if (runDependencies)
            {
                dependencies = new List<VesselDependency>();
                foreach (VesselDependency dependency in context.OutdatedDependencies.Concat(context.VulnerableDependencies))
                    DotnetListParser.Merge(dependencies, dependency);

                bool dependencyToolFailed = findings.Any(f => IsDependencyCriterion(f.Criterion) && f.Status == VesselHealthStatusEnum.Unknown);
                context.Health.ManifestHash = manifestHash;
                context.Health.DependenciesEvaluatedUtc = dependencyToolFailed ? null : context.NowUtc;
            }
            else if (context.Previous != null)
            {
                context.Health.ManifestHash = context.Previous.ManifestHash;
                context.Health.DependenciesEvaluatedUtc = context.Previous.DependenciesEvaluatedUtc;
                context.Health.OutdatedCount = context.Previous.OutdatedCount;
                context.Health.OutdatedMajorCount = context.Previous.OutdatedMajorCount;
                context.Health.VulnerableCount = context.Previous.VulnerableCount;
                context.Health.MaxVulnerabilitySeverity = context.Previous.MaxVulnerabilitySeverity;
            }

            List<VesselHealthOverride> overrides = await _Database.VesselHealthOverrides.ReadByVesselAsync(tenantId, vessel.Id, token).ConfigureAwait(false);
            VesselHealthRollup.ApplyEffectiveStatuses(context.Health, findings, overrides, settings.ScoredCriteria);

            total.Stop();
            context.Health.EvaluatedUtc = context.NowUtc;
            context.Health.EvaluationDurationMs = total.ElapsedMilliseconds;

            token.ThrowIfCancellationRequested();
            await _Database.VesselHealthFindings.ReplaceForVesselAsync(tenantId, vessel.Id, findings, token).ConfigureAwait(false);
            if (dependencies != null)
                await _Database.VesselDependencies.ReplaceForVesselAsync(tenantId, vessel.Id, dependencies, token).ConfigureAwait(false);
            VesselHealth saved = await _Database.VesselHealth.UpsertAsync(context.Health, token).ConfigureAwait(false);

            root?.SetTag("armada.health.overall", saved.OverallStatus.ToString());
            _Logging.Debug(_Header + "evaluated " + vessel.Id + " in " + total.ElapsedMilliseconds + "ms: " + saved.OverallStatus + (runDependencies ? "" : " (dependencies fresh, skipped)"));
            return saved;
        }

        /// <summary>
        /// Whether the dependency and vulnerability checks must run: when forced, when there are no previous results
        /// for both criteria, when the manifest hash changed, or when the previous dependency results are missing or
        /// older than DependencyMaxAgeHours.
        /// </summary>
        /// <param name="context">Evaluation context (with Previous set).</param>
        /// <param name="manifestHash">Current manifest hash, or null.</param>
        /// <param name="previousFindings">Previously stored findings.</param>
        /// <returns>True when the checks must run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context or previousFindings is null.</exception>
        public static bool ShouldRunDependencies(VesselHealthContext context, string? manifestHash, List<VesselHealthFinding> previousFindings)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (previousFindings == null) throw new ArgumentNullException(nameof(previousFindings));
            if (context.ForceDependencies) return true;
            if (!context.RepositoryAvailable) return true;
            VesselHealth? previous = context.Previous;
            if (previous == null) return true;
            if (!previousFindings.Any(f => f.Criterion == VesselHealthCriterionEnum.Dependencies)) return true;
            if (!previousFindings.Any(f => f.Criterion == VesselHealthCriterionEnum.Vulnerabilities)) return true;
            if (!String.Equals(previous.ManifestHash, manifestHash, StringComparison.Ordinal)) return true;
            if (previous.DependenciesEvaluatedUtc == null) return true;
            return previous.DependenciesEvaluatedUtc.Value < context.NowUtc.AddHours(-context.Settings.DependencyMaxAgeHours);
        }

        #endregion

        #region Private-Methods

        private static bool IsDependencyCriterion(VesselHealthCriterionEnum code)
        {
            return code == VesselHealthCriterionEnum.Dependencies || code == VesselHealthCriterionEnum.Vulnerabilities;
        }

        private async Task SelectPathAsync(VesselHealthContext context, CancellationToken token)
        {
            string? workingDirectory = context.Vessel.WorkingDirectory;
            if (!String.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
                && await _Git.IsRepositoryAsync(workingDirectory, token).ConfigureAwait(false))
            {
                context.EvaluatedPath = workingDirectory;
                context.IsBare = await _Git.IsBareRepositoryAsync(workingDirectory, token).ConfigureAwait(false);
                context.RepositoryAvailable = true;
                return;
            }

            string? localPath = context.Vessel.LocalPath;
            if (!String.IsNullOrWhiteSpace(localPath) && Directory.Exists(localPath)
                && await _Git.IsRepositoryAsync(localPath, token).ConfigureAwait(false))
            {
                context.EvaluatedPath = localPath;
                context.IsBare = await _Git.IsBareRepositoryAsync(localPath, token).ConfigureAwait(false);
                context.RepositoryAvailable = true;
                return;
            }

            if (Hosts != null)
            {
                VesselHostResolution resolution = await Hosts.TryResolveAsync(context.Vessel, null, token).ConfigureAwait(false);
                if (resolution.Host != null && resolution.Host.IsHarbor)
                {
                    context.Host = resolution.Host;
                    context.Git = resolution.Host.Git;
                    context.EvaluatedPath = resolution.Host.WorkingDirectory;
                    context.IsBare = false;
                    context.RepositoryAvailable = true;
                    return;
                }

                if (resolution.Message != null)
                    _Logging.Debug(_Header + "no checkout of " + context.Vessel.Id + " to evaluate: " + resolution.Message);
            }

            context.EvaluatedPath = !String.IsNullOrWhiteSpace(workingDirectory) ? workingDirectory : localPath;
            context.RepositoryAvailable = false;
        }

        private async Task FetchAsync(VesselHealthContext context, CancellationToken token)
        {
            using Activity? span = VesselHealthTelemetry.StartSpan("stage:Fetch");
            try
            {
                if (context.IsBare) await context.Git.FetchAsync(context.EvaluatedPath!, token).ConfigureAwait(false);
                else await context.Git.FetchRemotesAsync(context.EvaluatedPath!, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                context.FetchFailed = true;
                _Logging.Debug(_Header + "fetch failed for " + context.Vessel.Id + ": " + ex.Message);
            }
            catch (TimeoutException ex)
            {
                context.FetchFailed = true;
                _Logging.Debug(_Header + "fetch timed out for " + context.Vessel.Id + ": " + ex.Message);
            }
        }

        private async Task<VesselHealthCriterionResult> RunCriterionAsync(IVesselHealthCriterion criterion, VesselHealthContext context, CancellationToken token)
        {
            string code = criterion.Code.ToString();
            using Activity? span = VesselHealthTelemetry.StartSpan("stage:" + code);
            Stopwatch watch = Stopwatch.StartNew();
            VesselHealthCriterionResult result;
            try
            {
                if (criterion.RequiresRepository && !context.RepositoryAvailable)
                {
                    result = new VesselHealthCriterionResult(VesselHealthStatusEnum.Unknown, VesselHealthDetailCodes.RepositoryUnavailable);
                }
                else if (!await criterion.AppliesAsync(context, token).ConfigureAwait(false))
                {
                    result = new VesselHealthCriterionResult(
                        VesselHealthStatusEnum.NotApplicable,
                        context.IsBare ? VesselHealthDetailCodes.BareRepository : VesselHealthDetailCodes.NotApplicable);
                }
                else
                {
                    result = await criterion.EvaluateAsync(context, token).ConfigureAwait(false)
                        ?? new VesselHealthCriterionResult(VesselHealthStatusEnum.Unknown, VesselHealthDetailCodes.EvaluationError);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "criterion " + code + " failed for vessel " + context.Vessel.Id + ": " + ex.Message);
                result = new VesselHealthCriterionResult(VesselHealthStatusEnum.Unknown, VesselHealthDetailCodes.EvaluationError);
            }

            watch.Stop();
            VesselHealthTelemetry.RecordCriterionDuration(code, watch.Elapsed.TotalSeconds);
            span?.SetTag("armada.health.status", result.Status.ToString());
            return result;
        }

        #endregion
    }
}
