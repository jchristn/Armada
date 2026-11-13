namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>Creation-time slot defaults. Changing runtime alone must not rewrite a stored captain limit.</summary>
    public static class CaptainConcurrencyDefaults
    {
        /// <summary>Endpoint and Mux captains default to four slots; all CLI runtimes (including pi) default to one.</summary>
        public static int For(AgentRuntimeEnum runtime, ConcurrencySettings? settings = null)
        {
            bool endpoint = runtime == AgentRuntimeEnum.ApiEndpoint || runtime == AgentRuntimeEnum.Mux;
            int value = endpoint
                ? settings?.DefaultEndpointCaptainMaxConcurrency ?? Constants.DefaultEndpointCaptainMaxConcurrency
                : settings?.DefaultCliCaptainMaxConcurrency ?? Constants.DefaultCliCaptainMaxConcurrency;
            return ConcurrencyClamp.CaptainLimit(value, settings?.MaxCaptainConcurrency ?? Constants.CaptainConcurrencyCeiling);
        }
    }
}
