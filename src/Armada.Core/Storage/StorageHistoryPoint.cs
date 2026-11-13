namespace Armada.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Services;

    /// <summary>
    /// Storage HistoryPoint measurement contract. Each breakdown (categories, hosts, tables) keeps the largest
    /// <see cref="MaxBreakdownEntries"/> minus one entries and folds the rest into one <see cref="OtherKey"/> entry, so
    /// nothing is dropped and the totals still add up.
    /// </summary>
    public class StorageHistoryPoint
    {
        /// <summary>Most entries in one breakdown dictionary, including the folded <see cref="OtherKey"/> entry.</summary>
        public const int MaxBreakdownEntries = 25;

        /// <summary>Key of the entry that holds the sum of every breakdown entry beyond the largest ones.</summary>
        public const string OtherKey = "(other)";

        /// <summary>Sample completion time.</summary>
        public DateTime MeasuredUtc { get; set; } = DateTime.MinValue;

        /// <summary>Observed filesystem bytes across reported hosts.</summary>
        public long ObservedFileBytes { get; set; } = 0;

        /// <summary>Database allocation, counted separately.</summary>
        public long? DatabaseBytes { get; set; } = null;

        /// <summary>Observed bytes by category (largest categories, the rest folded into OtherKey).</summary>
        [CollectionBound(MaxBreakdownEntries, Reason = "Largest entries plus one folded (other) entry")]
        public Dictionary<string, long> Categories { get; set; } = new Dictionary<string, long>();

        /// <summary>Observed bytes by reported host (largest hosts, the rest folded into OtherKey).</summary>
        [CollectionBound(MaxBreakdownEntries, Reason = "Largest entries plus one folded (other) entry")]
        public Dictionary<string, long> Hosts { get; set; } = new Dictionary<string, long>();

        /// <summary>Database table allocation by schema and table (largest tables, the rest folded into OtherKey).</summary>
        [CollectionBound(MaxBreakdownEntries, Reason = "Largest entries plus one folded (other) entry")]
        public Dictionary<string, long> Tables { get; set; } = new Dictionary<string, long>();

        /// <summary>
        /// Keep the largest entries of a breakdown and fold the rest into one <see cref="OtherKey"/> entry, so the
        /// result has at most <see cref="MaxBreakdownEntries"/> entries and the same total.
        /// </summary>
        /// <param name="values">Every entry.</param>
        /// <returns>The bounded breakdown.</returns>
        /// <exception cref="ArgumentNullException">Thrown when values is null.</exception>
        public static Dictionary<string, long> Fold(Dictionary<string, long> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Count <= MaxBreakdownEntries) return new Dictionary<string, long>(values);
            List<KeyValuePair<string, long>> ordered = values
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();
            Dictionary<string, long> folded = new Dictionary<string, long>();
            foreach (KeyValuePair<string, long> kv in ordered.Take(MaxBreakdownEntries - 1)) folded[kv.Key] = kv.Value;
            folded[OtherKey] = ordered.Skip(MaxBreakdownEntries - 1).Sum(kv => kv.Value);
            return folded;
        }

    }
}
