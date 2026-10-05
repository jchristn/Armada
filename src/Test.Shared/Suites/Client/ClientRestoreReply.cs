namespace Test.Shared.Suites.Client
{
    /// <summary>
    /// The restore reply fields <see cref="ClientDestructiveCallsSuite"/> checks.
    /// </summary>
    public class ClientRestoreReply
    {
        #region Public-Members

        /// <summary>
        /// True when the server accepted the archive.
        /// </summary>
        public bool? Success { get; set; } = null;

        #endregion
    }
}
