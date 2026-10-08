namespace Armada.Harbor
{
    using System;
    using Avalonia;
    using Avalonia.Automation;
    using Avalonia.Controls;
    using Avalonia.Layout;

    /// <summary>
    /// A URL field with a Validate button next to it. Validate opens <see cref="UrlProbeDialog"/>, which tests whether
    /// the URL is reachable from this computer (DNS, TCP, TLS, then HTTP or a WebSocket upgrade) without sending any
    /// credentials. Every URL Harbor shows or edits uses this control; <see cref="IsReadOnly"/> shows a URL that cannot
    /// be edited here.
    /// </summary>
    public class UrlTextBox : UserControl
    {
        #region Public-Members

        /// <summary>
        /// The URL.
        /// </summary>
        public string? Text
        {
            get { return _Input.Text; }
            set { _Input.Text = value; }
        }

        /// <summary>
        /// What the URL is (for example "Admiral address"), for the accessible names and the dialog's title.
        /// </summary>
        public string Label
        {
            get { return _Label; }
            set
            {
                _Label = String.IsNullOrWhiteSpace(value) ? "URL" : value;
                AutomationProperties.SetName(_Input, _Label);
                AutomationProperties.SetName(_Validate, "Validate " + _Label);
                ToolTip.SetTip(_Validate, "Test whether " + _Label + " is reachable from this computer (no credentials are sent)");
            }
        }

        /// <summary>
        /// True to show the URL without letting it be edited.
        /// </summary>
        public bool IsReadOnly
        {
            get { return _Input.IsReadOnly; }
            set { _Input.IsReadOnly = value; }
        }

        /// <summary>
        /// The text box (for watermarks and change events).
        /// </summary>
        public TextBox Input
        {
            get { return _Input; }
        }

        /// <summary>
        /// The Validate button.
        /// </summary>
        public Button ValidateButton
        {
            get { return _Validate; }
        }

        #endregion

        #region Private-Members

        private readonly TextBox _Input = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Button _Validate = new Button { Content = "Validate", VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
        private string _Label = "URL";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty, editable URL field.
        /// </summary>
        public UrlTextBox() : this(null, "URL")
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="url">The URL, or null.</param>
        /// <param name="label">What the URL is.</param>
        /// <param name="isReadOnly">True to show it without editing.</param>
        public UrlTextBox(string? url, string label, bool isReadOnly = false)
        {
            Grid root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            root.Children.Add(_Input);
            Grid.SetColumn(_Validate, 1);
            root.Children.Add(_Validate);
            Content = root;

            _Validate.Click += (sender, args) => _ = UrlProbeDialog.ShowAsync(TopLevel.GetTopLevel(this) as Window, _Label, (_Input.Text ?? String.Empty).Trim());
            _Input.TextChanged += (sender, args) => _Validate.IsEnabled = !String.IsNullOrWhiteSpace(_Input.Text);
            Label = label;
            IsReadOnly = isReadOnly;
            Text = url;
            _Validate.IsEnabled = !String.IsNullOrWhiteSpace(url);
        }

        #endregion
    }
}
