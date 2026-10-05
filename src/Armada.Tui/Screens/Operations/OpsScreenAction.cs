namespace Armada.Tui.Screens.Operations
{
    using System;

    /// <summary>
    /// A screen-level action (toolbar button, header action, or bulk action): drives a button, the Actions menu and
    /// palette, the help overlay, and an optional key binding.
    /// </summary>
    public class OpsScreenAction
    {
        #region Public-Members

        /// <summary>
        /// Command id suffix.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// English label (catalog key).
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Builds the shown label (for example "Delete Selected (3)"), or null to use <see cref="Label"/>.
        /// </summary>
        public Func<string>? DynamicLabel { get; set; } = null;

        /// <summary>
        /// Key binding (single character, <c>del</c>, <c>ctrl+x</c> style), or null.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Available only when this returns true (null: always).
        /// </summary>
        public Func<bool>? When { get; set; } = null;

        /// <summary>
        /// Handler.
        /// </summary>
        public Action Run { get; }

        /// <summary>
        /// Show as a toolbar button. Default true.
        /// </summary>
        public bool Toolbar { get; set; } = true;

        /// <summary>
        /// Destructive.
        /// </summary>
        public bool Danger { get; set; } = false;

        /// <summary>
        /// Owner data (for example the button drawn for the action).
        /// </summary>
        public object? Tag { get; set; } = null;

        /// <summary>
        /// True when available now.
        /// </summary>
        public bool Available
        {
            get { return When == null || When(); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Id suffix.</param>
        /// <param name="label">English label.</param>
        /// <param name="run">Handler.</param>
        /// <param name="key">Key, or null.</param>
        /// <param name="when">Availability predicate, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public OpsScreenAction(string id, string label, Action run, string? key = null, Func<bool>? when = null)
        {
            Id = String.IsNullOrEmpty(id) ? throw new ArgumentNullException(nameof(id)) : id;
            Label = label ?? id;
            Run = run ?? throw new ArgumentNullException(nameof(run));
            Key = key;
            When = when;
        }

        #endregion
    }
}
