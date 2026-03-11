namespace Armada.Desktop.Views
{
    using System;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Armada.Core.Models;

    /// <summary>
    /// Dialog for editing vessel properties including name, repo URL, branch, project context, and style guide.
    /// </summary>
    public partial class EditVesselDialog : Window
    {
        #region Private-Members

        private bool _Saved;

        #endregion

        #region Public-Members

        /// <summary>
        /// The edited vessel name.
        /// </summary>
        public string EditedName => this.FindControl<TextBox>("NameInput")?.Text ?? "";

        /// <summary>
        /// The edited repo URL.
        /// </summary>
        public string EditedRepoUrl => this.FindControl<TextBox>("RepoUrlInput")?.Text ?? "";

        /// <summary>
        /// The edited default branch.
        /// </summary>
        public string EditedBranch => this.FindControl<TextBox>("BranchInput")?.Text ?? "main";

        /// <summary>
        /// The edited project context.
        /// </summary>
        public string? EditedProjectContext
        {
            get
            {
                string? val = this.FindControl<TextBox>("ProjectContextInput")?.Text;
                return string.IsNullOrWhiteSpace(val) ? null : val;
            }
        }

        /// <summary>
        /// The edited style guide.
        /// </summary>
        public string? EditedStyleGuide
        {
            get
            {
                string? val = this.FindControl<TextBox>("StyleGuideInput")?.Text;
                return string.IsNullOrWhiteSpace(val) ? null : val;
            }
        }

        /// <summary>
        /// The edited working directory.
        /// </summary>
        public string? EditedWorkingDirectory
        {
            get
            {
                string? val = this.FindControl<TextBox>("WorkingDirectoryInput")?.Text;
                return string.IsNullOrWhiteSpace(val) ? null : val;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EditVesselDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Instantiate with vessel data.
        /// </summary>
        /// <param name="vessel">Vessel to edit.</param>
        public EditVesselDialog(Vessel vessel) : this()
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            TextBox? nameInput = this.FindControl<TextBox>("NameInput");
            TextBox? repoUrlInput = this.FindControl<TextBox>("RepoUrlInput");
            TextBox? branchInput = this.FindControl<TextBox>("BranchInput");
            TextBox? projectContextInput = this.FindControl<TextBox>("ProjectContextInput");
            TextBox? styleGuideInput = this.FindControl<TextBox>("StyleGuideInput");
            TextBox? workingDirectoryInput = this.FindControl<TextBox>("WorkingDirectoryInput");

            if (nameInput != null) nameInput.Text = vessel.Name;
            if (repoUrlInput != null) repoUrlInput.Text = vessel.RepoUrl ?? "";
            if (branchInput != null) branchInput.Text = vessel.DefaultBranch ?? "main";
            if (projectContextInput != null) projectContextInput.Text = vessel.ProjectContext ?? "";
            if (styleGuideInput != null) styleGuideInput.Text = vessel.StyleGuide ?? "";
            if (workingDirectoryInput != null) workingDirectoryInput.Text = vessel.WorkingDirectory ?? "";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show the dialog and return whether the user saved.
        /// </summary>
        /// <param name="owner">Owner window.</param>
        /// <returns>True if saved.</returns>
        public async Task<bool> ShowEditAsync(Window owner)
        {
            await ShowDialog(owner);
            return _Saved;
        }

        #endregion

        #region Private-Methods

        private void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            _Saved = true;
            Close();
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            _Saved = false;
            Close();
        }

        #endregion
    }
}
