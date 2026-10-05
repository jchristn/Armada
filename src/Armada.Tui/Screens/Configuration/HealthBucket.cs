namespace Armada.Tui.Screens.Configuration
{
    using System;

    /// <summary>
    /// One time bucket of endpoint health probes (the dashboard's <c>HealthHistogram</c> bucket).
    /// </summary>
    public class HealthBucket
    {
        #region Public-Members

        /// <summary>
        /// Successful probes.
        /// </summary>
        public int Success { get; set; } = 0;

        /// <summary>
        /// Failed probes.
        /// </summary>
        public int Fail { get; set; } = 0;

        /// <summary>
        /// Bucket start (UTC).
        /// </summary>
        public DateTime TimeUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Marker: <c>+</c> all succeeded, <c>x</c> all failed, <c>~</c> mixed (never color alone).
        /// </summary>
        public char Marker
        {
            get { return Fail == 0 ? '+' : Success == 0 ? 'x' : '~'; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HealthBucket()
        {
        }

        #endregion
    }
}
