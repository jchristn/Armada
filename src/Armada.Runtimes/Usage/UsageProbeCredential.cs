namespace Armada.Runtimes.Usage
{
    /// <summary>Host-local credential read result. Never persisted, logged or sent over the Harbor link.</summary>
    public class UsageProbeCredential
    {
        /// <summary>Existing bearer token; never refreshed by a quota probe.</summary>
        public string? AccessToken { get; set; }
        /// <summary>Stable provider-issued account identity, kept on this host.</summary>
        public string? AccountIdentity { get; set; }
        /// <summary>Active local profile reference for host-local HMAC namespacing.</summary>
        public string? Profile { get; set; }
    }
}
