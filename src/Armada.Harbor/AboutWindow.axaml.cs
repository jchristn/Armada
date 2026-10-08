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

            List<string[]> rows = new List<string[]>
            {
                new string[] { "Harbor", settings.Name },
                new string[] { "Harbor ID", settings.HarborId },
                new string[] { "Link URL", settings.ServerLinkUrl },
                new string[] { "Harbor settings", HarborAppSettings.DefaultPath() },
                new string[] { "Armada data", admiral == null ? "-" : admiral.DataDirectory + (admiral.IsLocal ? "" : " (not available: " + admiral.Reason.TrimEnd('.') + ")") },
                new string[] { ".NET", RuntimeInformation.FrameworkDescription },
                new string[] { "OS", RuntimeInformation.OSDescription }
            };

            for (int i = 0; i < rows.Count; i++)
            {
                DetailsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                TextBlock label = new TextBlock { Text = rows[i][0], FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top };
                Grid.SetRow(label, i);
                Grid.SetColumn(label, 0);
                DetailsGrid.Children.Add(label);

                SelectableTextBlock value = new SelectableTextBlock { Text = rows[i][1], TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                value.Bind(SelectableTextBlock.ForegroundProperty, value.GetResourceObservable("HarborSecondaryTextBrush"));
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);
                DetailsGrid.Children.Add(value);
            }
        }

        #endregion

        #region Private-Methods

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
