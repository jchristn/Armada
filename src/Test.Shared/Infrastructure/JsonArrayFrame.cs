namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// One open object or array while <see cref="JsonArrays"/> reads a document.
    /// </summary>
    internal sealed class JsonArrayFrame
    {
        /// <summary>
        /// True for an array, false for an object.
        /// </summary>
        public bool IsArray { get; set; } = false;

        /// <summary>
        /// Property name holding the value, or "[]" inside an array or at the root.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Dotted path from the root.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Items read so far (arrays).
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Enclosing frame, or null at the root.
        /// </summary>
        public JsonArrayFrame? Parent { get; set; } = null;

        /// <summary>
        /// Property names read so far (objects).
        /// </summary>
        public List<string> PropertyNames { get; } = new List<string>();
    }
}
