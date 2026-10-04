namespace Armada.Client
{
    using System;

    /// <summary>
    /// Describes a completed <see cref="ArmadaClient"/> request.
    /// </summary>
    public class ArmadaRequestCompletedEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Request path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// HTTP status code, or 0 for a transport failure or timeout.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Elapsed time.
        /// </summary>
        public TimeSpan Duration { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="duration">Elapsed time.</param>
        public ArmadaRequestCompletedEventArgs(string method, string path, int statusCode, TimeSpan duration)
        {
            Method = method ?? "";
            Path = path ?? "";
            StatusCode = statusCode;
            Duration = duration;
        }

        #endregion
    }
}
