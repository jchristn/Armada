namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// The single set of bounds for every collection a REST route or MCP tool accepts or returns (docs/ENUMERATION.md).
    /// Stored collections are read one page at a time through <see cref="EnumerationQuery"/> and
    /// <see cref="EnumerationResult{T}"/> (page size <see cref="MinPageSize"/> to <see cref="MaxPageSize"/>, default
    /// <see cref="DefaultPageSize"/>). Collections that are not stored rows (ids in a bulk request, lists stored on one
    /// entity, summary buckets) are bounded by the limits here and rejected with
    /// <see cref="CollectionLimitExceededException"/> when they exceed them; they are never truncated silently.
    /// Thread safe: the members are read-only.
    /// </summary>
    public static class CollectionLimits
    {
        #region Public-Members

        /// <summary>
        /// Smallest page size. A smaller requested size is normalized to this.
        /// </summary>
        public const int MinPageSize = 1;

        /// <summary>
        /// Largest page size. A larger requested size is normalized to this; the result's page size and
        /// TotalRecords say so.
        /// </summary>
        public const int MaxPageSize = 1000;

        /// <summary>
        /// Page size when the caller names none.
        /// </summary>
        public const int DefaultPageSize = 10;

        /// <summary>
        /// Most items one request may carry in a list of ids or keys (bulk delete, purge, reorder, cleanup keys). A
        /// result list derived from such a request (for example DeleteMultipleResult.Skipped) is bounded by it too.
        /// </summary>
        public const int MaxRequestItems = 1000;

        /// <summary>
        /// Most entries a configuration list stored on one entity may hold (patterns, tags, headers, stages,
        /// priorities, inputs, parameters, and similar). Writes with more are rejected.
        /// </summary>
        public const int MaxConfigItems = 256;

        /// <summary>
        /// Most ids a link list stored on one entity may hold (for example Objective.MissionIds or
        /// Release.VoyageIds). A write or link that would exceed it is rejected.
        /// </summary>
        public const int MaxLinkedIds = 1000;

        /// <summary>
        /// Most buckets one summary or history read may produce. A window and bucket size that would produce more is
        /// rejected; widen the bucket or narrow the window.
        /// </summary>
        public const int MaxSummaryBuckets = 1000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Throw when a collection holds more than <paramref name="limit"/> items. Null is treated as empty.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="items">Collection to check, or null.</param>
        /// <param name="field">Field name used in the error, for example Ids.</param>
        /// <param name="limit">Most items allowed.</param>
        /// <exception cref="CollectionLimitExceededException">Thrown when the collection holds more than the limit.</exception>
        public static void Require<T>(ICollection<T>? items, string field, int limit)
        {
            if (items != null && items.Count > limit) throw new CollectionLimitExceededException(field, limit, items.Count);
        }

        /// <summary>
        /// Throw when a dictionary holds more than <paramref name="limit"/> entries. Null is treated as empty.
        /// </summary>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TValue">Value type.</typeparam>
        /// <param name="items">Dictionary to check, or null.</param>
        /// <param name="field">Field name used in the error, for example Headers.</param>
        /// <param name="limit">Most entries allowed.</param>
        /// <exception cref="CollectionLimitExceededException">Thrown when the dictionary holds more than the limit.</exception>
        public static void RequireEntries<TKey, TValue>(IDictionary<TKey, TValue>? items, string field, int limit) where TKey : notnull
        {
            if (items != null && items.Count > limit) throw new CollectionLimitExceededException(field, limit, items.Count);
        }

        /// <summary>
        /// Throw when a count is above <paramref name="limit"/>. Use when the collection is produced rather than
        /// received, for example the number of buckets a window would produce.
        /// </summary>
        /// <param name="count">Item count.</param>
        /// <param name="field">Field name used in the error.</param>
        /// <param name="limit">Most items allowed.</param>
        /// <exception cref="CollectionLimitExceededException">Thrown when the count is above the limit.</exception>
        public static void RequireCount(long count, string field, int limit)
        {
            if (count > limit) throw new CollectionLimitExceededException(field, limit, count);
        }

        /// <summary>
        /// Throw when a request id or key list holds more than <see cref="MaxRequestItems"/> items.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="items">Collection to check, or null.</param>
        /// <param name="field">Field name used in the error, for example Ids.</param>
        /// <exception cref="CollectionLimitExceededException">Thrown when the collection holds too many items.</exception>
        public static void RequireRequestItems<T>(ICollection<T>? items, string field)
        {
            Require(items, field, MaxRequestItems);
        }

        /// <summary>
        /// Throw when a configuration list stored on an entity holds more than <see cref="MaxConfigItems"/> items.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="items">Collection to check, or null.</param>
        /// <param name="field">Field name used in the error, for example Tags.</param>
        /// <exception cref="CollectionLimitExceededException">Thrown when the collection holds too many items.</exception>
        public static void RequireConfigItems<T>(ICollection<T>? items, string field)
        {
            Require(items, field, MaxConfigItems);
        }

        /// <summary>
        /// Throw when a configuration dictionary stored on an entity holds more than <see cref="MaxConfigItems"/> entries.
        /// </summary>
        /// <typeparam name="TKey">Key type.</typeparam>
        /// <typeparam name="TValue">Value type.</typeparam>
        /// <param name="items">Dictionary to check, or null.</param>
        /// <param name="field">Field name used in the error, for example Headers.</param>
        /// <exception cref="CollectionLimitExceededException">Thrown when the dictionary holds too many entries.</exception>
        public static void RequireConfigEntries<TKey, TValue>(IDictionary<TKey, TValue>? items, string field) where TKey : notnull
        {
            RequireEntries(items, field, MaxConfigItems);
        }

        /// <summary>
        /// Throw when a link id list stored on an entity holds more than <see cref="MaxLinkedIds"/> ids.
        /// </summary>
        /// <param name="ids">Ids to check, or null.</param>
        /// <param name="field">Field name used in the error, for example MissionIds.</param>
        /// <exception cref="CollectionLimitExceededException">Thrown when the list holds too many ids.</exception>
        public static void RequireLinkedIds(ICollection<string>? ids, string field)
        {
            Require(ids, field, MaxLinkedIds);
        }

        /// <summary>
        /// Throw when a summary window divided into buckets of <paramref name="bucketSize"/> would produce more than
        /// <see cref="MaxSummaryBuckets"/> buckets.
        /// </summary>
        /// <param name="window">Length of the window.</param>
        /// <param name="bucketSize">Length of one bucket. Must be positive.</param>
        /// <param name="field">Field name used in the error, for example Buckets.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the bucket size is not positive.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when the window would produce too many buckets.</exception>
        public static void RequireBuckets(TimeSpan window, TimeSpan bucketSize, string field = "Buckets")
        {
            if (bucketSize <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bucketSize), "Bucket size must be positive.");
            if (window < TimeSpan.Zero) window = TimeSpan.Zero;
            long count = (long)Math.Floor(window.Ticks / (double)bucketSize.Ticks) + 1;
            if (count > MaxSummaryBuckets)
                throw new CollectionLimitExceededException(field, MaxSummaryBuckets, count,
                    "The window would produce " + count + " buckets; the most allowed is " + MaxSummaryBuckets + ". Use a larger bucket size or a shorter window.");
        }

        /// <summary>
        /// Page an in-memory list that is itself bounded (for example the children of one entity, already limited by
        /// <see cref="MaxConfigItems"/>), returning an <see cref="EnumerationResult{T}"/>. Stored rows must be paged
        /// in the data source instead.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="query">Paging query.</param>
        /// <param name="items">Every item, already ordered.</param>
        /// <returns>The requested page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        public static EnumerationResult<T> Page<T>(EnumerationQuery query, IEnumerable<T>? items)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return EnumerationResult<T>.Page(query, items == null ? new List<T>() : items.ToList());
        }

        #endregion
    }
}
