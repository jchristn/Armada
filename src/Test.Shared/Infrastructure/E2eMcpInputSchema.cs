namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// The parts of an MCP tool's input schema a test reads: the argument names and the required list. Property values
    /// are kept as untyped objects because only their names are compared.
    /// </summary>
    public class E2eMcpInputSchema
    {
        #region Public-Members

        /// <summary>
        /// Declared arguments by name.
        /// </summary>
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Required argument names.
        /// </summary>
        public List<string> Required { get; set; } = new List<string>();

        #endregion
    }
}
