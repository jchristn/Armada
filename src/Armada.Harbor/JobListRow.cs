namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.Shapes;
    using Avalonia.Input;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.VisualTree;

    /// <summary>
    /// One row of the Running now list: a status dot, what the job is and its runtime, where it belongs and how long it
    /// has run, who runs it, its branch and IDs (copyable), its latest activity, and Open in Dashboard and View output.
    /// A click (or Enter or Space) expands it to show the job's last output lines.
    /// </summary>
    public class JobListRow : Border
    {
        #region Public-Members

        /// <summary>
        /// The job this row shows.
        /// </summary>
        public string JobId { get; }

        /// <summary>
        /// Whether the last output lines are shown.
        /// </summary>
        public bool IsExpanded
        {
            get => _IsExpanded;
            set
            {
                _IsExpanded = value;
                _Recent.IsVisible = value;
                ToolTip.SetTip(this, value ? "Click to hide the last output lines" : "Click to show the last output lines");
            }
        }

        /// <summary>
        /// Raised when the row is expanded or collapsed by the user.
        /// </summary>
        public event EventHandler? ExpandedChanged;

        /// <summary>
        /// Raised with the dashboard link when Open in Dashboard is clicked.
        /// </summary>
        public event Action<string>? OpenLink;

        /// <summary>
        /// Raised when View output is clicked.
        /// </summary>
        public event Action<HarborRunningJobView>? ViewOutput;

        #endregion

        #region Private-Members

        private readonly TextBlock _Title = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _Runtime = new TextBlock { FontSize = 12 };
        private readonly TextBlock _Context = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _Elapsed;
        private readonly TextBlock _Captain = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _ConversationLabel = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private readonly CopyableIdText _Conversation = new CopyableIdText(null, "Conversation ID");
        private readonly WrapPanel _CaptainLine = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6 };
        private readonly TextBlock _BranchLabel = new TextBlock { Text = "Branch", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _Branch = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private readonly CopyableIdText _Mission = new CopyableIdText(null, "Mission ID");
        private readonly TextBlock _CaptainIdLabel = new TextBlock { Text = "Captain", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        private readonly CopyableIdText _CaptainId = new CopyableIdText(null, "Captain ID");
        private readonly WrapPanel _IdLine = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6 };
        private readonly TextBlock _Activity = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _Recent = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private readonly Button _OpenDashboard;
        private readonly Button _ViewOutput;
        private HarborRunningJobView? _View = null;
        private bool _IsExpanded = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="jobId">The job.</param>
        public JobListRow(string jobId)
        {
            JobId = jobId ?? String.Empty;
            Tag = JobId;
            Padding = new Thickness(0, 6, 0, 8);
            BorderThickness = new Thickness(0, 0, 0, 1);
            Background = Brushes.Transparent;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            this.Bind(BorderBrushProperty, this.GetResourceObservable("HarborBorderBrush"));

            Ellipse dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 5, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            dot.Bind(Shape.FillProperty, dot.GetResourceObservable("HarborSuccessBrush"));
            ToolTip.SetTip(dot, "Running");

            _Runtime.Bind(TextBlock.ForegroundProperty, _Runtime.GetResourceObservable("HarborAccentSoftTextBrush"));
            Border runtime = new Border
            {
                Child = _Runtime,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            runtime.Bind(Border.BackgroundProperty, runtime.GetResourceObservable("HarborAccentSoftBrush"));

            _Elapsed = HarborUi.Secondary(new TextBlock
            {
                FontFamily = new FontFamily(HarborUi.MonospaceFonts),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            });
            ToolTip.SetTip(_Elapsed, "Running for this long");

            HarborUi.Secondary(_Context);
            HarborUi.Secondary(_Captain);
            HarborUi.Secondary(_ConversationLabel);
            HarborUi.Secondary(_BranchLabel);
            HarborUi.Secondary(_CaptainIdLabel);
            _Branch.FontFamily = new FontFamily(HarborUi.MonospaceFonts);
            _Activity.FontFamily = new FontFamily(HarborUi.MonospaceFonts);
            _Recent.FontFamily = new FontFamily(HarborUi.MonospaceFonts);
            HarborUi.Secondary(_Recent);
            _Activity.Bind(TextBlock.ForegroundProperty, _Activity.GetResourceObservable("HarborTextBrush"));

            _CaptainLine.Children.Add(_Captain);
            _CaptainLine.Children.Add(_ConversationLabel);
            _CaptainLine.Children.Add(_Conversation);

            _IdLine.Children.Add(_BranchLabel);
            _IdLine.Children.Add(_Branch);
            _IdLine.Children.Add(_Mission);
            _IdLine.Children.Add(_CaptainIdLabel);
            _IdLine.Children.Add(_CaptainId);

            _OpenDashboard = HarborUi.Button("Open in Dashboard", () =>
            {
                if (_View?.DashboardLink != null) OpenLink?.Invoke(_View.DashboardLink);
            }, "Open this job's page in the Armada dashboard");
            _OpenDashboard.Classes.Add("subtle");
            _ViewOutput = HarborUi.Button("View output", () =>
            {
                if (_View != null) ViewOutput?.Invoke(_View);
            }, "Open this job's log in Status > Logs");
            _ViewOutput.Classes.Add("subtle");
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(_OpenDashboard);
            actions.Children.Add(_ViewOutput);

            // Two columns: what the job is on the left, its runtime and elapsed time on the right; the rest spans both.
            Grid body = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
                ColumnSpacing = 10,
                RowSpacing = 2
            };
            Place(body, _Title, 0, 0, 1);
            Place(body, runtime, 0, 1, 1);
            Place(body, _Context, 1, 0, 1);
            Place(body, _Elapsed, 1, 1, 1);
            Place(body, _CaptainLine, 2, 0, 2);
            Place(body, _IdLine, 3, 0, 2);
            Place(body, _Activity, 4, 0, 2);
            Border recent = new Border { Child = _Recent, Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 4, 0, 0) };
            recent.Bind(Border.BackgroundProperty, recent.GetResourceObservable("HarborLogBrush"));
            recent.Bind(IsVisibleProperty, _Recent.GetObservable(IsVisibleProperty));
            Place(body, recent, 5, 0, 2);
            Place(body, actions, 6, 0, 2);

            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            row.Children.Add(dot);
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            Child = row;

            Tapped += OnTapped;
            KeyDown += OnKeyDown;
            IsExpanded = false;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show a job's current state.
        /// </summary>
        /// <param name="view">The row as text.</param>
        public void Apply(HarborRunningJobView view)
        {
            _View = view ?? throw new ArgumentNullException(nameof(view));
            _Title.Text = view.Title;
            ToolTip.SetTip(_Title, view.Title);
            _Runtime.Text = view.Runtime;
            _Elapsed.Text = view.Elapsed;

            _Context.Text = view.Context ?? String.Empty;
            _Context.IsVisible = view.Context != null;

            _Captain.Text = view.Captain ?? String.Empty;
            _Captain.IsVisible = view.Captain != null;
            bool hasConversation = view.ConversationId != null;
            _ConversationLabel.Text = (view.Captain != null ? "-  " : String.Empty) + (view.ConversationLabel ?? "conversation");
            _ConversationLabel.IsVisible = hasConversation;
            _Conversation.Id = view.ConversationId;
            _Conversation.IsVisible = hasConversation;
            _CaptainLine.IsVisible = view.Captain != null || hasConversation;

            // The second line sits beside the elapsed time: the vessel and voyage of a mission, else who runs the job.
            bool captainOnSecondLine = view.Context == null;
            Grid.SetRow(_CaptainLine, captainOnSecondLine ? 1 : 2);
            Grid.SetColumnSpan(_CaptainLine, captainOnSecondLine ? 1 : 2);

            _Branch.Text = view.Branch ?? String.Empty;
            _BranchLabel.IsVisible = view.Branch != null;
            _Branch.IsVisible = view.Branch != null;
            _Mission.Id = view.MissionId;
            _Mission.IsVisible = view.MissionId != null;
            // The captain's ID stands in for its name when the Admiral did not send one (an older Admiral).
            bool showCaptainId = view.Captain == null && view.CaptainId != null;
            _CaptainIdLabel.IsVisible = showCaptainId;
            _CaptainId.Id = view.CaptainId;
            _CaptainId.IsVisible = showCaptainId;
            _IdLine.IsVisible = view.Branch != null || view.MissionId != null || showCaptainId;

            _Activity.Text = view.Activity ?? String.Empty;
            ToolTip.SetTip(_Activity, view.Activity);
            _Activity.IsVisible = view.Activity != null;

            List<string> lines = view.RecentLines;
            _Recent.Text = lines.Count == 0 ? "No output yet." : String.Join("\n", lines);

            _OpenDashboard.IsVisible = view.DashboardLink != null;
            _ViewOutput.IsVisible = view.LogPath != null;
        }

        #endregion

        #region Private-Methods

        private static void Place(Grid grid, Control control, int row, int column, int columnSpan)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, columnSpan);
            grid.Children.Add(control);
        }

        private void OnTapped(object? sender, TappedEventArgs e)
        {
            // Buttons and copyable IDs keep their own clicks.
            if (e.Source is Visual source && IsInsideInteractive(source)) return;
            Toggle();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Source != this) return;
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            e.Handled = true;
            Toggle();
        }

        private void Toggle()
        {
            IsExpanded = !IsExpanded;
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool IsInsideInteractive(Visual source)
        {
            Visual? current = source;
            while (current != null && current != this)
            {
                if (current is Button || current is CopyableIdText || current is TextBox) return true;
                current = current.GetVisualParent();
            }

            return false;
        }

        #endregion
    }
}
