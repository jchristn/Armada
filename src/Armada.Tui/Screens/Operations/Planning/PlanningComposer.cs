namespace Armada.Tui.Screens.Operations
{
    using System;
    using TUIKit.Input;

    /// <summary>
    /// The planning message composer: a multi-line <see cref="OpsTextArea"/> where <c>Enter</c> sends
    /// (<see cref="Submitted"/>) and <c>Ctrl+J</c> adds a line (terminals do not report Shift+Enter); <c>Ctrl+E</c>
    /// opens <c>$EDITOR</c>. Not thread-safe.
    /// </summary>
    public class PlanningComposer : OpsTextArea
    {
        #region Public-Members

        /// <summary>
        /// Raised for Enter.
        /// </summary>
        public event EventHandler? Submitted;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (key.Code == KeyCode.Enter && (key.Modifiers & (KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Ctrl)) == 0)
            {
                Submitted?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if ((ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 'j') || (key.Code == KeyCode.Enter && (key.Modifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) != 0))
            {
                Editor.InsertNewline();
                return true;
            }

            return base.HandleKey(key);
        }

        #endregion
    }
}
