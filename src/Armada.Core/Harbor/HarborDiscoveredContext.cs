namespace Armada.Core.Harbor
{
    using System;

    /// <summary>
    /// What a Harbor learned about one local model endpoint's served context window (RS-7.8) the last time an API captain
    /// launched against it. Kept on the Harbor so its app can show it (RS-12.19).
    /// </summary>
    public class HarborDiscoveredContext
    {
        /// <summary>
        /// Endpoint name as the Admiral configured it.
        /// </summary>
        public string EndpointName { get; set; } = String.Empty;

        /// <summary>
        /// Model asked about, or null when the endpoint names none.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Served context window in tokens, or null when the model server did not report one.
        /// </summary>
        public int? ContextLength { get; set; } = null;

        /// <summary>
        /// Where <see cref="ContextLength"/> was read, or null.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Why no window was found, or null when one was.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// When the window was read (UTC).
        /// </summary>
        public DateTime DiscoveredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborDiscoveredContext()
        {
        }
    }
}
