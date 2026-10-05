namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Retention group of PUT /api/v1/settings; nullable so an absent field reads as null (the production defaults would mask it).
    /// </summary>
    public class ServerSettingsRetentionBody
    {
        #region Public-Members

        /// <summary>
        /// Job retention days.
        /// </summary>
        public int? JobRetentionDays { get; set; } = null;

        /// <summary>
        /// Ask thread archive days.
        /// </summary>
        public int? AskThreadArchiveAfterDays { get; set; } = null;

        #endregion
    }
}
