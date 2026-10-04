namespace Test.Shared.Infrastructure.ApiSurface
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
    using System.Xml.Linq;
    using Armada.Core.ApiSurface;
    using Armada.Core.Authorization;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Server;
    using Armada.Server.WebSocket;

    /// <summary>
    /// Builds the live public API surface of Armada from the running code: REST routes and their OpenAPI metadata from the
    /// Admiral's web server joined with <see cref="RouteAuthorizationRegistry"/>, MCP tools and input schemas as registered
    /// on the MCP server joined with <see cref="McpToolAuthorizationRegistry"/>, the WebSocket contract from
    /// <see cref="WebSocketSurface"/>, the CLI command model from Helm (Spectre <c>cli xmldoc</c>), and settings keys by
    /// reflection over <see cref="ArmadaSettings"/>. Every list is sorted so the output is deterministic.
    /// </summary>
    public static class ApiSurfaceBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build the full surface from a running in-process Admiral.
        /// </summary>
        /// <param name="server">Running server.</param>
        /// <returns>Surface document.</returns>
        public static ApiSurfaceDocument Build(ArmadaServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            ApiSurfaceDocument document = new ApiSurfaceDocument();
            document.Rest = BuildRest(server.GetRestRouteDescriptors());
            document.Mcp = BuildMcp(server.RegisteredMcpToolDescriptors);
            document.WebSocket = BuildWebSocket();
            document.Cli = BuildCli(Armada.Helm.Program.DescribeCommandModelXml());
            document.Settings = BuildSettings();
            return document;
        }

        /// <summary>
        /// Build the REST surface from route descriptors.
        /// </summary>
        /// <param name="routes">Registered routes.</param>
        /// <returns>Sorted REST routes.</returns>
        public static List<ApiRestRoute> BuildRest(List<RestRouteDescriptor> routes)
        {
            List<ApiRestRoute> result = new List<ApiRestRoute>();
            foreach (RestRouteDescriptor descriptor in routes)
            {
                ApiRestRoute route = new ApiRestRoute();
                route.Method = descriptor.Method;
                route.Route = descriptor.Template;
                AuthorizationRequirement? requirement;
                if (RouteAuthorizationRegistry.TryGetByTemplate(descriptor.Method, descriptor.Template, out requirement) && requirement != null)
                {
                    route.Auth = requirement.Level.ToString();
                    route.Resource = requirement.ResourceType + ":" + requirement.Operation;
                }
                else
                {
                    route.Auth = "Undeclared";
                    route.Resource = "Undeclared";
                }

                route.RequestType = descriptor.RequestType;
                route.RequestRequired = descriptor.RequestRequired;
                route.Responses = new SortedDictionary<string, string>(descriptor.Responses, StringComparer.Ordinal);
                route.Tags = descriptor.Tags.OrderBy(t => t, StringComparer.Ordinal).ToList();
                route.Summary = Ascii(descriptor.Summary);
                route.Experimental = ExperimentalSurface.IsExperimentalRoute(descriptor.Method, descriptor.Template);
                result.Add(route);
            }

            return result
                .OrderBy(r => r.Route, StringComparer.Ordinal)
                .ThenBy(r => r.Method, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Build the MCP surface from registered tool descriptors.
        /// </summary>
        /// <param name="tools">Registered tools.</param>
        /// <returns>Sorted MCP tools.</returns>
        public static List<ApiMcpTool> BuildMcp(IEnumerable<CaptainToolSummary> tools)
        {
            List<ApiMcpTool> result = new List<ApiMcpTool>();
            foreach (CaptainToolSummary summary in tools)
            {
                ApiMcpTool tool = new ApiMcpTool();
                tool.Name = summary.Name;
                tool.Description = Ascii(summary.Description);
                AuthorizationRequirement requirement = McpToolAuthorizationRegistry.GetOrDefault(summary.Name);
                tool.Auth = McpToolAuthorizationRegistry.TryGet(summary.Name, out AuthorizationRequirement? _) ? requirement.Level.ToString() : "Undeclared";
                tool.Resource = requirement.ResourceType + ":" + requirement.Operation;
                tool.Experimental = ExperimentalSurface.IsExperimentalTool(summary.Name);
                tool.Arguments = ParseArguments(summary.InputSchemaJson);
                result.Add(tool);
            }

            return result.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Build the WebSocket surface from <see cref="WebSocketSurface"/>.
        /// </summary>
        /// <returns>WebSocket surface.</returns>
        public static ApiWebSocketSurface BuildWebSocket()
        {
            ApiWebSocketSurface surface = new ApiWebSocketSurface();
            foreach (KeyValuePair<string, string> endpoint in WebSocketSurface.Endpoints.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                surface.Endpoints.Add(new ApiWebSocketEndpoint
                {
                    Name = endpoint.Key,
                    Path = endpoint.Value,
                    Experimental = ExperimentalSurface.IsExperimentalWebSocketEndpoint(endpoint.Key)
                });
            }

            surface.Routes = WebSocketSurface.Routes.OrderBy(r => r, StringComparer.Ordinal).ToList();
            surface.ClientMessageFields = WebSocketSurface.ClientMessageFields.OrderBy(f => f, StringComparer.Ordinal).ToList();
            surface.EventEnvelopeFields = WebSocketSurface.EventEnvelopeFields.ToList();
            surface.CommandReplyFields = WebSocketSurface.CommandReplyFields.ToList();
            surface.Commands = WebSocketSurface.CommandActions.OrderBy(c => c, StringComparer.Ordinal).ToList();
            foreach (WebSocketEventDescriptor descriptor in WebSocketSurface.Events.OrderBy(e => e.Type, StringComparer.Ordinal))
            {
                surface.Events.Add(new ApiWebSocketEvent
                {
                    Type = descriptor.Type,
                    Scope = descriptor.Scope,
                    HasMessage = descriptor.HasMessage,
                    PayloadType = descriptor.PayloadType,
                    Fields = descriptor.Fields.ToList(),
                    Generic = descriptor.Generic
                });
            }

            return surface;
        }

        /// <summary>
        /// Build the CLI surface from the Spectre command model XML.
        /// </summary>
        /// <param name="xml">Output of <c>cli xmldoc</c>.</param>
        /// <returns>Sorted leaf commands.</returns>
        public static List<ApiCliCommand> BuildCli(string xml)
        {
            if (String.IsNullOrWhiteSpace(xml)) throw new ArgumentNullException(nameof(xml));
            int start = xml.IndexOf("<Model", StringComparison.Ordinal);
            XDocument document = XDocument.Parse(start > 0 ? xml.Substring(start) : xml);
            List<ApiCliCommand> result = new List<ApiCliCommand>();
            XElement? root = document.Root;
            if (root == null) return result;
            foreach (XElement command in root.Elements("Command")) AddCliCommand(result, command, "", new List<ApiCliParameter>());
            return result.OrderBy(c => c.Command, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Build the settings surface by reflection over <see cref="ArmadaSettings"/>.
        /// </summary>
        /// <returns>Sorted settings keys.</returns>
        public static List<ApiSettingKey> BuildSettings()
        {
            List<ApiSettingKey> result = new List<ApiSettingKey>();
            AddSettings(result, typeof(ArmadaSettings), new ArmadaSettings(), "", 0);
            return result.OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Keep the surface files plain ASCII: typographic punctuation in descriptions becomes its ASCII spelling.
        /// </summary>
        private static string? Ascii(string? text)
        {
            if (text == null) return null;
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c < 128) { sb.Append(c); continue; }
                switch (c)
                {
                    case '\u2014': sb.Append("--"); break;
                    case '\u2013': sb.Append('-'); break;
                    case '\u2018':
                    case '\u2019': sb.Append('\''); break;
                    case '\u201C':
                    case '\u201D': sb.Append('"'); break;
                    case '\u2026': sb.Append("..."); break;
                    case '\u2192': sb.Append("->"); break;
                    case '\u2190': sb.Append("<-"); break;
                    case '\u00A0': sb.Append(' '); break;
                    case '\u00D7': sb.Append('x'); break;
                    case '\u2265': sb.Append(">="); break;
                    case '\u2264': sb.Append("<="); break;
                    default: sb.Append('?'); break;
                }
            }

            return sb.ToString();
        }

        private static List<ApiMcpArgument> ParseArguments(string? schemaJson)
        {
            List<ApiMcpArgument> arguments = new List<ApiMcpArgument>();
            if (String.IsNullOrWhiteSpace(schemaJson)) return arguments;
            using (JsonDocument document = JsonDocument.Parse(schemaJson))
            {
                JsonElement root = document.RootElement;
                HashSet<string> required = new HashSet<string>(StringComparer.Ordinal);
                if (root.TryGetProperty("required", out JsonElement requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement name in requiredElement.EnumerateArray())
                        if (name.ValueKind == JsonValueKind.String) required.Add(name.GetString() ?? "");
                }

                if (root.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in properties.EnumerateObject())
                    {
                        arguments.Add(new ApiMcpArgument
                        {
                            Name = property.Name,
                            Type = SchemaType(property.Value),
                            Required = required.Contains(property.Name)
                        });
                    }
                }
            }

            return arguments.OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
        }

        private static string SchemaType(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object) return "any";
            string type = "any";
            if (schema.TryGetProperty("type", out JsonElement typeElement))
            {
                if (typeElement.ValueKind == JsonValueKind.String) type = typeElement.GetString() ?? "any";
                else if (typeElement.ValueKind == JsonValueKind.Array)
                    type = String.Join("|", typeElement.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()));
            }

            if (type == "array" && schema.TryGetProperty("items", out JsonElement items)) return "array<" + SchemaType(items) + ">";
            return type;
        }

        private static void AddCliCommand(List<ApiCliCommand> result, XElement command, string prefix, List<ApiCliParameter> inherited)
        {
            string name = (string?)command.Attribute("Name") ?? "";
            string path = prefix.Length == 0 ? name : prefix + " " + name;
            bool isBranch = String.Equals((string?)command.Attribute("IsBranch"), "true", StringComparison.OrdinalIgnoreCase);

            List<ApiCliParameter> parameters = new List<ApiCliParameter>(inherited);
            XElement? parameterElement = command.Element("Parameters");
            if (parameterElement != null)
            {
                foreach (XElement argument in parameterElement.Elements("Argument"))
                {
                    parameters.Add(new ApiCliParameter
                    {
                        Kind = "argument",
                        Name = (string?)argument.Attribute("Name") ?? "",
                        ValueKind = (string?)argument.Attribute("Kind") ?? "",
                        Type = ClrTypeName((string?)argument.Attribute("ClrType")),
                        Required = String.Equals((string?)argument.Attribute("Required"), "true", StringComparison.OrdinalIgnoreCase)
                    });
                }

                foreach (XElement option in parameterElement.Elements("Option"))
                {
                    string longName = (string?)option.Attribute("Long") ?? "";
                    string shortName = (string?)option.Attribute("Short") ?? "";
                    parameters.Add(new ApiCliParameter
                    {
                        Kind = "option",
                        Name = longName.Length > 0 ? longName : shortName,
                        Short = shortName.Length > 0 && longName.Length > 0 ? shortName : null,
                        ValueKind = (string?)option.Attribute("Kind") ?? "",
                        Type = ClrTypeName((string?)option.Attribute("ClrType")),
                        Required = String.Equals((string?)option.Attribute("Required"), "true", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }

            if (isBranch)
            {
                foreach (XElement child in command.Elements("Command")) AddCliCommand(result, child, path, parameters);
                return;
            }

            ApiCliCommand entry = new ApiCliCommand();
            entry.Command = path;
            entry.Description = Ascii(((string?)command.Element("Description"))?.Trim());
            entry.Parameters = parameters
                .OrderBy(p => p.Kind, StringComparer.Ordinal)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
            result.Add(entry);
        }

        private static string ClrTypeName(string? clrType)
        {
            if (String.IsNullOrEmpty(clrType)) return "";
            string value = clrType;
            if (value.StartsWith("System.Nullable`1[[", StringComparison.Ordinal))
            {
                string inner = value.Substring("System.Nullable`1[[".Length);
                int comma = inner.IndexOf(',');
                if (comma > 0) inner = inner.Substring(0, comma);
                return ShortTypeName(inner) + "?";
            }

            return ShortTypeName(value);
        }

        private static string ShortTypeName(string fullName)
        {
            int bracket = fullName.IndexOf('[');
            string head = bracket > 0 ? fullName.Substring(0, bracket) : fullName;
            int dot = head.LastIndexOf('.');
            string shortName = dot >= 0 ? head.Substring(dot + 1) : head;
            if (fullName.EndsWith("[]", StringComparison.Ordinal) && !shortName.EndsWith("[]", StringComparison.Ordinal)) shortName += "[]";
            return shortName;
        }

        private static void AddSettings(List<ApiSettingKey> result, Type type, object? instance, string prefix, int depth)
        {
            if (depth > 6) return;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (!property.CanRead || !property.CanWrite || property.GetSetMethod() == null) continue;
                if (property.GetIndexParameters().Length > 0) continue;
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

                JsonPropertyNameAttribute? named = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                string name = named != null ? named.Name : JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                string key = prefix.Length == 0 ? name : prefix + "." + name;
                Type propertyType = property.PropertyType;
                Type effective = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
                object? value = null;
                try { value = instance == null ? null : property.GetValue(instance); }
                catch { value = null; }

                if (IsSettingsObject(effective))
                {
                    result.Add(new ApiSettingKey { Key = key, Type = "object", Default = null, Experimental = ExperimentalSurface.IsExperimentalSetting(key) });
                    AddSettings(result, effective, value ?? CreateOrNull(effective), key, depth + 1);
                    continue;
                }

                Type? element = ElementType(effective);
                if (element != null && IsSettingsObject(element))
                {
                    result.Add(new ApiSettingKey { Key = key, Type = "array<object>", Default = DefaultJson(value), Experimental = ExperimentalSurface.IsExperimentalSetting(key) });
                    AddSettings(result, element, CreateOrNull(element), key + "[]", depth + 1);
                    continue;
                }

                result.Add(new ApiSettingKey
                {
                    Key = key,
                    Type = TypeLabel(propertyType),
                    Default = DefaultJson(value),
                    Experimental = ExperimentalSurface.IsExperimentalSetting(key)
                });
            }
        }

        private static bool IsSettingsObject(Type type)
        {
            if (!type.IsClass || type == typeof(string)) return false;
            if (typeof(IEnumerable).IsAssignableFrom(type)) return false;
            string ns = type.Namespace ?? "";
            return ns.StartsWith("Armada.", StringComparison.Ordinal);
        }

        private static Type? ElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            {
                Type[] args = type.GetGenericArguments();
                if (args.Length == 1) return args[0];
            }

            return null;
        }

        private static object? CreateOrNull(Type type)
        {
            try { return Activator.CreateInstance(type); }
            catch { return null; }
        }

        private static string TypeLabel(Type type)
        {
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) return TypeLabel(underlying) + "?";
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(double)) return "double";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(DateTime)) return "datetime";
            if (type == typeof(TimeSpan)) return "timespan";
            if (type.IsEnum) return "enum " + type.Name + " (" + String.Join("|", Enum.GetNames(type)) + ")";
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type[] args = type.GetGenericArguments();
                return "map<" + TypeLabel(args[0]) + "," + TypeLabel(args[1]) + ">";
            }

            Type? element = ElementType(type);
            if (element != null) return "array<" + TypeLabel(element) + ">";
            return type.Name;
        }

        private static string? DefaultJson(object? value)
        {
            if (value == null) return "null";
            string json;
            try
            {
                json = JsonSerializer.Serialize(value, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });
            }
            catch
            {
                return null;
            }

            // Normalize machine-specific paths so the file is identical on every host.
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!String.IsNullOrEmpty(home))
            {
                json = json.Replace(JsonEscape(home), "~", StringComparison.Ordinal);
                json = json.Replace(home, "~", StringComparison.Ordinal);
            }

            json = json.Replace("\\\\", "/", StringComparison.Ordinal);
            return json;
        }

        private static string JsonEscape(string value)
        {
            string encoded = JsonSerializer.Serialize(value);
            return encoded.Substring(1, encoded.Length - 2);
        }

        #endregion
    }
}
