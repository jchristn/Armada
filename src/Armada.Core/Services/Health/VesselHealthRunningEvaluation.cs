namespace Armada.Core.Services.Health
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-memory record of a vessel health evaluation job running in this process.
    /// </summary>
    internal sealed class VesselHealthRunningEvaluation
    {
        public string TenantId { get; set; } = "";

        public string JobId { get; set; } = "";

        public CancellationTokenSource Cancellation { get; set; } = new CancellationTokenSource();

        public Task Completion { get; set; } = Task.CompletedTask;

        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    }
}
