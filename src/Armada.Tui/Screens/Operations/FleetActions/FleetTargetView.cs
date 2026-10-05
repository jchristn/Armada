namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Services;
    using TUIKit.Input;

    /// <summary>
    /// The target drawer's content (the dashboard's TargetDetailDrawer): status, reason, vessel, exit code, duration,
    /// timing, voyage, the truncation note, the rendered command, and the last 30 lines of stdout and stderr. Keys:
    /// <c>o</c> opens the full output, <c>e</c> the full error output, <c>y</c> copies the rendered command, <c>r</c>
    /// refreshes, <c>v</c> opens the vessel, <c>V</c> the voyage. Not thread-safe.
    /// </summary>
    public class FleetTargetView : OpsDocumentView
    {
        #region Public-Members

        /// <summary>
        /// Lines of output shown as a preview (the dashboard's PREVIEW_LINES).
        /// </summary>
        public const int PreviewLines = 30;

        /// <summary>
        /// The target, or null while loading.
        /// </summary>
        public FleetActionRunTarget? Target { get; set; } = null;

        /// <summary>
        /// Load error (translated), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Raised for a key action: "stdout", "stderr", "copy", "refresh", "vessel", "voyage".
        /// </summary>
        public event EventHandler<string>? ActionRequested;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetTargetView()
        {
            Builder = Build;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The last lines of a text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="lines">Lines.</param>
        /// <returns>Tail.</returns>
        public static string Tail(string text, int lines)
        {
            string[] all = (text ?? "").Replace("\r\n", "\n").Split('\n');
            return all.Length <= lines ? (text ?? "") : String.Join("\n", all.Skip(all.Length - lines));
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (!Searching && key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None)
            {
                string? action = null;
                switch (key.Rune)
                {
                    case 'o': action = "stdout"; break;
                    case 'e': action = "stderr"; break;
                    case 'y': action = "copy"; break;
                    case 'r': action = "refresh"; break;
                    case 'v': action = "vessel"; break;
                }

                if (key.Rune == 'V') action = "voyage";
                if (action != null)
                {
                    ActionRequested?.Invoke(this, action);
                    return true;
                }
            }

            return base.HandleKey(key);
        }

        #endregion

        #region Private-Methods

        private OpsDocument Build(OpsDocument doc)
        {
            doc.LabelWidth = 14;
            if (Error != null) doc.Text("! " + Error, doc.Theme.Error);
            FleetActionRunTarget? t = Target;
            if (t == null)
            {
                if (Error == null) doc.Note("Loading...");
                return doc;
            }

            string tone = FleetActionLabels.TargetTone(t.Status);
            doc.Field("Status", Armada.Tui.Widgets.StatusBadge.Marker(tone) + " " + doc.Loc.T(FleetActionLabels.TargetStatusLabel(t.Status)), Armada.Tui.Widgets.StatusBadge.Style(tone, doc.Theme));
            string reason = FleetActionLabels.ReasonLabel(doc.Loc, t.SkipReason, t.FailureReason);
            if (reason.Length > 0) doc.Field("Reason", reason + " (" + (t.SkipReason ?? t.FailureReason) + ")");
            doc.Field("Vessel", t.VesselName + "  (v)");
            doc.Field("Exit code", t.ExitCode.HasValue ? t.ExitCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-");
            doc.Field("Duration", FleetActionLabels.FormatDuration(doc.Loc, t.DurationMs));
            doc.Field("Started", t.StartedUtc.HasValue ? doc.Loc.FormatDateTime(t.StartedUtc.Value) : "-");
            doc.Field("Completed", t.CompletedUtc.HasValue ? doc.Loc.FormatDateTime(t.CompletedUtc.Value) : "-");
            if (!String.IsNullOrEmpty(t.VoyageId)) doc.Field("Voyage", t.VoyageId + "  (V)");
            if (t.OutputTruncated) doc.Blank().Text("! " + doc.Loc.T("Output was truncated. Only the end of each stream was kept (FleetActions.MaxOutputBytes)."), doc.Theme.Warning);
            doc.Section("Rendered command", "  (y " + doc.Loc.T("Copy command") + ")");
            doc.Text(String.IsNullOrEmpty(t.RenderedText) ? doc.Loc.T("(not rendered)") : t.RenderedText, doc.Theme.Code);
            doc.Section("Standard output", " (" + Bytes(doc.Loc, (t.OutputText ?? "").Length) + ")  o " + doc.Loc.T("Open full output"));
            if (String.IsNullOrEmpty(t.OutputText)) doc.Note("No output captured.");
            else doc.Text(Tail(t.OutputText!, PreviewLines), doc.Theme.Code);
            doc.Section("Standard error", " (" + Bytes(doc.Loc, (t.ErrorText ?? "").Length) + ")  e " + doc.Loc.T("Open full error output"));
            if (String.IsNullOrEmpty(t.ErrorText)) doc.Note("No error output captured.");
            else doc.Text(Tail(t.ErrorText!, PreviewLines), doc.Theme.Error);
            doc.Blank().Note("r " + doc.Loc.T("Refresh") + "   Esc " + doc.Loc.T("Close"));
            return doc;
        }

        private static string Bytes(ITextLocalizer loc, long length)
        {
            if (length < 1024) return loc.FormatNumber(length) + " B";
            if (length < 1024 * 1024) return (length / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            return (length / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }

        #endregion
    }
}
