namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Models;

    /// <summary>
    /// Shared JSON and enum helpers for normalized objective persistence.
    /// Empty or null text keeps the current empty list or enum fallback.
    /// Non-empty text that cannot be decoded fails the read and names the objective and field.
    /// Callers that do not name an objective keep the enum fallback.
    /// </summary>
    internal static class ObjectivePersistenceHelper
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        internal static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _JsonOptions);
        }

        internal static List<string> DeserializeList(object? value, string objectiveId, string fieldName)
        {
            if (objectiveId == null) throw new ArgumentNullException(nameof(objectiveId));
            if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));

            string? json = value?.ToString();
            if (String.IsNullOrWhiteSpace(json))
                return new List<string>();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json, _JsonOptions) ?? new List<string>();
            }
            catch (JsonException exception)
            {
                throw new StoredObjectiveDataException(objectiveId, fieldName, exception);
            }
        }

        internal static List<SelectedPlaybook> DeserializePlaybooks(object? value, string objectiveId, string fieldName)
        {
            if (objectiveId == null) throw new ArgumentNullException(nameof(objectiveId));
            if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));

            string? json = value?.ToString();
            if (String.IsNullOrWhiteSpace(json))
                return new List<SelectedPlaybook>();

            try
            {
                return JsonSerializer.Deserialize<List<SelectedPlaybook>>(json, _JsonOptions) ?? new List<SelectedPlaybook>();
            }
            catch (JsonException exception)
            {
                throw new StoredObjectiveDataException(objectiveId, fieldName, exception);
            }
        }

        internal static TEnum ParseEnum<TEnum>(object? value, TEnum fallback) where TEnum : struct
        {
            string? raw = value?.ToString();
            if (String.IsNullOrWhiteSpace(raw))
                return fallback;

            return Enum.TryParse<TEnum>(raw, true, out TEnum parsed) ? parsed : fallback;
        }

        internal static TEnum ParseEnum<TEnum>(object? value, TEnum fallback, string objectiveId, string fieldName) where TEnum : struct
        {
            if (objectiveId == null) throw new ArgumentNullException(nameof(objectiveId));
            if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));

            string? raw = value?.ToString();
            if (String.IsNullOrWhiteSpace(raw))
                return fallback;

            if (Enum.TryParse<TEnum>(raw, true, out TEnum parsed))
                return parsed;

            throw new StoredObjectiveDataException(objectiveId, fieldName);
        }
    }
}
