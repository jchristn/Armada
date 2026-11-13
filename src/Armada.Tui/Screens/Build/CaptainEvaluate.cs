namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The captain Evaluate view (the dashboard's Evaluate dialog and report): a form for the depth (Quick or Full, Full
    /// by default), the evaluator captain (optional; idle captains, labeled when one shares the evaluated captain's model
    /// family), the battery packs (none means the server default), the personas (none means all the captain is allowed)
    /// and the run count; then a viewer that polls the run's status every few seconds until it finishes and shows the
    /// probe results and the Full report. <c>c</c> cancels a running evaluation. Advisory only.
    /// </summary>
    public static class CaptainEvaluate
    {
        #region Public-Members

        /// <summary>
        /// Seconds between status polls while a run is queued or running.
        /// </summary>
        public const int PollSeconds = 3;

        /// <summary>
        /// Depth choice values.
        /// </summary>
        public const string DepthQuick = "Quick";

        /// <summary>
        /// Depth choice values.
        /// </summary>
        public const string DepthFull = "Full";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the idle captains and the installed battery packs, then open the Evaluate form for a captain.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="captain">Captain to evaluate.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void Open(OpsScreen screen, Captain captain)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            screen.Call(async (c, t) =>
            {
                ArmadaPageQuery query = new ArmadaPageQuery();
                query.PageSize = 1000;
                EnumerationResult<Captain>? captains = await c.ListCaptainsAsync(query, t).ConfigureAwait(false);
                EnumerationResult<BatteryPackInfo>? packs = await c.ListBatteryPacksAsync(1, 1000, t).ConfigureAwait(false);
                return new KeyValuePair<List<Captain>, List<BatteryPackInfo>>(captains?.Objects ?? new List<Captain>(), packs?.Objects ?? new List<BatteryPackInfo>());
            }, data => ShowForm(screen, captain, IdleEvaluators(captain, data.Key), data.Value), "Failed to load evaluation options.");
        }

        /// <summary>
        /// Show the Evaluate form.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="captain">Captain to evaluate.</param>
        /// <param name="evaluators">Idle captains that may evaluate it.</param>
        /// <param name="packs">Installed battery packs.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static OpsFormDialog ShowForm(OpsScreen screen, Captain captain, List<Captain> evaluators, List<BatteryPackInfo> packs)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            evaluators = evaluators ?? new List<Captain>();
            packs = packs ?? new List<BatteryPackInfo>();

            OpsFormDialog dialog = screen.NewForm(screen.Tr("Evaluate {{name}}", LocalizationArgs.Of("name", captain.Name)), "Start evaluation");
            dialog.Intro = "Quick runs the mechanical probes. Full adds the fixture battery and an evaluator captain's grading.";
            SelectField<string> depth = screen.NewSelect("Depth", new List<SelectOption<string>>
            {
                new SelectOption<string>(DepthQuick, screen.Tr("Quick")),
                new SelectOption<string>(DepthFull, screen.Tr("Full")),
            });
            depth.Choose(depth.Options.First(o => o.Value == DepthFull));

            List<SelectOption<string>> evaluatorOptions = new List<SelectOption<string>>
            {
                new SelectOption<string>("", screen.Tr("Automatic (highest-tier idle captain of another model family)"))
            };
            evaluatorOptions.AddRange(evaluators.Select(e => new SelectOption<string>(e.Id, EvaluatorLabel(captain, e, screen.Context.Loc))));
            SelectField<string> evaluator = screen.NewSelect("Evaluator captain", evaluatorOptions);
            evaluator.Choose(evaluatorOptions[0]);

            List<KeyValuePair<string, OpsCheckField>> packChecks = packs.Where(p => p.Valid)
                .Select(p => new KeyValuePair<string, OpsCheckField>(p.Id, new OpsCheckField(String.IsNullOrEmpty(p.Name) ? p.Id : p.Name + " (" + p.Id + ")")))
                .ToList();
            List<KeyValuePair<string, OpsCheckField>> personaChecks = PersonaOptions(captain, packs)
                .Select(p => new KeyValuePair<string, OpsCheckField>(p, new OpsCheckField(p)))
                .ToList();
            InputField runCount = new InputField();
            runCount.Placeholder = "3";

            dialog.AddField("Depth", depth);
            dialog.AddField("Evaluator captain", evaluator, evaluators.Count == 0 ? "No other captain is idle." : null);
            if (packChecks.Count == 0) dialog.Notes.Add(screen.Tr("No battery packs are installed."));
            for (int i = 0; i < packChecks.Count; i++)
                dialog.AddField(i == 0 ? "Battery packs" : "", packChecks[i].Value, i == 0 ? "None selected: the packs matching your repositories' languages." : null);
            for (int i = 0; i < personaChecks.Count; i++)
                dialog.AddField(i == 0 ? "Personas" : "", personaChecks[i].Value, i == 0 ? "None selected: every persona the captain is allowed." : null);
            dialog.AddField("Run count", runCount, "Runs of each battery task, 1 to 10 (empty: 3).");

            evaluator.ValueChanged += (s, e) =>
            {
                dialog.Notes.Clear();
                Captain? chosen = evaluators.FirstOrDefault(x => x.Id == evaluator.Value);
                string? note = FamilyNote(captain, chosen, screen.Context.Loc);
                if (note != null) dialog.Notes.Add(note);
            };

            dialog.Validate = () =>
            {
                string? error;
                Build(depth.Value, null, null, null, runCount.Value, out error);
                return error == null ? null : screen.Tr(error);
            };
            dialog.Submit = d =>
            {
                string? error;
                EvaluateCaptainRequest request = Build(
                    depth.Value,
                    evaluator.Value,
                    packChecks.Where(p => p.Value.Checked).Select(p => p.Key),
                    personaChecks.Where(p => p.Value.Checked).Select(p => p.Key),
                    runCount.Value,
                    out error);
                screen.Call((c, t) => c.EvaluateCaptainWithRequestAsync(captain.Id, request, t), run =>
                {
                    d.Complete();
                    if (run != null) ShowRun(screen, captain.Name, run);
                }, null, ex => d.Fail(ex, screen.Tr("Evaluate failed.")));
                return false;
            };
            screen.Context.Modals.Show(dialog);
            return dialog;
        }

        /// <summary>
        /// Show a run and poll it every <see cref="PollSeconds"/> seconds until it finishes or the viewer closes.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="captainName">Evaluated captain's name.</param>
        /// <param name="run">The started run.</param>
        /// <returns>The viewer.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static CaptainEvaluateViewer ShowRun(OpsScreen screen, string captainName, ModelProbeRun run)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (run == null) throw new ArgumentNullException(nameof(run));
            ModelProbeRun current = run;
            string? error = null;
            bool closed = false;
            Timer? timer = null;
            OpsDocumentView view = new OpsDocumentView();
            view.Builder = doc => Build(doc, captainName, current, error);
            CaptainEvaluateViewer modal = new CaptainEvaluateViewer(screen.Tr("Evaluation"), view, screen.Context.Loc, screen.Context.Theme.Current);
            modal.WidthRatio = 0.9;
            modal.HeightRatio = 0.9;
            modal.CanCancel = !IsFinished(current);
            modal.CopyRequested += (s, e) => screen.Context.Clipboard.Copy(view.PlainText, "Evaluation");

            void Update(ModelProbeRun? latest)
            {
                // A poll answered after a cancel must not replace the finished run the cancel returned.
                if (latest != null && !(IsFinished(current) && !IsFinished(latest))) current = latest;
                error = null;
                modal.CanCancel = !IsFinished(current);
                if (IsFinished(current))
                {
                    timer?.Dispose();
                    timer = null;
                }

                view.Invalidate();
            }

            modal.CancelRequested += (s, e) => screen.Call((c, t) => c.CancelModelProbeRunAsync(current.Id, t), Update, null, ex =>
            {
                error = OpsFormDialog.ServerMessage(ex) ?? screen.Tr("Evaluate failed.");
                view.Invalidate();
            });

            screen.Context.Modals.Show(modal, result =>
            {
                closed = true;
                timer?.Dispose();
                timer = null;
            });

            if (!IsFinished(current))
            {
                timer = new Timer(_ =>
                {
                    if (closed || !screen.IsLive) return;
                    screen.Call((c, t) => c.GetModelProbeRunAsync(current.Id, t), Update, null, ex =>
                    {
                        error = OpsFormDialog.ServerMessage(ex) ?? screen.Tr("Failed to load evaluation.");
                        timer?.Dispose();
                        timer = null;
                        view.Invalidate();
                    });
                }, null, PollSeconds * 1000, PollSeconds * 1000);
            }

            return modal;
        }

        /// <summary>
        /// Build the Evaluate request from the form values. Quick sends only the depth; Full sends the evaluator, packs and
        /// personas (each omitted when empty, so the server applies its default) and the run count (omitted when blank).
        /// </summary>
        /// <param name="depth">Depth choice (<see cref="DepthQuick"/> or <see cref="DepthFull"/>; anything else is Full).</param>
        /// <param name="evaluatorId">Evaluator captain id, or empty for automatic.</param>
        /// <param name="packs">Selected pack ids.</param>
        /// <param name="personas">Selected personas.</param>
        /// <param name="runCount">Run count text, or blank for the default.</param>
        /// <param name="error">English error for an invalid run count, or null.</param>
        /// <returns>The request.</returns>
        public static EvaluateCaptainRequest Build(string? depth, string? evaluatorId, IEnumerable<string>? packs, IEnumerable<string>? personas, string? runCount, out string? error)
        {
            error = null;
            EvaluateCaptainRequest request = new EvaluateCaptainRequest();
            if (String.Equals(depth, DepthQuick, StringComparison.Ordinal))
            {
                request.Depth = ModelProbeDepthEnum.Quick;
                return request;
            }

            request.Depth = ModelProbeDepthEnum.Full;
            request.EvaluatorCaptainId = String.IsNullOrWhiteSpace(evaluatorId) ? null : evaluatorId.Trim();
            List<string> packList = (packs ?? Enumerable.Empty<string>()).Where(p => !String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToList();
            List<string> personaList = (personas ?? Enumerable.Empty<string>()).Where(p => !String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            request.Packs = packList.Count > 0 ? packList : null;
            request.Personas = personaList.Count > 0 ? personaList : null;
            string text = (runCount ?? "").Trim();
            if (text.Length > 0)
            {
                if (!Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 10)
                    error = "The run count must be a whole number from 1 to 10.";
                else
                    request.RunCount = count;
            }

            return request;
        }

        /// <summary>
        /// Idle captains other than the evaluated one, ordered by name.
        /// </summary>
        /// <param name="candidate">Evaluated captain.</param>
        /// <param name="captains">Every captain.</param>
        /// <returns>The idle evaluators.</returns>
        public static List<Captain> IdleEvaluators(Captain candidate, IEnumerable<Captain>? captains)
        {
            return (captains ?? Enumerable.Empty<Captain>())
                .Where(c => c != null && c.State == CaptainStateEnum.Idle && !String.Equals(c.Id, candidate?.Id, StringComparison.Ordinal))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The personas the form offers: the captain's allowed personas when it lists any, else every persona of the
        /// valid packs' tasks, sorted.
        /// </summary>
        /// <param name="captain">Evaluated captain.</param>
        /// <param name="packs">Installed packs.</param>
        /// <returns>Persona names.</returns>
        public static List<string> PersonaOptions(Captain captain, IEnumerable<BatteryPackInfo>? packs)
        {
            List<string> allowed = AllowedPersonas(captain?.AllowedPersonas);
            if (allowed.Count > 0) return allowed;
            return (packs ?? Enumerable.Empty<BatteryPackInfo>())
                .Where(p => p.Valid)
                .SelectMany(p => p.Tasks.Select(t => t.Persona ?? ""))
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// An evaluator's option label: its name, marked when it shares the evaluated captain's model family.
        /// </summary>
        /// <param name="candidate">Evaluated captain.</param>
        /// <param name="evaluator">Evaluator.</param>
        /// <param name="loc">Localizer.</param>
        /// <returns>The label.</returns>
        public static string EvaluatorLabel(Captain candidate, Captain evaluator, ITextLocalizer loc)
        {
            return SameFamily(candidate, evaluator)
                ? loc.T("{{name}} (same model family)", LocalizationArgs.Of("name", evaluator.Name))
                : evaluator.Name;
        }

        /// <summary>
        /// The warning shown when the chosen evaluator shares the evaluated captain's model family, or null.
        /// </summary>
        /// <param name="candidate">Evaluated captain.</param>
        /// <param name="evaluator">Chosen evaluator, or null for automatic.</param>
        /// <param name="loc">Localizer.</param>
        /// <returns>The note, or null.</returns>
        public static string? FamilyNote(Captain candidate, Captain? evaluator, ITextLocalizer loc)
        {
            if (evaluator == null || !SameFamily(candidate, evaluator)) return null;
            return loc.T("{{name}} is of the same model family ({{family}}) as the evaluated captain; the report will carry a warning.",
                LocalizationArgs.Of("name", evaluator.Name, "family", ModelFamily.Of(candidate, null)));
        }

        /// <summary>
        /// True when the run is no longer queued or running.
        /// </summary>
        /// <param name="run">Run, or null.</param>
        /// <returns>True when finished.</returns>
        public static bool IsFinished(ModelProbeRun? run)
        {
            return run != null && (run.Status == ModelProbeRunStatusEnum.Completed || run.Status == ModelProbeRunStatusEnum.Failed || run.Status == ModelProbeRunStatusEnum.Cancelled);
        }

        /// <summary>
        /// Build the run document: status and counts, the failure reason, the probe results and, for Full, the report
        /// (completeness, evaluator, warnings, per-persona counts and the narrative).
        /// </summary>
        /// <param name="doc">Document.</param>
        /// <param name="captainName">Evaluated captain's name.</param>
        /// <param name="run">Run.</param>
        /// <param name="error">Poll or cancel error, or null.</param>
        /// <returns>The document.</returns>
        public static OpsDocument Build(OpsDocument doc, string captainName, ModelProbeRun run, string? error)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (run == null) throw new ArgumentNullException(nameof(run));
            doc.Text(captainName + "  " + run.Id, doc.Theme.Muted);
            doc.Field("Depth", doc.Loc.T(run.Depth.ToString()));
            doc.Field("Status", StatusBadge.Label(run.Status), StatusBadge.Style(run.Status, doc.Theme));
            doc.Field("Counts", doc.Loc.T("{{passed}} passed, {{failed}} failed, {{couldNotRun}} could not run",
                LocalizationArgs.Of("passed", run.PassedCount, "failed", run.FailedCount, "couldNotRun", run.CouldNotRunCount)));
            if (!IsFinished(run)) doc.Note(doc.Loc.T("Live: refreshing every {{seconds}} s.", LocalizationArgs.Of("seconds", PollSeconds)));
            if (error != null) doc.Text(error, doc.Theme.Error);
            if (IsFinished(run) && !String.IsNullOrEmpty(run.FailureReason)) doc.Text(run.FailureReason, doc.Theme.Error);

            if (IsFinished(run) && run.Results.Count > 0)
            {
                doc.Section("Probes");
                doc.Table(
                    new List<string> { "Probe", "Outcome", "Summary" },
                    run.Results.Select(r => (IList<string>)new List<string>
                    {
                        r.Probe.ToString(),
                        doc.Loc.T(r.Outcome == ModelProbeOutcomeEnum.CouldNotRun ? "Could not run" : r.Outcome.ToString()),
                        (r.Summary ?? "") + (String.IsNullOrEmpty(r.Reason) ? "" : " (" + r.Reason + ")"),
                    }),
                    60);
            }

            if (IsFinished(run) && run.Report != null) Report(doc, run.Report);
            return doc;
        }

        #endregion

        #region Private-Methods

        private static void Report(OpsDocument doc, ModelEvaluationReport report)
        {
            doc.Section("Report");
            doc.Text(report.Complete
                ? doc.Loc.T("Report complete")
                : doc.Loc.T("Incomplete: {{reason}}", LocalizationArgs.Of("reason", report.IncompleteReason ?? "")),
                report.Complete ? doc.Theme.Muted : doc.Theme.Error);
            if (!String.IsNullOrEmpty(report.EvaluatorCaptainName))
                doc.Text(doc.Loc.T("Evaluator: {{name}}", LocalizationArgs.Of("name", report.EvaluatorCaptainName)), doc.Theme.Muted);
            foreach (string warning in report.Warnings) doc.Text(warning, doc.Theme.Warning);
            foreach (ModelEvaluationPersonaOutcome outcome in report.PersonaOutcomes)
            {
                Dictionary<string, object?> args = LocalizationArgs.Of("persona", outcome.Persona, "passed", outcome.Totals.Passed, "graded", outcome.Totals.Graded);
                args["pending"] = outcome.Totals.PendingEvaluation;
                args["couldNotRun"] = outcome.Totals.CouldNotRun;
                doc.Text(doc.Loc.T("{{persona}}: {{passed}} of {{graded}} passed, {{pending}} pending, {{couldNotRun}} could not run", args));
            }

            ModelEvaluationNarrative narrative = report.Narrative;
            if (narrative.Status == ModelEvaluationNarrativeStatusEnum.Accepted)
            {
                Findings(doc, "Strengths", narrative.Strengths.Select(f => f.Text));
                Findings(doc, "Weaknesses", narrative.Weaknesses.Select(f => f.Text));
                Findings(doc, "Observed problems", narrative.ObservedProblems);
                if (!String.IsNullOrEmpty(narrative.HowToWork)) doc.Field("How to work with this model", narrative.HowToWork);
            }
            else if (narrative.Status == ModelEvaluationNarrativeStatusEnum.Skipped)
            {
                doc.Text(doc.Loc.T("Subjective grading skipped: {{reason}}", LocalizationArgs.Of("reason", narrative.Reason ?? "")), doc.Theme.Muted);
            }
            else if (narrative.Status == ModelEvaluationNarrativeStatusEnum.Rejected)
            {
                doc.Text(doc.Loc.T("Narrative rejected: {{reason}}", LocalizationArgs.Of("reason", narrative.Reason ?? "")), doc.Theme.Warning);
            }
        }

        private static void Findings(OpsDocument doc, string title, IEnumerable<string> items)
        {
            List<string> list = items.Where(i => !String.IsNullOrEmpty(i)).ToList();
            if (list.Count == 0) return;
            doc.Section(title);
            foreach (string item in list) doc.Text("- " + item);
        }

        private static bool SameFamily(Captain candidate, Captain evaluator)
        {
            if (candidate == null || evaluator == null) return false;
            return String.Equals(ModelFamily.Of(candidate, null), ModelFamily.Of(evaluator, null), StringComparison.Ordinal);
        }

        private static List<string> AllowedPersonas(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return new List<string>();
            try
            {
                List<string>? parsed = JsonSerializer.Deserialize<List<string>>(value);
                return (parsed ?? new List<string>()).Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        #endregion
    }
}
