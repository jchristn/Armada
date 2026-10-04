namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// An advisory attached to a discovery result. Clients localize by <see cref="Code"/>; the message is an English
    /// fallback.
    /// </summary>
    public class VesselImportHint
    {
        #region Public-Members

        /// <summary>
        /// Stable hint code, for example PathNotVisibleToAdmiral. Never null.
        /// </summary>
        public string Code
        {
            get => _Code;
            set => _Code = value ?? String.Empty;
        }

        /// <summary>
        /// English fallback message. Never null.
        /// </summary>
        public string Message
        {
            get => _Message;
            set => _Message = value ?? String.Empty;
        }

        #endregion

        #region Private-Members

        private string _Code = String.Empty;
        private string _Message = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportHint()
        {
        }

        /// <summary>
        /// Instantiate with a code and message.
        /// </summary>
        /// <param name="code">Stable hint code.</param>
        /// <param name="message">English fallback message.</param>
        public VesselImportHint(string code, string message)
        {
            Code = code;
            Message = message;
        }

        #endregion
    }
}
