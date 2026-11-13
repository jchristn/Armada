namespace Armada.Runtimes.Usage
{
    /// <summary>Host-only projection of the provider's read-only OAuth userinfo response.</summary>
    internal class GeminiAccountInfo
    {
        /// <summary>Stable provider account ID, used only as input to the host HMAC.</summary>
        public string? Id { get; set; }
        /// <summary>Compared with the selected CLI profile; never transmitted to Armada.</summary>
        public string? Email { get; set; }
    }
}
