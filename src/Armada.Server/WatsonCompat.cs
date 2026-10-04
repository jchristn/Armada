namespace Armada.Server
{
    using System.Collections.Generic;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Fluent helpers on <see cref="OpenApiRouteMetadata"/> for concise route-metadata call sites
    /// on top of Watson's OpenAPI model.
    /// </summary>
    public static class OpenApiRouteMetadataExtensions
    {
        /// <summary>
        /// Set the operation summary.
        /// </summary>
        /// <param name="metadata">Route metadata.</param>
        /// <param name="summary">Short summary string.</param>
        /// <returns>The same metadata instance for chaining.</returns>
        public static OpenApiRouteMetadata WithSummary(this OpenApiRouteMetadata metadata, string summary)
        {
            if (metadata == null) throw new System.ArgumentNullException(nameof(metadata));
            metadata.Summary = summary;
            return metadata;
        }

        /// <summary>
        /// Add a named security scheme requirement to the operation.
        /// </summary>
        /// <param name="metadata">Route metadata.</param>
        /// <param name="schemeName">Security scheme name (e.g., "ApiKey").</param>
        /// <returns>The same metadata instance for chaining.</returns>
        public static OpenApiRouteMetadata WithSecurity(this OpenApiRouteMetadata metadata, string schemeName)
        {
            if (metadata == null) throw new System.ArgumentNullException(nameof(metadata));
            if (metadata.Security == null) metadata.Security = new List<string>();
            metadata.Security.Add(schemeName);
            return metadata;
        }
    }

    /// <summary>
    /// Typed response factories for <see cref="OpenApiResponseMetadata"/> providing a concise
    /// <c>OpenApiJson.For&lt;T&gt;(description)</c> shape on top of Watson's OpenAPI model.
    /// </summary>
    public static class OpenApiJson
    {
        /// <summary>
        /// Create a JSON response descriptor for the specified payload type.
        /// </summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <param name="description">Response description.</param>
        /// <returns>An OpenApiResponseMetadata for the application/json content type.</returns>
        public static OpenApiResponseMetadata For<T>(string description)
        {
            OpenApiSchemaMetadata schema = new OpenApiSchemaMetadata
            {
                Type = "object",
                Description = FriendlyName(typeof(T))
            };
            return OpenApiResponseMetadata.Json(description, schema);
        }

        /// <summary>
        /// Create a JSON request body descriptor for the specified payload type.
        /// </summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <param name="description">Body description.</param>
        /// <param name="required">Whether the body is required.</param>
        /// <returns>An OpenApiRequestBodyMetadata for the application/json content type.</returns>
        public static OpenApiRequestBodyMetadata BodyFor<T>(string description, bool required = true)
        {
            OpenApiSchemaMetadata schema = new OpenApiSchemaMetadata
            {
                Type = "object",
                Description = FriendlyName(typeof(T))
            };
            return OpenApiRequestBodyMetadata.Json(schema, description, required);
        }

        /// <summary>
        /// Readable type name for OpenAPI and the API surface file: generic arguments are spelled out
        /// (EnumerationResult&lt;Fleet&gt; rather than EnumerationResult`1).
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>Readable name.</returns>
        public static string FriendlyName(System.Type type)
        {
            if (type == null) throw new System.ArgumentNullException(nameof(type));
            if (type.IsArray) return FriendlyName(type.GetElementType()!) + "[]";
            if (!type.IsGenericType) return type.Name;
            string name = type.Name;
            int tick = name.IndexOf('`');
            if (tick > 0) name = name.Substring(0, tick);
            List<string> args = new List<string>();
            foreach (System.Type argument in type.GetGenericArguments()) args.Add(FriendlyName(argument));
            return name + "<" + string.Join(",", args) + ">";
        }
    }
}
