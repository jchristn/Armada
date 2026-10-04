namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One settings key in the API surface.
    /// </summary>
    public sealed class ApiSettingKey
    {
        #region Public-Members

        /// <summary>
        /// Dotted camelCase key as it appears in settings.json (list elements as key[].child).
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Value type.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Default value as JSON (machine-specific paths normalized), or null for objects.
        /// </summary>
        public string? Default { get; set; } = null;

        /// <summary>
        /// Whether the key is experimental.
        /// </summary>
        public bool Experimental { get; set; } = false;

        #endregion
    }
}
