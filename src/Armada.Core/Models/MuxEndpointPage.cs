namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One page of the endpoints saved in a Mux configuration directory, ordered by name. The mux CLI reads the whole
    /// configuration file and has no paging of its own, so the Admiral pages its answer: <see cref="EnumerationResult{T}.Objects"/>
    /// holds one page and <see cref="EnumerationResult{T}.TotalRecords"/> counts every saved endpoint. When mux fails,
    /// <see cref="EnumerationResult{T}.Success"/> is false and <see cref="ErrorCode"/> and <see cref="ErrorMessage"/> say why.
    /// </summary>
    public class MuxEndpointPage : EnumerationResult<MuxEndpointInfo>
    {
        #region Public-Members

        /// <summary>
        /// Structured output contract version mux reported, or 0 when mux did not answer.
        /// </summary>
        public int ContractVersion { get; set; } = 0;

        /// <summary>
        /// Harbor whose mux answered, or null when the Admiral ran mux itself (standalone mode).
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Mux config directory that was read.
        /// </summary>
        public string ConfigDirectory { get; set; } = String.Empty;

        /// <summary>
        /// Machine-readable error code when <see cref="EnumerationResult{T}.Success"/> is false, otherwise empty.
        /// </summary>
        public string ErrorCode { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable error message when <see cref="EnumerationResult{T}.Success"/> is false, otherwise empty.
        /// </summary>
        public string ErrorMessage { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MuxEndpointPage()
        {
        }

        #endregion
    }
}
