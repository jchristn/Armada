namespace Armada.Client
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Per-call overrides for an <see cref="ArmadaClient"/> request.
    /// </summary>
    public class ArmadaRequestOptions
    {
        #region Public-Members

        /// <summary>
        /// Timeout for this call in milliseconds, or null to use <see cref="ArmadaClientOptions.TimeoutMs"/>.
        /// Clamped to 1000..3600000 when set.
        /// </summary>
        public int? TimeoutMs
        {
            get { return _TimeoutMs; }
            set { _TimeoutMs = value.HasValue ? Math.Clamp(value.Value, 1000, 3600000) : null; }
        }

        /// <summary>
        /// Non-success statuses whose JSON body is returned like a success instead of throwing (for example 409 from a
        /// vessel health evaluation that is already running). Never null.
        /// </summary>
        public List<int> AcceptStatuses
        {
            get { return _AcceptStatuses; }
            set { _AcceptStatuses = value ?? new List<int>(); }
        }

        #endregion

        #region Private-Members

        private int? _TimeoutMs = null;
        private List<int> _AcceptStatuses = new List<int>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with no overrides.
        /// </summary>
        public ArmadaRequestOptions()
        {
        }

        /// <summary>
        /// Options with a timeout override.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>Options.</returns>
        public static ArmadaRequestOptions WithTimeout(int timeoutMs)
        {
            ArmadaRequestOptions options = new ArmadaRequestOptions();
            options.TimeoutMs = timeoutMs;
            return options;
        }

        #endregion
    }
}
