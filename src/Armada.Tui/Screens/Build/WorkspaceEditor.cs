namespace Armada.Tui.Screens.Build
{
    using System;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Workspace code editor: the shared multi-line editor without word wrap (code keeps its lines, like the
    /// dashboard's <c>wrap="off"</c> textarea), with <c>PgUp</c>/<c>PgDn</c> paging, the caret staying inside the file
    /// at its first and last line, a line and column indicator, and no Ctrl+U clear (a stray keystroke must not wipe a
    /// file). <c>Ctrl+E</c> opens <c>$EDITOR</c>. Not thread-safe.
    /// </summary>
    public class WorkspaceEditor : OpsTextArea
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WorkspaceEditor()
        {
            Editor.WordWrap = false;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// "Ln X, Col Y" for the caret.
        /// </summary>
        /// <returns>Position text.</returns>
        public string Position()
        {
            return "Ln " + (Editor.CaretRow + 1) + ", Col " + (Editor.CaretColumn + 1);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 'u') return true;
            switch (key.Code)
            {
                case KeyCode.PageUp:
                    for (int i = 0; i < 20 && Editor.CaretRow > 0; i++) Editor.MoveUp();
                    return true;
                case KeyCode.PageDown:
                    int last = Editor.Text.Split('\n').Length - 1;
                    for (int i = 0; i < 20 && Editor.CaretRow < last; i++) Editor.MoveDown();
                    return true;
                case KeyCode.Up:
                    if (Editor.CaretRow > 0) Editor.MoveUp();
                    return true;
                case KeyCode.Down:
                    if (Editor.CaretRow < Editor.Text.Split('\n').Length - 1) Editor.MoveDown();
                    return true;
            }

            return base.HandleKey(key);
        }

        #endregion
    }
}
