namespace Armada.Harbor
{
    using System;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// A small modal message or confirmation window, so failures and risky actions are seen even when the main
    /// window is hidden (a tray command).
    /// </summary>
    public class HarborDialog : Window
    {
        #region Private-Members

        private bool _Confirmed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parameterless constructor for the Avalonia runtime loader / previewer.
        /// </summary>
        public HarborDialog() : this("Armada Harbor", String.Empty, null, "OK")
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">Window title.</param>
        /// <param name="message">Message.</param>
        /// <param name="confirmText">Confirm button text, or null for a message with only a dismiss button.</param>
        /// <param name="dismissText">Dismiss (cancel) button text.</param>
        public HarborDialog(string title, string message, string? confirmText, string dismissText)
        {
            Title = title ?? "Armada Harbor";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
            Button dismiss = new Button { Content = dismissText, IsCancel = true, IsDefault = confirmText == null };
            dismiss.Click += (sender, args) => Close();
            buttons.Children.Add(dismiss);
            if (confirmText != null)
            {
                Button confirm = new Button { Content = confirmText, IsDefault = true };
                confirm.Click += (sender, args) =>
                {
                    _Confirmed = true;
                    Close();
                };
                buttons.Children.Add(confirm);
            }

            StackPanel root = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };
            root.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(new SelectableTextBlock { Text = message ?? String.Empty, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(buttons);
            Content = root;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show a message. Modal to <paramref name="owner"/> when it is visible, else a free-standing window.
        /// </summary>
        /// <param name="owner">Owner window, or null.</param>
        /// <param name="title">Title.</param>
        /// <param name="message">Message.</param>
        /// <returns>Task that completes when dismissed.</returns>
        public static async Task ShowMessageAsync(Window? owner, string title, string message)
        {
            HarborDialog dialog = new HarborDialog(title, message, null, "OK");
            await ShowAsync(dialog, owner).ConfigureAwait(true);
        }

        /// <summary>
        /// Ask for confirmation.
        /// </summary>
        /// <param name="owner">Owner window, or null.</param>
        /// <param name="title">Title.</param>
        /// <param name="message">Message.</param>
        /// <param name="confirmText">Confirm button text.</param>
        /// <returns>True when confirmed.</returns>
        public static async Task<bool> ConfirmAsync(Window? owner, string title, string message, string confirmText)
        {
            HarborDialog dialog = new HarborDialog(title, message, confirmText, "Cancel");
            await ShowAsync(dialog, owner).ConfigureAwait(true);
            return dialog._Confirmed;
        }

        #endregion

        #region Private-Methods

        private static async Task ShowAsync(HarborDialog dialog, Window? owner)
        {
            if (owner != null && owner.IsVisible)
            {
                await dialog.ShowDialog(owner).ConfigureAwait(true);
                return;
            }

            // No visible owner (a tray command): show it on its own and bring Harbor forward so it is seen.
            MacActivationPolicy.ShowInDock();
            TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>();
            dialog.Closed += (sender, args) => closed.TrySetResult(true);
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Show();
            dialog.Activate();
            await closed.Task.ConfigureAwait(true);
            HideFromDockWhenNoWindows();
        }

        /// <summary>
        /// On macOS, go back to a menu-bar-only app when no Harbor window is showing.
        /// </summary>
        public static void HideFromDockWhenNoWindows()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (Window window in desktop.Windows)
                {
                    if (window.IsVisible) return;
                }
            }

            MacActivationPolicy.HideFromDock();
        }

        #endregion
    }
}
