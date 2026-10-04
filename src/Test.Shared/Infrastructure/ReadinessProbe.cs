namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// Last observed state of the two listeners <see cref="E2EServerFixture"/> waits for.
    /// </summary>
    public sealed class ReadinessProbe
    {
        #region Public-Members

        /// <summary>
        /// Value recorded for a probe that returned HTTP 200.
        /// </summary>
        public const string Ok = "200 OK";

        /// <summary>
        /// Last REST health probe outcome, or null before the first probe.
        /// </summary>
        public string? Rest { get; set; } = null;

        /// <summary>
        /// Last MCP health probe outcome, or null before the first probe.
        /// </summary>
        public string? Mcp { get; set; } = null;

        /// <summary>
        /// True when the REST probe succeeded.
        /// </summary>
        public bool RestOk
        {
            get { return String.Equals(Rest, Ok, StringComparison.Ordinal); }
        }

        /// <summary>
        /// True when the MCP probe succeeded.
        /// </summary>
        public bool McpOk
        {
            get { return String.Equals(Mcp, Ok, StringComparison.Ordinal); }
        }

        /// <summary>
        /// True when both listeners answered.
        /// </summary>
        public bool Ready
        {
            get { return RestOk && McpOk; }
        }

        #endregion
    }
}
