namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// Serializes the applied-action audit record of a model alert (model_alerts.actions_json) for every provider.
    /// </summary>
    public static class ModelAlertActionsJson
    {
        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Serialize the records; null or empty becomes null.
        /// </summary>
        /// <param name="actions">Records.</param>
        /// <returns>JSON, or null.</returns>
        public static string? Serialize(List<ModelAlertActionRecord>? actions)
        {
            if (actions == null || actions.Count == 0) return null;
            return JsonSerializer.Serialize(actions, _Options);
        }

        /// <summary>
        /// Deserialize the records; null, empty or unreadable JSON becomes an empty list.
        /// </summary>
        /// <param name="json">JSON, or null.</param>
        /// <returns>The records.</returns>
        public static List<ModelAlertActionRecord> Deserialize(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new List<ModelAlertActionRecord>();
            try
            {
                return JsonSerializer.Deserialize<List<ModelAlertActionRecord>>(json, _Options) ?? new List<ModelAlertActionRecord>();
            }
            catch (JsonException)
            {
                return new List<ModelAlertActionRecord>();
            }
        }
    }
}
