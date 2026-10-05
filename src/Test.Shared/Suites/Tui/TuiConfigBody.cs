namespace Test.Shared.Suites.Tui
{
    using Test.Shared.Infrastructure;

    /// <summary>
    /// A captured request body.
    /// </summary>
    internal sealed class TuiConfigBody
    {
        public string? Body { get; set; } = null;

        public int Count { get; set; } = 0;

        /// <summary>
        /// The captured body deserialized (case-insensitive names, string enums).
        /// </summary>
        /// <typeparam name="T">Body model.</typeparam>
        /// <returns>Model.</returns>
        /// <exception cref="AssertionException">Thrown when nothing was captured.</exception>
        public T As<T>()
        {
            if (Body == null) throw new AssertionException("no body captured");
            return JsonHelper.Deserialize<T>(Body);
        }
    }
}
