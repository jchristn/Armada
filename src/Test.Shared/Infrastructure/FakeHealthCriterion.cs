namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services.Health;

    /// <summary>
    /// Configurable vessel health criterion for evaluator and service tests: counts evaluations, returns a fixed
    /// result, can throw, and can block on a gate so a job stays running.
    /// </summary>
    public sealed class FakeHealthCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code { get; set; } = VesselHealthCriterionEnum.GitDivergence;

        /// <inheritdoc />
        public bool RequiresRepository { get; set; } = false;

        /// <summary>
        /// Status returned by each evaluation.
        /// </summary>
        public VesselHealthStatusEnum Status { get; set; } = VesselHealthStatusEnum.Pass;

        /// <summary>
        /// Detail code returned by each evaluation.
        /// </summary>
        public string? DetailCode { get; set; } = "Fake";

        /// <summary>
        /// When true, evaluation throws.
        /// </summary>
        public bool Throw { get; set; } = false;

        /// <summary>
        /// When set, evaluation waits for this task before returning.
        /// </summary>
        public TaskCompletionSource<bool>? Gate { get; set; } = null;

        /// <summary>
        /// Completes when the first evaluation has started (before it waits on <see cref="Gate"/>), so a test can act
        /// once the criterion is known to be running instead of polling for a proxy such as the job status.
        /// </summary>
        public TaskCompletionSource<bool> FirstEvaluationStarted { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Number of evaluations performed.
        /// </summary>
        public int Evaluations => _Evaluations;

        #endregion

        #region Private-Members

        private int _Evaluations = 0;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<bool> AppliesAsync(VesselHealthContext context, CancellationToken token = default)
        {
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public async Task<VesselHealthCriterionResult> EvaluateAsync(VesselHealthContext context, CancellationToken token = default)
        {
            Interlocked.Increment(ref _Evaluations);
            FirstEvaluationStarted.TrySetResult(true);
            if (Gate != null) await Gate.Task.WaitAsync(token).ConfigureAwait(false);
            if (Throw) throw new InvalidOperationException("simulated criterion failure");
            return new VesselHealthCriterionResult(Status, DetailCode, 1, 2);
        }

        #endregion
    }
}
