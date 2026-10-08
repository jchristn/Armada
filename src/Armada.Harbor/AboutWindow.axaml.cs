namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using Armada.Core.Hosting;
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Avalonia.Layout;

    /// <summary>
    /// The About Armada Harbor window: version, identity, and the paths Harbor uses. Framework code-behind (partial
    /// class as Avalonia requires).
    /// </summary>
    public partial class AboutWindow : Window
    {
        #region Private-Members

        private readonly IHarborMenuHost? _Host;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parameterless constructor for the Avalonia runtime XAML loader / previewer.
        /// </summary>
        public AboutWindow() : this(new HarborAppSettings(), null, null)
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Harbor settings.</param>
        /// <param name="admiral">Local Admiral paths, or null when not resolved.</param>
        /// <param name="host">Menu host, for Copy Diagnostics; null hides nothing but makes the button a no-op.</param>
        public AboutWindow(HarborAppSettings settings, LocalAdmiralInfo? admiral, IHarborMenuHost? host)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Host = host;
            InitializeComponent();

            VersionText.Text = "Version " + HarborDiagnostics.Version();

            AddText("Harbor", settings.Name);
            AddValue("Harbor ID", new CopyableIdText(settings.HarborId, "Harbor ID") { HorizontalAlignment = HorizontalAlignment.Left });
            AddValue("Link URL", new UrlTextBox(settings.ServerLinkUrl, "Link URL", true));
            AddText("Harbor settings", HarborAppSettings.DefaultPath());
            AddText("Armada data", admiral == null ? "-" : admiral.DataDirectory + (admiral.IsLocal ? "" : " (not available: " + admiral.Reason.TrimEnd('.') + ")"));
            AddText(".NET", RuntimeInformation.FrameworkDescription);
            AddText("OS", RuntimeInformation.OSDescription);
        }

        #endregion

        #region Private-Methods

        private void AddText(string label, string value)
        {
            SelectableTextBlock text = new SelectableTextBlock { Text = value, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            text.Bind(SelectableTextBlock.ForegroundProperty, text.GetResourceObservable("HarborSecondaryTextBrush"));
            AddValue(label, text);
        }

        private void AddValue(string label, Control value)
        {
            int row = DetailsGrid.RowDefinitions.Count;
            DetailsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            TextBlock labelBlock = new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            DetailsGrid.Children.Add(labelBlock);

            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            DetailsGrid.Children.Add(value);
        }

        private void OnCopyDiagnosticsClick(object? sender, RoutedEventArgs e)
        {
            if (_Host != null && _Host.CanExecute(HarborMenuCommandEnum.CopyDiagnostics))
                _Host.Execute(HarborMenuCommandEnum.CopyDiagnostics);
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
