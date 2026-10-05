namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// Reads <see cref="AskQuickActionList"/> from a JSON array of actions or from an object wrapper with a
    /// QuickActions, Actions, or Objects array. Null reads as an empty list; any other token is a
    /// <see cref="JsonException"/>. Writes a bare array.
    /// </summary>
    public class AskQuickActionListConverter : JsonConverter<AskQuickActionList>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override AskQuickActionList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            AskQuickActionList result = new AskQuickActionList();
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return result;
                case JsonTokenType.StartArray:
                    result.Actions = JsonSerializer.Deserialize<List<AskQuickAction>>(ref reader, options) ?? new List<AskQuickAction>();
                    return result;
                case JsonTokenType.StartObject:
                    AskQuickActionEnvelope? envelope = JsonSerializer.Deserialize<AskQuickActionEnvelope>(ref reader, options);
                    result.Actions = envelope?.QuickActions ?? envelope?.Actions ?? envelope?.Objects ?? new List<AskQuickAction>();
                    return result;
                default:
                    throw new JsonException("Quick actions must be an array or an object wrapper.");
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, AskQuickActionList value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value?.Actions ?? new List<AskQuickAction>(), options);
        }

        #endregion
    }
}
