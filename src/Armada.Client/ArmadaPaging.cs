namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Helpers for walking paged <see cref="EnumerationResult{T}"/> endpoints. Thread-safe (stateless).
    /// </summary>
    public static class ArmadaPaging
    {
        #region Public-Members

        /// <summary>
        /// Default cap on pages read by <see cref="ReadAllAsync{T}"/> (100), so a runaway server cannot loop forever.
        /// </summary>
        public const int DefaultMaxPages = 100;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stream every object across pages, fetching page N+1 only after page N is consumed.
        /// </summary>
        /// <typeparam name="T">Object type.</typeparam>
        /// <param name="fetchPage">Fetches a 1-based page.</param>
        /// <param name="maxPages">Maximum pages to read (1..10000). Default 100.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The objects.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fetchPage"/> is null.</exception>
        public static async IAsyncEnumerable<T> StreamAsync<T>(
            Func<int, CancellationToken, Task<EnumerationResult<T>?>> fetchPage,
            int maxPages = DefaultMaxPages,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            if (fetchPage == null) throw new ArgumentNullException(nameof(fetchPage));
            int cap = Math.Clamp(maxPages, 1, 10000);
            for (int page = 1; page <= cap; page++)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<T>? result = await fetchPage(page, token).ConfigureAwait(false);
                if (result == null || result.Objects == null || result.Objects.Count == 0) yield break;
                foreach (T item in result.Objects) yield return item;
                if (result.TotalPages > 0 && page >= result.TotalPages) yield break;
                if (result.TotalPages <= 0 && result.Objects.Count < Math.Max(1, result.PageSize)) yield break;
            }
        }

        /// <summary>
        /// Read every object across pages into a list.
        /// </summary>
        /// <typeparam name="T">Object type.</typeparam>
        /// <param name="fetchPage">Fetches a 1-based page.</param>
        /// <param name="maxPages">Maximum pages to read (1..10000). Default 100.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All objects. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fetchPage"/> is null.</exception>
        public static async Task<List<T>> ReadAllAsync<T>(
            Func<int, CancellationToken, Task<EnumerationResult<T>?>> fetchPage,
            int maxPages = DefaultMaxPages,
            CancellationToken token = default)
        {
            List<T> all = new List<T>();
            await foreach (T item in StreamAsync(fetchPage, maxPages, token).ConfigureAwait(false))
            {
                all.Add(item);
            }

            return all;
        }

        /// <summary>
        /// Describe the visible window of a page for paging bars ("Showing 1-25 of 248. Page 1 of 10.").
        /// </summary>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Page size.</param>
        /// <param name="totalRecords">Total records.</param>
        /// <returns>The window.</returns>
        public static PageWindow Window(int pageNumber, int pageSize, long totalRecords)
        {
            return new PageWindow(pageNumber, pageSize, totalRecords);
        }

        #endregion
    }
}
