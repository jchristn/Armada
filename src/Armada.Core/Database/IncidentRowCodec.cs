namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// Row encoding shared by every provider's incidents table: the full incident is stored as JSON in the payload
    /// column, and the filterable fields are stored in their own columns (search_text holds the lower-cased searchable
    /// text). Thread safe.
    /// </summary>
    internal static class IncidentRowCodec
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
        /// Serialize an incident to its payload column.
        /// </summary>
        internal static string Serialize(Incident incident)
        {
            return JsonSerializer.Serialize(incident, _Json);
        }

        /// <summary>
        /// Deserialize a payload column, keeping the row's id; null when the payload is empty or unreadable.
        /// </summary>
        internal static Incident? Deserialize(string id, string? payload)
        {
            if (String.IsNullOrWhiteSpace(payload)) return null;
            try
            {
                Incident? incident = JsonSerializer.Deserialize<Incident>(payload, _Json);
                if (incident != null) incident.Id = id;
                return incident;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Lower-cased text the free-text filter searches: title, summary, impact, root cause, recovery notes,
        /// postmortem, and environment name.
        /// </summary>
        internal static string SearchText(Incident incident)
        {
            List<string> parts = new List<string>();
            Add(parts, incident.Title);
            Add(parts, incident.Summary);
            Add(parts, incident.Impact);
            Add(parts, incident.RootCause);
            Add(parts, incident.RecoveryNotes);
            Add(parts, incident.Postmortem);
            Add(parts, incident.EnvironmentName);
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
