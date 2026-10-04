namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The shared i18n catalog served at /dashboard/i18n/armada.json.
    /// </summary>
    public class I18nCatalog
    {
        #region Public-Members

        /// <summary>
        /// Default locale code.
        /// </summary>
        public string DefaultLocale { get; set; } = "en";

        /// <summary>
        /// Supported locales. Never null.
        /// </summary>
        public List<I18nLocaleMeta> SupportedLocales { get; set; } = new List<I18nLocaleMeta>();

        /// <summary>
        /// Locale packs keyed by code. Never null.
        /// </summary>
        public Dictionary<string, I18nLocalePack> Locales { get; set; } = new Dictionary<string, I18nLocalePack>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public I18nCatalog()
        {
        }

        #endregion
    }
}
