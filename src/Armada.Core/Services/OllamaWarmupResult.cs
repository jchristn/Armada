namespace Armada.Core.Services
{
    /// <summary>
    /// Outcome of <see cref="OllamaModelWarmup.WarmAsync"/>.
    /// </summary>
    public sealed class OllamaWarmupResult
    {
        /// <summary>Whether a load request was sent. False when the endpoint is not Ollama or has no usable base URL.</summary>
        public bool Attempted { get; set; } = false;

        /// <summary>Whether the server answered the load request with a success status.</summary>
        public bool Succeeded { get; set; } = false;

        /// <summary>HTTP status of the reply, when one arrived.</summary>
        public int? HttpStatusCode { get; set; } = null;

        /// <summary>Time from sending the request to the reply or failure, in milliseconds.</summary>
        public long ElapsedMs { get; set; } = 0;

        /// <summary>Why the warm-up was skipped or failed, or null on success.</summary>
        public string? Reason { get; set; } = null;
    }
}
