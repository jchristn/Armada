namespace Armada.Core.BlackBox
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON Schema (draft 2020-12) node, typed, as written by <see cref="BlackBoxSchemaGenerator"/> to
    /// docs/blackbox-record.schema.json. Only the keywords the generator uses; null members are omitted.
    /// </summary>
    public class BlackBoxJsonSchemaNode
    {
        /// <summary>$schema (root only).</summary>
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; } = null;

        /// <summary>$id (root only).</summary>
        [JsonPropertyName("$id")]
        public string? Id { get; set; } = null;

        /// <summary>$ref.</summary>
        [JsonPropertyName("$ref")]
        public string? Ref { get; set; } = null;

        /// <summary>title.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>description (from the C# XML documentation).</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; } = null;

        /// <summary>type, always as an array (for example ["string", "null"]).</summary>
        [JsonPropertyName("type")]
        public List<string>? Type { get; set; } = null;

        /// <summary>format (date-time for timestamps).</summary>
        [JsonPropertyName("format")]
        public string? Format { get; set; } = null;

        /// <summary>enum values (names; null when nullable).</summary>
        [JsonPropertyName("enum")]
        public List<string?>? Enum { get; set; } = null;

        /// <summary>anyOf (a nullable $ref).</summary>
        [JsonPropertyName("anyOf")]
        public List<BlackBoxJsonSchemaNode>? AnyOf { get; set; } = null;

        /// <summary>properties, in declaration order.</summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, BlackBoxJsonSchemaNode>? Properties { get; set; } = null;

        /// <summary>required property names.</summary>
        [JsonPropertyName("required")]
        public List<string>? Required { get; set; } = null;

        /// <summary>items of an array.</summary>
        [JsonPropertyName("items")]
        public BlackBoxJsonSchemaNode? Items { get; set; } = null;

        /// <summary>additionalProperties of a dictionary.</summary>
        [JsonPropertyName("additionalProperties")]
        public BlackBoxJsonSchemaNode? AdditionalProperties { get; set; } = null;

        /// <summary>maxItems ([CollectionBound] limit).</summary>
        [JsonPropertyName("maxItems")]
        public int? MaxItems { get; set; } = null;

        /// <summary>maxProperties ([CollectionBound] limit of a dictionary).</summary>
        [JsonPropertyName("maxProperties")]
        public int? MaxProperties { get; set; } = null;

        /// <summary>maxLength (string cap enforced by BlackBoxRecordBounder).</summary>
        [JsonPropertyName("maxLength")]
        public int? MaxLength { get; set; } = null;

        /// <summary>Armada record schema version (root only).</summary>
        [JsonPropertyName("x-armada-schemaVersion")]
        public int? ArmadaSchemaVersion { get; set; } = null;

        /// <summary>$defs (root only), sorted by name.</summary>
        [JsonPropertyName("$defs")]
        public SortedDictionary<string, BlackBoxJsonSchemaNode>? Defs { get; set; } = null;
    }
}
