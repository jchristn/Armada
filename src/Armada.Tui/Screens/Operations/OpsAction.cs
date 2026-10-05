namespace Armada.Tui.Screens.Operations
{
    using System;

    /// <summary>
    /// A row action (the dashboard's row ActionMenu item): one definition drives the row menu (<c>.</c>), the Actions
    /// menu and palette (acting on the selected row), the help overlay, and an optional single-key binding.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    public class OpsAction<T>
    {
        #region Public-Members

        /// <summary>
        /// Command id suffix (for example <c>restart</c>).
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// English label (catalog key), for example <c>View Diff</c>.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Key binding (a single character such as <c>r</c>, or <c>del</c>), or null.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Shown for a row only when this returns true (null: always).
        /// </summary>
        public Func<T, bool>? When { get; set; } = null;

        /// <summary>
        /// Runs the action for a row.
        /// </summary>
        public Action<T> Run { get; }

        /// <summary>
        /// Destructive (shown with a marker in the menu).
        /// </summary>
        public bool Danger { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Id suffix.</param>
        /// <param name="label">English label.</param>
        /// <param name="run">Handler.</param>
        /// <param name="key">Key, or null.</param>
        /// <param name="when">Visibility predicate, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public OpsAction(string id, string label, Action<T> run, string? key = null, Func<T, bool>? when = null)
        {
            Id = String.IsNullOrEmpty(id) ? throw new ArgumentNullException(nameof(id)) : id;
            Label = label ?? id;
            Run = run ?? throw new ArgumentNullException(nameof(run));
            Key = key;
            When = when;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the action applies to a row.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>True when visible.</returns>
        public bool AppliesTo(T row)
        {
            return row != null && (When == null || When(row));
        }

        #endregion
    }
}
