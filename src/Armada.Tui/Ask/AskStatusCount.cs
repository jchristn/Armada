namespace Armada.Tui.Ask
{
    /// <summary>
    /// A status and how many children of a tracked item have it.
    /// </summary>
    public class AskStatusCount
    {
        #region Public-Members

        /// <summary>
        /// Status.
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// Count.
        /// </summary>
        public int Count { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="count">Count.</param>
        public AskStatusCount(string status, int count)
        {
            Status = status ?? "";
            Count = count;
        }

        #endregion
    }
}
