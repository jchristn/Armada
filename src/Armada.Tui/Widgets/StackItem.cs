namespace Armada.Tui.Widgets
{
    using System;
    using TUIKit.Widgets;

    /// <summary>
    /// One child of a <see cref="StackPanel"/>.
    /// </summary>
    public class StackItem
    {
        #region Public-Members

        /// <summary>
        /// Child widget.
        /// </summary>
        public IWidget Child { get; }

        /// <summary>
        /// Fixed height, or null to fill.
        /// </summary>
        public int? Height { get; set; }

        /// <summary>
        /// English title drawn above the child, or null.
        /// </summary>
        public string? Title { get; }

        /// <summary>
        /// Share of the remaining space for fill children.
        /// </summary>
        public int Weight { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="child">Child.</param>
        /// <param name="height">Height or null.</param>
        /// <param name="title">Title or null.</param>
        /// <param name="weight">Weight.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="child"/> is null.</exception>
        public StackItem(IWidget child, int? height, string? title, int weight)
        {
            Child = child ?? throw new ArgumentNullException(nameof(child));
            Height = height;
            Title = title;
            Weight = weight;
        }

        #endregion
    }
}
