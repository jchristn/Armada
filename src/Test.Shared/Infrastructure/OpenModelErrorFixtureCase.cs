namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>One open-model server error response and its expected classification.</summary>
    public sealed class OpenModelErrorFixtureCase
    {
        /// <summary>Server that produced the response.</summary>
        [JsonPropertyName("server")]
        public string Server { get; set; } = "";

        /// <summary>"captured" for a recorded live response, "upstream" for the server's source text.</summary>
        [JsonPropertyName("provenance")]
        public string Provenance { get; set; } = "";

        /// <summary>HTTP status.</summary>
        [JsonPropertyName("status")]
        public int Status { get; set; } = 0;

        /// <summary>Response body.</summary>
        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        /// <summary>Expected machine error type.</summary>
        [JsonPropertyName("errorType")]
        public string ErrorType { get; set; } = "";

        /// <summary>Expected runtime failure kind name.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";
    }
}
