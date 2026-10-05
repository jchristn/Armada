namespace Test.Shared.Infrastructure
{
    using System.Text.Json;

    /// <summary>
    /// One property seen while reading a JSON document: its name, nesting depth, and the token that starts its value.
    /// Used by tests that must check the wire shape itself (a property is absent, or is an explicit null, or is sent
    /// as a string rather than a number), which a typed deserialization cannot express.
    /// </summary>
    public class JsonPropertyShape
    {
        #region Public-Members

        /// <summary>
        /// Property name exactly as written.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Depth of the object that holds the property (1 for a top-level object's properties).
        /// </summary>
        public int Depth { get; set; } = 0;

        /// <summary>
        /// Token that starts the property's value (String, Number, True, False, Null, StartObject, StartArray).
        /// </summary>
        public JsonTokenType ValueToken { get; set; } = JsonTokenType.None;

        /// <summary>
        /// The value when it is a string or number token, otherwise null.
        /// </summary>
        public string? ScalarText { get; set; } = null;

        #endregion
    }
}
