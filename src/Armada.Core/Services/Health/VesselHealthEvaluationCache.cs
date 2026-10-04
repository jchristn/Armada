namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Values computed once per vessel evaluation and shared by every criterion: the repository file inventory and the
    /// local branch list. Criteria run sequentially within one vessel evaluation, so the cache is not thread-safe.
    /// </summary>
    public class VesselHealthEvaluationCache
    {
        #region Public-Members

        /// <summary>
        /// Free-form values shared between criteria (for example a parsed manifest). Never null.
        /// </summary>
        public Dictionary<string, object> Items { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        #endregion

        #region Private-Members

        private RepositoryFileInventory? _Inventory = null;
        private IReadOnlyList<BranchInfo>? _Branches = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthEvaluationCache()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the repository file inventory, building it on first use: a disk scan of a working directory, or the
        /// tracked files of a bare repository. Returns an empty inventory when the repository is unavailable.
        /// </summary>
        /// <param name="context">Evaluation context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The inventory.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public async Task<RepositoryFileInventory> GetInventoryAsync(VesselHealthContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (_Inventory != null) return _Inventory;

            if (String.IsNullOrEmpty(context.EvaluatedPath) || !context.RepositoryAvailable)
            {
                _Inventory = new RepositoryFileInventory();
            }
            else if (context.IsBare)
            {
                IReadOnlyList<string> tracked = await context.Git.ListTrackedFilesAsync(context.EvaluatedPath, token).ConfigureAwait(false);
                _Inventory = RepositoryFileInventory.FromTrackedFiles(tracked, context.ExcludedDirectoryNames);
            }
            else
            {
                string root = context.EvaluatedPath;
                _Inventory = await Task.Run(() => RepositoryFileInventory.FromDirectory(root, context.ExcludedDirectoryNames), token).ConfigureAwait(false);
            }

            return _Inventory;
        }

        /// <summary>
        /// Get the local branches (with ahead/behind against the vessel's default branch), listing them on first use.
        /// </summary>
        /// <param name="context">Evaluation context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The branches.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(VesselHealthContext context, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (_Branches != null) return _Branches;
            if (String.IsNullOrEmpty(context.EvaluatedPath) || !context.RepositoryAvailable)
            {
                _Branches = new List<BranchInfo>();
                return _Branches;
            }

            _Branches = await context.Git.ListBranchesAsync(context.EvaluatedPath, context.DefaultBranch, token).ConfigureAwait(false);
            return _Branches;
        }

        #endregion
    }
}
