namespace Armada.Tui.Modals
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Confirmation dialog matching the dashboard's ConfirmDialog, including the typed variant that requires typing a
    /// word (for example <c>delete</c>) before the confirm button enables. Closes with true (confirmed) or false.
    /// Keys: Tab/Left/Right switch buttons, Enter activates, Esc cancels, <c>y</c>/<c>n</c> when no typing is required.
    /// Not thread-safe.
    /// </summary>
    public class ConfirmDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Message (translated by the caller).
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Word the user must type to confirm, or null.
        /// </summary>
        public string? RequiredText { get; }

        /// <summary>
        /// The typing field (when <see cref="RequiredText"/> is set).
        /// </summary>
        public TextInput Input { get; } = new TextInput();

        /// <summary>
        /// True when the confirm action is destructive (drawn in the error style).
        /// </summary>
        public bool Destructive { get; set; } = false;

        /// <summary>
        /// Index of the highlighted button: 0 confirm, 1 cancel.
        /// </summary>
        public int SelectedButton { get; private set; } = 1;

        #endregion

        #region Private-Members

        private readonly string _ConfirmLabel;
        private readonly string _CancelLabel;
        private bool _InputFocused;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="message">Message (already translated).</param>
        /// <param name="confirmLabel">English confirm label.</param>
        /// <param name="cancelLabel">English cancel label.</param>
        /// <param name="requiredText">Word to type, or null.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public ConfirmDialog(string title, string message, string confirmLabel = "Confirm", string cancelLabel = "Cancel", string? requiredText = null, ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
            : base(title, localizer, theme)
        {
            Message = message ?? "";
            _ConfirmLabel = T(confirmLabel);
            _CancelLabel = T(cancelLabel);
            RequiredText = String.IsNullOrEmpty(requiredText) ? null : requiredText;
            Input.Localizer = Localizer;
            Input.ApplyTheme(Theme);
            if (RequiredText != null)
            {
                _InputFocused = true;
                Input.OnFocusChanged(true);
                SelectedButton = 0;
            }

            FooterHint = " Enter " + T("Select") + "  Esc " + T("Cancel") + " ";
            MinContentWidth = 36;
            MaxContentWidth = 70;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the confirm button can be used.
        /// </summary>
        public bool CanConfirm
        {
            get { return RequiredText == null || String.Equals(Input.Value.Trim(), RequiredText, StringComparison.OrdinalIgnoreCase); }
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, false)) return true;
            if (key.Code == KeyCode.Tab || key.Code == KeyCode.Left || key.Code == KeyCode.Right)
            {
                if (_InputFocused && key.Code != KeyCode.Tab) return Input.HandleKey(key);
                if (RequiredText != null && key.Code == KeyCode.Tab)
                {
                    _InputFocused = !_InputFocused;
                    Input.OnFocusChanged(_InputFocused);
                    return true;
                }

                SelectedButton = SelectedButton == 0 ? 1 : 0;
                return true;
            }

            if (key.Code == KeyCode.Enter)
            {
                bool confirm = _InputFocused || SelectedButton == 0;
                if (confirm && !CanConfirm) return true;
                RequestClose(confirm);
                return true;
            }

            if (_InputFocused) return Input.HandleKey(key);
            if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None)
            {
                if (key.Rune == 'y' || key.Rune == 'Y')
                {
                    RequestClose(true);
                    return true;
                }

                if (key.Rune == 'n' || key.Rune == 'N')
                {
                    RequestClose(false);
                    return true;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            if (_InputFocused) Input.Insert(text);
            return true;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(36, Math.Min(70, TextCells.Width(Message) + 2)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            int lines = TextCells.Wrap(Message, contentWidth).Count;
            return lines + 2 + (RequiredText != null ? 3 : 0);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            List<string> lines = TextCells.Wrap(Message, width);
            int y = 0;
            foreach (string line in lines)
            {
                SurfaceText.Draw(content, 0, y++, line, Body(), width);
            }

            if (RequiredText != null)
            {
                y++;
                SurfaceText.Draw(content, 0, y++, T("Type") + " \"" + RequiredText + "\" " + T("to confirm") + ":", Dim(), width);
                Input.Render(new SurfaceView(content, new Rect(0, y++, Math.Min(width, 30), 1)));
            }

            y++;
            CellStyle confirmStyle = !CanConfirm ? On(Theme.Disabled) : SelectedButton == 0 && !_InputFocused ? Theme.ButtonFocused : (Destructive ? On(Theme.Error) : Body());
            CellStyle cancelStyle = SelectedButton == 1 && !_InputFocused ? Theme.ButtonFocused : Body();
            int x = SurfaceText.Draw(content, 0, Math.Min(y, content.Size.Height - 1), "[ " + _ConfirmLabel + " ]", confirmStyle, width);
            SurfaceText.Draw(content, x + 2, Math.Min(y, content.Size.Height - 1), "[ " + _CancelLabel + " ]", cancelStyle, width - x - 2);
        }

        #endregion
    }
}
