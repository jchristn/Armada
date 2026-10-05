namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Multi-step flow (setup wizard, import wizard): a step header ("Step 2 of 6") with the step list, the current
    /// step's content, and Back / Skip / Next (Finish) / Cancel buttons. <c>Alt+Left</c>/<c>Alt+Right</c> also move
    /// between steps. Raises <see cref="Finished"/> and <see cref="Cancelled"/>. Not thread-safe.
    /// </summary>
    public class Wizard : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Steps. Never null.
        /// </summary>
        public IReadOnlyList<WizardStep> Steps
        {
            get { return _Steps; }
        }

        /// <summary>
        /// Current step index.
        /// </summary>
        public int Index
        {
            get { return _Index; }
        }

        /// <summary>
        /// Last validation error (English), or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        /// <summary>
        /// Raised when Finish is pressed on the last step.
        /// </summary>
        public event EventHandler? Finished;

        /// <summary>
        /// Raised when Cancel is pressed.
        /// </summary>
        public event EventHandler? Cancelled;

        /// <summary>
        /// Raised after the step changes.
        /// </summary>
        public event EventHandler<int>? StepChanged;

        #endregion

        #region Private-Members

        private readonly List<WizardStep> _Steps = new List<WizardStep>();
        private readonly ButtonRow _Buttons = new ButtonRow();
        private readonly Button _Back;
        private readonly Button _Skip;
        private readonly Button _Next;
        private readonly Button _Cancel;
        private int _Index = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="steps">Steps.</param>
        public Wizard(IEnumerable<WizardStep> steps)
        {
            _Steps.AddRange(steps ?? Enumerable.Empty<WizardStep>());
            _Back = new Button("Back", () => Go(_Index - 1, false));
            _Skip = new Button("Skip", () => Go(_Index + 1, false));
            _Next = new Button("Next", Next);
            _Cancel = new Button("Cancel", () => Cancelled?.Invoke(this, EventArgs.Empty));
            _Buttons.Add(_Back);
            _Buttons.Add(_Skip);
            _Buttons.Add(_Next);
            _Buttons.Add(_Cancel);
            Rebuild();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate the current step and advance, or finish on the last step.
        /// </summary>
        public void Next()
        {
            WizardStep step = _Steps[_Index];
            Error = step.Validate != null ? step.Validate() : null;
            if (Error != null) return;
            if (_Index >= _Steps.Count - 1)
            {
                EventHandler? handler = Finished;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }

            Go(_Index + 1, false);
        }

        /// <summary>
        /// Jump to a step (no validation).
        /// </summary>
        /// <param name="index">Step index.</param>
        /// <param name="focusButtons">Focus the buttons instead of the content.</param>
        public void Go(int index, bool focusButtons)
        {
            if (index < 0 || index >= _Steps.Count || index == _Index) return;
            _Index = index;
            Error = null;
            Rebuild();
            if (focusButtons) Scope.Focus(_Buttons);
            EventHandler<int>? handler = StepChanged;
            if (handler != null) handler(this, index);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if ((key.Modifiers & KeyModifiers.Alt) != 0 && key.Code == KeyCode.Left)
            {
                Go(_Index - 1, false);
                return true;
            }

            if ((key.Modifiers & KeyModifiers.Alt) != 0 && key.Code == KeyCode.Right)
            {
                Next();
                return true;
            }

            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (_Steps.Count == 0 || height < 4) return;
            string header = T("Step") + " " + (_Index + 1) + " / " + _Steps.Count + ": " + T(_Steps[_Index].Title);
            SurfaceText.Draw(surface, 0, 0, header, Theme.Accent, width);
            int x = 0;
            for (int i = 0; i < _Steps.Count && x < width; i++)
            {
                // Completed steps carry a "+" so progress never depends on color alone.
                string chip = (i == _Index ? "[" : i < _Index ? "+" : " ") + (i + 1) + ". " + T(_Steps[i].Title) + (i == _Index ? "]" : " ");
                CellStyle style = i == _Index ? Theme.TabActive : i < _Index ? Theme.Success : Theme.Muted;
                x += SurfaceText.Draw(surface, x, 1, chip, style, width - x) + 1;
            }

            int contentHeight = Math.Max(1, height - 5);
            Scope.RenderChild(surface, _Steps[_Index].Content, new Rect(0, 3, width, contentHeight));
            if (Error != null) SurfaceText.Draw(surface, 0, height - 2, "! " + T(Error), Theme.Error, width);
            Scope.RenderChild(surface, _Buttons, new Rect(0, height - 1, width, 1));
        }

        #endregion

        #region Private-Methods

        private void Rebuild()
        {
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            Scope.Clear();
            if (_Steps.Count == 0) return;
            AddChild(_Steps[_Index].Content);
            AddChild(_Buttons);
            _Back.Visible = _Index > 0;
            _Skip.Visible = _Steps[_Index].Optional && _Index < _Steps.Count - 1;
            _Next.Label = _Index >= _Steps.Count - 1 ? "Finish" : "Next";
            if (active) Scope.SetActive(true);
        }

        #endregion
    }
}
