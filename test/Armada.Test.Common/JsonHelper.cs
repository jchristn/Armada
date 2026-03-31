namespace Armada.Test.Common
{
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// Shared JSON serialization options and deserialization helpers for tests.
    /// Uses case-insensitive deserialization to handle the server's camelCase responses.
    /// Does NOT use camelCase naming policy for serialization — Watson WebServer expects PascalCase request bodies.
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>
        /// Shared serializer options: case-insensitive input, string enums, null omission.
        /// No naming policy — serialization preserves property names as-is (PascalCase for anonymous types).
        /// </summary>
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Deserialize the response body into a typed object.
        /// </summary>
        public static async Task<T> DeserializeAsync<T>(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(body, Options)!;
        }

        /// <summary>
        /// Deserialize a JSON string into a typed object.
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Options)!;
        }

        /// <summary>
        /// Serialize an object to JSON string.
        /// </summary>
        public static string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, Options);
        }

        /// <summary>
        /// Create a StringContent with JSON payload for HTTP requests.
        /// </summary>
        public static StringContent ToJsonContent(object value)
        {
            return new StringContent(
                JsonSerializer.Serialize(value, Options),
                Encoding.UTF8,
                "application/json");
        }
    }
}
