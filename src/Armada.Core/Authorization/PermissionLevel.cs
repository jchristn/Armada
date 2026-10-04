namespace Armada.Core.Authorization
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Permission levels for API authorization. Every REST route and MCP tool declares one of these through its
    /// <see cref="AuthorizationRequirement"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PermissionLevel
    {
        /// <summary>
        /// No authentication required.
        /// </summary>
        NoAuthRequired,

        /// <summary>
        /// Requires a valid authenticated identity.
        /// </summary>
        Authenticated,

        /// <summary>
        /// Requires an authenticated admin identity.
        /// </summary>
        AdminOnly,

        /// <summary>
        /// Requires a global admin or a tenant-scoped admin identity.
        /// </summary>
        TenantAdmin
    }
}
