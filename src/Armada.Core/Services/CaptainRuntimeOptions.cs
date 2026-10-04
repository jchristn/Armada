namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// Helper methods for serializing and deserializing runtime-specific captain options.
    /// </summary>
    public static class CaptainRuntimeOptions
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a runtime options payload to JSON.
        /// </summary>
        public static string? Serialize<T>(T? value) where T : class
        {
            if (value == null) return null;
            return JsonSerializer.Serialize(value, _SerializerOptions);
        }

        /// <summary>
        /// Deserialize a runtime options payload from JSON.
        /// </summary>
        public static T? Deserialize<T>(string? json) where T : class
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<T>(json, _SerializerOptions);
        }

        /// <summary>
        /// Retrieve typed Mux options for a captain.
        /// Returns null when the captain is null or has no runtime options payload.
        /// </summary>
        public static MuxCaptainOptions? GetMuxOptions(Captain? captain)
        {
            if (captain == null) return null;
            return Deserialize<MuxCaptainOptions>(captain.RuntimeOptionsJson);
        }

        /// <summary>
        /// The explicit <c>autoApprove</c> value in a runtime options payload, or null when absent or unreadable.
        /// </summary>
        /// <param name="json">Runtime options JSON.</param>
        /// <returns>Explicit value or null.</returns>
        public static bool? GetExplicitAutoApprove(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return Deserialize<CaptainApprovalOptions>(json)?.AutoApprove;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Set or remove the <c>autoApprove</c> key in a runtime options payload, keeping every other key.
        /// </summary>
        /// <param name="json">Existing runtime options JSON, or null.</param>
        /// <param name="autoApprove">Value to set, or null to remove the key.</param>
        /// <returns>Updated JSON, or null when the result is empty.</returns>
        public static string? WithAutoApprove(string? json, bool? autoApprove)
        {
            Dictionary<string, JsonElement> values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (!String.IsNullOrWhiteSpace(json))
            {
                try
                {
                    values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _SerializerOptions) ?? values;
                }
                catch (JsonException)
                {
                    values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                }
            }

            values.Remove("autoApprove");
            values.Remove("AutoApprove");
            if (autoApprove.HasValue) values["autoApprove"] = JsonSerializer.SerializeToElement(autoApprove.Value);
            if (values.Count == 0) return null;
            return JsonSerializer.Serialize(values, _SerializerOptions);
        }

        /// <summary>
        /// Whether a CLI captain runs with its runtime's auto-approve or permission-bypass flag (see
        /// <see cref="CaptainApprovalOptions.AutoApprove"/>). True unless the captain's runtime options set
        /// <c>autoApprove</c> to false; unreadable options keep the default.
        /// </summary>
        /// <param name="captain">Captain, or null.</param>
        /// <returns>True when auto-approve flags are used.</returns>
        public static bool GetAutoApprove(Captain? captain)
        {
            if (captain == null || String.IsNullOrWhiteSpace(captain.RuntimeOptionsJson)) return true;
            try
            {
                CaptainApprovalOptions? options = Deserialize<CaptainApprovalOptions>(captain.RuntimeOptionsJson);
                return options?.AutoApprove ?? true;
            }
            catch (JsonException)
            {
                return true;
            }
        }

        #endregion
    }
}
