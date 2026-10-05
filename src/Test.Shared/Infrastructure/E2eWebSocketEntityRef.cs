namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// An entity nested in a /ws event payload, reduced to its id.
    /// </summary>
    public class E2eWebSocketEntityRef
    {
        #region Public-Members

        /// <summary>
        /// Entity id.
        /// </summary>
        public string? Id { get; set; } = null;

        #endregion
    }
}
