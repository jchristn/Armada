namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Everything a criterion needs to evaluate one vessel: the vessel, the path being evaluated (working directory or
    /// bare clone), the live settings, the health row being built, the previous row, and a per-evaluation cache.
    /// One context is created per vessel evaluation and used by one thread at a time.
    /// </summary>
    public class VesselHealthContext
    {
        #region Public-Members

        /// <summary>
        /// The vessel being evaluated. Never null.
        /// </summary>
        public Vessel Vessel { get; }

        /// <summary>
        /// Tenant that owns the vessel. Never null.
        /// </summary>
        public string TenantId { get; }

        /// <summary>
        /// Path being evaluated: the working directory when it exists and is a git repository, otherwise the bare clone
        /// (LocalPath), or null when neither is usable.
        /// </summary>
        public string? EvaluatedPath { get; set; } = null;

        /// <summary>
        /// Whether <see cref="EvaluatedPath"/> is a bare repository (no checkout).
        /// </summary>
        public bool IsBare { get; set; } = false;

        /// <summary>
        /// Whether <see cref="EvaluatedPath"/> is a usable git repository.
        /// </summary>
        public bool RepositoryAvailable { get; set; } = false;

        /// <summary>
        /// Whether git fetch was attempted and failed for this evaluation.
        /// </summary>
        public bool FetchFailed { get; set; } = false;

        /// <summary>
        /// Whether dependency and vulnerability checks run regardless of manifest freshness.
        /// </summary>
        public bool ForceDependencies { get; set; } = false;

        /// <summary>
        /// The vessel's default branch name (never empty; "main" when the vessel has none).
        /// </summary>
        public string DefaultBranch
        {
            get => String.IsNullOrWhiteSpace(Vessel.DefaultBranch) ? "main" : Vessel.DefaultBranch.Trim();
        }

        /// <summary>
        /// Live repository health settings. Never null.
        /// </summary>
        public RepositoryHealthSettings Settings { get; }

        /// <summary>
        /// Directory names skipped when scanning the repository (the import exclude list). Never null.
        /// </summary>
        public List<string> ExcludedDirectoryNames { get; }

        /// <summary>
        /// Git service. Never null.
        /// </summary>
        public IGitService Git { get; }

        /// <summary>
        /// The health row being built by this evaluation. Criteria write their measured columns here. Never null.
        /// </summary>
        public VesselHealth Health { get; }

        /// <summary>
        /// The previously stored health row, or null when the vessel was never evaluated.
        /// </summary>
        public VesselHealth? Previous { get; set; } = null;

        /// <summary>
        /// Evaluation timestamp in UTC (used for age calculations so one evaluation is internally consistent).
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Outdated dependencies found by the Dependencies criterion. Never null.
        /// </summary>
        public List<VesselDependency> OutdatedDependencies { get; } = new List<VesselDependency>();

        /// <summary>
        /// Vulnerable dependencies found by the Vulnerabilities criterion. Never null.
        /// </summary>
        public List<VesselDependency> VulnerableDependencies { get; } = new List<VesselDependency>();

        /// <summary>
        /// Shared per-evaluation cache. Never null.
        /// </summary>
        public VesselHealthEvaluationCache Cache { get; } = new VesselHealthEvaluationCache();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="vessel">Vessel being evaluated.</param>
        /// <param name="tenantId">Owning tenant.</param>
        /// <param name="settings">Live repository health settings.</param>
        /// <param name="excludedDirectoryNames">Directory names skipped when scanning, or null for none.</param>
        /// <param name="git">Git service.</param>
        /// <exception cref="ArgumentNullException">Thrown when vessel, tenantId, settings, or git is null.</exception>
        public VesselHealthContext(Vessel vessel, string tenantId, RepositoryHealthSettings settings, IEnumerable<string>? excludedDirectoryNames, IGitService git)
        {
            Vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            TenantId = tenantId;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Git = git ?? throw new ArgumentNullException(nameof(git));
            ExcludedDirectoryNames = excludedDirectoryNames == null ? new List<string>() : new List<string>(excludedDirectoryNames);
            Health = new VesselHealth();
            Health.TenantId = tenantId;
            Health.VesselId = vessel.Id;
        }

        #endregion
    }
}
