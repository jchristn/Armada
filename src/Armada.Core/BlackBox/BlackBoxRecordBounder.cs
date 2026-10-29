namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Enforces the black box size budget on a record in place: every collection to its [CollectionBound] limit, every
    /// string to its cap (head kept, or tail kept for output tails), and the whole record.json to
    /// <see cref="BlackBoxLimits.MaxRecordBytes"/>. Each cut is recorded in the record's Truncation section with the original
    /// size; nothing is dropped silently. Thread-safe (stateless).
    /// </summary>
    public static class BlackBoxRecordBounder
    {
        #region Private-Members

        private static readonly Dictionary<string, int> _StringCaps = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "RawReason", BlackBoxLimits.MaxRawReasonChars },
            { "ExceptionMessage", BlackBoxLimits.MaxExceptionMessageChars },
            { "CommandOutputTail", BlackBoxLimits.MaxCommandOutputChars },
            { "GitStderrTail", BlackBoxLimits.MaxGitStderrChars },
            { "Summary", BlackBoxLimits.MaxSummaryChars },
            { "ReasonExcerpt", BlackBoxLimits.MaxReasonExcerptChars },
            { "Excerpt", BlackBoxLimits.MaxEvidenceExcerptChars },
            { "Message", BlackBoxLimits.MaxEvidenceExcerptChars },
            { "Title", BlackBoxLimits.MaxTitleChars },
            { "VoyageTitle", BlackBoxLimits.MaxTitleChars },
            { "MissionTitle", BlackBoxLimits.MaxTitleChars },
            { "ResolutionNote", BlackBoxLimits.MaxResolutionNoteChars }
        };

        private static readonly HashSet<string> _TailFields = new HashSet<string>(StringComparer.Ordinal) { "CommandOutputTail", "GitStderrTail" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Bound a record in place.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <exception cref="ArgumentNullException">Thrown when record is null.</exception>
        public static void Bound(BlackBoxRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            BlackBoxTruncation truncation = record.Truncation ?? new BlackBoxTruncation();
            record.Truncation = truncation;
            Walk(record, String.Empty, truncation, 1.0, 0);
            CapTruncation(truncation);

            double factor = 1.0;
            for (int pass = 0; pass < 6 && BlackBoxJson.SerializeRecord(record).Length > BlackBoxLimits.MaxRecordBytes; pass++)
            {
                factor /= 2;
                Walk(record, String.Empty, truncation, factor, 0);
                AddNote(truncation, "record.json exceeded " + BlackBoxLimits.MaxRecordBytes + " bytes; collections and long text were cut to " + (int)(factor * 100) + "% of their limits.");
                CapTruncation(truncation);
            }
        }

        /// <summary>
        /// The character cap the bounder applies to a record string property of this name (BlackBoxLimits; 2000 when the
        /// name has no specific cap). Strings inside record lists are capped at BlackBoxLimits.MaxPathChars.
        /// </summary>
        /// <param name="propertyName">Property name.</param>
        /// <returns>The cap in characters.</returns>
        public static int StringCap(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));
            return _StringCaps.TryGetValue(propertyName, out int cap) ? cap : BlackBoxLimits.MaxShortTextChars;
        }

        /// <summary>
        /// Add a capture note (bounded; further notes are dropped once the limit is reached, counted in FieldTotal).
        /// </summary>
        /// <param name="truncation">Truncation section.</param>
        /// <param name="note">Note.</param>
        /// <exception cref="ArgumentNullException">Thrown when truncation is null.</exception>
        public static void AddNote(BlackBoxTruncation truncation, string note)
        {
            if (truncation == null) throw new ArgumentNullException(nameof(truncation));
            if (String.IsNullOrWhiteSpace(note) || truncation.Notes.Contains(note)) return;
            if (truncation.Notes.Count < BlackBoxLimits.MaxNotes) truncation.Notes.Add(BlackBoxLimits.Clip(note, BlackBoxLimits.MaxShortTextChars)!);
        }

        /// <summary>
        /// Record a truncated field.
        /// </summary>
        /// <param name="truncation">Truncation section.</param>
        /// <param name="field">Field path.</param>
        /// <param name="originalSize">Original size.</param>
        /// <param name="keptSize">Kept size.</param>
        /// <param name="unit">chars, items or bytes.</param>
        /// <exception cref="ArgumentNullException">Thrown when truncation is null.</exception>
        public static void AddField(BlackBoxTruncation truncation, string field, long originalSize, long keptSize, string unit)
        {
            if (truncation == null) throw new ArgumentNullException(nameof(truncation));
            BlackBoxTruncatedField? existing = truncation.Fields.FirstOrDefault(f => f.Field == field);
            if (existing != null)
            {
                existing.KeptSize = keptSize;
                if (originalSize > existing.OriginalSize) existing.OriginalSize = originalSize;
                return;
            }
            truncation.FieldTotal++;
            if (truncation.Fields.Count < BlackBoxLimits.MaxTruncatedFields)
                truncation.Fields.Add(new BlackBoxTruncatedField { Field = field, OriginalSize = originalSize, KeptSize = keptSize, Unit = unit });
        }

        #endregion

        #region Private-Methods

        private static void Walk(object? node, string path, BlackBoxTruncation truncation, double factor, int depth)
        {
            if (node == null || depth > 12) return;
            if (node is BlackBoxTruncation) return;
            foreach (PropertyInfo prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
                object? value = prop.GetValue(node);
                if (value == null) continue;
                string field = path.Length == 0 ? prop.Name : path + "." + prop.Name;
                if (value is string text)
                {
                    int cap = Scaled(_StringCaps.TryGetValue(prop.Name, out int c) ? c : BlackBoxLimits.MaxShortTextChars, factor);
                    if (text.Length > cap && prop.CanWrite)
                    {
                        prop.SetValue(node, _TailFields.Contains(prop.Name) ? BlackBoxLimits.Tail(text, cap) : BlackBoxLimits.Clip(text, cap));
                        AddField(truncation, field, text.Length, cap, "chars");
                    }
                    continue;
                }
                CollectionBoundAttribute? bound = prop.GetCustomAttribute<CollectionBoundAttribute>();
                if (value is IList list)
                {
                    if (bound != null)
                    {
                        int limit = Scaled(bound.Limit, factor);
                        if (list.Count > limit)
                        {
                            int original = list.Count;
                            while (list.Count > limit) list.RemoveAt(list.Count - 1);
                            AddField(truncation, field, original, limit, "items");
                        }
                    }
                    for (int i = 0; i < list.Count; i++)
                    {
                        object? item = list[i];
                        if (item is string s)
                        {
                            int cap = Scaled(BlackBoxLimits.MaxPathChars, factor);
                            if (s.Length > cap)
                            {
                                list[i] = s.Substring(0, cap);
                                AddField(truncation, field + "[]", s.Length, cap, "chars");
                            }
                        }
                        else if (IsModel(item)) Walk(item, field + "[]", truncation, factor, depth + 1);
                    }
                    continue;
                }
                if (value is IDictionary dict)
                {
                    if (bound != null)
                    {
                        int limit = Scaled(bound.Limit, factor);
                        if (dict.Count > limit)
                        {
                            int original = dict.Count;
                            List<object> keys = dict.Keys.Cast<object>().Skip(limit).ToList();
                            foreach (object key in keys) dict.Remove(key);
                            AddField(truncation, field, original, limit, "items");
                        }
                    }
                    continue;
                }
                if (IsModel(value)) Walk(value, field, truncation, factor, depth + 1);
            }
        }

        private static int Scaled(int limit, double factor)
        {
            if (factor >= 1.0) return limit;
            int scaled = (int)(limit * factor);
            return scaled < 1 ? 1 : scaled;
        }

        private static bool IsModel(object? value)
        {
            if (value == null) return false;
            Type type = value.GetType();
            return type.IsClass && type != typeof(string) && type.Namespace != null && type.Namespace.StartsWith("Armada.Core", StringComparison.Ordinal);
        }

        private static void CapTruncation(BlackBoxTruncation truncation)
        {
            while (truncation.Fields.Count > BlackBoxLimits.MaxTruncatedFields) truncation.Fields.RemoveAt(truncation.Fields.Count - 1);
            while (truncation.Notes.Count > BlackBoxLimits.MaxNotes) truncation.Notes.RemoveAt(truncation.Notes.Count - 1);
        }

        #endregion
    }
}
