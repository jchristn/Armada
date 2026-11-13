namespace Armada.Server.Mcp
{
    /// <summary>Identity-only first pass, before validating secondary usage filters.</summary>
    public class UsageIdentityArgs
    {
        /// <summary>Explicit global-administrator tenant scope.</summary>
        public string? TenantId { get; set; }
        /// <summary>Captain filter.</summary>
        public string? CaptainId { get; set; }
        /// <summary>Harbor filter.</summary>
        public string? HarborId { get; set; }
    }
}
