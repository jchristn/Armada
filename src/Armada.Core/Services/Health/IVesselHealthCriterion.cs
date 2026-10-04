namespace Armada.Core.Services.Health
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;

    /// <summary>
    /// One pluggable vessel health criterion. Implementations grade a single aspect of a vessel, write their measured
    /// values onto <see cref="VesselHealthContext.Health"/>, and return a raw status with a stable detail code. The
    /// evaluator isolates exceptions (an exception grades Unknown with <see cref="VesselHealthDetailCodes.EvaluationError"/>)
    /// and times each criterion.
    /// </summary>
    public interface IVesselHealthCriterion
    {
        /// <summary>
        /// Stable criterion code.
        /// </summary>
        VesselHealthCriterionEnum Code { get; }

        /// <summary>
        /// Whether the criterion needs a usable git repository. When true and neither the working directory nor the
        /// bare clone is usable, the evaluator records Unknown with RepositoryUnavailable without calling the criterion.
        /// </summary>
        bool RequiresRepository { get; }

        /// <summary>
        /// Whether the criterion applies to the vessel in this context. When false, the evaluator records
        /// NotApplicable (detail code BareRepository for a bare clone, otherwise NotApplicable) without calling
        /// <see cref="EvaluateAsync"/>.
        /// </summary>
        /// <param name="context">Evaluation context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the criterion applies.</returns>
        Task<bool> AppliesAsync(VesselHealthContext context, CancellationToken token = default);

        /// <summary>
        /// Evaluate the criterion.
        /// </summary>
        /// <param name="context">Evaluation context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The raw result.</returns>
        Task<VesselHealthCriterionResult> EvaluateAsync(VesselHealthContext context, CancellationToken token = default);
    }
}
