namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// One mission in the dispatch quick action arguments.
    /// </summary>
    public class AskDispatchMissionArgumentsBody
    {
        #region Public-Members

        /// <summary>
        /// Mission title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Mission description.
        /// </summary>
        public string? Description { get; set; } = null;

        #endregion
    }
}
