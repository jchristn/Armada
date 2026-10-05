namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The dashboard's filter row for list screens: a search box and select/tri-state filters laid out left to right,
    /// wrapping to more lines when the terminal is narrow. Every filter has a key so screens can read, set, and deep
    /// link them (<c>?status=Failed</c>). <c>Tab</c> moves between filters, <c>Enter</c> or <c>Esc</c> hands focus back
    /// (<see cref="Escaped"/>), and every change raises <see cref="Changed"/>. Select filters use an empty string for
    /// "all". Not thread-safe.
    /// </summary>
    public class FilterBar : ContainerWidget, IKeyHintSource
    {
        #region Public-Members

        /// <summary>
        /// English status bar description of where <c>Esc</c> takes focus. Default "Back to the list".
        /// </summary>
        public string ExitLabel { get; set; } = "Back to the list";

        /// <summary>
        /// Filter keys in display order.
        /// </summary>
        public IReadOnlyList<string> Keys
        {
            get { return _Items.Select(i => i.Key).ToList(); }
        }

        /// <summary>
        /// Raised after any filter value changes.
        /// </summary>
        public event EventHandler<string>? Changed;

        /// <summary>
        /// Raised when the user presses Enter or Esc inside the bar (the screen moves focus back to its list).
        /// </summary>
        public event EventHandler? Escaped;

        #endregion

        #region Private-Members

        private readonly List<FilterBarItem> _Items = new List<FilterBarItem>();
        private readonly IModalHost? _Modals;
        private bool _Suppress = false;
        private readonly Dictionary<string, string> _Pending = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="modals">Modal host for select pickers.</param>
        public FilterBar(IModalHost? modals)
        {
            _Modals = modals;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a search box.
        /// </summary>
        /// <param name="key">Filter key.</param>
        /// <param name="placeholder">English placeholder.</param>
        /// <param name="width">Field width in cells.</param>
        /// <returns>The input.</returns>
        public TextInput AddSearch(string key, string placeholder, int width = 34)
        {
            TextInput input = new TextInput();
            input.Placeholder = placeholder ?? "";
            input.ValueChanged += (s, e) => Raise(key);
            Add(new FilterBarItem(key, "", input, width));
            return input;
        }

        /// <summary>
        /// Add a select filter whose first option is "all" (value <c>""</c>).
        /// </summary>
        /// <param name="key">Filter key.</param>
        /// <param name="allLabel">English label of the "all" option (for example "All statuses").</param>
        /// <param name="options">Options (value, label).</param>
        /// <param name="width">Field width in cells.</param>
        /// <returns>The select field.</returns>
        public SelectField<string> AddSelect(string key, string allLabel, IEnumerable<SelectOption<string>> options, int width = 24)
        {
            SelectField<string> field = new SelectField<string>();
            field.ModalHost = _Modals;
            field.PickerTitle = allLabel;
            field.Placeholder = allLabel;
            field.Options = BuildOptions(allLabel, options);
            field.SetValue("");
            field.ValueChanged += (s, e) =>
            {
                _Pending.Remove(key);
                Raise(key);
            };
            Add(new FilterBarItem(key, "", field, width));
            return field;
        }

        /// <summary>
        /// Add a tri-state (Any / Yes / No) filter.
        /// </summary>
        /// <param name="key">Filter key.</param>
        /// <param name="label">English label.</param>
        /// <param name="width">Field width in cells.</param>
        /// <returns>The field.</returns>
        public TriStateField AddTriState(string key, string label, int width = 10)
        {
            TriStateField field = new TriStateField();
            field.ValueChanged += (s, e) => Raise(key);
            Add(new FilterBarItem(key, label, field, width));
            return field;
        }

        /// <summary>
        /// Replace a select filter's options (keeps the current value when it is still offered).
        /// </summary>
        /// <param name="key">Filter key.</param>
        /// <param name="options">Options without the "all" entry.</param>
        public void SetOptions(string key, IEnumerable<SelectOption<string>> options)
        {
            FilterBarItem? item = Find(key);
            if (item == null || !(item.Field is SelectField<string> select)) return;
            string current = select.Value ?? "";
            string allLabel = select.Placeholder;
            select.Options = BuildOptions(allLabel, options);
            _Suppress = true;
            try
            {
                if (_Pending.TryGetValue(key, out string? pending) && select.Options.Any(o => String.Equals(o.Value, pending, StringComparison.OrdinalIgnoreCase)))
                {
                    current = select.Options.First(o => String.Equals(o.Value, pending, StringComparison.OrdinalIgnoreCase)).Value;
                    _Pending.Remove(key);
                }

                select.SetValue(select.Options.Any(o => o.Value == current) ? current : "");
            }
            finally
            {
                _Suppress = false;
            }
        }

        /// <summary>
        /// Current value of a filter: the text for search, the option value for selects (<c>""</c> for all), and
        /// <c>"true"</c>/<c>"false"</c>/<c>""</c> for tri-state.
        /// </summary>
        /// <param name="key">Filter key.</param>
        /// <returns>Value; empty when unset or unknown.</returns>
        public string Value(string key)
        {
            FilterBarItem? item = Find(key);
            if (item == null) return "";
            if (item.Field is TextInput input) return input.Value;
            if (item.Field is SelectField<string> select)
            {
                string v = select.Value ?? "";
                if (v.Length == 0 && _Pending.TryGetValue(key, out string? pending)) return pending;
                return v;
            }

            if (item.Field is TriStateField tri) return tri.AsBoolean.HasValue ? (tri.AsBoolean.Value ? "true" : "false") : "";
            return "";
        }

        /// <summary>
        /// Set a filter's value without raising <see cref="Changed"/>.
        /// </summary>
        /// <param name="key">Filter key.</param>
        /// <param name="value">Value (see <see cref="Value"/>).</param>
        public void SetValue(string key, string? value)
        {
            FilterBarItem? item = Find(key);
            if (item == null) return;
            string v = value ?? "";
            _Suppress = true;
            try
            {
                if (item.Field is TextInput input) input.Value = v;
                else if (item.Field is SelectField<string> select)
                {
                    SelectOption<string>? match = select.Options.FirstOrDefault(o => String.Equals(o.Value, v, StringComparison.OrdinalIgnoreCase));
                    select.SetValue(match != null ? match.Value : "");
                    if (match == null && v.Length > 0) _Pending[key] = v;
                    else _Pending.Remove(key);
                }
                else if (item.Field is TriStateField tri)
                {
                    tri.SetValue(v == "true" ? TriStateEnum.Yes : v == "false" ? TriStateEnum.No : TriStateEnum.Any, false);
                }
            }
            finally
            {
                _Suppress = false;
            }
        }

        /// <summary>
        /// Every filter's current value.
        /// </summary>
        /// <returns>Key to value.</returns>
        public Dictionary<string, string> Values()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FilterBarItem item in _Items) values[item.Key] = Value(item.Key);
            return values;
        }

        /// <summary>
        /// Reset every filter to empty without raising per-field events; raises <see cref="Changed"/> once.
        /// </summary>
        public void Clear()
        {
            foreach (FilterBarItem item in _Items) SetValue(item.Key, "");
            Raise("");
        }

        /// <summary>
        /// The field widget for a key.
        /// </summary>
        /// <param name="key">Filter key.</param>
        /// <returns>Widget or null.</returns>
        public IWidget? Field(string key)
        {
            return Find(key)?.Field;
        }

        /// <summary>
        /// Focus the first filter.
        /// </summary>
        /// <returns>True when focused.</returns>
        public bool FocusFirst()
        {
            return Scope.FocusFirst();
        }

        /// <summary>
        /// Lines needed at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Line count (0 without filters).</returns>
        public int LinesFor(int width)
        {
            if (_Items.Count == 0) return 0;
            List<Rect> rects = Layout(width);
            return rects.Count == 0 ? 0 : rects.Max(r => r.Y) + 1;
        }

        /// <inheritdoc />
        public IReadOnlyList<KeyHint>? GetKeyHints()
        {
            // A control below that describes its own keys speaks for itself.
            if (KeyHints.Deeper(this) != null) return null;
            return FilterRowHints.For(Scope.Focused, ExitLabel);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape || key.Code == KeyCode.Enter && !(Scope.Focused is SelectField<string>) && !(Scope.Focused is TriStateField))
            {
                EventHandler? handler = Escaped;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                    return true;
                }
            }

            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, Math.Min(available.Height, LinesFor(available.Width)));
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            List<Rect> rects = Layout(width);
            for (int i = 0; i < _Items.Count && i < rects.Count; i++)
            {
                FilterBarItem item = _Items[i];
                Rect r = rects[i];
                if (r.Y >= height) continue;
                int x = r.X;
                if (item.Label.Length > 0)
                {
                    string label = T(item.Label) + ":";
                    x += SurfaceText.Draw(surface, x, r.Y, label, Theme.Muted, r.Width) + 1;
                }

                int fieldWidth = Math.Max(1, r.X + r.Width - x);
                Scope.RenderChild(surface, item.Field, new Rect(x, r.Y, fieldWidth, 1));
            }
        }

        #endregion

        #region Private-Methods

        private void Add(FilterBarItem item)
        {
            _Items.Add(item);
            AddChild(item.Field);
        }

        private FilterBarItem? Find(string key)
        {
            return _Items.FirstOrDefault(i => String.Equals(i.Key, key, StringComparison.Ordinal));
        }

        private void Raise(string key)
        {
            if (_Suppress) return;
            EventHandler<string>? handler = Changed;
            if (handler != null) handler(this, key);
        }

        private List<Rect> Layout(int width)
        {
            List<Rect> rects = new List<Rect>();
            int x = 0;
            int y = 0;
            foreach (FilterBarItem item in _Items)
            {
                int labelWidth = item.Label.Length > 0 ? TextCells.Width(T(item.Label)) + 2 : 0;
                int w = Math.Min(Math.Max(4, width), labelWidth + item.Width);
                if (x > 0 && x + w > width)
                {
                    x = 0;
                    y++;
                }

                rects.Add(new Rect(x, y, w, 1));
                x += w + 2;
            }

            return rects;
        }

        private List<SelectOption<string>> BuildOptions(string allLabel, IEnumerable<SelectOption<string>> options)
        {
            List<SelectOption<string>> list = new List<SelectOption<string>>();
            SelectOption<string> all = new SelectOption<string>("", T(allLabel));
            all.Pinned = true;
            list.Add(all);
            if (options != null) list.AddRange(options);
            return list;
        }

        #endregion
    }
}
