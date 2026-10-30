namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;

    /// <summary>
    /// Records black box events for assertions; optionally throws (a sink must never break capture).
    /// </summary>
    public sealed class RecordingBlackBoxEventSink : IBlackBoxEventSink
    {
        /// <summary>Recorded event types with their payloads.</summary>
        public ConcurrentQueue<RecordedBlackBoxEvent> Events { get; } = new ConcurrentQueue<RecordedBlackBoxEvent>();

        /// <summary>When true, EmitAsync throws after recording.</summary>
        public bool Throw { get; set; } = false;

        /// <summary>Count of events of a type.</summary>
        /// <param name="eventType">Event type.</param>
        /// <returns>The count.</returns>
        public int Count(string eventType)
        {
            return Events.Count(e => e.EventType == eventType);
        }

        /// <inheritdoc />
        public Task EmitAsync(string eventType, string message, string? tenantId, string? entryId, string? missionId, string? voyageId, string? vesselId, object payload, CancellationToken token = default)
        {
            Events.Enqueue(new RecordedBlackBoxEvent { EventType = eventType, Message = message, EntryId = entryId, MissionId = missionId, Payload = payload });
            if (Throw) throw new InvalidOperationException("scripted sink failure");
            return Task.CompletedTask;
        }
    }
}
