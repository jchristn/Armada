namespace Armada.Gate
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A model served by a Conductor virtual model runner.
    /// </summary>
    public sealed class GateConductorModel
    {
        /// <summary>The runner's path segment after /v1.0/api/.</summary>
        [JsonPropertyName("vmr")]
        public string Vmr { get; set; } = String.Empty;

        /// <summary>The model name the runner accepts.</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = String.Empty;
    }
}
