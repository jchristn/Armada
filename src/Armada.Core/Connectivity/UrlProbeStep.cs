namespace Armada.Core.Connectivity
{
    using System;

    /// <summary>
    /// The outcome of one stage of a <see cref="UrlProbe"/>.
    /// </summary>
    public class UrlProbeStep
    {
        #region Public-Members

        /// <summary>
        /// Which stage.
        /// </summary>
        public UrlProbeStepEnum Step { get; set; } = UrlProbeStepEnum.Parse;

        /// <summary>
        /// How it ended.
        /// </summary>
        public UrlProbeStepStatusEnum Status { get; set; } = UrlProbeStepStatusEnum.Passed;

        /// <summary>
        /// What happened, in a sentence (addresses, the TLS protocol and certificate, the HTTP status, or the error).
        /// </summary>
        public string Detail
        {
            get { return _Detail; }
            set { _Detail = value ?? String.Empty; }
        }

        /// <summary>
        /// Time the stage took, in milliseconds.
        /// </summary>
        public long ElapsedMs { get; set; } = 0;

        #endregion

        #region Private-Members

        private string _Detail = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UrlProbeStep()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="step">Stage.</param>
        /// <param name="status">Outcome.</param>
        /// <param name="detail">What happened.</param>
        /// <param name="elapsedMs">Time taken, in milliseconds.</param>
        public UrlProbeStep(UrlProbeStepEnum step, UrlProbeStepStatusEnum status, string detail, long elapsedMs)
        {
            Step = step;
            Status = status;
            Detail = detail;
            ElapsedMs = elapsedMs;
        }

        #endregion
    }
}
