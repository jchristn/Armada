namespace Armada.Harbor
{
    using System;
    using Armada.Core.Hosting;
    using Avalonia;
    using Avalonia.Controls;

    /// <summary>
    /// The TUI tab: edits the terminal UI's preferences file (tui.json in the Armada data directory) as JSON.
    /// </summary>
    public class TuiSettingsView : UserControl
    {
        #region Private-Members

        private readonly HarborSession _Session;
        private string? _EditorPath = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public TuiSettingsView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));
            AttachedToVisualTree += (sender, args) =>
            {
                _Session.Changed += OnSessionChanged;
                Refresh();
            };
            DetachedFromVisualTree += (sender, args) => _Session.Changed -= OnSessionChanged;
        }

        #endregion

        #region Private-Methods

        private void OnSessionChanged(object? sender, EventArgs e)
        {
            Refresh();
        }

        private void Refresh()
        {
            LocalAdmiralInfo? admiral = _Session.Admiral;
            if (admiral == null || !admiral.IsLocal)
            {
                _EditorPath = null;
                Content = new StackPanel
                {
                    Margin = new Thickness(4, 8),
                    Children = { HarborUi.Note(admiral == null ? "Looking for the Armada data directory..." : admiral.Reason) }
                };
                return;
            }

            if (String.Equals(_EditorPath, admiral.TuiPreferencesFile, StringComparison.Ordinal)) return;

            StackPanel header = new StackPanel { Spacing = 6 };
            header.Children.Add(HarborUi.Heading("Terminal UI Preferences"));
            header.Children.Add(HarborUi.Note(admiral.TuiPreferencesFile));
            header.Children.Add(HarborUi.Notice(HarborUi.Note("armada tui reads this file when it starts and saves it when you change a preference there, so close the TUI before editing here, and restart it to see the changes. ARMADA_TUI_PREFERENCES can point the TUI at a different file.")));

            _EditorPath = admiral.TuiPreferencesFile;
            Content = new JsonFileEditorView(admiral.TuiPreferencesFile, SettingsTextValidator.ValidateJsonObject, header);
        }

        #endregion
    }
}
