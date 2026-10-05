namespace Armada.Tui.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Input;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The menu bar (File, Go, View, Actions, Ask, Help) built from <see cref="CommandService"/>, with each item's key
    /// binding shown beside it. Drawn with cell widths (CJK-safe; TUIKit's MenuBar measures with string length).
    /// <c>F10</c> opens it; arrows move; Enter runs; Esc closes; clicks work. The Actions menu always equals the current
    /// screen's commands. Not thread-safe.
    /// </summary>
    public class MenuBarView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Menus in order.
        /// </summary>
        public static IReadOnlyList<CommandMenuEnum> Menus { get; } = new List<CommandMenuEnum>
        {
            CommandMenuEnum.File, CommandMenuEnum.Go, CommandMenuEnum.View, CommandMenuEnum.Actions, CommandMenuEnum.Ask, CommandMenuEnum.Help
        };

        /// <summary>
        /// True while a drop-down is open.
        /// </summary>
        public bool IsOpen { get; private set; } = false;

        /// <summary>
        /// Active menu index.
        /// </summary>
        public int ActiveMenu { get; private set; } = 0;

        /// <summary>
        /// Highlighted item index.
        /// </summary>
        public int Highlight { get; private set; } = 0;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        #endregion

        #region Private-Members

        private readonly CommandService _Commands;
        private readonly List<int> _TitleStarts = new List<int>();
        private Rect _Dropdown = Rect.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="commands">Command registry.</param>
        public MenuBarView(CommandService commands)
        {
            _Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open a menu.
        /// </summary>
        /// <param name="menu">Menu index.</param>
        public void Open(int menu = 0)
        {
            IsOpen = true;
            ActiveMenu = Math.Clamp(menu, 0, Menus.Count - 1);
            Highlight = 0;
        }

        /// <summary>
        /// Close.
        /// </summary>
        public void Close()
        {
            IsOpen = false;
        }

        /// <summary>
        /// Items of a menu.
        /// </summary>
        /// <param name="menu">Menu.</param>
        /// <returns>Commands.</returns>
        public IReadOnlyList<ArmadaCommand> Items(CommandMenuEnum menu)
        {
            return _Commands.ForMenu(menu);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (!IsOpen) return false;
            IReadOnlyList<ArmadaCommand> items = Items(Menus[ActiveMenu]);
            switch (key.Code)
            {
                case KeyCode.Escape:
                case KeyCode.F10:
                    Close();
                    return true;
                case KeyCode.Left:
                    ActiveMenu = (ActiveMenu - 1 + Menus.Count) % Menus.Count;
                    Highlight = 0;
                    return true;
                case KeyCode.Right:
                    ActiveMenu = (ActiveMenu + 1) % Menus.Count;
                    Highlight = 0;
                    return true;
                case KeyCode.Up:
                    if (items.Count > 0) Highlight = (Highlight - 1 + items.Count) % items.Count;
                    return true;
                case KeyCode.Down:
                    if (items.Count > 0) Highlight = (Highlight + 1) % items.Count;
                    return true;
                case KeyCode.Home:
                    Highlight = 0;
                    return true;
                case KeyCode.End:
                    Highlight = Math.Max(0, items.Count - 1);
                    return true;
                case KeyCode.Enter:
                    Activate(items);
                    return true;
                default:
                    return true;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left) return false;
            if (mouse.Y == 0)
            {
                for (int i = _TitleStarts.Count - 1; i >= 0; i--)
                {
                    if (mouse.X >= _TitleStarts[i])
                    {
                        if (IsOpen && ActiveMenu == i) Close();
                        else Open(i);
                        return true;
                    }
                }

                return false;
            }

            if (IsOpen && _Dropdown.Contains(new Point(mouse.X, mouse.Y)))
            {
                int idx = mouse.Y - _Dropdown.Y - 1;
                IReadOnlyList<ArmadaCommand> items = Items(Menus[ActiveMenu]);
                if (idx >= 0 && idx < items.Count)
                {
                    Highlight = idx;
                    Activate(items);
                }

                return true;
            }

            if (IsOpen)
            {
                Close();
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            SurfaceText.FillRow(surface, 0, 0, width, Theme.MenuBar);
            _TitleStarts.Clear();
            int x = 0;
            int activeX = 0;
            for (int i = 0; i < Menus.Count; i++)
            {
                string title = " " + T(Menus[i].ToString()) + " ";
                bool active = IsOpen && i == ActiveMenu;
                _TitleStarts.Add(x);
                if (active) activeX = x;
                x += SurfaceText.Draw(surface, x, 0, title, active ? Theme.MenuActive : Theme.MenuBar, width - x);
            }

            string hint = "F10 " + T("Menu");
            if (x + TextCells.Width(hint) + 2 < width) SurfaceText.Draw(surface, width - TextCells.Width(hint) - 1, 0, hint, Theme.MenuBar.WithForeground(Theme.Muted.Foreground), width);
            _Dropdown = Rect.Empty;
            if (IsOpen && surface.Size.Height > 2) RenderDropdown(surface, activeX);
        }

        #endregion

        #region Private-Methods

        private void Activate(IReadOnlyList<ArmadaCommand> items)
        {
            if (Highlight < 0 || Highlight >= items.Count) return;
            ArmadaCommand command = items[Highlight];
            if (!command.Enabled) return;
            Close();
            _Commands.Run(command, Armada.Tui.Services.TuiTelemetry.SourceMenu);
        }

        private void RenderDropdown(ISurface surface, int x)
        {
            IReadOnlyList<ArmadaCommand> items = Items(Menus[ActiveMenu]);
            List<string> labels = items.Select(c => T(c.Title)).ToList();
            if (items.Count == 0) labels.Add(T("(no actions on this screen)"));
            int labelWidth = labels.Select(TextCells.Width).DefaultIfEmpty(10).Max();
            int keyWidth = items.Select(c => TextCells.Width(c.KeyLabel)).DefaultIfEmpty(0).Max();
            int boxWidth = Math.Min(surface.Size.Width, labelWidth + keyWidth + 6);
            int boxHeight = Math.Min(surface.Size.Height - 1, labels.Count + 2);
            int left = Math.Max(0, Math.Min(x, surface.Size.Width - boxWidth));
            Rect box = new Rect(left, 1, boxWidth, boxHeight);
            _Dropdown = box;
            SurfaceText.FillRect(surface, box, Theme.MenuDropdown);
            surface.DrawBox(box, Theme.MenuDropdown.WithForeground(Theme.Border.Foreground), Theme.AsciiBorders ? BorderStyle.Ascii : BorderStyle.Line);
            int rows = boxHeight - 2;
            int scroll = Math.Max(0, Highlight - rows + 1);
            for (int r = 0; r < rows; r++)
            {
                int i = scroll + r;
                if (i >= labels.Count) break;
                int y = 2 + r;
                bool enabled = i < items.Count && items[i].Enabled;
                bool high = i == Highlight && items.Count > 0;
                CellStyle style = high ? Theme.MenuActive : enabled ? Theme.MenuDropdown : Theme.MenuDropdown.WithForeground(Theme.Disabled.Foreground);
                SurfaceText.FillRow(surface, left + 1, y, boxWidth - 2, style);
                SurfaceText.Draw(surface, left + 2, y, labels[i], style, labelWidth);
                if (i < items.Count && items[i].KeyLabel.Length > 0)
                {
                    string k = items[i].KeyLabel;
                    SurfaceText.Draw(surface, left + boxWidth - 2 - TextCells.Width(k), y, k, high ? style : style.WithForeground(Theme.Muted.Foreground), keyWidth);
                }
            }
        }

        #endregion
    }
}
