namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// Edits a playbook selection: <c>a</c> adds a playbook (then picks its delivery mode), <c>m</c> or <c>Enter</c>
    /// changes the delivery mode of the selected row, <c>p</c> swaps its playbook, <c>Del</c> removes it, and
    /// <c>Esc</c> closes (changes apply as they are made, like the dashboard). Not thread-safe.
    /// </summary>
    public class PlaybookSelectionDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Current selection.
        /// </summary>
        public List<SelectedPlaybook> Items { get; }

        /// <summary>
        /// Cursor row.
        /// </summary>
        public int Cursor { get; private set; } = 0;

        #endregion

        #region Private-Members

        private readonly PlaybookSelectionField _Field;
        private readonly IModalHost _Host;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="field">Owning field.</param>
        /// <param name="host">Modal host for pickers.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public PlaybookSelectionDialog(PlaybookSelectionField field, IModalHost host, ITextLocalizer? localizer, ArmadaTheme? theme)
            : base("Playbooks", localizer, theme)
        {
            _Field = field ?? throw new ArgumentNullException(nameof(field));
            _Host = host ?? throw new ArgumentNullException(nameof(host));
            Items = field.Value.Select(PlaybookSelectionField.Clone).ToList();
            FooterHint = " a " + T("Add playbook") + "  m " + T("Delivery Mode") + "  p " + T("Playbook") + "  Del " + T("Remove") + "  Esc " + T("Done") + " ";
            MinContentWidth = 60;
            MaxContentWidth = 110;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Active playbooks not yet selected (optionally keeping one id).
        /// </summary>
        /// <param name="keep">Id to keep, or null.</param>
        /// <returns>Playbooks.</returns>
        public List<Playbook> Choices(string? keep)
        {
            HashSet<string> taken = new HashSet<string>(Items.Select(i => i.PlaybookId).Where(id => id != keep));
            return _Field.Available.Where(p => p.Active && !taken.Contains(p.Id)).ToList();
        }

        /// <summary>
        /// Add a playbook through pickers.
        /// </summary>
        public void Add()
        {
            List<Playbook> choices = Choices(null);
            if (choices.Count == 0) return;
            PickerModal<string> picker = new PickerModal<string>("Add Playbook", choices.Select(p => new SelectOption<string>(p.Id, p.FileName, p.Description ?? "")), Localizer, Theme);
            _Host.Show(picker, r =>
            {
                if (!(r is SelectOption<string> chosen)) return;
                PickMode(PlaybookDeliveryModeEnum.InlineFullContent, mode =>
                {
                    SelectedPlaybook item = new SelectedPlaybook();
                    item.PlaybookId = chosen.Value;
                    item.DeliveryMode = mode;
                    Items.Add(item);
                    Cursor = Items.Count - 1;
                });
            });
        }

        /// <summary>
        /// Change the delivery mode of the selected row.
        /// </summary>
        public void ChangeMode()
        {
            if (Cursor < 0 || Cursor >= Items.Count) return;
            SelectedPlaybook item = Items[Cursor];
            PickMode(item.DeliveryMode, mode => item.DeliveryMode = mode);
        }

        /// <summary>
        /// Swap the playbook of the selected row.
        /// </summary>
        public void ChangePlaybook()
        {
            if (Cursor < 0 || Cursor >= Items.Count) return;
            SelectedPlaybook item = Items[Cursor];
            PickerModal<string> picker = new PickerModal<string>("Playbook", Choices(item.PlaybookId).Select(p => new SelectOption<string>(p.Id, p.FileName, p.Description ?? "")), Localizer, Theme);
            _Host.Show(picker, r =>
            {
                if (r is SelectOption<string> chosen) item.PlaybookId = chosen.Value;
            });
        }

        /// <summary>
        /// Remove the selected row.
        /// </summary>
        public void Remove()
        {
            if (Cursor < 0 || Cursor >= Items.Count) return;
            Items.RemoveAt(Cursor);
            Cursor = Math.Clamp(Cursor, 0, Math.Max(0, Items.Count - 1));
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, Items)) return true;
            switch (key.Code)
            {
                case KeyCode.Up:
                    Cursor = Math.Max(0, Cursor - 1);
                    return true;
                case KeyCode.Down:
                    Cursor = Math.Min(Math.Max(0, Items.Count - 1), Cursor + 1);
                    return true;
                case KeyCode.Enter:
                    if (Items.Count == 0) Add();
                    else ChangeMode();
                    return true;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    Remove();
                    return true;
                case KeyCode.Insert:
                    Add();
                    return true;
                case KeyCode.Character:
                    if (key.Modifiers != KeyModifiers.None) return true;
                    if (key.Rune == 'a' || key.Rune == 'n' || key.Rune == '+') Add();
                    else if (key.Rune == 'm') ChangeMode();
                    else if (key.Rune == 'p') ChangePlaybook();
                    else if (key.Rune == 'x' || key.Rune == '-') Remove();
                    return true;
                default:
                    return true;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, 100);
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(6, Items.Count + 5);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int y = 0;
            int nameWidth = Math.Max(20, width / 2);
            SurfaceText.Draw(content, 2, y, TextCells.PadRight(T("Playbook"), nameWidth) + " " + T("Delivery Mode"), Dim(), width - 2);
            y++;
            if (_Field.Available.Count(p => p.Active) == 0 && Items.Count == 0)
            {
                SurfaceText.Draw(content, 0, y++, T("No active playbooks found."), Body(), width);
                SurfaceText.Draw(content, 0, y++, T("Create one from the Playbooks page, then return here to attach it."), Dim(), width);
                return;
            }

            for (int i = 0; i < Items.Count && y < content.Size.Height - 2; i++)
            {
                SelectedPlaybook item = Items[i];
                CellStyle style = i == Cursor ? Theme.Selection : Body();
                SurfaceText.FillRow(content, 0, y, width, style);
                string row = (i + 1) + ". " + TextCells.PadRight(TextCells.Truncate(_Field.NameOf(item.PlaybookId), nameWidth - 3), nameWidth - 3) + " " + T(PlaybookSelectionField.ModeLabel(item.DeliveryMode));
                SurfaceText.Draw(content, 0, y++, row, style, width);
            }

            if (Items.Count == 0) SurfaceText.Draw(content, 0, y++, T("None") + "  (a " + T("Add playbook") + ")", Dim(), width);
            if (Cursor >= 0 && Cursor < Items.Count && y < content.Size.Height)
            {
                SurfaceText.Draw(content, 0, content.Size.Height - 1, T(PlaybookSelectionField.ModeDescription(Items[Cursor].DeliveryMode)), Dim(), width);
            }
        }

        #endregion

        #region Private-Methods

        private void PickMode(PlaybookDeliveryModeEnum current, Action<PlaybookDeliveryModeEnum> apply)
        {
            List<SelectOption<PlaybookDeliveryModeEnum>> options = new List<SelectOption<PlaybookDeliveryModeEnum>>();
            foreach (PlaybookDeliveryModeEnum mode in new[] { PlaybookDeliveryModeEnum.InlineFullContent, PlaybookDeliveryModeEnum.InstructionWithReference, PlaybookDeliveryModeEnum.AttachIntoWorktree })
            {
                options.Add(new SelectOption<PlaybookDeliveryModeEnum>(mode, T(PlaybookSelectionField.ModeLabel(mode)), T(PlaybookSelectionField.ModeDescription(mode))));
            }

            PickerModal<PlaybookDeliveryModeEnum> picker = new PickerModal<PlaybookDeliveryModeEnum>("Delivery Mode", options, Localizer, Theme);
            picker.List.SelectValue(current);
            _Host.Show(picker, r =>
            {
                if (r is SelectOption<PlaybookDeliveryModeEnum> chosen) apply(chosen.Value);
            });
        }

        #endregion
    }
}
