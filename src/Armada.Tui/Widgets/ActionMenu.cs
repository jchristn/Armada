namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Theming;

    /// <summary>
    /// Row and bulk action menus (<c>.</c> or <c>Shift+F10</c> on a row): a filterable picker of
    /// <see cref="ActionMenuItem"/> whose choice runs on the UI loop thread. Thread-safe to construct; show on the UI
    /// loop thread.
    /// </summary>
    public static class ActionMenu
    {
        #region Public-Methods

        /// <summary>
        /// Show a menu and run the chosen item.
        /// </summary>
        /// <param name="host">Modal host.</param>
        /// <param name="title">English title.</param>
        /// <param name="items">Items.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>The modal (for tests).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="host"/> or <paramref name="items"/> is null.</exception>
        public static PickerModal<ActionMenuItem> Show(IModalHost host, string title, IEnumerable<ActionMenuItem> items, ITextLocalizer localizer, ArmadaTheme theme)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (items == null) throw new ArgumentNullException(nameof(items));
            List<SelectOption<ActionMenuItem>> options = items
                .Select(i =>
                {
                    SelectOption<ActionMenuItem> option = new SelectOption<ActionMenuItem>(i, localizer.T(i.Label), i.KeyHint);
                    option.Enabled = i.Enabled;
                    return option;
                })
                .ToList();
            PickerModal<ActionMenuItem> modal = new PickerModal<ActionMenuItem>(title, options, localizer, theme);
            host.Show(modal, result =>
            {
                if (result is SelectOption<ActionMenuItem> chosen && chosen.Enabled) chosen.Value.Action();
            });
            return modal;
        }

        #endregion
    }
}
