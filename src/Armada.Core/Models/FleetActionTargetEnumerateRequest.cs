namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Enumeration query for the targets of one fleet action run.
    /// </summary>
    public class FleetActionTargetEnumerateRequest
    {
        #region Public-Members

        /// <summary>
        /// Page number, starting at 1. Values below 1 are clamped to 1.
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Page size. Default 25, minimum 1, maximum 500.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = value < 1 ? 1 : (value > 500 ? 500 : value);
        }

        /// <summary>
        /// Optional status filter.
        /// </summary>
        public FleetActionTargetStatusEnum? Status { get; set; } = null;

        #endregion

        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 25;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionTargetEnumerateRequest()
        {
        }

        #endregion
    }
}
