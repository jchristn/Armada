namespace Armada.Tui.Screens.Build
{
    /// <summary>
    /// A Plan or Dispatch handoff drafted from a Workspace selection: title and prompt.
    /// </summary>
    public class WorkspaceDraft
    {
        #region Public-Members

        /// <summary>
        /// Title (session or voyage title).
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Prompt.
        /// </summary>
        public string Prompt { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="prompt">Prompt.</param>
        public WorkspaceDraft(string title, string prompt)
        {
            Title = title ?? "";
            Prompt = prompt ?? "";
        }

        #endregion
    }
}
