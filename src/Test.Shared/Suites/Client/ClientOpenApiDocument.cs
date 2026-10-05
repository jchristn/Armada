namespace Test.Shared.Suites.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// The part of the OpenAPI document the client contract checks: the route templates under "paths".
    /// </summary>
    public class ClientOpenApiDocument
    {
        #region Public-Members

        /// <summary>
        /// Route template to its operations.
        /// </summary>
        public Dictionary<string, ClientOpenApiPathItem>? Paths { get; set; } = null;

        #endregion
    }
}
