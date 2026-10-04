namespace Armada.Core.Services.Health
{
    using Armada.Core.Enums;

    /// <summary>
    /// The raw outcome of one vessel health criterion: a status plus a stable detail code and two typed values (see
    /// <see cref="VesselHealthDetailCodes"/> for the meaning of each code's values).
    /// </summary>
    public class VesselHealthCriterionResult
    {
        #region Public-Members

        /// <summary>
        /// Raw status. Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum Status { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Stable detail code, or null.
        /// </summary>
        public string? DetailCode { get; set; } = null;

        /// <summary>
        /// First typed value, or null.
        /// </summary>
        public long? ValueA { get; set; } = null;

        /// <summary>
        /// Second typed value, or null.
        /// </summary>
        public long? ValueB { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthCriterionResult()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="status">Raw status.</param>
        /// <param name="detailCode">Stable detail code, or null.</param>
        /// <param name="valueA">First typed value, or null.</param>
        /// <param name="valueB">Second typed value, or null.</param>
        public VesselHealthCriterionResult(VesselHealthStatusEnum status, string? detailCode, long? valueA = null, long? valueB = null)
        {
            Status = status;
            DetailCode = detailCode;
            ValueA = valueA;
            ValueB = valueB;
        }

        #endregion
    }
}
