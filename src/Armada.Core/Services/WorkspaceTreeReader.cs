namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Reads every entry of one checkout directory by walking its listing one bounded page at a time (for internal
    /// checks such as which files are at the checkout root). API responses return one page instead.
    /// Thread safe: stateless.
    /// </summary>
    public static class WorkspaceTreeReader
    {
        #region Public-Methods

        /// <summary>
        /// Read every direct child of one directory, page by page (1000 per page).
        /// </summary>
        /// <param name="files">Checkout file operations.</param>
        /// <param name="path">Relative directory path, or null for the checkout root.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Every entry, directories first, then files, by name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when files is null.</exception>
        public static async Task<List<WorkspaceTreeEntry>> ReadAllEntriesAsync(IVesselCheckoutFiles files, string? path, CancellationToken token = default)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            List<WorkspaceTreeEntry> entries = new List<WorkspaceTreeEntry>();
            int pageNumber = 1;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                EnumerationQuery page = new EnumerationQuery { PageNumber = pageNumber, PageSize = CollectionLimits.MaxPageSize };
                WorkspaceTreeResult tree = await files.GetTreeAsync(path, page, token).ConfigureAwait(false);
                entries.AddRange(tree.Entries);
                if (tree.Entries.Count == 0 || pageNumber >= tree.TotalPages) break;
                pageNumber++;
            }

            return entries;
        }

        #endregion
    }
}
