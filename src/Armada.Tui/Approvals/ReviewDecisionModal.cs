namespace Armada.Tui.Approvals
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The dashboard's Resolve Review dialog: feedback plus Approve, Conditionally Approve, More Work Required, Deny,
    /// and Cancel. Conditionally Approve and More Work Required need feedback ("Add feedback first"). Opens with a
    /// verdict preselected (the Approvals center's <c>a</c>/<c>c</c>/<c>m</c>/<c>d</c>); <c>Enter</c> in the feedback
    /// field or <c>Ctrl+S</c> submits the selected verdict. Closes with a <see cref="ReviewDecision"/> or null. Not
    /// thread-safe.
    /// </summary>
    public class ReviewDecisionModal : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Feedback field.
        /// </summary>
        public TextInput Feedback { get; } = new TextInput();

        /// <summary>
        /// Selected button (0 Approve, 1 Conditionally Approve, 2 More Work Required, 3 Deny, 4 Cancel).
        /// </summary>
        public int Selected { get; private set; } = 0;

        /// <summary>
        /// English validation error, or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        #endregion

        #region Private-Members

        private static readonly string[] _Labels = new string[] { "Approve", "Conditionally Approve", "More Work Required", "Deny", "Cancel" };
        private readonly string _MissionTitle;
        private bool _InputFocused = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="missionTitle">Mission title.</param>
        /// <param name="verdict">Preselected verdict.</param>
        /// <param name="initialComment">Initial feedback (the mission's review comment), or null.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Theme.</param>
        public ReviewDecisionModal(string missionTitle, ReviewVerdictEnum verdict, string? initialComment, ITextLocalizer? localizer, ArmadaTheme? theme)
            : base("Resolve Review", localizer, theme)
        {
            _MissionTitle = missionTitle ?? "";
            Selected = (int)verdict;
            Feedback.Localizer = Localizer;
            Feedback.ApplyTheme(Theme);
            Feedback.Placeholder = "Required for Conditionally Approve and More Work Required; optional for Approve/Deny.";
            Feedback.Value = initialComment ?? "";
            Feedback.OnFocusChanged(true);
            FooterHint = " Enter " + T("Submit") + "  Tab " + T("Buttons") + "  Esc " + T("Cancel") + " ";
            MinContentWidth = 50;
            MaxContentWidth = 96;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Submit a verdict (validates feedback); closes the dialog when valid.
        /// </summary>
        /// <param name="verdict">Verdict.</param>
        /// <returns>True when closed.</returns>
        public bool Submit(ReviewVerdictEnum verdict)
        {
            if (ReviewDecision.RequiresFeedback(verdict) && Feedback.Value.Trim().Length == 0)
            {
                Error = "Add feedback first";
                return false;
            }

            Error = null;
            RequestClose(new ReviewDecision(verdict, Feedback.Value));
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 's')
            {
                if (Selected < 4) Submit((ReviewVerdictEnum)Selected);
                return true;
            }

            if (key.Code == KeyCode.Tab)
            {
                _InputFocused = !_InputFocused;
                Feedback.OnFocusChanged(_InputFocused);
                return true;
            }

            if (key.Code == KeyCode.Enter)
            {
                if (Selected == 4)
                {
                    RequestClose(null);
                    return true;
                }

                Submit((ReviewVerdictEnum)Selected);
                return true;
            }

            if (!_InputFocused || key.Code == KeyCode.Up || key.Code == KeyCode.Down)
            {
                if (key.Code == KeyCode.Left || key.Code == KeyCode.Up)
                {
                    Selected = (Selected + _Labels.Length - 1) % _Labels.Length;
                    return true;
                }

                if (key.Code == KeyCode.Right || key.Code == KeyCode.Down)
                {
                    Selected = (Selected + 1) % _Labels.Length;
                    return true;
                }
            }

            if (_InputFocused)
            {
                bool handled = Feedback.HandleKey(key);
                if (handled) Error = null;
                return true;
            }

            return true;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            if (_InputFocused) Feedback.Insert(text);
            return true;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, 90);
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return 13;
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int y = 0;
            SurfaceText.Draw(content, 0, y++, _MissionTitle, On(Theme.Accent), width);
            foreach (string line in TextCells.Wrap(T("Choose how to resolve this review gate. Your feedback is carried into the next step or the re-run."), width))
                SurfaceText.Draw(content, 0, y++, line, Dim(), width);
            y++;
            SurfaceText.Draw(content, 0, y++, T("Feedback"), Body(), width);
            Feedback.Render(new SurfaceView(content, new Rect(0, y++, width, 1)));
            List<string> help = new List<string>
            {
                T("Approve") + " " + T("- accept this stage and continue."),
                T("Conditionally Approve") + " " + T("- continue, but the next step must consider your feedback."),
                T("More Work Required") + " " + T("- redo this same step with your feedback."),
                T("Deny") + " " + T("- reject this stage and fail the pipeline.")
            };
            foreach (string h in help) SurfaceText.Draw(content, 0, y++, h, Dim(), width);
            if (Error != null) SurfaceText.Draw(content, 0, y, "! " + T(Error), On(Theme.Error), width);
            y++;
            int x = 0;
            for (int i = 0; i < _Labels.Length; i++)
            {
                bool disabled = i < 4 && ReviewDecision.RequiresFeedback((ReviewVerdictEnum)i) && Feedback.Value.Trim().Length == 0;
                CellStyle style = i == Selected ? Theme.ButtonFocused : disabled ? On(Theme.Disabled) : i == 3 ? On(Theme.Error) : Body();
                string label = "[ " + T(_Labels[i]) + " ]";
                if (x + TextCells.Width(label) > width && x > 0)
                {
                    y++;
                    x = 0;
                }

                x += SurfaceText.Draw(content, x, Math.Min(y, content.Size.Height - 1), label, style, width - x) + 1;
            }
        }

        #endregion
    }
}
