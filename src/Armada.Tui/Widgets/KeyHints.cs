namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Helpers for Armada's status bar hints on top of TUIKit's <see cref="KeyHint"/>, <see cref="IKeyHintSource"/>, and
    /// <see cref="KeyHintResolver"/>. TUIKit's resolver merges every source on the focus path, leaf first; Armada keeps
    /// one rule on top of it: the deepest control that describes its keys speaks for its subtree, so a container
    /// (a form, a filter row) answers only when nothing below it does (<see cref="Deeper"/>), and the innermost screen
    /// then adds what still works there (see <c>ScreenBase.ComposeHints</c>). Not thread-safe; use on the UI thread.
    /// </summary>
    public static class KeyHints
    {
        #region Public-Methods

        /// <summary>
        /// Convert key label and English description pairs to hints, in order.
        /// </summary>
        /// <param name="keys">Pairs (key label first), or null.</param>
        /// <returns>Hints. Never null.</returns>
        public static List<KeyHint> Of(IEnumerable<KeyValuePair<string, string>>? keys)
        {
            List<KeyHint> hints = new List<KeyHint>();
            if (keys == null) return hints;
            foreach (KeyValuePair<string, string> pair in keys)
            {
                if (!String.IsNullOrEmpty(pair.Key)) hints.Add(new KeyHint(pair.Key, pair.Value ?? ""));
            }

            return hints;
        }

        /// <summary>
        /// Convert hints back to key label and description pairs, in order.
        /// </summary>
        /// <param name="hints">Hints, or null.</param>
        /// <returns>Pairs. Never null.</returns>
        public static List<KeyValuePair<string, string>> Pairs(IEnumerable<KeyHint>? hints)
        {
            List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
            if (hints == null) return pairs;
            foreach (KeyHint hint in hints) pairs.Add(new KeyValuePair<string, string>(hint.Key, hint.Description));
            return pairs;
        }

        /// <summary>
        /// The answer of the deepest <see cref="IKeyHintSource"/> on the focus path below a widget that has keys of its
        /// own right now, or null when none does.
        /// </summary>
        /// <param name="owner">Widget whose focused subtree is searched (not itself).</param>
        /// <returns>Hints, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner"/> is null.</exception>
        public static IReadOnlyList<KeyHint>? Deeper(IFocusPathNode owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            IReadOnlyList<object> nodes = FocusPath.Build(null, owner).Nodes;
            for (int i = nodes.Count - 1; i >= 1; i--)
            {
                if (!(nodes[i] is IKeyHintSource source)) continue;
                IReadOnlyList<KeyHint>? hints = source.GetKeyHints();
                if (hints != null) return hints;
            }

            return null;
        }

        /// <summary>
        /// True when the focused leaf below a widget takes typed text now (TUIKit's <see cref="ITextEntry"/>).
        /// </summary>
        /// <param name="path">Focus path.</param>
        /// <returns>True while typing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null.</exception>
        public static bool Typing(FocusPath path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return path.Leaf is ITextEntry entry && entry.AcceptsText;
        }

        #endregion
    }
}
