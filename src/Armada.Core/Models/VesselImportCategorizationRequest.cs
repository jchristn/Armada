namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Optional fleet categorization requested with an import: after the vessels are created, a captain analyzes the
    /// imported repositories and recommends fleets for them.
    /// </summary>
    public class VesselImportCategorizationRequest
    {
        #region Public-Members

        /// <summary>
        /// True to run fleet categorization after the import. Default false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Captain (cpt_ prefix) that performs the analysis. Required when <see cref="Enabled"/> is true; the captain
        /// must exist in the caller's tenant.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Instructions for the captain, or null/empty to use the import.fleet_categorization prompt template. The
        /// Admiral always appends the output-format contract, so editing this text cannot break result parsing.
        /// Values longer than <see cref="MaxPromptLength"/> characters are truncated.
        /// </summary>
        public string? Prompt
        {
            get => _Prompt;
            set => _Prompt = value == null ? null : (value.Length > MaxPromptLength ? value.Substring(0, MaxPromptLength) : value);
        }

        /// <summary>
        /// True to apply the captain's recommendations automatically when the analysis completes (fleets are created
        /// or reused and vessels assigned). Default false: recommendations wait for review.
        /// </summary>
        public bool ApplyAutomatically { get; set; } = false;

        /// <summary>
        /// Maximum prompt length in characters (32768).
        /// </summary>
        public const int MaxPromptLength = 32768;

        #endregion

        #region Private-Members

        private string? _Prompt = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportCategorizationRequest()
        {
        }

        #endregion
    }
}
