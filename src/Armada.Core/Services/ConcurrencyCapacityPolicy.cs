namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>Capacity accounting shared by admission and overrun observation.</summary>
    internal static class ConcurrencyCapacityPolicy
    {
        internal static Dictionary<AgentRuntimeEnum, int> RuntimeLimits(IEnumerable<AgentSettings>? agents)
        {
            Dictionary<AgentRuntimeEnum, int> limits = new Dictionary<AgentRuntimeEnum, int>();
            foreach (AgentSettings agent in agents ?? Enumerable.Empty<AgentSettings>())
                if (agent.MaxConcurrent > 0 && (!limits.TryGetValue(agent.Runtime, out int existing) || agent.MaxConcurrent < existing))
                    limits[agent.Runtime] = agent.MaxConcurrent;
            return limits;
        }

        internal static string EndpointKey(ModelEndpoint endpoint)
        {
            string url = (endpoint.BaseUrl ?? String.Empty).Trim().TrimEnd('/');
            return String.IsNullOrEmpty(url) ? "endpoint:" + endpoint.Id : url.ToLowerInvariant();
        }

        // Preserve the staged migration's Working-with-no-count fallback used by admission.
        internal static int Occupancy(Captain captain) => captain.ActiveMissionCount > 0 ? captain.ActiveMissionCount
            : captain.State == CaptainStateEnum.Working ? 1 : 0;
    }
}
