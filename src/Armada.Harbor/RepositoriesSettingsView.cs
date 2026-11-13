namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Avalonia;
    using Avalonia.Automation;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Platform.Storage;

    /// <summary>
    /// The Repositories tab: where this machine keeps each vessel's checkout (named per vessel, or found under root folders
    /// by matching the vessel's repository URL), where mission docks are created, and where the Harbor keeps its own
    /// clones. Saving validates (<see cref="HarborAppSettings.Validate"/>) and applies the values without reconnecting the
    /// link; the Harbor reads them on every dock request.
    /// </summary>
    public class RepositoriesSettingsView : UserControl
    {
        #region Private-Members

        private readonly HarborSession _Session;
        private readonly TextBlock _Message = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        private readonly TextBox _DocksDirectory = new TextBox { PlaceholderText = HarborAppSettings.DefaultDocksDirectory() };
        private readonly TextBox _ReposDirectory = new TextBox { PlaceholderText = HarborAppSettings.DefaultReposDirectory() };
        private readonly StackPanel _MappingRows = new StackPanel { Spacing = 6 };
        private readonly StackPanel _RootRows = new StackPanel { Spacing = 6 };
        private readonly List<MappingRow> _Mappings = new List<MappingRow>();
        private readonly List<TextBox> _Roots = new List<TextBox>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public RepositoriesSettingsView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));
            StackPanel checkouts = new StackPanel { Spacing = 8 };
            checkouts.Children.Add(HarborUi.Help("A vessel (by name or ID) and the folder of its checkout on this computer."));
            checkouts.Children.Add(_MappingRows);
            checkouts.Children.Add(HarborUi.Button("Add Vessel Checkout", () => AddMapping(String.Empty, String.Empty), "Name a vessel (by name or ID) and the folder of its checkout on this machine"));

            StackPanel roots = new StackPanel { Spacing = 8 };
            roots.Children.Add(HarborUi.Help("Searched up to three folders deep for checkouts of vessels not named above."));
            roots.Children.Add(_RootRows);
            roots.Children.Add(HarborUi.Button("Add Root Folder", () => AddRoot(String.Empty), "Add a folder that holds your code, such as ~/Code. Harbor searches it up to three levels deep for checkouts of vessels."));

            StackPanel folders = new StackPanel { Spacing = 6 };
            folders.Children.Add(Label("Docks folder"));
            ToolTip.SetTip(_DocksDirectory, "Folder where Harbor creates a git worktree (dock) for each mission, as <vessel>/<mission>. Leave empty for the default shown. Keep it outside your code folders so tools do not index the docks.");
            ToolTip.SetTip(_ReposDirectory, "Folder where Harbor keeps its own bare clone of a vessel it has no checkout of. Leave empty for the default shown.");
            AutomationProperties.SetName(_DocksDirectory, "Docks folder");
            AutomationProperties.SetName(_ReposDirectory, "Clones folder");
            folders.Children.Add(PathRow(_DocksDirectory));
            folders.Children.Add(HarborUi.Help("Mission docks are created here as <vessel>/<mission>. Keep it outside your code folders."));
            folders.Children.Add(Label("Clones folder"));
            folders.Children.Add(PathRow(_ReposDirectory));
            folders.Children.Add(HarborUi.Help("Where this Harbor clones a vessel it has no checkout of."));

            StackPanel buttons = HarborUi.ButtonRow();
            Button save = HarborUi.Button("Save", Save, "Save these folders to Harbor's settings file. New missions use them right away.");
            save.Classes.Add("accent");
            buttons.Children.Add(save);
            buttons.Children.Add(HarborUi.Button("Revert", () => Load(_Session.Settings), "Discard unsaved changes"));
            StackPanel footer = new StackPanel { Spacing = 8 };
            footer.Children.Add(buttons);
            footer.Children.Add(_Message);

            Content = HarborUi.Page(
                HarborUi.Note("Missions routed to this Harbor get their dock (a git worktree) on this machine. A vessel's dock is made from "
                    + "the checkout named for it below, else from a checkout under a root folder whose remote matches the vessel's repository URL "
                    + "(https and ssh forms both match), else from a clone this Harbor makes. Docks never change your checkout's files, index, or "
                    + "current branch; LocalMerge and MergeAndPush land into the checkout, which must have no uncommitted changes."),
                HarborUi.Card("Vessel checkouts", checkouts, null),
                HarborUi.Card("Root folders", roots, null),
                HarborUi.Card("Folders this Harbor manages", folders, null),
                footer);
            Load(_Session.Settings);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the repository values of a settings instance.
        /// </summary>
        /// <param name="settings">Settings.</param>
        public void Load(HarborAppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _DocksDirectory.Text = settings.DocksDirectory;
            _ReposDirectory.Text = settings.ReposDirectory;

            _Mappings.Clear();
            _MappingRows.Children.Clear();
            foreach (HarborRepositoryMapping mapping in settings.Repositories ?? new List<HarborRepositoryMapping>())
                if (mapping != null) AddMapping(mapping.Vessel, mapping.Path);

            _Roots.Clear();
            _RootRows.Children.Clear();
            foreach (string folder in settings.RepositoryRoots ?? new List<string>())
                AddRoot(folder);
            SetMessage(null, false);
        }

        /// <summary>
        /// Copy the edited repository values into a settings instance. Rows left completely empty are dropped.
        /// </summary>
        /// <param name="settings">Settings to update.</param>
        public void Apply(HarborAppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.DocksDirectory = (_DocksDirectory.Text ?? String.Empty).Trim();
            settings.ReposDirectory = (_ReposDirectory.Text ?? String.Empty).Trim();

            settings.Repositories = new List<HarborRepositoryMapping>();
            foreach (MappingRow row in _Mappings)
            {
                string vessel = (row.Vessel.Text ?? String.Empty).Trim();
                string path = (row.Path.Text ?? String.Empty).Trim();
                if (vessel.Length == 0 && path.Length == 0) continue;
                settings.Repositories.Add(new HarborRepositoryMapping(vessel, path));
            }

            settings.RepositoryRoots = new List<string>();
            foreach (TextBox box in _Roots)
            {
                string folder = (box.Text ?? String.Empty).Trim();
                if (folder.Length > 0) settings.RepositoryRoots.Add(folder);
            }
        }

        #endregion

        #region Private-Methods

        private void Save()
        {
            HarborAppSettings edited = _Session.Settings.Clone();
            Apply(edited);
            if (!_Session.ApplySettings(edited, out List<string> errors))
            {
                SetMessage(String.Join("\n", errors), true);
                return;
            }

            Load(_Session.Settings);
            SetMessage("Saved. New missions use these repositories; the link stays connected.", false);
        }

        private void SetMessage(string? text, bool isError)
        {
            HarborUi.SetMessage(_Message, text, isError);
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
        }

        private void AddMapping(string vessel, string path)
        {
            TextBox vesselBox = new TextBox { Text = vessel, PlaceholderText = "Vessel name or ID" };
            AutomationProperties.SetName(vesselBox, "Vessel name or ID");
            ToolTip.SetTip(vesselBox, "The vessel, by its name or its vsl_ ID, whose checkout is the folder on the right");
            TextBox pathBox = new TextBox { Text = path, PlaceholderText = "/path/to/checkout" };
            AutomationProperties.SetName(pathBox, "Checkout folder");
            ToolTip.SetTip(pathBox, "The folder of your own git checkout of that vessel on this computer. Harbor runs checks and local merges there.");
            MappingRow row = new MappingRow(vesselBox, pathBox);

            Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*,Auto,Auto"), ColumnSpacing = 6 };
            grid.Children.Add(vesselBox);
            Grid.SetColumn(pathBox, 1);
            grid.Children.Add(pathBox);
            Button browse = HarborUi.Button("Browse...", () => _ = BrowseAsync(pathBox), "Pick the checkout folder from a folder chooser");
            Grid.SetColumn(browse, 2);
            grid.Children.Add(browse);
            Button remove = HarborUi.Button("Remove", () =>
            {
                _Mappings.Remove(row);
                _MappingRows.Children.Remove(grid);
            }, "Remove this vessel checkout from the list (the folder itself is not touched). Save to keep the change.");
            Grid.SetColumn(remove, 3);
            grid.Children.Add(remove);

            _Mappings.Add(row);
            _MappingRows.Children.Add(grid);
        }

        private void AddRoot(string folder)
        {
            TextBox box = new TextBox { Text = folder, PlaceholderText = "/path/to/your/code" };
            AutomationProperties.SetName(box, "Root folder");
            ToolTip.SetTip(box, "A folder that holds your code. Harbor looks up to three levels deep for checkouts of vessels not listed above.");
            Grid grid = PathRow(box);
            Button remove = HarborUi.Button("Remove", () =>
            {
                _Roots.Remove(box);
                _RootRows.Children.Remove(grid);
            }, "Stop searching this folder for checkouts (the folder itself is not touched). Save to keep the change.");
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);

            _Roots.Add(box);
            _RootRows.Children.Add(grid);
        }

        private Grid PathRow(TextBox box)
        {
            Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            grid.Children.Add(box);
            Button browse = HarborUi.Button("Browse...", () => _ = BrowseAsync(box), "Pick the folder from a folder chooser");
            Grid.SetColumn(browse, 1);
            grid.Children.Add(browse);
            return grid;
        }

        private async Task BrowseAsync(TextBox target)
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            try
            {
                IReadOnlyList<IStorageFolder> folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = "Choose a folder" });
                if (folders.Count == 0) return;
                string? local = folders[0].TryGetLocalPath();
                if (!String.IsNullOrEmpty(local)) target.Text = local;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException || ex is PlatformNotSupportedException)
            {
                // No folder picker on this platform: the path can still be typed.
            }
        }

        #endregion

        #region Private-Types

        private sealed class MappingRow
        {
            public TextBox Vessel { get; }

            public TextBox Path { get; }

            public MappingRow(TextBox vessel, TextBox path)
            {
                Vessel = vessel;
                Path = path;
            }
        }

        #endregion
    }
}
