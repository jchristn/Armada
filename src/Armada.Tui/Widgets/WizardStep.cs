namespace Armada.Tui.Widgets
{
    using System;
    using TUIKit.Widgets;

    /// <summary>
    /// One step of a <see cref="Wizard"/>.
    /// </summary>
    public class WizardStep
    {
        #region Public-Members

        /// <summary>
        /// English title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Step content.
        /// </summary>
        public IWidget Content { get; }

        /// <summary>
        /// The step may be skipped.
        /// </summary>
        public bool Optional { get; set; } = false;

        /// <summary>
        /// Returns an English error that blocks Next, or null.
        /// </summary>
        public Func<string?>? Validate { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="content">Content.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
        public WizardStep(string title, IWidget content)
        {
            Title = title ?? "";
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        #endregion
    }
}
