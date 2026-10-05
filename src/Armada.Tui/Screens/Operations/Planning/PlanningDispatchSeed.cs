namespace Armada.Tui.Screens.Operations
{
    using System;

    /// <summary>
    /// The dispatch draft seeded from a planning reply (the dashboard's DispatchSeedState): which session and message
    /// it came from, the seeded title and description, and whether it came from the reply itself (<c>auto</c>) or
    /// from Summarize Draft (<c>summary</c>).
    /// </summary>
    public class PlanningDispatchSeed
    {
        #region Public-Members

        /// <summary>
        /// "sessionId:messageId".
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Seeded title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Seeded description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// "auto" or "summary".
        /// </summary>
        public string Source { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="title">Title.</param>
        /// <param name="description">Description.</param>
        /// <param name="source">Source.</param>
        public PlanningDispatchSeed(string key, string title, string description, string source)
        {
            Key = key ?? "";
            Title = title ?? "";
            Description = description ?? "";
            Source = source ?? "auto";
        }

        #endregion
    }
}
