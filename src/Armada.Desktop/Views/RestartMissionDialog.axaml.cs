namespace Armada.Desktop.Views
{
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Interactivity;

    /// <summary>
    /// Dialog for restarting a mission with optional instruction edits.
    /// </summary>
    public partial class RestartMissionDialog : Window
    {
        #region Private-Members

        private bool _Confirmed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RestartMissionDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Instantiate with current mission title and description pre-filled.
        /// </summary>
        /// <param name="title">Current mission title.</param>
        /// <param name="description">Current mission description.</param>
        public RestartMissionDialog(string title, string description) : this()
        {
            TextBox? titleBox = this.FindControl<TextBox>("TitleBox");
            TextBox? descBox = this.FindControl<TextBox>("DescriptionBox");

            if (titleBox != null) titleBox.Text = title;
            if (descBox != null) descBox.Text = description;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The edited title after the dialog closes.
        /// </summary>
        public string ResultTitle { get; private set; } = "";

        /// <summary>
        /// The edited description after the dialog closes.
        /// </summary>
        public string ResultDescription { get; private set; } = "";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the dialog and return whether the user confirmed the restart.
        /// </summary>
        /// <param name="owner">Owner window.</param>
        /// <returns>True if the user clicked Restart Mission.</returns>
        public async Task<bool> ShowRestartAsync(Window owner)
        {
            await ShowDialog(owner);
            return _Confirmed;
        }

        #endregion

        #region Private-Methods

        private void OnRestartClick(object? sender, RoutedEventArgs e)
        {
            TextBox? titleBox = this.FindControl<TextBox>("TitleBox");
            TextBox? descBox = this.FindControl<TextBox>("DescriptionBox");

            ResultTitle = titleBox?.Text ?? "";
            ResultDescription = descBox?.Text ?? "";
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
