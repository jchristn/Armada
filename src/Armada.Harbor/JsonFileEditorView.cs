namespace Armada.Harbor
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Hosting;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Media;

    /// <summary>
    /// Edits a JSON settings file as text: validate, save atomically with a timestamped backup, revert, restore a previous
    /// version, or hand the file to an external editor. Saving refuses invalid text and asks before
    /// overwriting a file that changed on disk since it was loaded.
    /// </summary>
    public class JsonFileEditorView : UserControl
    {
        #region Public-Members

        /// <summary>
        /// Raised after a successful save.
        /// </summary>
        public event EventHandler? Saved;

        /// <summary>
        /// The file being edited.
        /// </summary>
        public string FilePath { get; }

        #endregion

        #region Private-Members

        private readonly Func<string, string?> _Validate;
        private readonly TextBox _Editor;
        private readonly TextBlock _Message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private DateTime _LoadedWriteUtc = DateTime.MinValue;
        private string _LoadedText = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="filePath">File to edit.</param>
        /// <param name="validate">Returns an error message for invalid text, or null when it is valid.</param>
        /// <param name="header">Controls shown above the editor (title, notes, banners), or null.</param>
        public JsonFileEditorView(string filePath, Func<string, string?> validate, Control? header)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            FilePath = filePath;
            _Validate = validate ?? throw new ArgumentNullException(nameof(validate));

            _Editor = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top
            };
            _Editor.Classes.Add("mono");

            StackPanel top = new StackPanel { Spacing = 8 };
            if (header != null) top.Children.Add(header);

            StackPanel buttons = HarborUi.ButtonRow();
            Button save = HarborUi.Button("Save", () => _ = SaveAsync());
            save.Classes.Add("accent");
            buttons.Children.Add(save);
            buttons.Children.Add(HarborUi.Button("Validate", () => ShowValidation(true)));
            buttons.Children.Add(HarborUi.Button("Revert", () => _ = RevertAsync(), "Reload the file, discarding unsaved changes"));
            buttons.Children.Add(HarborUi.Button("Restore Previous Version...", () => _ = RestorePreviousAsync(), "Choose a version this file had before an earlier save and put it back"));
            buttons.Children.Add(HarborUi.Button("Open File", OpenExternally, "Open in a text editor"));
            buttons.Children.Add(HarborUi.Button("Show in Folder", Reveal));

            StackPanel bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            bottom.Children.Add(_Message);
            bottom.Children.Add(new ScrollViewer
            {
                Content = buttons,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            });

            DockPanel root = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            top.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(top);
            root.Children.Add(bottom);
            root.Children.Add(_Editor);
            Content = root;

            AttachedToVisualTree += (sender, args) =>
            {
                if (!IsDirty) Reload();
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the editor holds unsaved changes.
        /// </summary>
        public bool IsDirty
        {
            get { return !String.Equals(_Editor.Text ?? String.Empty, _LoadedText, StringComparison.Ordinal); }
        }

        /// <summary>
        /// Load the file into the editor (an empty editor when it does not exist yet).
        /// </summary>
        public void Reload()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    _LoadedText = File.ReadAllText(FilePath);
                    _LoadedWriteUtc = File.GetLastWriteTimeUtc(FilePath);
                    SetMessage(null, false);
                }
                else
                {
                    _LoadedText = String.Empty;
                    _LoadedWriteUtc = DateTime.MinValue;
                    SetMessage(FilePath + " does not exist yet; saving creates it.", false);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _LoadedText = String.Empty;
                SetMessage("Could not read " + FilePath + ": " + ex.Message, true);
            }

            _Editor.Text = _LoadedText;
        }

        #endregion

        #region Private-Methods

        private bool ShowValidation(bool reportSuccess)
        {
            string? error = _Validate(_Editor.Text ?? String.Empty);
            if (error != null)
            {
                SetMessage("Not valid: " + error, true);
                return false;
            }

            if (reportSuccess) SetMessage("Valid.", false);
            return true;
        }

        private async Task SaveAsync()
        {
            if (!ShowValidation(false)) return;

            Window? owner = TopLevel.GetTopLevel(this) as Window;
            DateTime onDisk = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (!IsDirty && onDisk == _LoadedWriteUtc && onDisk != DateTime.MinValue)
            {
                SetMessage("No changes to save.", false);
                return;
            }

            if (onDisk != _LoadedWriteUtc)
            {
                bool overwrite = await HarborDialog.ConfirmAsync(owner, "File changed on disk",
                    FilePath + " changed since it was loaded (another program saved it). Overwrite it with the editor's text? The current file is kept as a backup either way.",
                    "Overwrite").ConfigureAwait(true);
                if (!overwrite) return;
            }

            string text = _Editor.Text ?? String.Empty;
            try
            {
                string? backup = SettingsFileStore.Save(FilePath, text);
                _LoadedText = text;
                _LoadedWriteUtc = File.GetLastWriteTimeUtc(FilePath);
                SetMessage("Saved" + (backup != null ? ". The previous version is kept; Restore Previous Version puts it back" : "") + ".", false);
                Saved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                SetMessage("Could not save: " + ex.Message, true);
            }
        }

        private async Task RevertAsync()
        {
            if (IsDirty)
            {
                bool discard = await HarborDialog.ConfirmAsync(TopLevel.GetTopLevel(this) as Window, "Discard changes?",
                    "Reload " + Path.GetFileName(FilePath) + " from disk and lose the unsaved edits?", "Discard").ConfigureAwait(true);
                if (!discard) return;
            }

            Reload();
        }

        private async Task RestorePreviousAsync()
        {
            Window? owner = TopLevel.GetTopLevel(this) as Window;
            SettingsBackupEntry? chosen = await RestoreVersionDialog.ChooseAsync(owner, FilePath).ConfigureAwait(true);
            if (chosen == null) return;

            string text;
            try
            {
                text = File.ReadAllText(chosen.Path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                SetMessage("Could not read that version: " + ex.Message, true);
                return;
            }

            string? error = _Validate(text);
            if (error != null)
            {
                SetMessage("That version is not valid, so it was not restored: " + error, true);
                return;
            }

            string when = HarborUi.LocalTime(chosen.TakenUtc);
            bool confirmed = await HarborDialog.ConfirmAsync(owner, "Restore this version?",
                "Replace " + Path.GetFileName(FilePath) + " with the version saved before " + when + "?"
                + (IsDirty ? " Your unsaved edits here are discarded." : "")
                + " The current version is kept, so you can switch back.",
                "Restore").ConfigureAwait(true);
            if (!confirmed) return;

            try
            {
                SettingsFileStore.Restore(FilePath, chosen.Path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                SetMessage("Could not restore: " + ex.Message, true);
                return;
            }

            Reload();
            SetMessage("Restored the version saved before " + when + ".", false);
            Saved?.Invoke(this, EventArgs.Empty);
        }

        private void OpenExternally()
        {
            if (!File.Exists(FilePath))
            {
                SetMessage(FilePath + " does not exist yet.", true);
                return;
            }

            if (!PlatformShell.OpenInTextEditor(FilePath, out string? error)) SetMessage("Could not open the file: " + error, true);
            else SetMessage("Opened in your editor. Click Revert here after saving it there.", false);
        }

        private void Reveal()
        {
            string target = File.Exists(FilePath) ? FilePath : (Path.GetDirectoryName(FilePath) ?? FilePath);
            if (!PlatformShell.Reveal(target, out string? error)) SetMessage("Could not show the file: " + error, true);
        }

        private void SetMessage(string? text, bool isError)
        {
            HarborUi.SetMessage(_Message, text, isError);
        }

        #endregion
    }
}
