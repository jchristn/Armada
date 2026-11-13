namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Generates docs/blackbox-record.schema.json (JSON Schema draft 2020-12) from the C# models by reflection, so the
    /// documented schema cannot drift from the code: camelCase names, types, nullability, enum values, required
    /// (non-nullable) members, [CollectionBound] limits as maxItems or maxProperties, the record's string caps as
    /// maxLength, and descriptions from the XML documentation. The root is the record (record.json); $defs also holds the
    /// entry, summary, object, chunk, manifest, digest, note and event payload shapes. Output is deterministic and the
    /// same on every target framework. Thread-safe (stateless).
    /// </summary>
    public static class BlackBoxSchemaGenerator
    {
        #region Public-Members

        /// <summary>
        /// $id of the schema document.
        /// </summary>
        public const string SchemaId = "urn:armada:blackbox:record-schema";

        /// <summary>
        /// Repository-relative path of the generated file.
        /// </summary>
        public const string RelativePath = "docs/blackbox-record.schema.json";

        /// <summary>
        /// Shapes published in $defs besides the record graph.
        /// </summary>
        public static readonly Type[] Shapes = new Type[]
        {
            typeof(BlackBoxEntry), typeof(BlackBoxEntrySummary), typeof(BlackBoxObjectSummary), typeof(BlackBoxObjectChunk),
            typeof(BlackBoxDownloadManifest), typeof(BlackBoxDigest), typeof(BlackBoxNote), typeof(BlackBoxSectionResult),
            typeof(BlackBoxCapturedPayload), typeof(BlackBoxCaptureFailedPayload), typeof(BlackBoxAnalyzedPayload),
            typeof(BlackBoxResolvedPayload), typeof(BlackBoxDeletedPayload), typeof(BlackBoxStorageFallbackPayload),
            typeof(BlackBoxRecoveryRunPayload), typeof(BlackBoxGcReport), typeof(BlackBoxResilienceGap)
        };

        #endregion

        #region Private-Members

        private static readonly Regex _Space = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly JsonSerializerOptions _WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate the schema document.
        /// </summary>
        /// <returns>The root node.</returns>
        /// <exception cref="InvalidOperationException">Thrown when Armada.Core's XML documentation file is missing.</exception>
        public static BlackBoxJsonSchemaNode Generate()
        {
            Dictionary<string, string> docs = LoadDocs();
            NullabilityInfoContext nullability = new NullabilityInfoContext();
            HashSet<Type> recordGraph = new HashSet<Type>();
            Reach(typeof(BlackBoxRecord), recordGraph);
            SortedDictionary<string, BlackBoxJsonSchemaNode> defs = new SortedDictionary<string, BlackBoxJsonSchemaNode>(StringComparer.Ordinal);
            Queue<Type> pending = new Queue<Type>(new[] { typeof(BlackBoxRecord) }.Concat(Shapes));
            HashSet<Type> done = new HashSet<Type>();
            while (pending.Count > 0)
            {
                Type type = pending.Dequeue();
                if (!done.Add(type)) continue;
                defs[type.Name] = ObjectNode(type, docs, nullability, recordGraph.Contains(type), pending);
            }
            return new BlackBoxJsonSchemaNode
            {
                Schema = "https://json-schema.org/draft/2020-12/schema",
                Id = SchemaId,
                Title = "Armada black box record (record.json)",
                Description = "Schema of record.json, the versioned BlackBoxRecord of a black box entry, plus the entry, summary, object, chunk, manifest, digest, note and event payload shapes in $defs. Generated from the C# models by BlackBoxSchemaGenerator; see docs/BLACKBOX_SCHEMA.md. Readers ignore unknown properties and read unknown enum values as Unknown.",
                ArmadaSchemaVersion = BlackBoxLimits.SchemaVersion,
                Ref = "#/$defs/BlackBoxRecord",
                Defs = defs
            };
        }

        /// <summary>
        /// Generate the schema document as indented JSON with a trailing newline (the committed file's content).
        /// </summary>
        /// <returns>JSON text.</returns>
        public static string GenerateJson()
        {
            return JsonSerializer.Serialize(Generate(), _WriteOptions).Replace("\r\n", "\n") + "\n";
        }

        #endregion

        #region Private-Methods

        private static BlackBoxJsonSchemaNode ObjectNode(Type type, Dictionary<string, string> docs, NullabilityInfoContext nullability, bool inRecord, Queue<Type> pending)
        {
            BlackBoxJsonSchemaNode node = new BlackBoxJsonSchemaNode
            {
                Title = type.Name,
                Description = Doc(docs, "T:" + type.FullName),
                Type = new List<string> { "object" },
                Properties = new Dictionary<string, BlackBoxJsonSchemaNode>(StringComparer.Ordinal),
                Required = new List<string>()
            };
            foreach (PropertyInfo prop in Properties(type))
            {
                string name = JsonName(prop);
                bool nullable = IsNullable(prop, nullability);
                BlackBoxJsonSchemaNode value = ValueNode(prop.PropertyType, prop, nullable, inRecord, pending);
                string? description = Doc(docs, "P:" + prop.DeclaringType!.FullName + "." + prop.Name);
                value.Description = description;
                node.Properties[name] = value;
                if (!nullable) node.Required.Add(name);
            }
            return node;
        }

        private static BlackBoxJsonSchemaNode ValueNode(Type type, PropertyInfo? prop, bool nullable, bool inRecord, Queue<Type> pending)
        {
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) { type = underlying; nullable = true; }
            if (type == typeof(string))
            {
                BlackBoxJsonSchemaNode s = new BlackBoxJsonSchemaNode { Type = Types("string", nullable) };
                if (inRecord && prop != null) s.MaxLength = BlackBoxRecordBounder.StringCap(prop.Name);
                return s;
            }
            if (type == typeof(bool)) return new BlackBoxJsonSchemaNode { Type = Types("boolean", nullable) };
            if (type == typeof(int) || type == typeof(long)) return new BlackBoxJsonSchemaNode { Type = Types("integer", nullable) };
            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return new BlackBoxJsonSchemaNode { Type = Types("number", nullable) };
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return new BlackBoxJsonSchemaNode { Type = Types("string", nullable), Format = "date-time" };
            if (type == typeof(byte[])) return new BlackBoxJsonSchemaNode { Type = Types("string", nullable), Format = "byte" };
            if (type.IsEnum)
            {
                List<string?> values = System.Enum.GetNames(type).Cast<string?>().ToList();
                if (nullable) values.Add(null);
                return new BlackBoxJsonSchemaNode { Type = Types("string", nullable), Enum = values };
            }
            CollectionBoundAttribute? bound = prop?.GetCustomAttribute<CollectionBoundAttribute>();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                return new BlackBoxJsonSchemaNode
                {
                    Type = Types("object", nullable),
                    AdditionalProperties = ValueNode(type.GetGenericArguments()[1], null, false, inRecord, pending),
                    MaxProperties = bound?.Limit
                };
            }
            if (type.IsGenericType && typeof(IList).IsAssignableFrom(type))
            {
                Type element = type.GetGenericArguments()[0];
                BlackBoxJsonSchemaNode items = ValueNode(element, null, Nullable.GetUnderlyingType(element) != null, inRecord, pending);
                if (inRecord && element == typeof(string)) items.MaxLength = BlackBoxLimits.MaxPathChars;
                return new BlackBoxJsonSchemaNode { Type = Types("array", nullable), Items = items, MaxItems = bound?.Limit };
            }
            if (type.IsClass)
            {
                pending.Enqueue(type);
                BlackBoxJsonSchemaNode reference = new BlackBoxJsonSchemaNode { Ref = "#/$defs/" + type.Name };
                if (!nullable) return reference;
                return new BlackBoxJsonSchemaNode { AnyOf = new List<BlackBoxJsonSchemaNode> { reference, new BlackBoxJsonSchemaNode { Type = new List<string> { "null" } } } };
            }
            throw new InvalidOperationException("BlackBoxSchemaGenerator does not know how to describe " + type.FullName + ".");
        }

        private static IEnumerable<PropertyInfo> Properties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
                .OrderBy(p => p.MetadataToken);
        }

        private static string JsonName(PropertyInfo prop)
        {
            JsonPropertyNameAttribute? attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            return attr != null ? attr.Name : JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
        }

        private static bool IsNullable(PropertyInfo prop, NullabilityInfoContext context)
        {
            if (prop.PropertyType.IsValueType) return Nullable.GetUnderlyingType(prop.PropertyType) != null;
            return context.Create(prop).ReadState == NullabilityState.Nullable;
        }

        private static List<string> Types(string type, bool nullable)
        {
            return nullable ? new List<string> { type, "null" } : new List<string> { type };
        }

        private static void Reach(Type type, HashSet<Type> seen)
        {
            if (!seen.Add(type)) return;
            foreach (PropertyInfo prop in Properties(type))
            {
                Type t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (t.IsGenericType) t = t.GetGenericArguments().Last();
                if (t.IsClass && t != typeof(string) && t.Namespace != null && t.Namespace.StartsWith("Armada.Core", StringComparison.Ordinal)) Reach(t, seen);
            }
        }

        private static Dictionary<string, string> LoadDocs()
        {
            string? dir = Path.GetDirectoryName(typeof(BlackBoxRecord).Assembly.Location);
            string path = Path.Combine(dir ?? String.Empty, "Armada.Core.xml");
            if (!File.Exists(path)) throw new InvalidOperationException("Armada.Core.xml (the XML documentation file) was not found next to Armada.Core.dll at " + path + "; the schema needs it for descriptions.");
            Dictionary<string, string> docs = new Dictionary<string, string>(StringComparer.Ordinal);
            XDocument xml = XDocument.Load(path);
            foreach (XElement member in xml.Descendants("member"))
            {
                string? name = member.Attribute("name")?.Value;
                XElement? summary = member.Element("summary");
                if (name == null || summary == null) continue;
                docs[name] = Flatten(summary);
            }
            return docs;
        }

        private static string Flatten(XElement element)
        {
            StringBuilder sb = new StringBuilder();
            foreach (XNode node in element.Nodes())
            {
                if (node is XText text) sb.Append(text.Value);
                else if (node is XElement child)
                {
                    string? cref = child.Attribute("cref")?.Value;
                    if (cref != null) sb.Append(cref.Substring(cref.LastIndexOf('.') + 1));
                    else if (child.Attribute("langword") != null) sb.Append(child.Attribute("langword")!.Value);
                    else sb.Append(Flatten(child));
                }
            }
            return _Space.Replace(sb.ToString(), " ").Trim();
        }

        private static string? Doc(Dictionary<string, string> docs, string key)
        {
            return docs.TryGetValue(key, out string? value) ? value : null;
        }

        #endregion
    }
}
