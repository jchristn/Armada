namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// A row of KPI cards (label over value, optional detail), the terminal form of the dashboard's summary cards.
    /// Renders boxed cards when given three or more rows, and a compact "Label: value" line otherwise. Not focusable.
    /// Not thread-safe.
    /// </summary>
    public class KpiBar : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Cards in order. Never null.
        /// </summary>
        public List<KpiCard> Cards { get; } = new List<KpiCard>();

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        /// <summary>
        /// Rows the bar wants: 3 for boxed cards (4 when any card has a detail line).
        /// </summary>
        public int PreferredHeight
        {
            get { return Cards.Any(c => c.Detail.Length > 0) ? 4 : 3; }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the cards.
        /// </summary>
        /// <param name="cards">Cards.</param>
        public void SetCards(IEnumerable<KpiCard> cards)
        {
            Cards.Clear();
            if (cards != null) Cards.AddRange(cards);
        }

        /// <summary>
        /// The cards as plain text ("Label: value" pairs), for snapshots and copying.
        /// </summary>
        /// <returns>Text.</returns>
        public string ToPlainText()
        {
            return String.Join("   ", Cards.Select(c => T(c.Label) + ": " + c.Value));
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (Cards.Count == 0 || width < 4 || height < 1) return;
            if (height < 3)
            {
                int x = 0;
                foreach (KpiCard card in Cards)
                {
                    if (x >= width) break;
                    x += SurfaceText.Draw(surface, x, 0, T(card.Label) + ": ", Theme.Muted, width - x);
                    x += SurfaceText.Draw(surface, x, 0, card.Value, card.Style != null ? card.Style(Theme) : Theme.Accent, width - x);
                    x += 3;
                }

                return;
            }

            int count = Cards.Count;
            int cardWidth = Math.Max(10, (width - (count - 1)) / count);
            int perRow = Math.Max(1, Math.Min(count, (width + 1) / (cardWidth + 1)));
            cardWidth = Math.Max(8, (width - (perRow - 1)) / perRow);
            int boxHeight = Math.Min(height, PreferredHeight);
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                int top = row * boxHeight;
                if (top + boxHeight > height) break;
                int left = col * (cardWidth + 1);
                DrawCard(surface, Cards[i], new Rect(left, top, Math.Min(cardWidth, width - left), boxHeight));
            }
        }

        #endregion

        #region Private-Methods

        private void DrawCard(ISurface surface, KpiCard card, Rect rect)
        {
            if (rect.Width < 4 || rect.Height < 3) return;
            surface.DrawBox(rect, Theme.Border, Theme.AsciiBorders ? BorderStyle.Ascii : BorderStyle.Rounded, null);
            int inner = rect.Width - 2;
            SurfaceText.Draw(surface, rect.X + 1, rect.Y, " " + T(card.Label) + " ", Theme.Muted, inner);
            SurfaceText.Draw(surface, rect.X + 2, rect.Y + 1, card.Value, (card.Style != null ? card.Style(Theme) : Theme.Accent).WithAttribute(CellAttributes.Bold, true), inner - 1);
            if (rect.Height >= 4 && card.Detail.Length > 0) SurfaceText.Draw(surface, rect.X + 2, rect.Y + 2, card.Detail, Theme.Muted, inner - 1);
        }

        #endregion
    }
}
