namespace Armada.Tui.Widgets
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Date (and optional time) field for range filters. Accepts <c>yyyy-MM-dd</c>, <c>yyyy-MM-dd HH:mm</c>,
    /// <c>today</c>, <c>now</c>, and relative offsets such as <c>-7d</c>, <c>-12h</c>, or <c>+30m</c>; Up/Down adjust by
    /// a day (Shift by an hour). Empty means no value. Values are local time; <see cref="ValueUtc"/> converts. Not
    /// thread-safe.
    /// </summary>
    public class DateField : TextInput, IFormField
    {
        #region Public-Members

        /// <summary>
        /// Parsed local value, or null when empty or invalid.
        /// </summary>
        public DateTime? ValueLocal
        {
            get { return Parse(Value, _Now()); }
        }

        /// <summary>
        /// Parsed value in UTC, or null.
        /// </summary>
        public DateTime? ValueUtc
        {
            get
            {
                DateTime? local = ValueLocal;
                return local.HasValue ? DateTime.SpecifyKind(local.Value, DateTimeKind.Local).ToUniversalTime() : (DateTime?)null;
            }
        }

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return Value; }
        }

        /// <inheritdoc />
        public string? FieldError
        {
            get { return Error; }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _Relative = new Regex(@"^([+-])(\d+)([mhdw])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Func<DateTime> _Now;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="now">Local clock (tests pass a fixed time), or null for <see cref="DateTime.Now"/>.</param>
        public DateField(Func<DateTime>? now = null)
        {
            _Now = now ?? (() => DateTime.Now);
            Placeholder = "yyyy-mm-dd";
            Validator = text => String.IsNullOrWhiteSpace(text) || Parse(text, _Now()).HasValue ? null : "Use yyyy-mm-dd, yyyy-mm-dd hh:mm, today, or -7d.";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse date text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="now">Reference time.</param>
        /// <returns>The value, or null.</returns>
        public static DateTime? Parse(string? text, DateTime now)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string t = text!.Trim().ToLowerInvariant();
            if (t == "today") return now.Date;
            if (t == "now") return now;
            if (t == "yesterday") return now.Date.AddDays(-1);
            Match m = _Relative.Match(t);
            if (m.Success)
            {
                int amount = Int32.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * (m.Groups[1].Value == "-" ? -1 : 1);
                switch (m.Groups[3].Value)
                {
                    case "m": return now.AddMinutes(amount);
                    case "h": return now.AddHours(amount);
                    case "w": return now.AddDays(7 * amount);
                    default: return now.AddDays(amount);
                }
            }

            string[] formats = { "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" };
            if (DateTime.TryParseExact(text!.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)) return parsed;
            return null;
        }

        /// <summary>
        /// Set the value from a date.
        /// </summary>
        /// <param name="local">Local date, or null to clear.</param>
        public void SetDate(DateTime? local)
        {
            Value = local.HasValue ? local.Value.ToString(local.Value.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
        }

        /// <inheritdoc />
        public bool ValidateField()
        {
            return Validate();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Up || key.Code == KeyCode.Down)
            {
                DateTime basis = ValueLocal ?? _Now().Date;
                bool hour = (key.Modifiers & KeyModifiers.Shift) != 0;
                int dir = key.Code == KeyCode.Up ? 1 : -1;
                SetDate(hour ? basis.AddHours(dir) : basis.AddDays(dir));
                return true;
            }

            return base.HandleKey(key);
        }

        #endregion
    }
}
