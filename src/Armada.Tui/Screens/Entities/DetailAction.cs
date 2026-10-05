namespace Armada.Tui.Screens.Entities
{
    using System;
    using Armada.Tui.Widgets;

    /// <summary>
    /// An action of an <see cref="EntityDetailScreen{T}"/>: a header button and a screen command with the same
    /// handler and visibility.
    /// </summary>
    public class DetailAction
    {
        #region Public-Members

        /// <summary>
        /// Id (unique per screen).
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Handler.
        /// </summary>
        public Action Handler { get; }

        /// <summary>
        /// Visibility, or null for always.
        /// </summary>
        public Func<bool>? Visible { get; }

        /// <summary>
        /// Key, or null.
        /// </summary>
        public string? Gesture { get; }

        /// <summary>
        /// The header button.
        /// </summary>
        public Button? Button { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="label">English label.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="visible">Visibility.</param>
        /// <param name="gesture">Key.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is null.</exception>
        public DetailAction(string id, string label, Action handler, Func<bool>? visible, string? gesture)
        {
            Id = id ?? "";
            Label = label ?? id ?? "";
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Visible = visible;
            Gesture = gesture;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Evaluate visibility.
        /// </summary>
        /// <returns>True when offered.</returns>
        public bool IsVisible()
        {
            return Visible == null || Visible();
        }

        #endregion
    }
}
