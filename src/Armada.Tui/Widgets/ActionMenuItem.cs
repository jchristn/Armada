namespace Armada.Tui.Widgets
{
    using System;

    /// <summary>
    /// One entry of a row or bulk action menu.
    /// </summary>
    public class ActionMenuItem
    {
        #region Public-Members

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Key hint shown on the right (for example Del), or empty.
        /// </summary>
        public string KeyHint { get; }

        /// <summary>
        /// Action run on the UI loop thread when chosen.
        /// </summary>
        public Action Action { get; }

        /// <summary>
        /// Disabled items are shown but cannot be chosen.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Destructive items (delete, purge) are listed last by convention.
        /// </summary>
        public bool Destructive { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="action">Action.</param>
        /// <param name="keyHint">Key hint.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
        public ActionMenuItem(string label, Action action, string keyHint = "")
        {
            Label = label ?? "";
            Action = action ?? throw new ArgumentNullException(nameof(action));
            KeyHint = keyHint ?? "";
        }

        #endregion
    }
}
