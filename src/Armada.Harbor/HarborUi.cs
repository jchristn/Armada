namespace Armada.Harbor
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// Small builders for the label/value grids, section headings, and notices the Harbor views assemble in code,
    /// so they share one look.
    /// </summary>
    public static class HarborUi
    {
        #region Public-Members

        /// <summary>
        /// Monospace font stack for logs and JSON.
        /// </summary>
        public const string MonospaceFonts = "Consolas, Menlo, monospace";

        #endregion

        #region Public-Methods

        /// <summary>
        /// A grid with a fixed label column and a stretching value column.
        /// </summary>
        /// <param name="labelWidth">Label column width.</param>
        /// <returns>The grid.</returns>
        public static Grid DetailGrid(double labelWidth = 150)
        {
            return new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(labelWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",*"),
                RowSpacing = 6
            };
        }

        /// <summary>
        /// Append a label and a selectable value to a <see cref="DetailGrid"/>.
        /// </summary>
        /// <param name="grid">Grid.</param>
        /// <param name="label">Label.</param>
        /// <param name="value">Value.</param>
        /// <returns>The value control, to update later.</returns>
        public static SelectableTextBlock AddRow(Grid grid, string label, string? value)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            TextBlock labelBlock = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            SelectableTextBlock valueBlock = Secondary(new SelectableTextBlock { Text = value ?? "-", TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
            return valueBlock;
        }

        /// <summary>
        /// A section heading.
        /// </summary>
        /// <param name="text">Heading text.</param>
        /// <returns>The heading.</returns>
        public static TextBlock Heading(string text)
        {
            return new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 2) };
        }

        /// <summary>
        /// Wrapped secondary-colored explanatory text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>The text block.</returns>
        public static TextBlock Note(string text)
        {
            return Secondary(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }

        /// <summary>
        /// Help text under a form field: smaller and dimmer than <see cref="Note"/> so it does not compete with the
        /// field.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>The text block.</returns>
        public static TextBlock Help(string text)
        {
            TextBlock block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("HarborHelpTextBrush"));
            return block;
        }

        /// <summary>
        /// A card: a titled, bordered, slightly raised panel that sets one section of a page apart from the next.
        /// </summary>
        /// <param name="title">Card title.</param>
        /// <param name="body">Card content.</param>
        /// <param name="headerActions">Controls at the right of the title (buttons), or null.</param>
        /// <returns>The card.</returns>
        public static Border Card(string title, Control body, Control? headerActions)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            Grid header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
            if (headerActions != null)
            {
                Grid.SetColumn(headerActions, 1);
                header.Children.Add(headerActions);
            }

            StackPanel content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(body);

            Border card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 14),
                Child = content
            };
            card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("HarborBorderBrush"));
            card.Bind(Border.BackgroundProperty, card.GetResourceObservable("HarborCardBrush"));
            return card;
        }

        /// <summary>
        /// A bordered notice panel (warnings such as "restart required").
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>The panel and its text block.</returns>
        public static Border Notice(TextBlock text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            text.TextWrapping = TextWrapping.Wrap;
            Border border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8),
                Child = text
            };
            border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("HarborBorderBrush"));
            return border;
        }

        /// <summary>
        /// Color a text control with the secondary text brush (follows light and dark).
        /// </summary>
        /// <typeparam name="T">Text control type.</typeparam>
        /// <param name="control">Control.</param>
        /// <returns>The control.</returns>
        public static T Secondary<T>(T control) where T : TextBlock
        {
            control.Bind(TextBlock.ForegroundProperty, control.GetResourceObservable("HarborSecondaryTextBrush"));
            return control;
        }

        /// <summary>
        /// A horizontal button row.
        /// </summary>
        /// <returns>The panel.</returns>
        public static StackPanel ButtonRow()
        {
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        }

        /// <summary>
        /// A button with a click handler.
        /// </summary>
        /// <param name="text">Caption.</param>
        /// <param name="onClick">Handler.</param>
        /// <param name="tip">Tooltip, or null.</param>
        /// <returns>The button.</returns>
        public static Button Button(string text, Action onClick, string? tip = null)
        {
            Button button = new Button { Content = text };
            button.Click += (sender, args) => onClick();
            if (!String.IsNullOrEmpty(tip)) ToolTip.SetTip(button, tip);
            return button;
        }

        /// <summary>
        /// Local time text for a UTC time.
        /// </summary>
        /// <param name="utc">Time.</param>
        /// <returns>Text, or "-" for an unset time.</returns>
        public static string LocalTime(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "-";
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
        }

        #endregion
    }
}
