namespace Armada.Harbor
{
    using Avalonia;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Markup.Xaml;

    /// <summary>
    /// The Armada Harbor Avalonia application. Framework code-behind, so it is a partial class as Avalonia
    /// requires.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initialize the application from XAML.
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Complete framework initialization by showing the main window.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
