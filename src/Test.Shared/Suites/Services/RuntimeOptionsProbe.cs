namespace Test.Shared.Suites.Services
{
    /// <summary>
    /// Captain runtime options fields read back by <see cref="SecurityHardeningSuite"/>.
    /// </summary>
    public class RuntimeOptionsProbe
    {
        #region Public-Members

        /// <summary>
        /// Endpoint name, or null.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Approval policy, or null.
        /// </summary>
        public string? ApprovalPolicy { get; set; } = null;

        /// <summary>
        /// Auto-approve flag, or null when not written.
        /// </summary>
        public bool? AutoApprove { get; set; } = null;

        #endregion
    }
}
