namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// An invalid vessel import or categorization request with a specific <see cref="VesselImportCodes"/> code (for
    /// example a categorization captain that does not exist). Derives from <see cref="ArgumentException"/>, so callers
    /// that only handle ArgumentException still answer 400 / InvalidArgument; routes and tools report <see cref="Code"/>
    /// instead of the generic <see cref="VesselImportCodes.InvalidRequest"/>.
    /// </summary>
    public class VesselImportRequestException : ArgumentException
    {
        #region Public-Members

        /// <summary>
        /// Machine-readable code from <see cref="VesselImportCodes"/>.
        /// </summary>
        public string Code { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Machine-readable code from <see cref="VesselImportCodes"/>.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="paramName">Name of the offending parameter, or null.</param>
        public VesselImportRequestException(string code, string message, string? paramName = null)
            : base(message, paramName)
        {
            Code = String.IsNullOrEmpty(code) ? VesselImportCodes.InvalidRequest : code;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The vessel import code for an invalid-request exception: its own code when it is a
        /// <see cref="VesselImportRequestException"/>, otherwise <see cref="VesselImportCodes.InvalidRequest"/>.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>Code.</returns>
        public static string CodeFor(ArgumentException ex)
        {
            if (ex is VesselImportRequestException coded) return coded.Code;
            return VesselImportCodes.InvalidRequest;
        }

        #endregion
    }
}
