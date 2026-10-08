namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised on the Admiral when a Harbor reports that it could not carry out a delegated captain launch (an
    /// <c>error</c> message naming the job), for example because the runtime's CLI is not installed on the Harbor host
    /// or the working directory does not exist there. The message names the Harbor and carries its reason.
    /// </summary>
    public class HarborJobFailedException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// The Harbor that reported the failure.
        /// </summary>
        public string HarborId { get; }

        /// <summary>
        /// The job that failed.
        /// </summary>
        public string JobId { get; }

        /// <summary>
        /// The Harbor's own error message.
        /// </summary>
        public string HarborMessage { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="harborId">The Harbor that reported the failure.</param>
        /// <param name="jobId">The job that failed.</param>
        /// <param name="harborMessage">The Harbor's error message.</param>
        public HarborJobFailedException(string harborId, string jobId, string harborMessage)
            : base("Harbor " + harborId + " could not start the captain: " + harborMessage)
        {
            HarborId = harborId ?? String.Empty;
            JobId = jobId ?? String.Empty;
            HarborMessage = harborMessage ?? String.Empty;
        }

        #endregion
    }
}
