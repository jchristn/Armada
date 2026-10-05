namespace Armada.Tui.Approvals
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The dialog behind <c>A</c> (allow and remember) and <c>d</c> (deny) on a CLI permission request in the Approvals
    /// center. Allow and remember edits the rule pattern (prefilled with the request's suggested rule) and picks the
    /// rule scope with Up/Down: this captain, this vessel (only when the request has a vessel), or everywhere; a pattern
    /// that allows every shell command (for example a bare <c>Bash</c> or <c>run_process</c>) shows a warning. Deny
    /// takes an optional message for the captain. Enter (or Ctrl+S) submits and closes with a
    /// <see cref="CliPermissionDecisionRequest"/>; Esc closes with null. Not thread-safe.
    /// </summary>
    public class CliPermissionDecisionModal : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// The rule pattern (allow and remember) or the optional message (deny).
        /// </summary>
        public TextInput Input { get; } = new TextInput();

        /// <summary>
        /// True for allow and remember; false for deny.
        /// </summary>
        public bool Remember { get; }

        /// <summary>
        /// Rule scopes offered (allow and remember only). Never null.
        /// </summary>
        public IReadOnlyList<CliPermissionRuleScopeEnum> Scopes
        {
            get { return _Scopes; }
        }

        /// <summary>
        /// Selected rule scope.
        /// </summary>
        public CliPermissionRuleScopeEnum Scope
        {
            get { return _Scopes.Count > 0 ? _Scopes[_ScopeIndex] : CliPermissionRuleScopeEnum.Captain; }
        }

        /// <summary>
        /// Validation message (English), or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        /// <summary>
        /// True while the rule pattern allows every shell command (allow and remember only).
        /// </summary>
        public bool AllowsEveryShellCommand
        {
            get { return Remember && Armada.Core.Services.CliPermissionRuleMatcher.IsUnrestrictedShellRule(Input.Value); }
        }

        #endregion

        #region Private-Members

        private readonly CliPermissionRequest _Request;
        private readonly List<CliPermissionRuleScopeEnum> _Scopes = new List<CliPermissionRuleScopeEnum>();
        private int _ScopeIndex = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="request">The pending request.</param>
        /// <param name="remember">True for allow and remember; false for deny.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public CliPermissionDecisionModal(CliPermissionRequest request, bool remember, ITextLocalizer? localizer, ArmadaTheme? theme)
            : base(remember ? "Allow and Remember" : "Deny Permission", localizer, theme)
        {
            _Request = request ?? throw new ArgumentNullException(nameof(request));
            Remember = remember;
            Input.Localizer = Localizer;
            Input.ApplyTheme(Theme);
            if (remember)
            {
                Input.Value = !String.IsNullOrWhiteSpace(request.SuggestedRule) ? request.SuggestedRule! : request.ToolName;
                Input.Placeholder = "Bash(git status:*)";
                if (!String.IsNullOrEmpty(request.CaptainId)) _Scopes.Add(CliPermissionRuleScopeEnum.Captain);
                if (!String.IsNullOrEmpty(request.VesselId)) _Scopes.Add(CliPermissionRuleScopeEnum.Vessel);
                _Scopes.Add(CliPermissionRuleScopeEnum.Global);
                FooterHint = " Enter " + T("Allow") + "  Up/Down " + T("Scope") + "  Esc " + T("Cancel") + " ";
            }
            else
            {
                Input.Placeholder = "Optional message for the captain";
                FooterHint = " Enter " + T("Deny") + "  Esc " + T("Cancel") + " ";
            }

            Input.OnFocusChanged(true);
            MinContentWidth = 50;
            MaxContentWidth = 96;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Select a rule scope (ignored when it is not offered).
        /// </summary>
        /// <param name="scope">Scope.</param>
        /// <returns>True when selected.</returns>
        public bool SelectScope(CliPermissionRuleScopeEnum scope)
        {
            int index = _Scopes.IndexOf(scope);
            if (index < 0) return false;
            _ScopeIndex = index;
            return true;
        }

        /// <summary>
        /// Validate and close with the decision.
        /// </summary>
        /// <returns>True when closed.</returns>
        public bool Submit()
        {
            string text = Input.Value.Trim();
            CliPermissionDecisionRequest decision = new CliPermissionDecisionRequest();
            if (Remember)
            {
                if (text.Length == 0)
                {
                    Error = "Enter a rule pattern.";
                    return false;
                }

                decision.Decision = CliPermissionDecisionEnum.AllowAndRemember;
                decision.RulePattern = text;
                decision.RuleScope = Scope;
            }
            else
            {
                decision.Decision = CliPermissionDecisionEnum.Deny;
                decision.Message = text.Length > 0 ? text : null;
            }

            Error = null;
            RequestClose(decision);
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (key.Code == KeyCode.Enter || (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 's'))
            {
                Submit();
                return true;
            }

            if (Remember && _Scopes.Count > 0 && (key.Code == KeyCode.Up || key.Code == KeyCode.Down))
            {
                int step = key.Code == KeyCode.Up ? _Scopes.Count - 1 : 1;
                _ScopeIndex = (_ScopeIndex + step) % _Scopes.Count;
                return true;
            }

            if (Input.HandleKey(key)) Error = null;
            return true;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            Input.Insert(text);
            Error = null;
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
            return Remember ? 10 + _Scopes.Count : 7;
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int y = 0;
            SurfaceText.Draw(content, 0, y++, CliPermissionText.Title(_Request), On(Theme.Accent), width);
            SurfaceText.Draw(content, 0, y++, CliPermissionText.Where(Localizer, _Request), Dim(), width);
            y++;
            SurfaceText.Draw(content, 0, y++, Remember ? T("Rule pattern") : T("Message (optional)"), Body(), width);
            Input.Render(new SurfaceView(content, new Rect(0, y++, width, 1)));
            string help = Remember
                ? T("Claude Code permission rule syntax, for example Bash(git status:*) or WebFetch(domain:example.com).")
                : T("The captain is told the tool call was denied.");
            SurfaceText.Draw(content, 0, y++, help, Dim(), width);
            if (Remember)
            {
                if (AllowsEveryShellCommand) SurfaceText.Draw(content, 0, y, "! " + T("This rule allows every shell command for its scope."), On(Theme.Warning), width);
                y++;
                y++;
                SurfaceText.Draw(content, 0, y++, T("Remember for") + "  (Up/Down)", Body(), width);
                for (int i = 0; i < _Scopes.Count; i++)
                {
                    bool selected = i == _ScopeIndex;
                    string label = (selected ? "(*) " : "( ) ") + CliPermissionText.Scope(Localizer, _Scopes[i]);
                    SurfaceText.Draw(content, 2, y++, label, selected ? Theme.ButtonFocused : Body(), width - 2);
                }
            }

            if (Error != null) SurfaceText.Draw(content, 0, Math.Min(y, content.Size.Height - 1), "! " + T(Error), On(Theme.Error), width);
        }

        #endregion
    }
}
