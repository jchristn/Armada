namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// A captured black box entry and its mission (<see cref="AskBlackBoxFixture.CaptureAsync"/>).
    /// </summary>
    public sealed class AskBlackBoxCaptured
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="entryId">Entry.</param>
        /// <param name="missionId">Mission.</param>
        public AskBlackBoxCaptured(string entryId, string missionId)
        {
            EntryId = entryId;
            MissionId = missionId;
        }

        /// <summary>Entry (bbx_ prefix).</summary>
        public string EntryId { get; }

        /// <summary>Mission (msn_ prefix).</summary>
        public string MissionId { get; }
    }
}
