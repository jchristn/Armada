namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// The API Explorer's OpenAPI logic, ported from the dashboard's <c>ApiExplorer.tsx</c>: parse the document into
    /// operations, resolve <c>$ref</c>/<c>allOf</c>/<c>oneOf</c>/<c>anyOf</c> schemas, build example bodies and initial
    /// parameter values, generate the curl, fetch, and C# snippets, and match a captured request to an operation for
    /// replay.
    /// </summary>
    public static class ApiExplorerSpec
    {
        #region Public-Members

        /// <summary>
        /// Code snippet kinds in display order.
        /// </summary>
        public static IReadOnlyList<string> CodeTabs { get; } = new List<string> { "curl", "fetch", "csharp" };

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _ReadOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions _JsOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private static readonly JsonSerializerOptions _JsIndented = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse an OpenAPI document.
        /// </summary>
        /// <param name="json">Document JSON.</param>
        /// <returns>Document (never null).</returns>
        /// <exception cref="JsonException">Thrown when the text is not a valid document.</exception>
        public static ApiExplorerDocument Parse(string json)
        {
            return JsonSerializer.Deserialize<ApiExplorerDocument>(json ?? "{}", _ReadOptions) ?? new ApiExplorerDocument();
        }

        /// <summary>
        /// Normalize the document's operations (methods get, post, put, delete, patch, head).
        /// </summary>
        /// <param name="doc">Document.</param>
        /// <returns>Operations in path order.</returns>
        public static List<ApiExplorerOperation> Operations(ApiExplorerDocument doc)
        {
            List<ApiExplorerOperation> result = new List<ApiExplorerOperation>();
            if (doc?.Paths == null) return result;
            foreach (KeyValuePair<string, ApiExplorerPathItem> path in doc.Paths)
            {
                ApiExplorerPathItem item = path.Value ?? new ApiExplorerPathItem();
                List<KeyValuePair<string, ApiExplorerOperationSource?>> methods = new List<KeyValuePair<string, ApiExplorerOperationSource?>>
                {
                    new KeyValuePair<string, ApiExplorerOperationSource?>("get", item.Get),
                    new KeyValuePair<string, ApiExplorerOperationSource?>("post", item.Post),
                    new KeyValuePair<string, ApiExplorerOperationSource?>("put", item.Put),
                    new KeyValuePair<string, ApiExplorerOperationSource?>("delete", item.Delete),
                    new KeyValuePair<string, ApiExplorerOperationSource?>("patch", item.Patch),
                    new KeyValuePair<string, ApiExplorerOperationSource?>("head", item.Head),
                };
                foreach (KeyValuePair<string, ApiExplorerOperationSource?> m in methods)
                {
                    if (m.Value == null) continue;
                    ApiExplorerOperationSource op = m.Value;
                    List<ApiExplorerParameterSource> merged = new List<ApiExplorerParameterSource>();
                    merged.AddRange(item.Parameters ?? new List<ApiExplorerParameterSource>());
                    merged.AddRange(op.Parameters ?? new List<ApiExplorerParameterSource>());
                    List<ApiExplorerParameterSource> parameters = new List<ApiExplorerParameterSource>();
                    HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ApiExplorerParameterSource p in merged)
                    {
                        if (seen.Add(p.In + ":" + p.Name)) parameters.Add(p);
                    }

                    Dictionary<string, ApiExplorerMediaType> content = op.RequestBody?.Content ?? new Dictionary<string, ApiExplorerMediaType>();
                    string contentType = content.ContainsKey("application/json") ? "application/json" : content.Keys.FirstOrDefault() ?? "";
                    ApiExplorerOperation normalized = new ApiExplorerOperation();
                    normalized.Id = !String.IsNullOrEmpty(op.OperationId) ? op.OperationId! : m.Key + ":" + path.Key;
                    normalized.Method = m.Key;
                    normalized.Path = path.Key;
                    normalized.Summary = !String.IsNullOrEmpty(op.Summary) ? op.Summary! : m.Key.ToUpperInvariant() + " " + path.Key;
                    normalized.Description = op.Description ?? "";
                    normalized.Tag = op.Tags != null && op.Tags.Count > 0 ? op.Tags[0] : "General";
                    normalized.Parameters = parameters;
                    normalized.HasBody = contentType.Length > 0 && content.ContainsKey(contentType);
                    normalized.BodySchema = normalized.HasBody ? content[contentType]?.Schema : null;
                    normalized.BodyContentType = contentType;
                    result.Add(normalized);
                }
            }

            return result;
        }

        /// <summary>
        /// Categories: "All" then each tag in first-seen order.
        /// </summary>
        /// <param name="operations">Operations.</param>
        /// <returns>Categories.</returns>
        public static List<string> Tags(IEnumerable<ApiExplorerOperation> operations)
        {
            List<string> tags = new List<string> { "All" };
            foreach (ApiExplorerOperation op in operations)
            {
                if (!tags.Contains(op.Tag)) tags.Add(op.Tag);
            }

            return tags;
        }

        /// <summary>
        /// Operations matching a category and a text filter (summary, path, or tag).
        /// </summary>
        /// <param name="operations">Operations.</param>
        /// <param name="tag">Category ("All" for every one).</param>
        /// <param name="filter">Text filter.</param>
        /// <returns>Matches.</returns>
        public static List<ApiExplorerOperation> Filter(IEnumerable<ApiExplorerOperation> operations, string tag, string filter)
        {
            string f = (filter ?? "").Trim().ToLowerInvariant();
            return operations.Where(op =>
            {
                bool matchesTag = String.IsNullOrEmpty(tag) || tag == "All" || op.Tag == tag;
                string haystack = (op.Summary + " " + op.Path + " " + op.Tag).ToLowerInvariant();
                return matchesTag && (f.Length == 0 || haystack.Contains(f));
            }).ToList();
        }

        /// <summary>
        /// Resolve references and compositions.
        /// </summary>
        /// <param name="schema">Schema.</param>
        /// <param name="doc">Document.</param>
        /// <param name="seen">Reference names already followed.</param>
        /// <returns>Resolved schema, or null.</returns>
        public static ApiExplorerSchema? Resolve(ApiExplorerSchema? schema, ApiExplorerDocument? doc, HashSet<string>? seen = null)
        {
            if (schema == null) return null;
            seen = seen ?? new HashSet<string>(StringComparer.Ordinal);
            if (!String.IsNullOrEmpty(schema.Ref))
            {
                string name = schema.Ref!.Split('/').Last();
                if (name.Length == 0 || seen.Contains(name)) return null;
                seen.Add(name);
                ApiExplorerSchema? target = null;
                if (doc?.Components?.Schemas != null) doc.Components.Schemas.TryGetValue(name, out target);
                return Resolve(target, doc, seen);
            }

            if (schema.AllOf != null && schema.AllOf.Count > 0)
            {
                ApiExplorerSchema merged = new ApiExplorerSchema();
                merged.Properties = new Dictionary<string, ApiExplorerSchema>(StringComparer.Ordinal);
                foreach (ApiExplorerSchema part in schema.AllOf)
                {
                    ApiExplorerSchema? resolved = Resolve(part, doc, new HashSet<string>(seen, StringComparer.Ordinal));
                    if (resolved == null) continue;
                    if (resolved.Type != null) merged.Type = resolved.Type;
                    if (resolved.Format != null) merged.Format = resolved.Format;
                    if (resolved.Example != null) merged.Example = resolved.Example;
                    if (resolved.Default != null) merged.Default = resolved.Default;
                    if (resolved.Enum != null) merged.Enum = resolved.Enum;
                    if (resolved.Items != null) merged.Items = resolved.Items;
                    foreach (KeyValuePair<string, ApiExplorerSchema> p in resolved.Properties ?? new Dictionary<string, ApiExplorerSchema>()) merged.Properties[p.Key] = p.Value;
                }

                return merged;
            }

            if (schema.OneOf != null && schema.OneOf.Count > 0) return Resolve(schema.OneOf[0], doc, seen);
            if (schema.AnyOf != null && schema.AnyOf.Count > 0) return Resolve(schema.AnyOf[0], doc, seen);
            return schema;
        }

        /// <summary>
        /// An example value for a schema (example, default, or a typed placeholder), as a serializable tree.
        /// </summary>
        /// <param name="schema">Schema.</param>
        /// <param name="doc">Document.</param>
        /// <param name="nowUtc">Now (for date-time strings).</param>
        /// <returns>Value.</returns>
        public static object? Example(ApiExplorerSchema? schema, ApiExplorerDocument? doc, DateTime nowUtc)
        {
            ApiExplorerSchema? resolved = Resolve(schema, doc);
            if (resolved == null) return new Dictionary<string, object?>();
            if (resolved.Example != null) return resolved.Example;
            if (resolved.Default != null) return resolved.Default;
            switch (TypeName(resolved))
            {
                case "object":
                    Dictionary<string, object?> value = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, ApiExplorerSchema> p in resolved.Properties ?? new Dictionary<string, ApiExplorerSchema>())
                    {
                        value[p.Key] = Example(p.Value, doc, nowUtc);
                    }

                    return value;
                case "array":
                    return resolved.Items != null ? new List<object?> { Example(resolved.Items, doc, nowUtc) } : new List<object?>();
                case "integer":
                case "number":
                    return 0;
                case "boolean":
                    return false;
                case "string":
                    if (resolved.Enum != null && resolved.Enum.Count > 0) return resolved.Enum[0];
                    if (resolved.Format == "date-time") return nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                    return "";
                default:
                    return new Dictionary<string, object?>();
            }
        }

        /// <summary>
        /// The example body for an operation as indented JSON, or empty when it has no body.
        /// </summary>
        /// <param name="op">Operation.</param>
        /// <param name="doc">Document.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Body text.</returns>
        public static string ExampleBody(ApiExplorerOperation op, ApiExplorerDocument? doc, DateTime nowUtc)
        {
            if (op == null || !op.HasBody) return "";
            return JsonSerializer.Serialize(Example(op.BodySchema, doc, nowUtc), _JsIndented);
        }

        /// <summary>
        /// Initial value for a parameter: its example, else the schema default, else the first enum value, else empty.
        /// </summary>
        /// <param name="parameter">Parameter.</param>
        /// <param name="doc">Document.</param>
        /// <returns>Value.</returns>
        public static string InitialValue(ApiExplorerParameterSource parameter, ApiExplorerDocument? doc)
        {
            if (parameter == null) return "";
            if (parameter.Example != null) return RawToString(parameter.Example);
            ApiExplorerSchema? resolved = Resolve(parameter.Schema, doc);
            if (resolved?.Default != null) return RawToString(resolved.Default);
            if (resolved?.Enum != null && resolved.Enum.Count > 0) return RawToString(resolved.Enum[0]);
            return "";
        }

        /// <summary>
        /// The JavaScript <c>String(x)</c> of a raw JSON value: strings unquoted, other values as JSON text.
        /// </summary>
        /// <param name="raw">Raw value.</param>
        /// <returns>Text.</returns>
        public static string RawToString(object? raw)
        {
            if (raw == null) return "";
            if (raw is string s) return s;
            string json = JsonSerializer.Serialize(raw, _JsOptions);
            if (json.StartsWith("\"", StringComparison.Ordinal)) return JsonSerializer.Deserialize<string>(json) ?? "";
            if (json == "null") return "null";
            return json;
        }

        /// <summary>
        /// The schema's primary type name ("string", "object", ...), or empty.
        /// </summary>
        /// <param name="schema">Schema.</param>
        /// <returns>Type name.</returns>
        public static string TypeName(ApiExplorerSchema? schema)
        {
            if (schema?.Type == null) return "";
            if (schema.Type is string s) return s;
            string json = JsonSerializer.Serialize(schema.Type, _JsOptions);
            if (json.StartsWith("\"", StringComparison.Ordinal)) return JsonSerializer.Deserialize<string>(json) ?? "";
            if (json.StartsWith("[", StringComparison.Ordinal))
            {
                List<string?>? list = JsonSerializer.Deserialize<List<string?>>(json);
                return list?.FirstOrDefault(t => t != null && t != "null") ?? "";
            }

            return "";
        }

        /// <summary>
        /// Prettify a body when the content type is JSON (the dashboard's <c>prettifyContent</c>).
        /// </summary>
        /// <param name="body">Body.</param>
        /// <param name="contentType">Content type.</param>
        /// <returns>Text, "(empty)" when empty.</returns>
        public static string Prettify(string? body, string? contentType)
        {
            if (String.IsNullOrEmpty(body)) return "(empty)";
            if ((contentType ?? "").Contains("application/json", StringComparison.Ordinal))
            {
                try
                {
                    using (JsonDocument parsed = JsonDocument.Parse(body!))
                    {
                        return JsonSerializer.Serialize(parsed.RootElement, _JsIndented);
                    }
                }
                catch (JsonException)
                {
                    return body!;
                }
            }

            return body!;
        }

        /// <summary>
        /// The curl, fetch, and C# snippets for a request (the dashboard's <c>generateCodeSnippets</c>).
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Snippets keyed by "curl", "fetch", "csharp".</returns>
        public static Dictionary<string, string> Snippets(ApiExplorerRequestPreview request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string method = request.Method.ToUpperInvariant();
            List<KeyValuePair<string, string>> headers = request.Headers;
            string curlHeaders = String.Join(" \\\n  ", headers.Select(h => "-H \"" + h.Key + ": " + h.Value + "\""));
            string fetchHeaders = "";
            if (headers.Count > 0)
            {
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> h in headers) map[h.Key] = h.Value;
                fetchHeaders = ",\n  headers: " + JsonSerializer.Serialize(map, _JsIndented).Replace("\r\n", "\n").Replace("\n", "\n  ");
            }

            string csharpHeaders = String.Join("\n", headers.Select(h => "request.Headers.TryAddWithoutValidation(\"" + h.Key + "\", \"" + h.Value + "\");"));
            string body = request.Body.Length > 0 ? Prettify(request.Body, String.IsNullOrEmpty(request.ContentType) ? "application/json" : request.ContentType) : "";
            string contentType = String.IsNullOrEmpty(request.ContentType) ? "application/json" : request.ContentType;
            string curlBody = request.Body.Length > 0 ? " \\\n  --data '" + body.Replace("'", "'\\''") + "'" : "";
            string fetchBody = request.Body.Length > 0 ? ",\n  body: " + Js(body) : "";
            string csharpBody = request.Body.Length > 0 ? "request.Content = new StringContent(" + Js(body) + ", Encoding.UTF8, \"" + contentType + "\");" : "";
            string httpMethod = method.Length == 0 ? "" : method.Substring(0, 1) + method.Substring(1).ToLowerInvariant();

            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            result["curl"] = "curl -X " + method + " \"" + request.Url + "\"" + (curlHeaders.Length > 0 ? " \\\n  " + curlHeaders : "") + curlBody;
            result["fetch"] = "const response = await fetch(" + Js(request.Url) + ", {\n  method: " + Js(method) + fetchHeaders + fetchBody + "\n});\n\nconst data = await response.text();";
            result["csharp"] = "using System.Net.Http;\nusing System.Text;\n\nusing var client = new HttpClient();\nusing var request = new HttpRequestMessage(HttpMethod." + httpMethod + ", " + Js(request.Url) + ");\n"
                + csharpHeaders + (csharpBody.Length > 0 ? "\n" + csharpBody : "") + "\nusing var response = await client.SendAsync(request);\nvar body = await response.Content.ReadAsStringAsync();";
            return result;
        }

        /// <summary>
        /// Path parameter values extracted from a concrete route matching a template, merged over the initial values
        /// (initial values win unless empty).
        /// </summary>
        /// <param name="template">Template ("/api/v1/missions/{id}").</param>
        /// <param name="route">Concrete route.</param>
        /// <param name="initial">Initial values.</param>
        /// <param name="matched">True when the route matched the template.</param>
        /// <returns>Values.</returns>
        public static Dictionary<string, string> ResolvePathValues(string template, string route, IEnumerable<KeyValuePair<string, string>>? initial, out bool matched)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kvp in initial ?? Enumerable.Empty<KeyValuePair<string, string>>()) values[kvp.Key] = kvp.Value;
            List<string> keys = new List<string>();
            string pattern = Regex.Replace(Regex.Escape(template ?? ""), @"\\\{([^}]+)}", m =>
            {
                keys.Add(m.Groups[1].Value);
                return "([^/]+)";
            });
            Match match = Regex.Match(route ?? "", "^" + pattern + "$");
            matched = match.Success;
            if (!match.Success) return values;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!values.TryGetValue(keys[i], out string? existing) || String.IsNullOrEmpty(existing))
                {
                    values[keys[i]] = Uri.UnescapeDataString(match.Groups[i + 1].Value);
                }
            }

            return values;
        }

        /// <summary>
        /// Find the operation a captured request belongs to (the dashboard's <c>findOperationForReplay</c>).
        /// </summary>
        /// <param name="operations">Operations.</param>
        /// <param name="method">Method.</param>
        /// <param name="route">Concrete route.</param>
        /// <param name="routeTemplate">Route template, or null.</param>
        /// <param name="initialPathValues">Stored path values.</param>
        /// <param name="pathValues">Resolved path values for the match.</param>
        /// <returns>The operation, or null.</returns>
        public static ApiExplorerOperation? FindForReplay(IEnumerable<ApiExplorerOperation> operations, string method, string route, string? routeTemplate, IEnumerable<KeyValuePair<string, string>>? initialPathValues, out Dictionary<string, string> pathValues)
        {
            pathValues = new Dictionary<string, string>(StringComparer.Ordinal);
            List<KeyValuePair<string, string>> initial = (initialPathValues ?? Enumerable.Empty<KeyValuePair<string, string>>()).ToList();
            foreach (ApiExplorerOperation op in operations)
            {
                if (!String.Equals(op.Method, method, StringComparison.OrdinalIgnoreCase)) continue;
                if (!String.IsNullOrEmpty(routeTemplate) && routeTemplate == op.Path)
                {
                    pathValues = ResolvePathValues(op.Path, route, initial, out bool unused);
                    return op;
                }

                Dictionary<string, string> values = ResolvePathValues(op.Path, route, initial, out bool matched);
                if (!matched && op.Path != route) continue;
                pathValues = values;
                return op;
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private static string Js(string value)
        {
            return JsonSerializer.Serialize(value ?? "", _JsOptions);
        }

        #endregion
    }
}
