namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One trailer of a commit message (for example <c>Armada-Mission-Id: msn_...</c>).
    /// </summary>
    public class GitTrailer
    {
        #region Public-Members

        /// <summary>
        /// Trailer key, without the colon.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Trailer value, trimmed.
        /// </summary>
        public string Value { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GitTrailer()
        {
        }

        #endregion
    }
}
