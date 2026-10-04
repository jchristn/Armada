namespace Armada.Tui.Screens.Entities
{
    using System;
    using TUIKit.Widgets;

    /// <summary>
    /// A named panel of an <see cref="EntityDetailScreen{T}"/>.
    /// </summary>
    public class DetailPanel
    {
        #region Public-Members

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Content widget.
        /// </summary>
        public IWidget Content { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="label">English label.</param>
        /// <param name="content">Content.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
        public DetailPanel(string key, string label, IWidget content)
        {
            Key = key ?? "";
            Label = label ?? key ?? "";
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        #endregion
    }
}
