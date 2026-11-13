namespace Armada.Core.Services
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// One timed captain-selection stage and its span. Records duration with a bounded outcome on dispose; a stage that
    /// is disposed without <see cref="Complete"/> is recorded as Error with an error span status.
    /// </summary>
    public sealed class CaptainSelectionStage : IDisposable
    {
        #region Private-Members

        private readonly string _Stage;
        private readonly Stopwatch _Watch = Stopwatch.StartNew();
        private readonly Activity? _Activity;
        private string? _Outcome;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        internal CaptainSelectionStage(string stage, string? missionId)
        {
            _Stage = stage;
            _Activity = CaptainSelectionTelemetry.StartActivity(stage);
            try { if (_Activity != null && !String.IsNullOrEmpty(missionId)) _Activity.SetTag("armada.mission_id", missionId); }
            catch (Exception) { }
        }

        #endregion

        #region Public-Methods

        /// <summary>Mark the stage finished with a bounded outcome code.</summary>
        /// <param name="outcome">Outcome code, for example Ok, Waiting, Admitted or Conflict.</param>
        public void Complete(string outcome = "Ok") => _Outcome = CaptainSelectionTelemetry.Label(outcome);

        /// <summary>Record the duration, outcome and span status.</summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            string outcome = _Outcome ?? "Error";
            CaptainSelectionTelemetry.RecordStage(_Stage, outcome, _Watch.Elapsed.TotalSeconds);
            try
            {
                if (_Activity == null) return;
                _Activity.SetTag("armada.selection.outcome", outcome);
                _Activity.SetStatus(_Outcome == null ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
                _Activity.Dispose();
            }
            catch (Exception) { }
        }

        #endregion
    }
}
