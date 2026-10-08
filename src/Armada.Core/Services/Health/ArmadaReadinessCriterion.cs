namespace Armada.Core.Services.Health
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Grades Armada vessel readiness by wrapping <see cref="VesselReadinessService.EvaluateAsync"/> (evaluated as the
    /// vessel's tenant administrator). Pass with no issues, Warn with warnings only, Fail with any error. Writes
    /// ReadinessErrorCount.
    /// </summary>
    public class ArmadaReadinessCriterion : IVesselHealthCriterion
    {
        #region Public-Members

        /// <inheritdoc />
        public VesselHealthCriterionEnum Code => VesselHealthCriterionEnum.ArmadaReadiness;

        /// <inheritdoc />
        public bool RequiresRepository => false;

        #endregion

        #region Private-Members

        private readonly VesselReadinessService _Readiness;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="readiness">Vessel readiness service.</param>
        /// <exception cref="ArgumentNullException">Thrown when readiness is null.</exception>
        public ArmadaReadinessCriterion(VesselReadinessService readiness)
        {
            _Readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        }

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
            if (context == null) throw new ArgumentNullException(nameof(context));
            AuthContext auth = AuthContext.Authenticated(
                context.TenantId,
                context.Vessel.UserId ?? Constants.DefaultUserId,
                false,
                true,
                "VesselHealth",
                null,
                "Vessel Health");
            VesselReadinessResult result = context.Host != null
                ? await _Readiness.EvaluateAsync(auth, context.Vessel, context.Host, null, null, null, true, token).ConfigureAwait(false)
                : await _Readiness.EvaluateAsync(auth, context.Vessel, null, null, null, true, token).ConfigureAwait(false);
            context.Health.ReadinessErrorCount = result.ErrorCount;
            return Grade(result.ErrorCount, result.WarningCount);
        }

        /// <summary>
        /// Grade readiness issue counts.
        /// </summary>
        /// <param name="errors">Error count.</param>
        /// <param name="warnings">Warning count.</param>
        /// <returns>The result.</returns>
        public static VesselHealthCriterionResult Grade(int errors, int warnings)
        {
            if (errors > 0) return new VesselHealthCriterionResult(VesselHealthStatusEnum.Fail, VesselHealthDetailCodes.ReadinessErrors, errors, warnings);
            if (warnings > 0) return new VesselHealthCriterionResult(VesselHealthStatusEnum.Warn, VesselHealthDetailCodes.ReadinessWarnings, 0, warnings);
            return new VesselHealthCriterionResult(VesselHealthStatusEnum.Pass, VesselHealthDetailCodes.ReadinessOk, 0, 0);
        }

        #endregion
    }
}
