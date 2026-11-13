namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// A black box entry and one of its runnable recovery paths, as seeded by the black box API suite.
    /// </summary>
    public sealed class RecoveryTarget
    {
        /// <summary>Entry id.</summary>
        public string EntryId { get; set; } = "";

        /// <summary>Recovery path id (for example bbx-scope-002.restart).</summary>
        public string PathId { get; set; } = "";
    }
}
