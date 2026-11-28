namespace Armada.Core.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The latest discovered context window per local endpoint and model on this Harbor (RS-7.8, RS-12.19). Thread-safe;
    /// keeps at most <see cref="MaxEntries"/> entries, dropping the oldest.
    /// </summary>
    public class HarborDiscoveredContextLog
    {
        #region Public-Members

        /// <summary>
        /// Most entries kept.
        /// </summary>
        public const int MaxEntries = 32;

        /// <summary>
        /// Raised after an entry is recorded. May be called on any thread.
        /// </summary>
        public event Action? Changed;

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly Dictionary<string, HarborDiscoveredContext> _Entries = new Dictionary<string, HarborDiscoveredContext>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborDiscoveredContextLog()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record what was learned about an endpoint and model, replacing the earlier entry for the same pair.
        /// </summary>
        /// <param name="entry">Entry to record.</param>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public void Record(HarborDiscoveredContext entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            lock (_Lock)
            {
                _Entries[Key(entry)] = entry;
                while (_Entries.Count > MaxEntries)
                {
                    string oldest = _Entries.OrderBy(e => e.Value.DiscoveredUtc).First().Key;
                    _Entries.Remove(oldest);
                }
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// The recorded entries, ordered by endpoint name then model.
        /// </summary>
        /// <returns>A copy of the entries.</returns>
        public List<HarborDiscoveredContext> Snapshot()
        {
            lock (_Lock)
            {
                return _Entries.Values
                    .OrderBy(e => e.EndpointName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Model ?? String.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>
        /// One line per entry for display, such as "local-qwen (qwen3.8): 131,072 tokens, from ollama /api/show".
        /// </summary>
        /// <param name="entries">Entries to describe.</param>
        /// <returns>The lines; empty when there are no entries.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entries is null.</exception>
        public static List<string> Describe(IEnumerable<HarborDiscoveredContext> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            List<string> lines = new List<string>();
            foreach (HarborDiscoveredContext entry in entries)
            {
                if (entry == null) continue;
                string name = entry.EndpointName + (String.IsNullOrWhiteSpace(entry.Model) ? "" : " (" + entry.Model + ")");
                if (entry.ContextLength.HasValue)
                    lines.Add(name + ": " + entry.ContextLength.Value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " tokens"
                        + (String.IsNullOrWhiteSpace(entry.Source) ? "" : ", from " + entry.Source));
                else
                    lines.Add(name + ": not reported" + (String.IsNullOrWhiteSpace(entry.Reason) ? "" : " (" + entry.Reason + ")"));
            }

            return lines;
        }

        #endregion

        #region Private-Methods

        private static string Key(HarborDiscoveredContext entry)
        {
            return (entry.EndpointName ?? String.Empty) + "\n" + (entry.Model ?? String.Empty);
        }

        #endregion
    }
}
