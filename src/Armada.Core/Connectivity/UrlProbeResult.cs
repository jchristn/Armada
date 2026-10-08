namespace Armada.Core.Connectivity
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What a <see cref="UrlProbe"/> found: whether the URL is reachable, each stage it ran, and, on failure, why.
    /// </summary>
    public class UrlProbeResult
    {
        #region Public-Members

        /// <summary>
        /// The URL as given.
        /// </summary>
        public string Url
        {
            get { return _Url; }
            set { _Url = value ?? String.Empty; }
        }

        /// <summary>
        /// True when every stage that applies passed: the server is reachable and answered as the scheme expects.
        /// </summary>
        public bool Succeeded
        {
            get { return Failure == UrlProbeFailureEnum.None; }
        }

        /// <summary>
        /// Why it failed, or <see cref="UrlProbeFailureEnum.None"/>.
        /// </summary>
        public UrlProbeFailureEnum Failure { get; set; } = UrlProbeFailureEnum.None;

        /// <summary>
        /// The stage that failed, or null when it succeeded.
        /// </summary>
        public UrlProbeStepEnum? FailedStep { get; set; } = null;

        /// <summary>
        /// One plain sentence: what was reached and how, or why it failed.
        /// </summary>
        public string Summary
        {
            get { return _Summary; }
            set { _Summary = value ?? String.Empty; }
        }

        /// <summary>
        /// Total time, in milliseconds.
        /// </summary>
        public long ElapsedMs { get; set; } = 0;

        /// <summary>
        /// HTTP status the server answered with, or null when no response was read.
        /// </summary>
        public int? HttpStatusCode { get; set; } = null;

        /// <summary>
        /// True when the server answered 401 or 403: it is reachable and asks for credentials, which the probe never
        /// sends.
        /// </summary>
        public bool CredentialsRequired { get; set; } = false;

        /// <summary>
        /// True when the URL carried a user name or password; they were not sent.
        /// </summary>
        public bool CredentialsInUrlIgnored { get; set; } = false;

        /// <summary>
        /// The address the TCP connection was made to, or null.
        /// </summary>
        public string? RemoteEndPoint { get; set; } = null;

        /// <summary>
        /// Each stage, in order.
        /// </summary>
        public List<UrlProbeStep> Steps
        {
            get { return _Steps; }
            set { _Steps = value ?? new List<UrlProbeStep>(); }
        }

        #endregion

        #region Private-Members

        private string _Url = String.Empty;
        private string _Summary = String.Empty;
        private List<UrlProbeStep> _Steps = new List<UrlProbeStep>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The result of one stage, or null when it did not run.
        /// </summary>
        /// <param name="step">Stage.</param>
        /// <returns>The stage's result, or null.</returns>
        public UrlProbeStep? Find(UrlProbeStepEnum step)
        {
            foreach (UrlProbeStep entry in _Steps)
            {
                if (entry.Step == step) return entry;
            }

            return null;
        }

        #endregion
    }
}
