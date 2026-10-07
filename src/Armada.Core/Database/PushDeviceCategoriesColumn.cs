namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;

    /// <summary>
    /// Storage format of the push_devices.categories column: comma-separated <see cref="PushCategoryEnum"/> names. An
    /// empty string stores an empty list (a muted device); unknown names are ignored when read.
    /// </summary>
    public static class PushDeviceCategoriesColumn
    {
        #region Public-Methods

        /// <summary>
        /// Format categories for storage.
        /// </summary>
        /// <param name="categories">Categories, or null.</param>
        /// <returns>The column value (never null).</returns>
        public static string Format(List<PushCategoryEnum>? categories)
        {
            if (categories == null || categories.Count == 0) return String.Empty;
            return String.Join(",", categories.Distinct().Select(c => c.ToString()));
        }

        /// <summary>
        /// Parse a stored column value.
        /// </summary>
        /// <param name="value">Column value, or null.</param>
        /// <returns>The categories (never null).</returns>
        public static List<PushCategoryEnum> Parse(string? value)
        {
            List<PushCategoryEnum> result = new List<PushCategoryEnum>();
            if (String.IsNullOrWhiteSpace(value)) return result;
            foreach (string part in value!.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<PushCategoryEnum>(part.Trim(), true, out PushCategoryEnum parsed) && !result.Contains(parsed))
                    result.Add(parsed);
            }

            return result;
        }

        #endregion
    }
}
