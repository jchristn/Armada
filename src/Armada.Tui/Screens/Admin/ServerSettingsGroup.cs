namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Widgets;
    using TUIKit.Widgets;
    using Button = Armada.Tui.Widgets.Button;
    using ButtonRow = Armada.Tui.Widgets.ButtonRow;

    /// <summary>
    /// One saveable group of the Server settings page (Server Configuration, Agent Settings, Vessel Import, ...): its
    /// fields, a clean snapshot for dirty tracking, its Save and Discard buttons, and whether saving needs changes or
    /// valid fields first (the dashboard's per-section rules). Not thread-safe.
    /// </summary>
    public class ServerSettingsGroup
    {
        #region Public-Members

        /// <summary>
        /// Group key (stable, for tests and commands).
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Fields that hold values (dirty tracking and validation).
        /// </summary>
        public List<IFormField> Fields { get; } = new List<IFormField>();

        /// <summary>
        /// Every widget of the group (fields and button rows), so Ctrl+S on any of them saves the group.
        /// </summary>
        public HashSet<IWidget> Members { get; } = new HashSet<IWidget>();

        /// <summary>
        /// Button row.
        /// </summary>
        public ButtonRow Buttons { get; } = new ButtonRow();

        /// <summary>
        /// Save button.
        /// </summary>
        public Button SaveButton { get; }

        /// <summary>
        /// Discard button, or null when the group has none.
        /// </summary>
        public Button? DiscardButton { get; }

        /// <summary>
        /// Save only when dirty (Vessel Import, Fleet Actions, Data Retention).
        /// </summary>
        public bool RequireDirty { get; set; } = false;

        /// <summary>
        /// Extra validation across fields (English messages already translated), or null.
        /// </summary>
        public Func<List<string>>? CrossErrors { get; set; } = null;

        /// <summary>
        /// True while a save is running.
        /// </summary>
        public bool Saving { get; set; } = false;

        /// <summary>
        /// True when the group may be edited (not locked by proxy mode or permissions).
        /// </summary>
        public bool Editable { get; set; } = true;

        /// <summary>
        /// True when a field differs from the clean snapshot.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                List<object?> now = Snapshot();
                if (now.Count != _Clean.Count) return true;
                for (int i = 0; i < now.Count; i++)
                {
                    if (!Equals(now[i], _Clean[i])) return true;
                }

                return false;
            }
        }

        #endregion

        #region Private-Members

        private List<object?> _Clean = new List<object?>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="saveLabel">English Save label.</param>
        /// <param name="onSave">Save action.</param>
        /// <param name="onDiscard">Discard action, or null for no Discard button.</param>
        public ServerSettingsGroup(string key, string saveLabel, Action onSave, Action? onDiscard = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            SaveButton = Buttons.Add(new Button(saveLabel, onSave));
            if (onDiscard != null)
            {
                DiscardButton = Buttons.Add(new Button("Discard changes", onDiscard));
                DiscardButton.Visible = false;
            }

            Members.Add(Buttons);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Track a field.
        /// </summary>
        /// <typeparam name="T">Field type.</typeparam>
        /// <param name="field">Field.</param>
        /// <returns>The field.</returns>
        public T Track<T>(T field) where T : IWidget
        {
            if (field is IFormField f) Fields.Add(f);
            Members.Add(field);
            return field;
        }

        /// <summary>
        /// Record the current values as clean.
        /// </summary>
        public void MarkClean()
        {
            _Clean = Snapshot();
        }

        /// <summary>
        /// Validate every field (shows inline errors).
        /// </summary>
        /// <returns>True when all fields and cross-field rules pass.</returns>
        public bool Validate()
        {
            bool ok = true;
            foreach (IFormField f in Fields)
            {
                if (!f.ValidateField()) ok = false;
            }

            if (CrossErrors != null && CrossErrors().Count > 0) ok = false;
            return ok;
        }

        /// <summary>
        /// True when the fields are valid without changing what is shown.
        /// </summary>
        /// <returns>True when valid.</returns>
        public bool IsValid()
        {
            return Fields.All(f => f.FieldError == null) && (CrossErrors == null || CrossErrors().Count == 0);
        }

        /// <summary>
        /// True when Save can run now.
        /// </summary>
        /// <returns>True when enabled.</returns>
        public bool CanSave()
        {
            if (!Editable || Saving) return false;
            if (RequireDirty && !IsDirty) return false;
            return IsValid();
        }

        /// <summary>
        /// Refresh button states (call each frame).
        /// </summary>
        public void UpdateButtons()
        {
            SaveButton.Enabled = CanSave();
            if (DiscardButton != null) DiscardButton.Visible = Editable && IsDirty;
        }

        #endregion

        #region Private-Methods

        private List<object?> Snapshot()
        {
            return Fields.Select(f => f.FieldValue).ToList();
        }

        #endregion
    }
}
