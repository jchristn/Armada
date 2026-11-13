namespace Armada.Core.Services
{
    using System;
    using System.Diagnostics;

    /// <summary>Operation scope recording duration, failures and active depth even when work throws.</summary>
    public sealed class UsageTelemetryOperation : IDisposable
    {
        private readonly string _Operation;
        private readonly string _Runtime;
        private readonly Stopwatch _Clock = Stopwatch.StartNew();
        private readonly Activity? _Activity;
        private string _Outcome = "Failed";
        private bool _Disposed;

        internal UsageTelemetryOperation(string operation, string runtime)
        {
            _Operation = operation; _Runtime = runtime;
            _Activity = UsageTelemetry.StartActivity(operation);
            UsageTelemetry.Active(operation, 1);
        }

        /// <summary>Mark success or a fixed protocol status. Never supply arbitrary error messages.</summary>
        public void Complete(string outcome = "Ok") { _Outcome = outcome; }

        /// <summary>Complete the measurement; telemetry listeners cannot fail the caller's operation.</summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            UsageTelemetry.Active(_Operation, -1);
            UsageTelemetry.Finish(_Operation, _Runtime, _Outcome, _Clock.Elapsed.TotalSeconds);
            try
            {
                _Activity?.SetStatus(_Outcome == "Ok" || _Outcome == "Estimated" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
                _Activity?.Dispose();
            }
            catch (Exception) { }
        }
    }
}
