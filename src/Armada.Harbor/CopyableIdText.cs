namespace Armada.Harbor
{
    using System;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Automation;
    using Avalonia.Controls;
    using Avalonia.Controls.Shapes;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;

    /// <summary>
    /// An identifier (a Harbor, mission, captain, job, tenant, or user ID) in a fixed-width font with a copy button next
    /// to it. Clicking the button copies the ID; its icon turns into a green check for about 1.5 seconds, then back.
    /// Every ID Harbor shows uses this control. With <see cref="IsEditable"/> the ID is in a text box (a settings field)
    /// and the button copies what is typed.
    /// </summary>
    public class CopyableIdText : UserControl
    {
        #region Public-Members

        /// <summary>
        /// How long the check mark shows after a copy.
        /// </summary>
        public static readonly TimeSpan CopiedDuration = TimeSpan.FromMilliseconds(1500);

        /// <summary>
        /// The ID shown and copied.
        /// </summary>
        public string? Id
        {
            get { return IsEditable ? _Input.Text : _Id; }
            set
            {
                _Id = value;
                _Display.Text = String.IsNullOrEmpty(value) ? "-" : value;
                _Input.Text = value ?? String.Empty;
                UpdateCopyEnabled();
            }
        }

        /// <summary>
        /// What the ID is (for example "Harbor ID"), for the button's accessible name and tooltip.
        /// </summary>
        public string Label
        {
            get { return _Label; }
            set
            {
                _Label = String.IsNullOrWhiteSpace(value) ? "ID" : value;
                AutomationProperties.SetName(_Input, _Label);
                AutomationProperties.SetName(_Display, _Label);
                ShowCopyIcon();
            }
        }

        /// <summary>
        /// True to show the ID in an editable text box instead of as text.
        /// </summary>
        public bool IsEditable
        {
            get { return _Input.IsVisible; }
            set
            {
                _Input.IsVisible = value;
                _Display.IsVisible = !value;
                UpdateCopyEnabled();
            }
        }

        /// <summary>
        /// The text box used when <see cref="IsEditable"/> (for watermarks and change events).
        /// </summary>
        public TextBox Input
        {
            get { return _Input; }
        }

        /// <summary>
        /// True while the check mark shows after a copy.
        /// </summary>
        public bool IsShowingCopied
        {
            get { return _CopiedTimer.IsEnabled; }
        }

        /// <summary>
        /// The copy button.
        /// </summary>
        public Button CopyButton
        {
            get { return _Button; }
        }

        #endregion

        #region Private-Members

        // A 16x16 copy glyph (two overlapping pages) and a check mark, drawn as strokes.
        private static readonly Geometry _CopyGeometry = Geometry.Parse("M 5.5,5.5 H 13.5 V 14.5 H 5.5 Z M 3,11 V 2.5 H 10.5");
        private static readonly Geometry _CheckGeometry = Geometry.Parse("M 2.5,8.5 L 6.5,12.5 L 13.5,4");

        private readonly SelectableTextBlock _Display;
        private readonly TextBox _Input;
        private readonly Button _Button;
        private readonly Path _Icon;
        private readonly DispatcherTimer _CopiedTimer;
        private IDisposable? _IconBrushBinding = null;
        private string _Label = "ID";
        private string? _Id = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty, read-only ID.
        /// </summary>
        public CopyableIdText() : this(null, "ID")
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">The ID, or null.</param>
        /// <param name="label">What the ID is, for example "Mission ID".</param>
        /// <param name="isEditable">True for an editable text box.</param>
        public CopyableIdText(string? id, string label, bool isEditable = false)
        {
            FontFamily mono = new FontFamily(HarborUi.MonospaceFonts);
            _Display = HarborUi.Secondary(new SelectableTextBlock
            {
                FontFamily = mono,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            _Input = new TextBox { FontFamily = mono, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
            _Input.TextChanged += (sender, args) => UpdateCopyEnabled();

            _Icon = new Path
            {
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.6,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round
            };
            _Button = new Button
            {
                Content = _Icon,
                Padding = new Thickness(5),
                MinWidth = 0,
                MinHeight = 0,
                VerticalAlignment = VerticalAlignment.Center
            };
            _Button.Classes.Add("subtle");
            _Button.Classes.Add("icon");
            _Button.Click += (sender, args) => _ = CopyAsync();

            _CopiedTimer = new DispatcherTimer { Interval = CopiedDuration };
            _CopiedTimer.Tick += (sender, args) =>
            {
                _CopiedTimer.Stop();
                ShowCopyIcon();
            };

            Grid root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 2 };
            root.Children.Add(_Display);
            root.Children.Add(_Input);
            Grid.SetColumn(_Button, 1);
            root.Children.Add(_Button);
            Content = root;

            Label = label;
            IsEditable = isEditable;
            if (isEditable)
            {
                // A settings field fills its column like the other inputs.
                root.ColumnDefinitions = new ColumnDefinitions("*,Auto");
                root.ColumnSpacing = 4;
            }

            Id = id;
            DetachedFromVisualTree += (sender, args) => _CopiedTimer.Stop();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Copy the ID to the clipboard and show the check mark (the button does this).
        /// </summary>
        /// <returns>True when copied.</returns>
        public async Task<bool> CopyAsync()
        {
            string id = (Id ?? String.Empty).Trim();
            if (id.Length == 0) return false;
            Avalonia.Input.Platform.IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return false;

            try
            {
                await clipboard.SetTextAsync(id).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.ExternalException || ex is NotSupportedException)
            {
                ToolTip.SetTip(_Button, "Could not copy " + _Label + ": " + ex.Message);
                return false;
            }

            ShowCopiedIcon();
            _CopiedTimer.Stop();
            _CopiedTimer.Start();
            return true;
        }

        #endregion

        #region Private-Methods

        private void ShowCopyIcon()
        {
            _Icon.Data = _CopyGeometry;
            BindIconBrush("HarborSecondaryTextBrush");
            string tip = "Copy " + _Label;
            ToolTip.SetTip(_Button, tip);
            AutomationProperties.SetName(_Button, tip);
        }

        private void ShowCopiedIcon()
        {
            _Icon.Data = _CheckGeometry;
            BindIconBrush("HarborSuccessBrush");
            ToolTip.SetTip(_Button, "Copied");
            AutomationProperties.SetName(_Button, _Label + " copied");
        }

        private void BindIconBrush(string key)
        {
            _IconBrushBinding?.Dispose();
            _IconBrushBinding = _Icon.Bind(Shape.StrokeProperty, _Icon.GetResourceObservable(key));
        }

        private void UpdateCopyEnabled()
        {
            if (_Button == null) return;
            _Button.IsEnabled = (Id ?? String.Empty).Trim().Length > 0;
        }

        #endregion
    }
}
