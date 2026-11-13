namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// Row encoding shared by every provider's runbook_executions table: the full execution is stored as JSON in the payload
    /// column, and the filterable fields are stored in their own columns (search_text holds the lower-cased searchable
    /// text). Thread safe.
    /// </summary>
    internal static class RunbookExecutionRowCodec
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Serialize an execution to its payload column.
        /// </summary>
        internal static string Serialize(RunbookExecution execution)
        {
            return JsonSerializer.Serialize(execution, _Json);
        }

        /// <summary>
        /// Deserialize a payload column, keeping the row's id; null when the payload is empty or unreadable.
        /// </summary>
        internal static RunbookExecution? Deserialize(string id, string? payload)
        {
            if (String.IsNullOrWhiteSpace(payload)) return null;
            try
            {
                RunbookExecution? execution = JsonSerializer.Deserialize<RunbookExecution>(payload, _Json);
                if (execution != null) execution.Id = id;
                return execution;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Lower-cased text the free-text filter searches: title, environment name, and notes.
        /// </summary>
        internal static string SearchText(RunbookExecution execution)
        {
            List<string> parts = new List<string>();
            Add(parts, execution.Title);
            Add(parts, execution.EnvironmentName);
            Add(parts, execution.Notes);
            return String.Join("\n", parts).ToLowerInvariant();
        }

        #endregion

        #region Private-Methods

        private static void Add(List<string> parts, string? value)
        {
            if (!String.IsNullOrWhiteSpace(value)) parts.Add(value);
        }

        #endregion
    }
}
