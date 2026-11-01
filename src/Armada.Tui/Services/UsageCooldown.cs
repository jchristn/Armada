namespace Armada.Tui.Services
{
    using System;
    using System.Text.Json;
    using Armada.Client;
    /// <summary>Read the server's cooldown deadline, preserving exact expiry in terminal controls.</summary>
    public static class UsageCooldown
    {
        /// <summary>Resolve a 429 response deadline, or a bounded one-minute wait when the server omitted it.</summary>
        public static DateTime Resolve(ArmadaApiException error, DateTime now)
        {
            try
            {
                UsageCooldownData? data = JsonSerializer.Deserialize<UsageCooldownData>(error.ResponseBody ?? "{}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return data?.Data?.NextEligibleUtc ?? data?.NextEligibleUtc ?? now.AddMinutes(1);
            }
            catch (JsonException) { return now.AddMinutes(1); }
        }
    }
}
