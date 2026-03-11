namespace Armada.Desktop.Views
{
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Interactivity;

    /// <summary>
    /// A simple confirmation dialog for destructive actions.
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        #region Private-Members

        private bool _Confirmed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ConfirmDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Instantiate with title, message, and confirm button text.
        /// </summary>
        /// <param name="title">Dialog title.</param>
        /// <param name="message">Confirmation message.</param>
        /// <param name="confirmText">Confirm button text (e.g., "Cancel Voyage", "Stop All").</param>
        /// <param name="isDanger">Whether this is a danger action (red button).</param>
        public ConfirmDialog(string title, string message, string confirmText = "Confirm", bool isDanger = true) : this()
        {
            TextBlock? titleBlock = this.FindControl<TextBlock>("TitleText");
            TextBlock? messageBlock = this.FindControl<TextBlock>("MessageText");
            Button? confirmButton = this.FindControl<Button>("ConfirmButton");

            if (titleBlock != null) titleBlock.Text = title;
            if (messageBlock != null) messageBlock.Text = message;
            if (confirmButton != null)
            {
                confirmButton.Content = confirmText;
                if (!isDanger)
                {
                    confirmButton.Background = Avalonia.Media.SolidColorBrush.Parse("#1E90FF");
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the dialog and return whether the user confirmed.
        /// </summary>
        /// <param name="owner">Owner window.</param>
        /// <returns>True if confirmed.</returns>
        public async Task<bool> ShowConfirmAsync(Window owner)
        {
            await ShowDialog(owner);
            return _Confirmed;
        }

        #endregion

        #region Private-Methods

        private void OnConfirmClick(object? sender, RoutedEventArgs e)
        {
            _Confirmed = true;
            Close();
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            _Confirmed = false;
            Close();
        }

        #endregion
    }
}
