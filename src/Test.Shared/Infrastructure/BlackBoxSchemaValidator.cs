namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Armada.Core.BlackBox;

    /// <summary>
    /// Minimal JSON Schema validator for the keywords <see cref="BlackBoxSchemaGenerator"/> emits ($ref into $defs,
    /// anyOf, type, enum, properties, required, items, additionalProperties, maxItems, maxProperties, maxLength and the
    /// date-time format). Collects every error with its JSON path.
    /// </summary>
    public static class BlackBoxSchemaValidator
    {
        /// <summary>
        /// Validate an instance against the root schema.
        /// </summary>
        /// <param name="instance">Instance.</param>
        /// <param name="root">Root schema.</param>
        /// <returns>Errors (empty when valid).</returns>
        public static List<string> Validate(JsonNode? instance, BlackBoxJsonSchemaNode root)
        {
            List<string> errors = new List<string>();
            Check(instance, root, root, "$", errors);
            return errors;
        }

        /// <summary>
        /// Validate an instance against a named $defs entry.
        /// </summary>
        /// <param name="instance">Instance.</param>
        /// <param name="root">Root schema.</param>
        /// <param name="defName">Definition name.</param>
        /// <returns>Errors.</returns>
        public static List<string> ValidateDef(JsonNode? instance, BlackBoxJsonSchemaNode root, string defName)
        {
            List<string> errors = new List<string>();
            Check(instance, root.Defs![defName], root, "$", errors);
            return errors;
        }

        private static void Check(JsonNode? value, BlackBoxJsonSchemaNode schema, BlackBoxJsonSchemaNode root, string path, List<string> errors)
        {
            if (schema.Ref != null)
            {
                string name = schema.Ref.Substring("#/$defs/".Length);
                if (root.Defs == null || !root.Defs.TryGetValue(name, out BlackBoxJsonSchemaNode? target)) { errors.Add(path + ": unresolved $ref " + schema.Ref); return; }
                Check(value, target, root, path, errors);
                return;
            }
            if (schema.AnyOf != null)
            {
                foreach (BlackBoxJsonSchemaNode option in schema.AnyOf)
                {
                    List<string> attempt = new List<string>();
                    Check(value, option, root, path, attempt);
                    if (attempt.Count == 0) return;
                }
                errors.Add(path + ": matches no anyOf option");
                return;
            }
            string kind = KindOf(value);
            if (schema.Type != null && !schema.Type.Contains(kind) && !(kind == "integer" && schema.Type.Contains("number")))
            {
                errors.Add(path + ": expected " + String.Join("|", schema.Type) + " but got " + kind);
                return;
            }
            if (value == null) return;
            if (schema.Enum != null && kind == "string" && !schema.Enum.Contains(value.GetValue<string>())) errors.Add(path + ": " + value.GetValue<string>() + " is not an allowed value");
            if (kind == "string")
            {
                string text = value.GetValue<string>();
                if (schema.MaxLength.HasValue && text.Length > schema.MaxLength.Value) errors.Add(path + ": longer than " + schema.MaxLength.Value);
                if (schema.Format == "date-time" && !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime _)) errors.Add(path + ": not a date-time");
            }
            if (value is JsonArray array)
            {
                if (schema.MaxItems.HasValue && array.Count > schema.MaxItems.Value) errors.Add(path + ": more than " + schema.MaxItems.Value + " items");
                if (schema.Items != null) for (int i = 0; i < array.Count; i++) Check(array[i], schema.Items, root, path + "[" + i + "]", errors);
            }
            if (value is JsonObject obj)
            {
                if (schema.MaxProperties.HasValue && obj.Count > schema.MaxProperties.Value) errors.Add(path + ": more than " + schema.MaxProperties.Value + " properties");
                if (schema.Required != null) foreach (string required in schema.Required) if (!obj.ContainsKey(required)) errors.Add(path + ": missing required " + required);
                foreach (KeyValuePair<string, JsonNode?> kv in obj)
                {
                    if (schema.Properties != null && schema.Properties.TryGetValue(kv.Key, out BlackBoxJsonSchemaNode? prop)) Check(kv.Value, prop, root, path + "." + kv.Key, errors);
                    else if (schema.AdditionalProperties != null) Check(kv.Value, schema.AdditionalProperties, root, path + "." + kv.Key, errors);
                }
            }
        }

        private static string KindOf(JsonNode? value)
        {
            if (value == null) return "null";
            if (value is JsonObject) return "object";
            if (value is JsonArray) return "array";
            JsonValue v = value.AsValue();
            if (v.TryGetValue(out string? _)) return "string";
            if (v.TryGetValue(out bool _)) return "boolean";
            if (v.TryGetValue(out long _)) return "integer";
            return "number";
        }
    }
}
