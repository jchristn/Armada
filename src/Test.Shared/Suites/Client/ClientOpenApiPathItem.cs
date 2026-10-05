namespace Test.Shared.Suites.Client
{
    /// <summary>
    /// One OpenAPI path item; the contract only needs to know the path exists and has a GET operation.
    /// </summary>
    public class ClientOpenApiPathItem
    {
        #region Public-Members

        /// <summary>
        /// GET operation, or null when the path has none.
        /// </summary>
        public ClientOpenApiOperation? Get { get; set; } = null;

        #endregion
    }
}
