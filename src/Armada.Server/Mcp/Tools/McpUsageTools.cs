namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using Armada.Core.Services;
    using Armada.Core.Models;

    /// <summary>Account usage tools with the same caller scope as REST.</summary>
    public static class McpUsageTools
    {
        /// <summary>Register: account usage contract.</summary>
        public static void Register(RegisterToolDelegate register, UsageAccessService? service)
        {
            JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            register("usage_status", "Read sanitized account usage. Without dates returns latest; dated reads use [from,to). Default page 1, size 25, maximum 100.",
                new { type = "object", properties = new { captainId = new { type = "string" }, harborId = new { type = "string" }, runtime = new { type = "string" }, provider = new { type = "string" }, tenantId = new { type = "string" }, from = new { type = "string" }, to = new { type = "string" }, pageNumber = new { type = "integer", @default = 1 }, pageSize = new { type = "integer", @default = 25, minimum = 1, maximum = 100 } } },
                async args => {
                    if (service == null) return (object)McpToolError.Unavailable("Usage service unavailable.");
                    UsageIdentityArgs identity = Read<UsageIdentityArgs>(args, options);
                    await service.ScopeAsync(McpToolHelpers.ResolveCallerContext(), new UsageStatusArgs { TenantId = identity.TenantId, CaptainId = identity.CaptainId, HarborId = identity.HarborId }).ConfigureAwait(false);
                    UsageStatusArgs query = Read<UsageStatusArgs>(args, options);
                    return query.FromUtc.HasValue || query.ToUtc.HasValue ? (object)await service.SnapshotsAsync(McpToolHelpers.ResolveCallerContext(), query).ConfigureAwait(false) : await service.LatestAsync(McpToolHelpers.ResolveCallerContext(), query).ConfigureAwait(false);
                });
            register("capture_captain_usage", "Capture usage for a tenant-owned captain. Requires tenant administrator. Returns an idempotent queued job receipt; disabled capture is Conflict, cooldown is rate limited and offline host is Unavailable.",
                new { type = "object", properties = new { captainId = new { type = "string" }, harborId = new { type = "string" } }, required = new[] { "captainId" } },
                async args => {
                    if (service == null) return (object)McpToolError.Unavailable("Usage service unavailable.");
                    UsageCaptureArgs request = Read<UsageCaptureArgs>(args, options);
                    if (String.IsNullOrWhiteSpace(request.CaptainId)) throw new ArgumentException("captainId is required.");
                    try { return await service.CaptureAsync(McpToolHelpers.ResolveCallerContext(), request.CaptainId, request.HarborId).ConfigureAwait(false); }
                    catch (UsageRequestException ex) { return ex.StatusCode == 503 ? McpToolError.Unavailable(ex.Message) : McpToolError.Conflict(ex.Message); }
                });
        }
        private static T Read<T>(JsonElement? args, JsonSerializerOptions options) where T : new()
        {
            try { return args == null ? new T() : JsonSerializer.Deserialize<T>(args.Value, options) ?? new T(); }
            catch (JsonException) { throw new ArgumentException("Invalid usage arguments."); }
        }
    }
}
