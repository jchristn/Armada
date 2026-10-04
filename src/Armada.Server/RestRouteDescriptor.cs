namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// One registered REST route with the parts of its OpenAPI metadata that make up the public contract: method, route
    /// template, summary, tags, request body type and required-ness, and response type names by status code. Type names
    /// are the names recorded by <see cref="OpenApiJson"/> (the schema description), for example EnumerationResult&lt;Fleet&gt;.
    /// </summary>
    public sealed class RestRouteDescriptor
    {
        #region Public-Members

        /// <summary>
        /// HTTP method in upper case.
        /// </summary>
        public string Method { get; set; } = "";

        /// <summary>
        /// Route template, for example /api/v1/fleets/{id}.
        /// </summary>
        public string Template { get; set; } = "";

        /// <summary>
        /// OpenAPI summary, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// OpenAPI tags.
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Request body type name, or null when the route documents no body.
        /// </summary>
        public string? RequestType { get; set; } = null;

        /// <summary>
        /// Whether the documented request body is required.
        /// </summary>
        public bool RequestRequired { get; set; } = false;

        /// <summary>
        /// Response type names keyed by status code ("200", "201"); a response without a JSON schema maps to its content
        /// type or to an empty string.
        /// </summary>
        public SortedDictionary<string, string> Responses { get; set; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the route is marked deprecated in OpenAPI.
        /// </summary>
        public bool Deprecated { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a descriptor from a route's method, template, and OpenAPI metadata.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="template">Route template.</param>
        /// <param name="metadata">OpenAPI metadata, or null.</param>
        /// <returns>Descriptor.</returns>
        public static RestRouteDescriptor From(string method, string template, OpenApiRouteMetadata? metadata)
        {
            RestRouteDescriptor descriptor = new RestRouteDescriptor();
            descriptor.Method = (method ?? "").ToUpperInvariant();
            descriptor.Template = template ?? "";
            if (metadata == null) return descriptor;

            descriptor.Summary = metadata.Summary;
            descriptor.Deprecated = metadata.Deprecated;
            if (metadata.Tags != null) descriptor.Tags = metadata.Tags.ToList();

            if (metadata.RequestBody != null)
            {
                descriptor.RequestRequired = metadata.RequestBody.Required;
                descriptor.RequestType = TypeName(metadata.RequestBody.Content);
            }

            if (metadata.Responses != null)
            {
                foreach (KeyValuePair<string, OpenApiResponseMetadata> response in metadata.Responses)
                {
                    descriptor.Responses[response.Key] = TypeName(response.Value?.Content) ?? "";
                }
            }

            return descriptor;
        }

        #endregion

        #region Private-Methods

        private static string? TypeName(Dictionary<string, OpenApiMediaTypeMetadata>? content)
        {
            if (content == null || content.Count == 0) return null;
            foreach (KeyValuePair<string, OpenApiMediaTypeMetadata> media in content)
            {
                OpenApiSchemaMetadata? schema = media.Value?.Schema;
                if (schema == null) return media.Key;
                if (!String.IsNullOrEmpty(schema.Ref)) return schema.Ref.Substring(schema.Ref.LastIndexOf('/') + 1);
                if (!String.IsNullOrEmpty(schema.Description)) return schema.Description;
                if (String.Equals(schema.Type, "array", StringComparison.Ordinal) && schema.Items != null)
                    return "array<" + (schema.Items.Description ?? schema.Items.Type ?? "object") + ">";
                return schema.Type ?? media.Key;
            }

            return null;
        }

        #endregion
    }
}
