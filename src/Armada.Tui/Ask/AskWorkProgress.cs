namespace Armada.Tui.Ask
{
    /// <summary>
    /// Progress of a tracked item for its live card's bar: finished and failed children out of the total.
    /// </summary>
    public class AskWorkProgress
    {
        #region Public-Members

        /// <summary>
        /// Total children.
        /// </summary>
        public int Total { get; set; } = 0;

        /// <summary>
        /// Finished children (including failed).
        /// </summary>
        public int Done { get; set; } = 0;

        /// <summary>
        /// Failed children.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Percent finished, 0 to 100.
        /// </summary>
        public int Percent { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkProgress()
        {
        }

        #endregion
    }
}
