namespace Armada.Test.Database
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    internal static class DatabaseAssert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static T NotNull<T>(T? value, string message) where T : class
        {
            if (value == null) throw new Exception(message);
            return value;
        }

        public static void Equal<T>(T expected, T actual, string fieldName)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(fieldName + " mismatch: expected '" + expected + "' got '" + actual + "'");
            }
        }

        public static void HasPrefix(string value, string prefix, string fieldName)
        {
            if (String.IsNullOrEmpty(value) || !value.StartsWith(prefix))
            {
                throw new Exception(fieldName + " should start with '" + prefix + "' but was '" + value + "'");
            }
        }

        public static void EnumerationPage<T>(EnumerationResult<T> result, int expectedPageNumber, int expectedPageSize, long expectedTotalRecords, int expectedTotalPages, int expectedObjectCount)
        {
            Equal(expectedPageNumber, result.PageNumber, "PageNumber");
            Equal(expectedPageSize, result.PageSize, "PageSize");
            Equal(expectedTotalRecords, result.TotalRecords, "TotalRecords");
            Equal(expectedTotalPages, result.TotalPages, "TotalPages");
            Equal(expectedObjectCount, result.Objects.Count, "Objects.Count");
            True(result.TotalMs >= 0, "TotalMs should be >= 0");
        }

        public static void ContainsIds<T>(IEnumerable<T> objects, Func<T, string> idSelector, params string[] expectedIds)
        {
            HashSet<string> actualIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (T obj in objects) actualIds.Add(idSelector(obj));

            foreach (string id in expectedIds)
            {
                if (!actualIds.Contains(id))
                {
                    throw new Exception("Expected ID '" + id + "' not found in result set");
                }
            }
        }
    }
}
