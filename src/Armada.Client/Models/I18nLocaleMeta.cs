namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Metadata for one supported locale in the shared dashboard catalog.
    /// </summary>
    public class I18nLocaleMeta
    {
        #region Public-Members

        /// <summary>
        /// BCP 47 code, for example ja.
        /// </summary>
        public string Code { get; set; } = "";

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// Autonym.
        /// </summary>
        public string NativeLabel { get; set; } = "";

        /// <summary>
        /// Text direction: ltr or rtl.
        /// </summary>
        public string Dir { get; set; } = "ltr";

        /// <summary>
        /// Alias codes, or null.
        /// </summary>
        public List<string>? Aliases { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public I18nLocaleMeta()
        {
        }

        #endregion
    }
}
