namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// In-memory <see cref="IFleetActionMissionDispatcher"/> for fleet action tests. Dispatched voyages start
    /// Running; tests drive outcomes with <see cref="SetOutcome"/>. Tracks the peak number of simultaneously running
    /// voyages. Thread-safe.
    /// </summary>
    public sealed class StubFleetActionMissionDispatcher : IFleetActionMissionDispatcher
    {
        #region Public-Members

        /// <summary>
        /// Vessel IDs whose dispatch validation is rejected with <see cref="RejectionMessage"/>.
        /// </summary>
        public HashSet<string> RejectVesselIds { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Message returned for rejected vessels.
        /// </summary>
        public string RejectionMessage { get; set; } = "Pipeline not found: stub";

        /// <summary>
        /// Peak number of voyages Running at the same time.
        /// </summary>
        public int MaxActive
        {
            get
            {
                lock (_Lock) return _MaxActive;
            }
        }

        /// <summary>
        /// Number of voyages currently Running.
        /// </summary>
        public int ActiveCount
        {
            get
            {
                lock (_Lock) return _Outcomes.Values.Count(o => o == FleetActionVoyageOutcomeEnum.Running);
            }
        }

        /// <summary>
        /// Voyage IDs in dispatch order.
        /// </summary>
        public List<string> Dispatched
        {
            get
            {
                lock (_Lock) return new List<string>(_Order);
            }
        }

        /// <summary>
        /// Voyage IDs that were cancelled through <see cref="CancelVoyageAsync"/>.
        /// </summary>
        public List<string> CancelledVoyages
        {
            get
            {
                lock (_Lock) return new List<string>(_Cancelled);
            }
        }

        /// <summary>
        /// Last prompt dispatched per vessel ID.
        /// </summary>
        public Dictionary<string, string> PromptsByVessel
        {
            get
            {
                lock (_Lock) return new Dictionary<string, string>(_Prompts);
            }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly Dictionary<string, FleetActionVoyageOutcomeEnum> _Outcomes = new Dictionary<string, FleetActionVoyageOutcomeEnum>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _Prompts = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _Order = new List<string>();
        private readonly List<string> _Cancelled = new List<string>();
        private int _MaxActive = 0;
        private int _Counter = 0;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<DispatchValidationResult> ValidateAsync(Vessel vessel, string? pipelineId, CancellationToken token = default)
        {
            if (RejectVesselIds.Contains(vessel.Id))
                return Task.FromResult(DispatchValidationResult.Invalid(DispatchValidationErrorEnum.PipelineNotFound, RejectionMessage));
            return Task.FromResult(DispatchValidationResult.Valid(pipelineId, false));
        }

        /// <inheritdoc />
        public Task<string> DispatchAsync(Vessel vessel, string title, string prompt, string? pipelineId, CancellationToken token = default)
        {
            lock (_Lock)
            {
                _Counter++;
                string id = "vyg_stub" + _Counter.ToString("D4");
                _Outcomes[id] = FleetActionVoyageOutcomeEnum.Running;
                _Order.Add(id);
                _Prompts[vessel.Id] = prompt;
                int active = _Outcomes.Values.Count(o => o == FleetActionVoyageOutcomeEnum.Running);
                if (active > _MaxActive) _MaxActive = active;
                return Task.FromResult(id);
            }
        }

        /// <inheritdoc />
        public Task<FleetActionVoyageOutcomeEnum> GetOutcomeAsync(string voyageId, CancellationToken token = default)
        {
            lock (_Lock)
            {
                if (!_Outcomes.TryGetValue(voyageId, out FleetActionVoyageOutcomeEnum outcome)) return Task.FromResult(FleetActionVoyageOutcomeEnum.Missing);
                return Task.FromResult(outcome);
            }
        }

        /// <summary>
        /// When true, <see cref="CancelVoyageAsync"/> throws, simulating a database or dispatcher failure.
        /// </summary>
        public bool ThrowOnCancel { get; set; } = false;

        /// <inheritdoc />
        public Task CancelVoyageAsync(string voyageId, CancellationToken token = default)
        {
            if (ThrowOnCancel) throw new InvalidOperationException("Simulated voyage cancel failure for " + voyageId);
            lock (_Lock)
            {
                _Cancelled.Add(voyageId);
                if (_Outcomes.ContainsKey(voyageId)) _Outcomes[voyageId] = FleetActionVoyageOutcomeEnum.Cancelled;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Set a voyage's outcome.
        /// </summary>
        /// <param name="voyageId">Voyage ID.</param>
        /// <param name="outcome">Outcome.</param>
        public void SetOutcome(string voyageId, FleetActionVoyageOutcomeEnum outcome)
        {
            lock (_Lock) _Outcomes[voyageId] = outcome;
        }

        /// <summary>
        /// Voyage IDs currently Running.
        /// </summary>
        /// <returns>Running voyage IDs.</returns>
        public List<string> RunningVoyages()
        {
            lock (_Lock) return _Outcomes.Where(kv => kv.Value == FleetActionVoyageOutcomeEnum.Running).Select(kv => kv.Key).ToList();
        }

        #endregion
    }
}
