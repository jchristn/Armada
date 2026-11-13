namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Tui.Services;

    /// <summary>
    /// Black box prevention on the TUI's create and dispatch flows: the preview action (overlap preview for a chosen
    /// vessel, which carries the brief lint, or the brief lint alone without one) and the warnings a create response
    /// returned (<c>OverlapWarnings</c>, <c>BriefLintWarnings</c>). Neither ever blocks a dispatch. Use on the UI loop.
    /// </summary>
    public static class BlackBoxDispatchPreview
    {
        #region Public-Members

        /// <summary>
        /// Title of the preview viewer.
        /// </summary>
        public const string PreviewTitle = "Dispatch Preview";

        /// <summary>
        /// Title of the warnings viewer shown after a create.
        /// </summary>
        public const string WarningsTitle = "Dispatch Warnings";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Preview one or more missions before they are dispatched: <c>overlap-preview</c> per mission when a vessel is
        /// chosen (each carries its brief lint), else <c>lint-brief</c> per mission. Shows the combined result.
        /// </summary>
        /// <param name="screen">Screen (calls and the viewer).</param>
        /// <param name="vesselId">Vessel, or null or empty.</param>
        /// <param name="missions">Missions (title, description).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void Run(OpsScreen screen, string? vesselId, IReadOnlyList<KeyValuePair<string, string>> missions)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            List<KeyValuePair<string, string>> work = missions.Where(m => !String.IsNullOrWhiteSpace(m.Key) || !String.IsNullOrWhiteSpace(m.Value)).ToList();
            if (work.Count == 0)
            {
                screen.Toast(NotificationSeverityEnum.Warning, screen.Tr("Write a title or description to preview."));
                return;
            }

            string? vessel = String.IsNullOrWhiteSpace(vesselId) ? null : vesselId;
            screen.Call(async (c, t) =>
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, string> m in work)
                {
                    string text;
                    if (vessel != null)
                    {
                        MissionOverlapPreviewRequest request = new MissionOverlapPreviewRequest();
                        request.Title = Blank(m.Key);
                        request.Description = Blank(m.Value);
                        MissionOverlapPreview? preview = await c.PreviewMissionOverlapAsync(vessel, request, t).ConfigureAwait(false);
                        BriefLintResult? lint = preview?.BriefLint;
                        if (lint == null) lint = await LintAsync(c, m, vessel, t).ConfigureAwait(false);
                        text = BlackBoxInsightText.Preview(preview, lint);
                    }
                    else
                    {
                        BriefLintResult? lint = await LintAsync(c, m, null, t).ConfigureAwait(false);
                        text = BlackBoxInsightText.Preview(null, lint) + "\n\nChoose a vessel to check overlaps with its active missions.";
                    }

                    parts.Add(work.Count > 1 ? "== " + (String.IsNullOrWhiteSpace(m.Key) ? "(untitled)" : m.Key.Trim()) + " ==\n" + text : text);
                }

                return String.Join("\n\n", parts);
            }, text => screen.ShowText(screen.Tr(PreviewTitle), text), "Preview failed");
        }

        /// <summary>
        /// Show the warnings a create or dispatch response returned (a viewer and a warning toast), or nothing.
        /// </summary>
        /// <param name="screen">Screen.</param>
        /// <param name="overlaps">Overlap warnings, or null.</param>
        /// <param name="lint">Brief lint findings, or null.</param>
        /// <returns>True when there were warnings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> is null.</exception>
        public static bool ShowWarnings(OpsScreen screen, IReadOnlyList<MissionOverlapWarning>? overlaps, IReadOnlyList<BriefLintFinding>? lint)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            string? text = BlackBoxInsightText.DispatchWarnings(overlaps, lint);
            if (text == null) return false;
            int count = (overlaps?.Count ?? 0) + (lint?.Count ?? 0);
            screen.Toast(NotificationSeverityEnum.Warning, screen.Tr("Dispatched with {{count}} black box warning(s).", LocalizationArgs.Of("count", count)));
            screen.ShowText(screen.Tr(WarningsTitle), text);
            return true;
        }

        #endregion

        #region Private-Methods

        private static Task<BriefLintResult?> LintAsync(Armada.Client.ArmadaClient c, KeyValuePair<string, string> m, string? vesselId, System.Threading.CancellationToken t)
        {
            BriefLintRequest request = new BriefLintRequest();
            request.Title = Blank(m.Key);
            request.Description = Blank(m.Value);
            request.VesselId = vesselId;
            return c.LintMissionBriefAsync(request, t);
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
