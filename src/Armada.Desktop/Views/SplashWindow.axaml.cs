namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Markup.Xaml;

    /// <summary>
    /// Splash screen shown during startup while connecting to the Admiral.
    /// </summary>
    public partial class SplashWindow : Window
    {
        #region Private-Members

        private TextBlock? _StatusText;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SplashWindow()
        {
            InitializeComponent();
            _StatusText = this.FindControl<TextBlock>("StatusText");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Update the status message displayed on the splash screen.
        /// </summary>
        /// <param name="message">Status message.</param>
        public void UpdateStatus(string message)
        {
            if (_StatusText != null)
            {
                _StatusText.Text = message;
            }
        }

        #endregion
    }
}
