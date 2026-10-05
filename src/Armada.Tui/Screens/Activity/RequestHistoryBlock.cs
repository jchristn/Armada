namespace Armada.Tui.Screens.Activity
{
    using System;

    /// <summary>
    /// One copyable detail block of a request-history entry: its kind, English title, and text.
    /// </summary>
    public class RequestHistoryBlock
    {
        #region Public-Members

        /// <summary>
        /// Block kind.
        /// </summary>
        public RequestHistoryBlockEnum Kind { get; }

        /// <summary>
        /// English title (display text only).
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Block text.
        /// </summary>
        public string Text { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="title">English title.</param>
        /// <param name="text">Text.</param>
        public RequestHistoryBlock(RequestHistoryBlockEnum kind, string title, string text)
        {
            Kind = kind;
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Text = text ?? "";
        }

        #endregion
    }
}
