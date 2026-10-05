namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// One request a <see cref="StubHttpHandler"/> received: method, path, query, and body recorded together, so a test
    /// selects requests by route and reads the body as a typed model instead of pairing two queues by position.
    /// </summary>
    public class StubRequest
    {
        #region Public-Members

        /// <summary>
        /// Order of arrival (0-based) within the handler.
        /// </summary>
        public int Sequence { get; set; } = 0;

        /// <summary>
        /// Upper-case HTTP method.
        /// </summary>
        public string Method { get; set; } = "";

        /// <summary>
        /// Absolute path without the query (still URL-encoded, as on the wire).
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Raw query including the leading '?', or empty.
        /// </summary>
        public string Query { get; set; } = "";

        /// <summary>
        /// Request body text, or empty.
        /// </summary>
        public string Body { get; set; } = "";

        /// <summary>
        /// "METHOD path?query", for failure messages.
        /// </summary>
        public string Text
        {
            get { return Method + " " + Path + Query; }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the method and path match exactly (path compared ordinally, query ignored).
        /// </summary>
        /// <param name="method">HTTP method (any case).</param>
        /// <param name="path">Absolute path.</param>
        /// <returns>True on match.</returns>
        public bool Is(string method, string path)
        {
            return String.Equals(Method, method, StringComparison.OrdinalIgnoreCase) && String.Equals(Path, path, StringComparison.Ordinal);
        }

        /// <summary>
        /// Deserialize the body (case-insensitive names, string enums).
        /// </summary>
        /// <typeparam name="T">Body model.</typeparam>
        /// <returns>Model.</returns>
        /// <exception cref="AssertionException">Thrown when the body is empty or is not valid JSON for <typeparamref name="T"/>.</exception>
        public T BodyAs<T>()
        {
            if (String.IsNullOrWhiteSpace(Body)) throw new AssertionException(Text + " has no body to read as " + typeof(T).Name);
            try
            {
                T? value = JsonSerializer.Deserialize<T>(Body, JsonHelper.Options);
                if (value == null) throw new AssertionException(Text + " body is null JSON");
                return value;
            }
            catch (JsonException e)
            {
                throw new AssertionException(Text + " body is not a " + typeof(T).Name + ": " + e.Message + "\n" + Body);
            }
        }

        /// <summary>
        /// Deserialize the body, or return null when it is empty or does not parse as <typeparamref name="T"/>.
        /// For selecting requests inside a predicate.
        /// </summary>
        /// <typeparam name="T">Body model.</typeparam>
        /// <returns>Model or null.</returns>
        public T? TryBodyAs<T>() where T : class
        {
            if (String.IsNullOrWhiteSpace(Body)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(Body, JsonHelper.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Decoded query parameters.
        /// </summary>
        /// <returns>Name to values.</returns>
        public Dictionary<string, List<string>> QueryParameters()
        {
            return QueryString.Parse(Query);
        }

        /// <summary>
        /// First decoded value of a query parameter, or null when absent.
        /// </summary>
        /// <param name="name">Parameter name (case-sensitive).</param>
        /// <returns>Value or null.</returns>
        public string? QueryValue(string name)
        {
            return QueryString.Get(Query, name);
        }

        /// <summary>
        /// Top-level body property (case-insensitive name), or null when absent. For wire-shape checks such as
        /// "sent as an explicit null" or "not sent at all".
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <returns>Shape or null.</returns>
        public JsonPropertyShape? BodyProperty(string name)
        {
            return JsonShape.TopLevelProperty(Body, name);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Text;
        }

        #endregion
    }
}
