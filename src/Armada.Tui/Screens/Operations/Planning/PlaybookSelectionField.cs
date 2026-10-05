namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The dashboard's PlaybookSelector as a one-line form field: shows the selected playbooks with their delivery
    /// modes; <c>Enter</c> or <c>Space</c> opens <see cref="PlaybookSelectionDialog"/> to add, remove, and change
    /// delivery modes (Inline Full Content, Instruction With Reference, Attach Into Worktree); <c>Del</c> clears.
    /// Not thread-safe.
    /// </summary>
    public class PlaybookSelectionField : ArmadaWidget, IFormField
    {
        #region Public-Members

        /// <summary>
        /// Selected playbooks in order.
        /// </summary>
        public List<SelectedPlaybook> Value { get; private set; } = new List<SelectedPlaybook>();

        /// <summary>
        /// Playbooks available to attach (the owner loads them).
        /// </summary>
        public List<Playbook> Available { get; set; } = new List<Playbook>();

        /// <summary>
        /// True once <see cref="Available"/> was loaded.
        /// </summary>
        public bool Loaded { get; set; } = false;

        /// <summary>
        /// Modal host for the dialog.
        /// </summary>
        public IModalHost? ModalHost { get; set; } = null;

        /// <summary>
        /// Disabled (while submitting).
        /// </summary>
        public bool Disabled { get; set; } = false;

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return String.Join(",", Value.Select(p => p.PlaybookId + ":" + p.DeliveryMode)); }
        }

        /// <inheritdoc />
        public string? FieldError
        {
            get { return null; }
        }

        /// <summary>
        /// Raised after the selection changes.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Public-Methods

        /// <summary>
        /// English label of a delivery mode (the dashboard's DELIVERY_MODE_COPY).
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <returns>Label.</returns>
        public static string ModeLabel(PlaybookDeliveryModeEnum mode)
        {
            switch (mode)
            {
                case PlaybookDeliveryModeEnum.InstructionWithReference: return "Instruction With Reference";
                case PlaybookDeliveryModeEnum.AttachIntoWorktree: return "Attach Into Worktree";
                default: return "Inline Full Content";
            }
        }

        /// <summary>
        /// English description of a delivery mode.
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <returns>Description.</returns>
        public static string ModeDescription(PlaybookDeliveryModeEnum mode)
        {
            switch (mode)
            {
                case PlaybookDeliveryModeEnum.InstructionWithReference: return "Tell the model to read the materialized playbook path outside the worktree.";
                case PlaybookDeliveryModeEnum.AttachIntoWorktree: return "Materialize the playbook in `.armada/playbooks/` and instruct the model to read it there.";
                default: return "Inject the complete markdown into the mission instructions.";
            }
        }

        /// <summary>
        /// Replace the selection.
        /// </summary>
        /// <param name="value">Selection.</param>
        public void SetValue(IEnumerable<SelectedPlaybook>? value)
        {
            Value = (value ?? Enumerable.Empty<SelectedPlaybook>()).Select(Clone).ToList();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// File name of a playbook id, or "{id} (Unavailable)".
        /// </summary>
        /// <param name="playbookId">Id.</param>
        /// <returns>Name.</returns>
        public string NameOf(string playbookId)
        {
            Playbook? p = Available.FirstOrDefault(x => x.Id == playbookId);
            return p != null ? p.FileName : Localizer.T("{{id}} (Unavailable)", LocalizationArgs.Of("id", playbookId));
        }

        /// <summary>
        /// Open the editor dialog.
        /// </summary>
        /// <returns>The dialog, or null without a host.</returns>
        public PlaybookSelectionDialog? Open()
        {
            if (ModalHost == null || Disabled) return null;
            PlaybookSelectionDialog dialog = new PlaybookSelectionDialog(this, ModalHost, Localizer, Theme);
            ModalHost.Show(dialog, r => SetValue(dialog.Items));
            return dialog;
        }

        /// <inheritdoc />
        public bool ValidateField()
        {
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter || (key.Code == KeyCode.Character && key.Rune == ' ' && key.Modifiers == KeyModifiers.None))
            {
                Open();
                return true;
            }

            if ((key.Code == KeyCode.Delete || key.Code == KeyCode.Backspace) && !Disabled)
            {
                SetValue(null);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press) return false;
            Open();
            return true;
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, 1);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            CellStyle style = IsFocused ? Theme.InputFocused : Theme.Input;
            SurfaceText.FillRow(surface, 0, 0, width, style);
            string text;
            CellStyle textStyle = style;
            if (!Loaded && Value.Count == 0)
            {
                text = T("Loading playbooks...");
                textStyle = style.WithForeground(Theme.Muted.Foreground);
            }
            else if (Value.Count == 0)
            {
                text = Available.Count(p => p.Active) == 0 ? T("No active playbooks found.") : T("None") + "  (" + T("Enter to add") + ")";
                textStyle = style.WithForeground(Theme.Muted.Foreground);
            }
            else
            {
                text = Value.Count + ": " + String.Join(", ", Value.Select(v => NameOf(v.PlaybookId) + " (" + T(ModeLabel(v.DeliveryMode)) + ")"));
            }

            SurfaceText.Draw(surface, 0, 0, text, textStyle, Math.Max(0, width - 2));
            if (width >= 2) surface.DrawText(width - 1, 0, ">", style);
        }

        #endregion

        #region Private-Methods

        internal static SelectedPlaybook Clone(SelectedPlaybook p)
        {
            SelectedPlaybook c = new SelectedPlaybook();
            c.PlaybookId = p.PlaybookId;
            c.DeliveryMode = p.DeliveryMode;
            return c;
        }

        #endregion
    }
}
