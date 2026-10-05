namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Models;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// The "Usage by model" chart: one horizontal bar per model, scaled to the largest model total, split into input,
    /// output, and cached segments (distinct glyphs) when the metric is by token type, with the total at the right.
    /// Not focusable. Not thread-safe.
    /// </summary>
    public class TokenUsageModelChart : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Models.
        /// </summary>
        public List<TokenUsageModelBreakdown> Models { get; set; } = new List<TokenUsageModelBreakdown>();

        /// <summary>
        /// Split bars by token type.
        /// </summary>
        public bool ByType { get; set; } = false;

        /// <summary>
        /// Text shown instead of the chart (already English; translated when drawn), or empty.
        /// </summary>
        public string Message { get; set; } = "";

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        /// <summary>
        /// Rows wanted: title, legend (by type), and one per model.
        /// </summary>
        public int PreferredHeight
        {
            get { return 1 + (ByType ? 1 : 0) + Math.Max(1, Models.Count); }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The data as a tab-separated table, for copying.
        /// </summary>
        /// <returns>Text.</returns>
        public string ToTextTable()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(T("Model")).Append('\t').Append(T("Input")).Append('\t').Append(T("Output")).Append('\t').Append(T("Cached")).Append('\t').Append(T("Total")).Append('\n');
            foreach (TokenUsageModelBreakdown m in Models)
            {
                sb.Append(m.Model).Append('\t').Append(m.InputTokens).Append('\t').Append(m.OutputTokens).Append('\t').Append(m.CachedTokens).Append('\t').Append(m.TotalTokens).Append('\n');
            }

            return sb.ToString();
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 20 || height < 1) return;
            SurfaceText.Draw(surface, 0, 0, T("Usage by model"), Theme.Accent, width);
            int y = 1;
            if (Message.Length > 0 || Models.Count == 0)
            {
                if (y < height) SurfaceText.Draw(surface, 0, y, T(Message.Length > 0 ? Message : "No token usage for this time range"), Theme.Muted, width);
                return;
            }

            string[] glyphs = Theme.AsciiBorders ? new string[] { "#", "=", "+" } : new string[] { "\u2588", "\u2593", "\u2592" };
            if (ByType && y < height)
            {
                int lx = 0;
                lx += SurfaceText.Draw(surface, lx, y, glyphs[0] + " " + T("Input") + "   ", Theme.Accent, width - lx);
                lx += SurfaceText.Draw(surface, lx, y, glyphs[1] + " " + T("Output") + "   ", Theme.Success, width - lx);
                SurfaceText.Draw(surface, lx, y, glyphs[2] + " " + T("Cached"), Theme.Warning, width - lx);
                y++;
            }

            int labelWidth = Math.Min(30, Math.Max(10, width / 4));
            int valueWidth = 8;
            int barWidth = Math.Max(1, width - labelWidth - valueWidth - 2);
            long max = Math.Max(1, Models.Max(m => m.TotalTokens));
            foreach (TokenUsageModelBreakdown m in Models)
            {
                if (y >= height) break;
                string label = TextCells.Truncate(m.Model, labelWidth - 1);
                SurfaceText.Draw(surface, 0, y, TextCells.PadLeft(label, labelWidth - 1), Theme.Text, labelWidth);
                int x = labelWidth;
                if (ByType)
                {
                    long[] values = new long[] { m.InputTokens, m.OutputTokens, m.CachedTokens };
                    CellStyle[] styles = new CellStyle[] { Theme.Accent, Theme.Success, Theme.Warning };
                    for (int s = 0; s < 3; s++)
                    {
                        if (values[s] <= 0) continue;
                        int w = Math.Max(1, (int)Math.Round(values[s] / (double)max * barWidth));
                        for (int i = 0; i < w && x < labelWidth + barWidth; i++) surface.DrawText(x++, y, glyphs[s], styles[s]);
                    }
                }
                else
                {
                    int w = Math.Max(m.TotalTokens > 0 ? 1 : 0, (int)Math.Round(m.TotalTokens / (double)max * barWidth));
                    for (int i = 0; i < w; i++) surface.DrawText(x++, y, glyphs[0], Theme.Accent);
                }

                string total = TokenUsageFormat.Tokens(m.TotalTokens);
                SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(total)), y, total, Theme.Muted, width);
                y++;
            }
        }

        #endregion
    }
}
