namespace Armada.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Avalonia.Controls;
    using Avalonia.Interactivity;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Dialog for editing captain properties including name, runtime, and parallelism.
    /// </summary>
    public partial class EditCaptainDialog : Window
    {
        #region Private-Members

        private bool _Saved;
        private Captain _Captain;

        #endregion

        #region Public-Members

        /// <summary>
        /// The edited captain name.
        /// </summary>
        public string EditedName => this.FindControl<TextBox>("NameInput")?.Text ?? "";

        /// <summary>
        /// The edited runtime.
        /// </summary>
        public AgentRuntimeEnum EditedRuntime
        {
            get
            {
                ComboBox? combo = this.FindControl<ComboBox>("RuntimeInput");
                if (combo?.SelectedItem is AgentRuntimeEnum rt) return rt;
                return AgentRuntimeEnum.ClaudeCode;
            }
        }

        /// <summary>
        /// The edited parallelism value.
        /// </summary>
        public int EditedParallelism
        {
            get
            {
                NumericUpDown? input = this.FindControl<NumericUpDown>("ParallelismInput");
                return (int)(input?.Value ?? 1);
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EditCaptainDialog()
        {
            InitializeComponent();
            _Captain = new Captain();
        }

        /// <summary>
        /// Instantiate with captain data.
        /// </summary>
        /// <param name="captain">Captain to edit.</param>
        public EditCaptainDialog(Captain captain) : this()
        {
            _Captain = captain ?? throw new ArgumentNullException(nameof(captain));

            TextBox? nameInput = this.FindControl<TextBox>("NameInput");
            ComboBox? runtimeInput = this.FindControl<ComboBox>("RuntimeInput");
            NumericUpDown? parallelismInput = this.FindControl<NumericUpDown>("ParallelismInput");

            if (nameInput != null) nameInput.Text = captain.Name;

            if (runtimeInput != null)
            {
                List<AgentRuntimeEnum> runtimes = new List<AgentRuntimeEnum>
                {
                    AgentRuntimeEnum.ClaudeCode,
                    AgentRuntimeEnum.Codex,
                    AgentRuntimeEnum.Custom
                };
                runtimeInput.ItemsSource = runtimes;
                runtimeInput.SelectedItem = captain.Runtime;
            }

            if (parallelismInput != null) parallelismInput.Value = captain.MaxParallelism;
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
