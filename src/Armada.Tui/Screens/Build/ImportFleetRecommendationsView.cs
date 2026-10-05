namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The fleet recommendations of an import batch as a list of fleets with their repositories (the dashboard's
    /// fleet cards): each fleet shows its name, description, the captain's rationale, and its repositories; the
    /// cursor moves over fleets and repositories. Editing (rename, describe, move, add, remove, apply) is driven by
    /// the import wizard's keys. Pure helpers port <c>validateDrafts</c>, <c>moveVessel</c>, and
    /// <c>buildApplyPayload</c>. Not thread-safe.
    /// </summary>
    public class ImportFleetRecommendationsView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// The bucket the server fills with vessels the captain did not assign; never created as a fleet.
        /// </summary>
        public const string Uncategorized = "Uncategorized";

        /// <summary>
        /// The drafts.
        /// </summary>
        public List<ImportFleetDraft> Drafts { get; } = new List<ImportFleetDraft>();

        /// <summary>
        /// Vessel names by id (from the batch items).
        /// </summary>
        public Dictionary<string, string> VesselNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// True while the drafts can be edited.
        /// </summary>
        public bool Editing { get; set; } = false;

        /// <summary>
        /// True once Apply was attempted (validation errors show).
        /// </summary>
        public bool ShowErrors { get; set; } = false;

        /// <summary>
        /// Cursor index among the rows.
        /// </summary>
        public int Cursor { get; private set; } = 0;

        #endregion

        #region Private-Members

        private int _Scroll = 0;
        private int _Counter = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the drafts from stored recommendations (<c>toDrafts</c>).
        /// </summary>
        /// <param name="recommendations">Recommendations.</param>
        public void Load(IEnumerable<VesselImportFleetRecommendation> recommendations)
        {
            Drafts.Clear();
            foreach (VesselImportFleetRecommendation r in recommendations ?? Enumerable.Empty<VesselImportFleetRecommendation>())
            {
                ImportFleetDraft d = new ImportFleetDraft();
                d.Key = String.IsNullOrEmpty(r.Id) ? "draft-" + (_Counter++) : r.Id;
                d.Name = r.Name ?? "";
                d.Description = r.Description ?? "";
                d.Rationale = r.Rationale ?? "";
                d.VesselIds = (r.VesselIds ?? new List<string>()).ToList();
                d.AppliedFleetId = r.AppliedFleetId;
                Drafts.Add(d);
            }

            Cursor = 0;
        }

        /// <summary>
        /// Add an empty fleet.
        /// </summary>
        /// <returns>The new draft.</returns>
        public ImportFleetDraft AddFleet()
        {
            ImportFleetDraft d = new ImportFleetDraft();
            d.Key = "new-" + (_Counter++);
            Drafts.Add(d);
            return d;
        }

        /// <summary>
        /// Move a vessel into a target draft (<c>moveVessel</c>).
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="targetKey">Target draft key.</param>
        public void MoveVessel(string vesselId, string targetKey)
        {
            foreach (ImportFleetDraft d in Drafts)
            {
                d.VesselIds.Remove(vesselId);
                if (d.Key == targetKey) d.VesselIds.Add(vesselId);
            }
        }

        /// <summary>
        /// True for the Uncategorized bucket.
        /// </summary>
        /// <param name="draft">Draft.</param>
        /// <returns>True when uncategorized.</returns>
        public static bool IsUncategorized(ImportFleetDraft draft)
        {
            return String.Equals(draft.Name.Trim(), Uncategorized, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validation errors by draft key (<c>validateDrafts</c>; English).
        /// </summary>
        /// <returns>Errors.</returns>
        public Dictionary<string, string> Validate()
        {
            Dictionary<string, string> errors = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ImportFleetDraft d in Drafts)
            {
                string name = d.Name.Trim();
                if (d.VesselIds.Count == 0) continue;
                if (name.Length == 0)
                {
                    errors[d.Key] = "Give this fleet a name.";
                    continue;
                }

                if (name.Length > 256)
                {
                    errors[d.Key] = "Fleet names must be 256 characters or fewer.";
                    continue;
                }

                string lower = name.ToLowerInvariant();
                if (seen.ContainsKey(lower)) errors[d.Key] = "Another fleet already uses this name.";
                else seen[lower] = d.Key;
            }

            return errors;
        }

        /// <summary>
        /// Repositories assigned to named, non-Uncategorized fleets.
        /// </summary>
        /// <returns>Count.</returns>
        public int AssignedCount()
        {
            return Drafts.Where(d => !IsUncategorized(d)).Sum(d => d.VesselIds.Count);
        }

        /// <summary>
        /// Fleets with repositories, excluding Uncategorized.
        /// </summary>
        /// <returns>Count.</returns>
        public int FleetCount()
        {
            return Drafts.Count(d => d.VesselIds.Count > 0 && !IsUncategorized(d));
        }

        /// <summary>
        /// The apply request (<c>buildApplyPayload</c>): fleets without vessels dropped, names trimmed.
        /// </summary>
        /// <returns>Request.</returns>
        public FleetRecommendationApplyRequest BuildApply()
        {
            FleetRecommendationApplyRequest request = new FleetRecommendationApplyRequest();
            foreach (ImportFleetDraft d in Drafts.Where(d => d.VesselIds.Count > 0))
            {
                FleetRecommendationApplyFleet f = new FleetRecommendationApplyFleet();
                f.Name = d.Name.Trim();
                f.Description = String.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim();
                f.VesselIds = d.VesselIds.ToList();
                request.Fleets.Add(f);
            }

            return request;
        }

        /// <summary>
        /// The display rows.
        /// </summary>
        /// <returns>Rows.</returns>
        public List<ImportFleetRow> Rows()
        {
            List<ImportFleetRow> rows = new List<ImportFleetRow>();
            foreach (ImportFleetDraft d in Drafts)
            {
                rows.Add(new ImportFleetRow(d, "fleet"));
                if (!String.IsNullOrWhiteSpace(d.Description)) rows.Add(new ImportFleetRow(d, "note", null, d.Description));
                if (IsUncategorized(d)) rows.Add(new ImportFleetRow(d, "note", null, T("Repositories in Uncategorized are left without a fleet when you apply.")));
                if (!String.IsNullOrWhiteSpace(d.Rationale)) rows.Add(new ImportFleetRow(d, "note", null, T("Why the captain grouped these") + ": " + d.Rationale));
                foreach (string id in d.VesselIds) rows.Add(new ImportFleetRow(d, "vessel", id));
                if (d.VesselIds.Count == 0) rows.Add(new ImportFleetRow(d, "note", null, T("No repositories. Move some here or remove this fleet.")));
            }

            return rows;
        }

        /// <summary>
        /// The row under the cursor, or null.
        /// </summary>
        public ImportFleetRow? Current
        {
            get
            {
                List<ImportFleetRow> rows = Rows();
                if (rows.Count == 0) return null;
                return rows[Math.Clamp(Cursor, 0, rows.Count - 1)];
            }
        }

        /// <summary>
        /// Move the cursor to a draft's header.
        /// </summary>
        /// <param name="key">Draft key.</param>
        public void Reveal(string key)
        {
            List<ImportFleetRow> rows = Rows();
            int idx = rows.FindIndex(r => r.Kind == "fleet" && r.Draft.Key == key);
            if (idx >= 0) Cursor = idx;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            List<ImportFleetRow> rows = Rows();
            if (rows.Count == 0) return false;
            switch (key.Code)
            {
                case KeyCode.Up:
                    for (int i = Cursor - 1; i >= 0; i--)
                    {
                        if (rows[i].Selectable)
                        {
                            Cursor = i;
                            return true;
                        }
                    }

                    return false;
                case KeyCode.Down:
                    for (int i = Cursor + 1; i < rows.Count; i++)
                    {
                        if (rows[i].Selectable)
                        {
                            Cursor = i;
                            return true;
                        }
                    }

                    return false;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            List<ImportFleetRow> rows = Rows();
            if (rows.Count == 0) return;
            Cursor = Math.Clamp(Cursor, 0, rows.Count - 1);
            if (!rows[Cursor].Selectable)
            {
                int next = rows.FindIndex(Cursor, r => r.Selectable);
                if (next >= 0) Cursor = next;
            }

            if (Cursor < _Scroll) _Scroll = Cursor;
            if (Cursor >= _Scroll + height) _Scroll = Cursor - height + 1;
            Dictionary<string, string> errors = ShowErrors ? Validate() : new Dictionary<string, string>();
            for (int row = 0; row < height; row++)
            {
                int idx = _Scroll + row;
                if (idx >= rows.Count) break;
                ImportFleetRow r = rows[idx];
                string text;
                CellStyle style;
                if (r.Kind == "fleet")
                {
                    string name = String.IsNullOrWhiteSpace(r.Draft.Name) ? T("Unnamed fleet") : r.Draft.Name;
                    text = name + "  (" + r.Draft.VesselIds.Count + ")" + (r.Draft.AppliedFleetId != null ? "  -> " + r.Draft.AppliedFleetId : "");
                    if (errors.TryGetValue(r.Draft.Key, out string? err)) text += "  ! " + T(err);
                    style = errors.ContainsKey(r.Draft.Key) ? Theme.Error : IsUncategorized(r.Draft) ? Theme.Muted : Theme.Accent;
                }
                else if (r.Kind == "vessel")
                {
                    text = "    - " + (r.VesselId != null && VesselNames.TryGetValue(r.VesselId, out string? n) ? n : r.VesselId);
                    style = Theme.Text;
                }
                else
                {
                    text = "    " + r.Text;
                    style = Theme.Muted;
                }

                if (idx == Cursor && r.Selectable)
                {
                    CellStyle cur = IsFocused ? Theme.GridCursor : Theme.SelectionInactive;
                    SurfaceText.FillRow(surface, 0, row, width, cur);
                    style = style.WithBackground(cur.Background);
                }

                SurfaceText.Draw(surface, 0, row, text, style, width);
            }
        }

        #endregion
    }
}
