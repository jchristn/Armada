namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Body of a request to set a manual vessel health override for one criterion.
    /// </summary>
    public class VesselHealthOverrideRequest
    {
        #region Public-Members

        /// <summary>
        /// Overriding status. Required.
        /// </summary>
        public VesselHealthStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Operator note explaining the override, or null. Trimmed; at most 4000 characters are kept.
        /// </summary>
        public string? Note
        {
            get => _Note;
            set
            {
                string? trimmed = value?.Trim();
                if (trimmed != null && trimmed.Length > 4000) trimmed = trimmed.Substring(0, 4000);
                _Note = string.IsNullOrEmpty(trimmed) ? null : trimmed;
            }
        }

        #endregion

        #region Private-Members

        private string? _Note = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthOverrideRequest()
        {
        }

        #endregion
    }
}
