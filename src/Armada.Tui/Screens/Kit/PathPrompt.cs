namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.IO;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Path prompts for saving and loading files through <see cref="ExternalService"/> (exports, backups, restores):
    /// a one-field <see cref="FormModal"/> that expands <c>~</c>, checks that a file to load exists, and confirms
    /// before overwriting a file on save.
    /// </summary>
    public static class PathPrompt
    {
        #region Public-Methods

        /// <summary>
        /// Default directory for saved files: the current directory when writable, else the home directory.
        /// </summary>
        /// <returns>Directory.</returns>
        public static string DefaultDirectory()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("ARMADA_TUI_SAVE_DIR");
            if (!String.IsNullOrWhiteSpace(overrideDir)) return overrideDir!;
            return Directory.GetCurrentDirectory();
        }

        /// <summary>
        /// Ask for a path to save to; calls <paramref name="onPath"/> with the expanded path on the UI loop.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="title">English title.</param>
        /// <param name="defaultFileName">Suggested file name (placed in <see cref="DefaultDirectory"/>).</param>
        /// <param name="onPath">Receives the full path.</param>
        /// <returns>The modal.</returns>
        public static FormModal AskSave(TuiContext context, string title, string defaultFileName, Action<string> onPath)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (onPath == null) throw new ArgumentNullException(nameof(onPath));
            FormView form = new FormView();
            InputField path = form.AddField("File path", new InputField(), "Where to save the file. ~ expands to your home directory.");
            path.Value = Path.Combine(DefaultDirectory(), defaultFileName ?? "armada-export.txt");
            path.Validator = v => String.IsNullOrWhiteSpace(v) ? "A path is required." : null;
            FormModal modal = new FormModal(title, form, context, "Save");
            string? resolved = null;
            modal.Submit = () =>
            {
                try
                {
                    resolved = ExternalService.ExpandPath(path.Value);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    return "The path is not valid.";
                }

                if (Directory.Exists(resolved)) return "The path is a directory. Add a file name.";
                return null;
            };
            context.Modals.Show(modal, result =>
            {
                if (!(result is bool ok) || !ok || resolved == null) return;
                string target = resolved;
                if (File.Exists(target))
                {
                    context.Confirm("Overwrite File", context.Loc.T("{{path}} exists. Overwrite it?", LocalizationArgs.Of("path", target)), () => onPath(target), "Overwrite");
                    return;
                }

                onPath(target);
            });
            return modal;
        }

        /// <summary>
        /// Ask for an existing file to load; calls <paramref name="onPath"/> with the expanded path on the UI loop.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="title">English title.</param>
        /// <param name="hint">English hint under the field.</param>
        /// <param name="onPath">Receives the full path.</param>
        /// <returns>The modal.</returns>
        public static FormModal AskOpen(TuiContext context, string title, string hint, Action<string> onPath)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (onPath == null) throw new ArgumentNullException(nameof(onPath));
            FormView form = new FormView();
            InputField path = form.AddField("File path", new InputField(), hint);
            path.Validator = v => String.IsNullOrWhiteSpace(v) ? "A path is required." : null;
            FormModal modal = new FormModal(title, form, context, "Open");
            string? resolved = null;
            modal.Submit = () =>
            {
                try
                {
                    resolved = ExternalService.ExpandPath(path.Value);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    return "The path is not valid.";
                }

                if (!File.Exists(resolved)) return "File not found.";
                return null;
            };
            context.Modals.Show(modal, result =>
            {
                if (result is bool ok && ok && resolved != null) onPath(resolved);
            });
            return modal;
        }

        #endregion
    }
}
