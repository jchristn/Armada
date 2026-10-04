namespace Armada.Tui.Screens.Entities
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Builds a <see cref="FormView"/> whose fields are wired to the TUI context: select pickers get the modal host,
    /// long text fields get <c>$EDITOR</c> through <see cref="Services.ExternalService"/> and the UI dispatcher, and
    /// required and numeric fields get validators with the dashboard's wording. Not thread-safe.
    /// </summary>
    public class EntityForm
    {
        #region Public-Members

        /// <summary>
        /// The form.
        /// </summary>
        public FormView View { get; } = new FormView();

        /// <summary>
        /// Context.
        /// </summary>
        public TuiContext Context { get; }

        /// <summary>
        /// When true, every field added afterwards is read-only (viewers without edit rights).
        /// </summary>
        public bool ReadOnly { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public EntityForm(TuiContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            View.Localizer = context.Loc;
            View.ApplyTheme(context.Theme.Current);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a section heading.
        /// </summary>
        /// <param name="title">English title.</param>
        public void Section(string title)
        {
            View.AddSection(title);
        }

        /// <summary>
        /// Add a one-line text field.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Initial value.</param>
        /// <param name="placeholder">Placeholder (shown as typed, for example "mis_...").</param>
        /// <param name="required">Required.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <param name="validator">Extra validator returning an English error or null.</param>
        /// <returns>The field.</returns>
        public InputField Text(string label, string? value, string placeholder = "", bool required = false, string? hint = null, Func<string, string?>? validator = null)
        {
            InputField field = new InputField();
            field.Value = value ?? "";
            field.Placeholder = placeholder ?? "";
            field.Validator = v =>
            {
                if (required && String.IsNullOrWhiteSpace(v)) return "This field is required.";
                return validator != null ? validator(v) : null;
            };
            return View.AddField(label, field, hint);
        }

        /// <summary>
        /// Add a whole-number field.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Initial value, or null for blank.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="max">Maximum.</param>
        /// <param name="required">Required.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <returns>The field.</returns>
        public InputField Number(string label, int? value, int min = 0, int max = Int32.MaxValue, bool required = false, string? hint = null)
        {
            InputField field = new InputField();
            field.Value = value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
            field.Validator = v =>
            {
                if (String.IsNullOrWhiteSpace(v)) return required ? "This field is required." : null;
                if (!Int32.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return "Enter a whole number.";
                if (n < min || n > max) return "The number is out of range.";
                return null;
            };
            return View.AddField(label, field, hint);
        }

        /// <summary>
        /// Add a multi-line text field (inline editing and <c>Ctrl+E</c> for <c>$EDITOR</c>).
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Initial value.</param>
        /// <param name="height">Rows.</param>
        /// <param name="required">Required.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <param name="extension">Temporary file extension for the external editor.</param>
        /// <returns>The field.</returns>
        public TextAreaField Area(string label, string? value, int height = 4, bool required = false, string? hint = null, string extension = ".md")
        {
            TextAreaField field = new TextAreaField(value ?? "");
            field.ReadOnly = ReadOnly;
            field.Dispatcher = Context.Dispatcher;
            field.EditorExtension = extension;
            field.ExternalEditor = text => Context.External.EditTextAsync(text, extension);
            if (required) field.Validator = v => String.IsNullOrWhiteSpace(v) ? "This field is required." : null;
            return View.AddField(label, field, hint, Math.Clamp(height, 2, 40));
        }

        /// <summary>
        /// Add a checkbox.
        /// </summary>
        /// <param name="label">English label (drawn beside the box).</param>
        /// <param name="value">Initial value.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <returns>The field.</returns>
        public CheckField Check(string label, bool value, string? hint = null)
        {
            CheckField field = new CheckField(label, value);
            field.ReadOnly = ReadOnly;
            return View.AddField("", field, hint);
        }

        /// <summary>
        /// Add a select of string values.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="options">Options.</param>
        /// <param name="value">Initial value.</param>
        /// <param name="emptyLabel">English label of a leading empty option (value ""), or null for none.</param>
        /// <param name="required">Required.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <returns>The field.</returns>
        public SelectField<string> Select(string label, IEnumerable<SelectOption<string>> options, string? value, string? emptyLabel = null, bool required = false, string? hint = null)
        {
            SelectField<string> field = new SelectField<string>();
            field.ModalHost = Context.Modals;
            field.PickerTitle = label;
            field.Required = required;
            SetOptions(field, options, emptyLabel);
            field.SetValue(value ?? "");
            if (field.Selected == null && emptyLabel != null) field.SetValue("");
            return View.AddField(label, field, hint);
        }

        /// <summary>
        /// Add a select over an enum's values.
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <param name="label">English label.</param>
        /// <param name="value">Initial value.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <returns>The field (values are enum names).</returns>
        public SelectField<string> Enum<TEnum>(string label, TEnum value, string? hint = null) where TEnum : struct, System.Enum
        {
            return Select(label, EnumOptions<TEnum>(), value.ToString(), null, true, hint);
        }

        /// <summary>
        /// Add a multi-select of string values.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="options">Options.</param>
        /// <param name="values">Initial values.</param>
        /// <param name="placeholder">English placeholder when nothing is selected.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <returns>The field.</returns>
        public MultiSelectField<string> Multi(string label, IEnumerable<SelectOption<string>> options, IEnumerable<string>? values, string placeholder = "None", string? hint = null)
        {
            MultiSelectField<string> field = new MultiSelectField<string>();
            field.ModalHost = Context.Modals;
            field.PickerTitle = label;
            field.Placeholder = placeholder;
            field.Options = options.ToList();
            field.SetValues(values, false);
            return View.AddField(label, field, hint);
        }

        /// <summary>
        /// Add the ownership scope select (admins choose; others get a fixed "Personal").
        /// </summary>
        /// <param name="value">Current scope.</param>
        /// <returns>The field (values are enum names).</returns>
        public SelectField<string> Scope(ScopeEnum value)
        {
            List<SelectOption<string>> options = new List<SelectOption<string>>
            {
                new SelectOption<string>(ScopeEnum.TenantWide.ToString(), Context.Loc.T("Tenant-wide")),
                new SelectOption<string>(ScopeEnum.UserSpecific.ToString(), Context.Loc.T("Personal"))
            };
            bool canChoose = ScopeRules.CanChooseScope(Context.Session);
            if (!canChoose)
            {
                options[0].Enabled = false;
                value = ScopeEnum.UserSpecific;
            }

            return Select("Scope", options, value.ToString(), null, true, canChoose ? null : "Only administrators can share items tenant-wide.");
        }

        /// <summary>
        /// Add an editable record list.
        /// </summary>
        /// <typeparam name="T">Record type.</typeparam>
        /// <param name="label">English label.</param>
        /// <param name="describe">One-line summary.</param>
        /// <param name="items">Initial records.</param>
        /// <param name="height">Rows.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <returns>The field.</returns>
        public RecordListField<T> List<T>(string label, Func<T, string> describe, IEnumerable<T>? items, int height = 5, string? hint = null) where T : class
        {
            RecordListField<T> field = new RecordListField<T>(describe);
            field.ReadOnly = ReadOnly;
            field.SetItems(items);
            return View.AddField(label, field, hint, Math.Clamp(height, 2, 30));
        }

        /// <summary>
        /// Record the current values as clean.
        /// </summary>
        public void MarkClean()
        {
            View.MarkClean();
        }

        /// <summary>
        /// Replace a select's options (keeps the value when still offered).
        /// </summary>
        /// <param name="field">Field.</param>
        /// <param name="options">Options.</param>
        /// <param name="emptyLabel">English label of a leading empty option, or null.</param>
        public void SetOptions(SelectField<string> field, IEnumerable<SelectOption<string>> options, string? emptyLabel = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            string? current = field.Value;
            List<SelectOption<string>> list = new List<SelectOption<string>>();
            if (emptyLabel != null)
            {
                SelectOption<string> empty = new SelectOption<string>("", Context.Loc.T(emptyLabel));
                empty.Pinned = true;
                list.Add(empty);
            }

            if (options != null) list.AddRange(options);
            field.Options = list;
            if (current != null) field.SetValue(current);
        }

        /// <summary>
        /// Options for every value of an enum (label = name).
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> EnumOptions<TEnum>() where TEnum : struct, System.Enum
        {
            return System.Enum.GetNames(typeof(TEnum)).Select(n => new SelectOption<string>(n, n)).ToList();
        }

        /// <summary>
        /// Parse a whole number field (null when blank or invalid).
        /// </summary>
        /// <param name="field">Field.</param>
        /// <returns>Value or null.</returns>
        public static int? IntValue(InputField field)
        {
            if (field == null || String.IsNullOrWhiteSpace(field.Value)) return null;
            return Int32.TryParse(field.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;
        }

        /// <summary>
        /// Parse an enum select (default when blank).
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <param name="field">Field.</param>
        /// <param name="fallback">Fallback.</param>
        /// <returns>Value.</returns>
        public static TEnum EnumValue<TEnum>(SelectField<string> field, TEnum fallback) where TEnum : struct, System.Enum
        {
            string? v = field?.Value;
            return !String.IsNullOrEmpty(v) && System.Enum.TryParse<TEnum>(v, true, out TEnum parsed) ? parsed : fallback;
        }

        #endregion
    }
}
