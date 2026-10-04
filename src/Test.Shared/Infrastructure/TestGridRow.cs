namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Row type for grid tests.
    /// </summary>
    public sealed class TestGridRow
    {
        /// <summary>
        /// Id.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Status.
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// Count.
        /// </summary>
        public int Count { get; set; }
    }
}
