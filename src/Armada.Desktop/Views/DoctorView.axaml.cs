namespace Armada.Desktop.Views
{
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Armada.Desktop.ViewModels;

    /// <summary>
    /// System health check view.
    /// </summary>
    public partial class DoctorView : UserControl
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public DoctorView()
        {
            InitializeComponent();
        }

        private async void OnRerunClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DoctorViewModel vm)
            {
                await vm.RunChecksAsync();
            }
        }
    }
}
