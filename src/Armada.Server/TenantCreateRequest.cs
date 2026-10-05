namespace Armada.Server
{
    /// <summary>
    /// Optional fields of a <c>POST /api/v1/tenants</c> body beyond <see cref="Armada.Core.Models.TenantMetadata"/>.
    /// </summary>
    public class TenantCreateRequest
    {
        /// <summary>
        /// Password for the tenant's seeded admin account (<c>admin@armada</c>). When omitted the server generates a
        /// random password and returns it once in the response. Must be at least
        /// <see cref="Armada.Core.Models.PasswordChangeRequest.MinimumLength"/> characters and not the default password.
        /// </summary>
        public string? AdminPassword { get; set; }
    }
}
