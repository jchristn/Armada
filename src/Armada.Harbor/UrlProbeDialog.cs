namespace Armada.Harbor
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Connectivity;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// The modal a <see cref="UrlTextBox"/>'s Validate button opens: it tests the URL with <see cref="UrlProbe"/> (DNS,
    /// TCP, TLS for https and wss, then an HTTP GET or a WebSocket upgrade, never with credentials) and shows each stage,
    /// the time taken, and a plain success or failure reason. Cancel stops a test in progress; Close dismisses it.
    /// </summary>
    public class UrlProbeDialog : Window
    {
        #region Public-Members

        /// <summary>
        /// How long the whole test may take.
        /// </summary>
        public const int TimeoutMs = 10000;

        /// <summary>
        /// The last result, or null while the first test runs.
        /// </summary>
        public UrlProbeResult? Result
        {
            get { return _Result; }
        }

        /// <summary>
        /// True while a test runs.
        /// </summary>
        public bool IsRunning
        {
            get { return _Cts != null; }
        }

        #endregion

        #region Private-Members

        private readonly string _Url;
        private readonly StackPanel _Steps = new StackPanel { Spacing = 8 };
        private readonly TextBlock _Summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        private readonly TextBlock _Elapsed = new TextBlock { FontSize = 12 };
        private readonly Border _SummaryPanel;
        private readonly ProgressBar _Progress = new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4 };
        private readonly Button _Again = new Button { Content = "Test Again" };
        private readonly Button _Close = new Button { Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        private CancellationTokenSource? _Cts = null;
        private UrlProbeResult? _Result = null;
        private IDisposable? _SummaryBrush = null;
        private IDisposable? _SummaryBackground = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parameterless constructor for the Avalonia runtime loader / previewer.
        /// </summary>
        public UrlProbeDialog() : this("URL", "http://127.0.0.1/")
        {
        }

        /// <summary>
        /// Instantiate. The test starts when the window opens.
        /// </summary>
        /// <param name="label">What the URL is, for the title.</param>
        /// <param name="url">URL to test.</param>
        public UrlProbeDialog(string label, string url)
        {
            _Url = url ?? String.Empty;
            Title = "Validate " + (label ?? "URL");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (Application.Current is App app && app.WindowIcon != null) Icon = app.WindowIcon;

            StackPanel root = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
            root.Children.Add(new TextBlock { Text = Title, FontSize = 16, FontWeight = FontWeight.SemiBold });
            SelectableTextBlock urlText = HarborUi.Secondary(new SelectableTextBlock
            {
                Text = _Url.Length > 0 ? _Url : "(empty)",
                FontFamily = new FontFamily(HarborUi.MonospaceFonts),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(urlText);
            root.Children.Add(HarborUi.Help("Tests whether this computer can reach the address. No access key, password, or other credential is sent."));
            root.Children.Add(_Progress);

            Border steps = new Border { Child = _Steps };
            steps.Classes.Add("card");
            root.Children.Add(steps);

            StackPanel summary = new StackPanel { Spacing = 4 };
            summary.Children.Add(_Summary);
            summary.Children.Add(HarborUi.Secondary(_Elapsed));
            _SummaryPanel = new Border { Child = summary, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10), BorderThickness = new Thickness(3, 0, 0, 0) };
            root.Children.Add(_SummaryPanel);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(_Again);
            buttons.Children.Add(_Close);
            root.Children.Add(buttons);
            Content = root;

            _Again.Click += (sender, args) => _ = RunAsync();
            _Close.Click += (sender, args) =>
            {
                if (_Cts != null) _Cts.Cancel();
                else Close();
            };
            Opened += (sender, args) => _ = RunAsync();
            Closed += (sender, args) => _Cts?.Cancel();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the dialog for a URL: modal to <paramref name="owner"/> when it is visible.
        /// </summary>
        /// <param name="owner">Owner window, or null.</param>
        /// <param name="label">What the URL is.</param>
        /// <param name="url">URL to test.</param>
        /// <returns>Task that completes when the dialog closes.</returns>
        public static async Task ShowAsync(Window? owner, string label, string url)
        {
            UrlProbeDialog dialog = new UrlProbeDialog(label, url);
            if (owner != null && owner.IsVisible)
            {
                await dialog.ShowDialog(owner).ConfigureAwait(true);
                return;
            }

            TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>();
            dialog.Closed += (sender, args) => closed.TrySetResult(true);
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Show();
            dialog.Activate();
            await closed.Task.ConfigureAwait(true);
        }

        /// <summary>
        /// Run the test (again), replacing what is shown. Does nothing while a test runs.
        /// </summary>
        /// <returns>The result.</returns>
        public async Task<UrlProbeResult?> RunAsync()
        {
            if (_Cts != null) return null;
            CancellationTokenSource cts = new CancellationTokenSource();
            _Cts = cts;
            ShowRunning();
            try
            {
                UrlProbeOptions options = new UrlProbeOptions { TimeoutMs = TimeoutMs, UserAgent = "Armada.Harbor/" + HarborDiagnostics.Version() + " (Validate)" };
                UrlProbeResult result = await Task.Run(() => UrlProbe.ProbeAsync(_Url, options, cts.Token)).ConfigureAwait(true);
                _Result = result;
                ShowResult(result);
                return result;
            }
            finally
            {
                _Cts = null;
                cts.Dispose();
                _Progress.IsVisible = false;
                _Again.IsEnabled = true;
                _Close.Content = "Close";
            }
        }

        #endregion

        #region Private-Methods

        private void ShowRunning()
        {
            _Steps.Children.Clear();
            _Steps.Children.Add(HarborUi.Note("Testing..."));
            _Progress.IsVisible = true;
            _Again.IsEnabled = false;
            _Close.Content = "Cancel";
            SetSummary("Testing " + (_Url.Length > 0 ? _Url : "the URL") + "...", null, "HarborSecondaryTextBrush", "HarborHoverBrush", "HarborBorderBrush");
        }

        private void ShowResult(UrlProbeResult result)
        {
            _Steps.Children.Clear();
            foreach (UrlProbeStep step in result.Steps) _Steps.Children.Add(BuildStep(step));

            string elapsed = "Total time: " + result.ElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms";
            if (result.Failure == UrlProbeFailureEnum.Cancelled)
                SetSummary("Cancelled.", elapsed, "HarborSecondaryTextBrush", "HarborHoverBrush", "HarborBorderBrush");
            else if (result.Succeeded)
                SetSummary(Sentence(result.Summary) + (result.CredentialsRequired ? " The server asks for credentials, which a validation never sends." : ""), elapsed, "HarborSuccessBrush", "HarborCardBrush", "HarborSuccessBrush");
            else
                SetSummary(Sentence("Failed: " + result.Summary), elapsed, "HarborDangerBrush", "HarborCardBrush", "HarborDangerBrush");
        }

        private static Control BuildStep(UrlProbeStep step)
        {
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,92,*,Auto"), ColumnSpacing = 8 };

            string glyph;
            string brush;
            switch (step.Status)
            {
                case UrlProbeStepStatusEnum.Passed:
                    glyph = "\u2713";
                    brush = "HarborSuccessBrush";
                    break;
                case UrlProbeStepStatusEnum.Failed:
                    glyph = "\u2715";
                    brush = "HarborDangerBrush";
                    break;
                default:
                    glyph = "\u2013";
                    brush = "HarborIdleBrush";
                    break;
            }

            TextBlock icon = new TextBlock { Text = glyph, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Top };
            icon.Bind(TextBlock.ForegroundProperty, icon.GetResourceObservable(brush));
            Avalonia.Automation.AutomationProperties.SetName(icon, step.Status.ToString());
            row.Children.Add(icon);

            TextBlock name = new TextBlock { Text = StepName(step.Step), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            SelectableTextBlock detail = HarborUi.Secondary(new SelectableTextBlock { Text = step.Detail, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
            Grid.SetColumn(detail, 2);
            row.Children.Add(detail);

            TextBlock ms = HarborUi.Secondary(new TextBlock
            {
                Text = step.Status == UrlProbeStepStatusEnum.Skipped ? "" : step.ElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms",
                FontFamily = new FontFamily(HarborUi.MonospaceFonts),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Top
            });
            Grid.SetColumn(ms, 3);
            row.Children.Add(ms);
            return row;
        }

        private static string StepName(UrlProbeStepEnum step)
        {
            switch (step)
            {
                case UrlProbeStepEnum.Parse: return "URL";
                case UrlProbeStepEnum.Dns: return "DNS";
                case UrlProbeStepEnum.Tcp: return "TCP";
                case UrlProbeStepEnum.Tls: return "TLS";
                case UrlProbeStepEnum.Http: return "HTTP";
                case UrlProbeStepEnum.WebSocket: return "WebSocket";
                default: return step.ToString();
            }
        }

        private void SetSummary(string text, string? elapsed, string textBrush, string backgroundBrush, string borderBrush)
        {
            _Summary.Text = text;
            _Elapsed.Text = elapsed ?? String.Empty;
            _Elapsed.IsVisible = elapsed != null;
            _SummaryBrush?.Dispose();
            _SummaryBrush = _Summary.Bind(TextBlock.ForegroundProperty, _Summary.GetResourceObservable(textBrush));
            _SummaryBackground?.Dispose();
            _SummaryBackground = _SummaryPanel.Bind(Border.BackgroundProperty, _SummaryPanel.GetResourceObservable(backgroundBrush));
            _SummaryPanel.Bind(Border.BorderBrushProperty, _SummaryPanel.GetResourceObservable(borderBrush));
        }

        private static string Sentence(string text)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return trimmed;
            char last = trimmed[trimmed.Length - 1];
            return last == '.' || last == '!' || last == '?' ? trimmed : trimmed + ".";
        }

        #endregion
    }
}
