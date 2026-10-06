namespace Armada.Helm.Infrastructure
{
    /// <summary>
    /// One row of <c>armada profile list --json</c>. Never carries the token itself.
    /// </summary>
    public class ProfileListEntry
    {
        #region Public-Members

        /// <summary>
        /// Profile name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Admiral URL.
        /// </summary>
        public string Url { get; set; } = "";

        /// <summary>
        /// True for the active profile.
        /// </summary>
        public bool Active { get; set; } = false;

        /// <summary>
        /// True when the profile follows this machine's Admiral.
        /// </summary>
        public bool Local { get; set; } = false;

        /// <summary>
        /// True when a token is stored for the profile.
        /// </summary>
        public bool HasToken { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ProfileListEntry()
        {
        }

        #endregion
    }
}
