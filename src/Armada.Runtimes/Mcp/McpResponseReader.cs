namespace Armada.Runtimes.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Reads the JSON-RPC response to one request out of an MCP streamable-HTTP body, which is either a plain JSON object
    /// or a <c>text/event-stream</c> (SSE). In an SSE body each event's <c>data:</c> lines are joined with newlines (per the
    /// SSE specification) and the event whose JSON-RPC <c>id</c> matches the request is selected, so progress and log
    /// notifications sent on the same stream are never mistaken for the result.
    /// </summary>
    public static class McpResponseReader
    {
        #region Public-Methods

        /// <summary>
        /// Split an SSE body into the data payload of each event.
        /// </summary>
        /// <param name="body">The SSE body.</param>
        /// <returns>One string per event that carried data.</returns>
        public static List<string> ReadEventData(string? body)
        {
            List<string> events = new List<string>();
            if (String.IsNullOrEmpty(body)) return events;

            StringBuilder current = new StringBuilder();
            bool hasData = false;
            string[] lines = body!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
            {
                if (line.Length == 0)
                {
                    if (hasData) events.Add(current.ToString());
                    current.Clear();
                    hasData = false;
                    continue;
                }

                if (line.StartsWith(":", StringComparison.Ordinal)) continue;

                string field = line;
                string value = String.Empty;
                int colon = line.IndexOf(':');
                if (colon >= 0)
                {
                    field = line.Substring(0, colon);
                    value = line.Substring(colon + 1);
                    if (value.StartsWith(" ", StringComparison.Ordinal)) value = value.Substring(1);
                }

                if (!String.Equals(field, "data", StringComparison.Ordinal)) continue;
                if (hasData) current.Append('\n');
                current.Append(value);
                hasData = true;
            }

            if (hasData) events.Add(current.ToString());
            return events;
        }

        /// <summary>
        /// Select and deserialize the response to the request with the given id.
        /// </summary>
        /// <typeparam name="T">Type of the <c>result</c> member.</typeparam>
        /// <param name="body">Response body (plain JSON or SSE).</param>
        /// <param name="requestId">The request id in canonical string form.</param>
        /// <returns>The typed response, or null when the body carries no response for the request.</returns>
        /// <exception cref="McpClientException">Thrown when the selected response is not valid JSON-RPC.</exception>
        public static JsonRpcResponse<T>? ReadResponse<T>(string? body, string requestId) where T : class
        {
            if (String.IsNullOrWhiteSpace(body)) return null;

            List<string> candidates = new List<string>();
            if (body!.TrimStart().StartsWith("{", StringComparison.Ordinal)) candidates.Add(body);
            else candidates.AddRange(ReadEventData(body));

            foreach (string candidate in candidates)
            {
                if (String.IsNullOrWhiteSpace(candidate)) continue;

                JsonRpcEnvelopeHeader? header;
                try
                {
                    header = JsonSerializer.Deserialize<JsonRpcEnvelopeHeader>(candidate);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (header == null || header.Method != null) continue;
                if (!String.Equals(header.Id, requestId, StringComparison.Ordinal)) continue;

                try
                {
                    return JsonSerializer.Deserialize<JsonRpcResponse<T>>(candidate);
                }
                catch (JsonException ex)
                {
                    throw new McpClientException("MCP response to request " + requestId + " is not a valid JSON-RPC response: " + ex.Message, ex);
                }
            }

            return null;
        }

        #endregion
    }
}
