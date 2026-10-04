namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Translations for one locale: single-word terms, exact phrases, and section labels.
    /// </summary>
    public class I18nLocalePack
    {
        #region Public-Members

        /// <summary>
        /// Single-token translations, or null.
        /// </summary>
        public Dictionary<string, string>? Terms { get; set; } = null;

        /// <summary>
        /// Exact phrase translations, or null.
        /// </summary>
        public Dictionary<string, string>? Phrases { get; set; } = null;

        /// <summary>
        /// Section label translations, or null.
        /// </summary>
        public Dictionary<string, string>? Sections { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public I18nLocalePack()
        {
        }

        #endregion
    }
}
