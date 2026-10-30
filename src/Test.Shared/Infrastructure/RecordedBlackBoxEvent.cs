namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One event recorded by <see cref="RecordingBlackBoxEventSink"/>.
    /// </summary>
    public sealed class RecordedBlackBoxEvent
    {
        /// <summary>Event type.</summary>
        public string EventType { get; set; } = "";

        /// <summary>Message.</summary>
        public string Message { get; set; } = "";

        /// <summary>Entry id.</summary>
        public string? EntryId { get; set; }

        /// <summary>Mission id.</summary>
        public string? MissionId { get; set; }

        /// <summary>Payload.</summary>
        public object? Payload { get; set; }
    }
}
