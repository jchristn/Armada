namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A GitHub-style contribution heatmap: one column per week (Sunday first), one row per weekday, a month label row,
    /// weekday labels, and a footer with the totals, the selected day, and the legend. Commit counts map to five
    /// intensity levels scaled by the largest day; the color palettes shade one glyph through a green ramp, and the
    /// high-contrast (no color) palette uses a different glyph per level instead, so the levels read without color.
    /// TUIKit's <c>HeatMap</c> has no selection cursor, per-level colors, or month labels, hence this widget. Focusable:
    /// Up/Down move the selected day by one, Left/Right by a week, Home/End to the first and last day, Enter raises
    /// <see cref="DayActivated"/>, and a click selects a day (a double click activates it). Shows as many recent weeks as
    /// fit, scrolling to keep the selected day in view. Dates are calendar days (no time zone). Not thread-safe.
    /// </summary>
    public class CommitHeatmap : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Rows the widget wants: the month labels, seven weekdays, and the footer.
        /// </summary>
        public const int PreferredHeight = 9;

        /// <summary>
        /// Columns of the weekday label gutter.
        /// </summary>
        public const int LabelWidth = 4;

        /// <summary>
        /// Columns per week (the cell and a gap).
        /// </summary>
        public const int WeekWidth = 2;

        /// <summary>
        /// First day of the range (inclusive).
        /// </summary>
        public DateTime From { get; private set; } = DateTime.MinValue.Date;

        /// <summary>
        /// Last day of the range (inclusive).
        /// </summary>
        public DateTime To { get; private set; } = DateTime.MinValue.Date;

        /// <summary>
        /// Largest single-day count (scales the levels).
        /// </summary>
        public int MaxCount { get; private set; } = 0;

        /// <summary>
        /// The selected day, always within <see cref="From"/>..<see cref="To"/> once a range is set.
        /// </summary>
        public DateTime Selected { get; private set; } = DateTime.MinValue.Date;

        /// <summary>
        /// Text at the start of the footer (already translated): the totals, or a loading or error message.
        /// </summary>
        public string Summary { get; set; } = "";

        /// <summary>
        /// Style of <see cref="Summary"/>, or null for the accent.
        /// </summary>
        public CellStyle? SummaryStyle { get; set; } = null;

        /// <summary>
        /// Index (from the left of the whole range) of the first week shown at the last render.
        /// </summary>
        public int FirstVisibleWeek
        {
            get { return _FirstWeek; }
        }

        /// <summary>
        /// Number of weeks shown at the last render.
        /// </summary>
        public int VisibleWeeks
        {
            get { return _VisibleWeeks; }
        }

        /// <summary>
        /// Raised when the selected day changes.
        /// </summary>
        public event EventHandler<DateTime>? SelectionChanged;

        /// <summary>
        /// Raised when Enter (or a double click) activates the selected day.
        /// </summary>
        public event EventHandler<DateTime>? DayActivated;

        #endregion

        #region Private-Members

        private static readonly string[] _UnicodeColorGlyphs = new string[] { "\u00B7", "\u25A0", "\u25A0", "\u25A0", "\u25A0" };
        private static readonly string[] _UnicodeShadeGlyphs = new string[] { "\u00B7", "\u2591", "\u2592", "\u2593", "\u2588" };
        private static readonly string[] _AsciiColorGlyphs = new string[] { ".", "#", "#", "#", "#" };
        private static readonly string[] _AsciiShadeGlyphs = new string[] { ".", "-", "+", "*", "#" };
        private static readonly int[] _DarkRamp = new int[] { 0x0E4429, 0x006D32, 0x26A641, 0x39D353 };
        private static readonly int[] _LightRamp = new int[] { 0x9BE9A8, 0x40C463, 0x30A14E, 0x216E39 };

        private Dictionary<DateTime, int> _Counts = new Dictionary<DateTime, int>();
        private bool _HasRange = false;
        private int _FirstWeek = 0;
        private int _VisibleWeeks = 0;
        private int _GridY = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CommitHeatmap()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Intensity level (0 for none, 1 to 4) of a day's count scaled by the largest day.
        /// </summary>
        /// <param name="count">Commits on the day.</param>
        /// <param name="max">Largest day in the range.</param>
        /// <returns>Level 0 to 4.</returns>
        public static int Level(int count, int max)
        {
            if (count <= 0 || max <= 0) return 0;
            int level = (int)Math.Ceiling(count * 4.0 / max);
            return Math.Clamp(level, 1, 4);
        }

        /// <summary>
        /// The glyph for a level in a palette: one glyph shaded by color in the color palettes, one glyph per level in
        /// the high-contrast palette, ASCII when the palette uses ASCII glyphs.
        /// </summary>
        /// <param name="level">Level 0 to 4.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>Glyph.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="theme"/> is null.</exception>
        public static string Glyph(int level, ArmadaTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            int i = Math.Clamp(level, 0, 4);
            bool shade = theme.Mode == ThemeModeEnum.HighContrast;
            if (theme.AsciiGlyphs) return shade ? _AsciiShadeGlyphs[i] : _AsciiColorGlyphs[i];
            return shade ? _UnicodeShadeGlyphs[i] : _UnicodeColorGlyphs[i];
        }

        /// <summary>
        /// The style for a level in a palette: muted for no commits, a green ramp in the color palettes, plain text in
        /// the high-contrast palette (whose glyphs carry the level).
        /// </summary>
        /// <param name="level">Level 0 to 4.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>Style.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="theme"/> is null.</exception>
        public static CellStyle LevelStyle(int level, ArmadaTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (level <= 0) return theme.Muted;
            if (theme.Mode == ThemeModeEnum.HighContrast) return theme.Text;
            int[] ramp = theme.Mode == ThemeModeEnum.Light ? _LightRamp : _DarkRamp;
            return theme.Text.WithForeground(Color.FromRgb(ramp[Math.Clamp(level, 1, 4) - 1]));
        }

        /// <summary>
        /// Set the range and the counts. The selection is kept when it is still in range, otherwise moved to the
        /// nearest end.
        /// </summary>
        /// <param name="from">First day.</param>
        /// <param name="to">Last day (not before <paramref name="from"/>).</param>
        /// <param name="counts">Commits per day; days not listed have none. Null for none.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="to"/> is before <paramref name="from"/>.</exception>
        public void SetData(DateTime from, DateTime to, IReadOnlyDictionary<DateTime, int>? counts)
        {
            if (to.Date < from.Date) throw new ArgumentException("The range ends before it starts.", nameof(to));
            From = from.Date;
            To = to.Date;
            _Counts = new Dictionary<DateTime, int>();
            int max = 0;
            if (counts != null)
            {
                foreach (KeyValuePair<DateTime, int> pair in counts)
                {
                    _Counts[pair.Key.Date] = pair.Value;
                    if (pair.Value > max) max = pair.Value;
                }
            }

            MaxCount = max;
            DateTime keep = _HasRange ? Selected : To;
            _HasRange = true;
            Select(keep);
        }

        /// <summary>
        /// Commits on a day (0 when unknown).
        /// </summary>
        /// <param name="day">Day.</param>
        /// <returns>Count.</returns>
        public int CountOn(DateTime day)
        {
            return _Counts.TryGetValue(day.Date, out int n) ? n : 0;
        }

        /// <summary>
        /// Select a day (clamped into the range).
        /// </summary>
        /// <param name="day">Day.</param>
        public void Select(DateTime day)
        {
            if (!_HasRange) return;
            DateTime d = day.Date;
            if (d < From) d = From;
            if (d > To) d = To;
            if (d == Selected) return;
            Selected = d;
            EventHandler<DateTime>? handler = SelectionChanged;
            if (handler != null) handler(this, d);
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, Math.Min(available.Height, PreferredHeight));
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (!_HasRange || key.Modifiers != KeyModifiers.None) return false;
            switch (key.Code)
            {
                case KeyCode.Up:
                    Select(Selected.AddDays(-1));
                    return true;
                case KeyCode.Down:
                    Select(Selected.AddDays(1));
                    return true;
                case KeyCode.Left:
                    Select(Selected.AddDays(-7));
                    return true;
                case KeyCode.Right:
                    Select(Selected.AddDays(7));
                    return true;
                case KeyCode.Home:
                    Select(From);
                    return true;
                case KeyCode.End:
                    Select(To);
                    return true;
                case KeyCode.Enter:
                    Activate();
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (!_HasRange || mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left) return false;
            int row = mouse.Y - _GridY;
            int col = mouse.X - LabelWidth;
            if (row < 0 || row > 6 || col < 0) return false;
            int week = _FirstWeek + col / WeekWidth;
            if (col / WeekWidth >= _VisibleWeeks) return false;
            DateTime day = GridStart().AddDays(week * 7 + row);
            if (day < From || day > To) return false;
            Select(day);
            if (mouse.ClickCount >= 2) Activate();
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < LabelWidth + WeekWidth || height < 1 || !_HasRange)
            {
                if (height >= 1) SurfaceText.Draw(surface, 0, 0, Summary, SummaryStyle ?? Theme.Muted, width);
                return;
            }

            DateTime start = GridStart();
            int totalWeeks = (To - start).Days / 7 + 1;
            int fit = Math.Max(1, (width - LabelWidth) / WeekWidth);
            _VisibleWeeks = Math.Min(totalWeeks, fit);
            int selectedWeek = (Selected - start).Days / 7;
            int maxFirst = totalWeeks - _VisibleWeeks;
            if (_FirstWeek > maxFirst) _FirstWeek = maxFirst;
            if (selectedWeek < _FirstWeek) _FirstWeek = selectedWeek;
            if (selectedWeek >= _FirstWeek + _VisibleWeeks) _FirstWeek = selectedWeek - _VisibleWeeks + 1;
            _FirstWeek = Math.Clamp(_FirstWeek, 0, Math.Max(0, maxFirst));

            // Without room for the month row and the footer, the weekday rows come first.
            bool months = height >= 8;
            bool footer = height >= 9;
            _GridY = months ? 1 : 0;
            if (months) RenderMonths(surface, start, width);

            string[] days = new string[] { "", T("Mon"), "", T("Wed"), "", T("Fri"), "" };
            for (int d = 0; d < 7; d++)
            {
                int y = _GridY + d;
                if (y >= height) break;
                SurfaceText.Draw(surface, 0, y, days[d], Theme.Muted, LabelWidth - 1);
                for (int w = 0; w < _VisibleWeeks; w++)
                {
                    DateTime day = start.AddDays((_FirstWeek + w) * 7 + d);
                    if (day < From || day > To) continue;
                    int level = Level(CountOn(day), MaxCount);
                    CellStyle style = LevelStyle(level, Theme);
                    if (day == Selected) style = IsFocused ? Theme.Selection : Theme.SelectionInactive;
                    SurfaceText.Draw(surface, LabelWidth + w * WeekWidth, y, Glyph(level, Theme), style, 1);
                }
            }

            if (footer) RenderFooter(surface, 8, width);
        }

        #endregion

        #region Private-Methods

        private DateTime GridStart()
        {
            return From.AddDays(-(int)From.DayOfWeek);
        }

        private void Activate()
        {
            if (!_HasRange) return;
            EventHandler<DateTime>? handler = DayActivated;
            if (handler != null) handler(this, Selected);
        }

        private void RenderMonths(ISurface surface, DateTime start, int width)
        {
            int nextFree = LabelWidth;
            for (int w = 0; w < _VisibleWeeks; w++)
            {
                DateTime weekStart = start.AddDays((_FirstWeek + w) * 7);
                DateTime first = weekStart < From ? From : weekStart;
                bool label = w == 0 || weekStart.AddDays(-7).Month != weekStart.Month;
                if (!label) continue;
                int x = LabelWidth + w * WeekWidth;
                string name = T(CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(first.Month));
                if (w == 0 || first.Month == 1) name = name + " " + first.Year.ToString(CultureInfo.InvariantCulture);
                if (x < nextFree || x + TextCells.Width(name) > width) continue;
                int drawn = SurfaceText.Draw(surface, x, 0, name, Theme.Muted, width - x);
                nextFree = x + drawn + 1;
            }
        }

        private void RenderFooter(ISurface surface, int y, int width)
        {
            int x = SurfaceText.Draw(surface, 0, y, Summary, SummaryStyle ?? Theme.Accent, width);
            int count = CountOn(Selected);
            Dictionary<string, object?> args = new Dictionary<string, object?>(StringComparer.Ordinal);
            args["count"] = count;
            string selected = Selected.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + Localizer.T("{count, plural, one {# commit} other {# commits}}", args);
            string legendLess = T("Less") + " ";
            string legendMore = " " + T("More");
            int legendWidth = TextCells.Width(legendLess) + 5 * 2 - 1 + TextCells.Width(legendMore);
            int selectedX = x > 0 ? x + 3 : 0;
            int room = width - selectedX - (legendWidth + 3);
            bool legend = room >= TextCells.Width(selected);
            if (selectedX < width) SurfaceText.Draw(surface, selectedX, y, selected, Theme.Text, legend ? room : width - selectedX);
            if (!legend) return;
            int lx = width - legendWidth;
            lx += SurfaceText.Draw(surface, lx, y, legendLess, Theme.Muted, width - lx);
            for (int level = 0; level <= 4; level++)
            {
                lx += SurfaceText.Draw(surface, lx, y, Glyph(level, Theme), LevelStyle(level, Theme), 1);
                if (level < 4) lx += 1;
            }

            SurfaceText.Draw(surface, lx, y, legendMore, Theme.Muted, width - lx);
        }

        #endregion
    }
}
