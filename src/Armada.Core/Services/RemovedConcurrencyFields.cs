namespace Armada.Core.Services
{
    using System.Text.Json;
    /// <summary>Reject removed concurrency request fields before binding ignores them.</summary>
    public static class RemovedConcurrencyFields
    {
        /// <summary>Migration guidance for callers using the removed boolean limit.</summary>
        public const string Message = "AllowConcurrentMissions was removed in v1.3.0. Use MaxParallelVoyages and MaxParallelMissionsPerVoyage.";
        /// <summary>Whether a JSON request contains the removed vessel concurrency field.</summary>
        public static bool Contains(JsonElement body) => body.ValueKind == JsonValueKind.Object && body.EnumerateObject().Any(p => String.Equals(p.Name, "AllowConcurrentMissions", StringComparison.OrdinalIgnoreCase));
    }
}
