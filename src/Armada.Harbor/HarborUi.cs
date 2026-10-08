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
        public const string MonospaceFonts = "Cascadia Code, SF Mono, Menlo, Consolas, monospace";

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
            TextBlock heading = new TextBlock { Text = text, Margin = new Thickness(0, 2, 0, 2) };
            heading.Classes.Add("title");
            return heading;
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
        /// A card: a titled, bordered panel a step above the window background that sets one section of a page apart
        /// from the next (styled by the "card" class in App.axaml).
        /// </summary>
        /// <param name="title">Card title, or null for none.</param>
        /// <param name="body">Card content.</param>
        /// <param name="headerActions">Controls at the right of the title (buttons), or null.</param>
        /// <returns>The card.</returns>
        public static Border Card(string? title, Control body, Control? headerActions)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            StackPanel content = new StackPanel();
            if (title != null || headerActions != null)
            {
                Grid header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
                if (title != null)
                {
                    TextBlock heading = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
                    heading.Classes.Add("title");
                    header.Children.Add(heading);
                }

                if (headerActions != null)
                {
                    Grid.SetColumn(headerActions, 1);
                    header.Children.Add(headerActions);
                }

                content.Children.Add(header);
            }

            content.Children.Add(body);

            Border card = new Border { Child = content };
            card.Classes.Add("card");
            return card;
        }

        /// <summary>
        /// A tab header. A text block keeps the header's font size from reaching the tab's content.
        /// </summary>
        /// <param name="text">Header text.</param>
        /// <returns>The header.</returns>
        public static TextBlock TabHeader(string text)
        {
            return new TextBlock { Text = text, FontSize = 15 };
        }

        /// <summary>
        /// The page layout every tab uses: a scrolling column of cards with consistent spacing.
        /// </summary>
        /// <param name="sections">Cards and notes, top to bottom.</param>
        /// <returns>The page.</returns>
        public static ScrollViewer Page(params Control[] sections)
        {
            StackPanel column = new StackPanel { Spacing = 14, Margin = new Thickness(20, 16, 20, 20) };
            foreach (Control section in sections) column.Children.Add(section);
            return new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        }

        /// <summary>
        /// Color a message block as an error (red that stays readable in light and dark) or as ordinary secondary text.
        /// </summary>
        /// <param name="block">Message block.</param>
        /// <param name="text">Text, or null to hide the block.</param>
        /// <param name="isError">True for an error.</param>
        public static void SetMessage(TextBlock block, string? text, bool isError)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            block.Text = text ?? String.Empty;
            block.IsVisible = !String.IsNullOrEmpty(text);
            block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(isError ? "HarborDangerBrush" : "HarborSecondaryTextBrush"));
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
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8),
                Child = text
            };
            border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("HarborAccentBrush"));
            border.Bind(Border.BackgroundProperty, border.GetResourceObservable("HarborAccentSoftBrush"));
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("HarborTextBrush"));
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
