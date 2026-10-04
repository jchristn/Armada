namespace Armada.Tui.Ask
{
    /// <summary>
    /// One mission in the Dispatch quick-action form.
    /// </summary>
    public class AskDispatchMissionDraft
    {
        #region Public-Members

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Description (optional; defaults to the title).
        /// </summary>
        public string Description { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="description">Description.</param>
        public AskDispatchMissionDraft(string title = "", string description = "")
        {
            Title = title ?? "";
            Description = description ?? "";
        }

        #endregion
    }
}
