namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One array found in a JSON document by <see cref="JsonArrays"/>: where it is, how many items it holds, and whether
    /// it is the Objects list of an enumeration page.
    /// </summary>
    public class JsonArrayShape
    {
        /// <summary>
        /// Property name holding the array, or "[]" for an array nested directly in another array or at the root.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Dotted path from the root, with [] for array items, for example Objects[].Tags.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Number of items in the array.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// True when the array is the Objects property of an object that also carries PageNumber, PageSize, and
        /// TotalRecords (an EnumerationResult page).
        /// </summary>
        public bool IsPageObjects { get; set; } = false;
    }
}
