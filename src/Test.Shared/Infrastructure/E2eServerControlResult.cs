namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// The body of a successful server control call such as <c>POST /api/v1/server/restart</c>.
    /// </summary>
    public sealed class E2eServerControlResult
    {
        #region Public-Members

        /// <summary>
        /// What the server is doing, for example "restarting".
        /// </summary>
        public string Status { get; set; } = String.Empty;

        #endregion
    }
}
