namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Pages a view made of several sources, each read in its own order and placed one after the other (for example the
    /// inbox's kinds, or InProgress voyages before Open ones), without loading any source in full. Each source pages
    /// itself (SQL LIMIT/OFFSET with COUNT, or an external API page); a page of the view reads each source's count with
    /// a one-row page and then at most two pages from each source it overlaps. Thread safe: stateless.
    /// </summary>
    public static class SectionedPaging
    {
        #region Public-Methods

        /// <summary>
        /// Read one page of the sections taken in order.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="query">Paging query (page number and size).</param>
        /// <param name="sections">Sections in view order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page, with TotalRecords summed over the sections.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<EnumerationResult<T>> PageAsync<T>(EnumerationQuery query, IList<PagedSection<T>> sections, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (sections == null) throw new ArgumentNullException(nameof(sections));

            long[] counts = new long[sections.Count];
            long total = 0;
            for (int i = 0; i < sections.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<T> probe = await sections[i].Fetch(1, CollectionLimits.MinPageSize, token).ConfigureAwait(false);
                counts[i] = Math.Max(0, probe.TotalRecords);
                total += counts[i];
            }

            long start = query.Offset;
            long end = start + query.PageSize;
            List<T> objects = new List<T>();
            long running = 0;
            for (int i = 0; i < sections.Count && running < end; i++)
            {
                long sectionStart = running;
                long sectionEnd = running + counts[i];
                running = sectionEnd;
                long from = Math.Max(start, sectionStart);
                long to = Math.Min(end, sectionEnd);
                if (to <= from) continue;
                List<T> rows = await ReadWindowAsync(sections[i].Fetch, from - sectionStart, (int)(to - from), Math.Min(query.PageSize, sections[i].MaxPageSize), token).ConfigureAwait(false);
                objects.AddRange(rows);
            }

            return EnumerationResult<T>.Create(query, objects, total);
        }

        /// <summary>
        /// Read the items at positions <paramref name="offset"/> to <paramref name="offset"/> + <paramref name="count"/>
        /// of a source that pages itself, using pages of <paramref name="pageSize"/>. Reads at most
        /// ceil(count / pageSize) + 1 pages. Returns fewer items when the source ends first.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="fetch">Page reader.</param>
        /// <param name="offset">Zero-based position of the first item.</param>
        /// <param name="count">Number of items to read. Minimum 0.</param>
        /// <param name="pageSize">Page size used to read the source. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The items in source order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fetch"/> is null.</exception>
        public static async Task<List<T>> ReadWindowAsync<T>(PageFetchDelegate<T> fetch, long offset, int count, int pageSize, CancellationToken token = default)
        {
            if (fetch == null) throw new ArgumentNullException(nameof(fetch));
            List<T> results = new List<T>();
            if (count <= 0) return results;
            if (offset < 0) offset = 0;
            if (pageSize < 1) pageSize = 1;

            long position = offset;
            long end = offset + count;
            while (position < end)
            {
                token.ThrowIfCancellationRequested();
                int pageNumber = (int)(position / pageSize) + 1;
                int skip = (int)(position % pageSize);
                EnumerationResult<T> page = await fetch(pageNumber, pageSize, token).ConfigureAwait(false);
                List<T> items = page.Objects ?? new List<T>();
                if (skip >= items.Count) break;
                int take = (int)Math.Min(items.Count - skip, end - position);
                results.AddRange(items.GetRange(skip, take));
                position += take;
                if (items.Count < pageSize) break;
            }

            return results;
        }

        #endregion
    }
}
