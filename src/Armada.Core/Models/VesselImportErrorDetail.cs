namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Machine-readable detail carried in the Data field of a vessel import error response.
    /// </summary>
    public class VesselImportErrorDetail
    {
        #region Public-Members

        /// <summary>
        /// Stable error code, for example PathNotAllowed. Never null.
        /// </summary>
        public string Code
        {
            get => _Code;
            set => _Code = value ?? String.Empty;
        }

        /// <summary>
        /// The path the error refers to, or null.
        /// </summary>
        public string? Path { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Code = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportErrorDetail()
        {
        }

        /// <summary>
        /// Instantiate with a code and optional path.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="path">Path the error refers to, or null.</param>
        public VesselImportErrorDetail(string code, string? path = null)
        {
            Code = code;
            Path = path;
        }

        #endregion
    }
}
