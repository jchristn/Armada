namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Models;

    /// <summary>
    /// Test summary writer: returns <see cref="Text"/>, throws when <see cref="Throw"/> is set, or waits
    /// <see cref="DelayMs"/> first (honoring cancellation). Counts its calls.
    /// </summary>
    public sealed class ScriptedBlackBoxSummaryWriter : IBlackBoxSummaryWriter
    {
        /// <summary>Text to return (null returns null).</summary>
        public string? Text { get; set; } = "A model wrote this summary.";

        /// <summary>Throw instead of answering.</summary>
        public bool Throw { get; set; } = false;

        /// <summary>Delay before answering, in milliseconds.</summary>
        public int DelayMs { get; set; } = 0;

        /// <summary>Calls made.</summary>
        public int Calls { get; private set; } = 0;

        /// <summary>Endpoint id of the last call.</summary>
        public string? LastEndpointId { get; private set; } = null;

        /// <inheritdoc />
        public async Task<string?> WriteSummaryAsync(string endpointId, BlackBoxRecord record, BlackBoxAnalysis analysis, CancellationToken token = default)
        {
            Calls++;
            LastEndpointId = endpointId;
            if (DelayMs > 0) await Task.Delay(DelayMs, token).ConfigureAwait(false);
            if (Throw) throw new InvalidOperationException("scripted summary failure");
            return Text;
        }
    }
}
