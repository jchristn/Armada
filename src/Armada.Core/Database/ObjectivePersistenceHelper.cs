namespace Armada.Core.Database
{
    using System;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Shared JSON and enum helpers for normalized objective persistence.
    /// </summary>
    internal static class ObjectivePersistenceHelper
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        internal static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _JsonOptions);
        }

        internal static List<string> DeserializeList(object? value)
        {
            string? json = value?.ToString();
            if (String.IsNullOrWhiteSpace(json))
                return new List<string>();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json, _JsonOptions) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        internal static List<SelectedPlaybook> DeserializePlaybooks(object? value)
        {
            string? json = value?.ToString();
            if (String.IsNullOrWhiteSpace(json))
                return new List<SelectedPlaybook>();

            try
            {
                return JsonSerializer.Deserialize<List<SelectedPlaybook>>(json, _JsonOptions) ?? new List<SelectedPlaybook>();
            }
            catch
            {
                return new List<SelectedPlaybook>();
            }
        }

        internal static TEnum ParseEnum<TEnum>(object? value, TEnum fallback) where TEnum : struct
        {
            string? raw = value?.ToString();
            if (String.IsNullOrWhiteSpace(raw))
                return fallback;

            return Enum.TryParse<TEnum>(raw, true, out TEnum parsed) ? parsed : fallback;
        }

        internal static ObjectiveRefinementSessionStatusEnum ParseRefinementSessionStatus(object? value, string sessionId)
        {
            string? raw = value?.ToString();
            if (String.IsNullOrWhiteSpace(raw))
                return ObjectiveRefinementSessionStatusEnum.Created;

            if (Enum.TryParse<ObjectiveRefinementSessionStatusEnum>(raw, true, out ObjectiveRefinementSessionStatusEnum parsed))
                return parsed;

            throw new InvalidOperationException("Invalid status '" + raw + "' for objective refinement session '" + sessionId + "'.");
        }
    }
}
