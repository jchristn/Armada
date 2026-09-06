namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ownership scope for shared configuration/definition entities (model endpoints, playbooks, runbooks,
    /// prompt templates, skills, workflow/project profiles, personas, pipelines). Determines who can see and
    /// edit the object: a tenant-wide object is visible to everyone in the tenant but editable only by tenant
    /// admins; a user-specific object is visible to and editable by its owning user (and tenant/global admins).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ScopeEnum
    {
        /// <summary>
        /// Shared across the whole tenant. Every user in the tenant can see and use it, but only tenant/global
        /// admins may edit or delete it.
        /// </summary>
        [EnumMember(Value = "TenantWide")]
        TenantWide,

        /// <summary>
        /// Owned by a single user. Visible to and editable by its owner (and tenant/global admins).
        /// </summary>
        [EnumMember(Value = "UserSpecific")]
        UserSpecific
    }
}
