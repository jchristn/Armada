namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit.Input;
    using TUIKit;

    /// <summary>
    /// The Failure tab of the mission and voyage screens (and the Brief tab of a black box entry): the entry's
    /// FIX THIS brief with its status, triage, and recovery paths. While the panel has focus its keys run before the
    /// screen's and are listed in the status bar: <c>c</c> copies the FIX THIS brief (OSC 52), <c>x</c> exports it to a
    /// file, <c>r</c> runs a recovery path (dry run, confirm, data-loss acknowledgement), <c>a</c> asks Armada about the
    /// failure, <c>j</c> shows the entry as JSON, <c>s</c> resolves it, <c>p</c> pins or unpins it, <c>n</c> adds a note,
    /// <c>o</c> opens the entry, and <c>A</c> drafts an Armada fix mission from the top proposed Armada change (opens
    /// Dispatch prefilled with its mission brief, on the Armada vessel when one exists). The Armada resilience section is
    /// shown right after the entry's summary fields. Not thread-safe; use on the UI loop.
    /// </summary>
    public class BlackBoxFailurePanel : OpsDocumentView, IPanelKeyHandler
    {
        #region Public-Members

        /// <summary>
        /// Where the entry is read from.
        /// </summary>
        public BlackBoxPanelSourceEnum Source { get; }

        /// <summary>
        /// The loaded entry (with its record), or null.
        /// </summary>
        public BlackBoxEntry? Entry { get; private set; } = null;

        /// <summary>
        /// The FIX THIS brief shown in the panel, which <c>c</c> copies and <c>x</c> exports, or null.
        /// </summary>
        public string? Brief { get; private set; } = null;

        /// <summary>
        /// True while loading.
        /// </summary>
        public bool Loading { get; private set; } = false;

        /// <summary>
        /// True once a load finished (found, not found, or failed).
        /// </summary>
        public bool Loaded { get; private set; } = false;

        /// <summary>
        /// True when the subject has no black box entry (404).
        /// </summary>
        public bool NotFound { get; private set; } = false;

        /// <summary>
        /// Load error (translated), or null.
        /// </summary>
        public string? LoadError { get; private set; } = null;

        /// <summary>
        /// Show the <c>o</c> Open entry key (off on the entry's own screen).
        /// </summary>
        public bool OffersOpen { get; set; } = true;

        /// <summary>
        /// Runs after the entry changed on the server (resolve, pin, note, recovery), or null.
        /// </summary>
        public Action? Changed { get; set; } = null;

        /// <summary>
        /// The recovery paths of the entry, best first.
        /// </summary>
        public List<BlackBoxRecoveryPath> RecoveryPaths
        {
            get
            {
                List<BlackBoxRecoveryPath>? paths = Entry?.Record?.Analysis?.RecoveryPaths;
                return paths == null ? new List<BlackBoxRecoveryPath>() : paths.OrderBy(p => p.Rank).ToList();
            }
        }

        #endregion

        #region Private-Members

        private readonly OpsScreen _Screen;
        private readonly Func<string> _SubjectId;
        private int _Generation = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="screen">Owning screen (calls, dialogs, toasts).</param>
        /// <param name="source">Where the entry is read from.</param>
        /// <param name="subjectId">Mission, voyage, or entry id (read at load time).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxFailurePanel(OpsScreen screen, BlackBoxPanelSourceEnum source, Func<string> subjectId)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            _SubjectId = subjectId ?? throw new ArgumentNullException(nameof(subjectId));
            Source = source;
            Builder = Build;
            Empty = "Open this tab to read the black box.";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the entry unless it is loaded or loading.
        /// </summary>
        public void Ensure()
        {
            if (Loaded || Loading) return;
            Reload();
        }

        /// <summary>
        /// Load (or reload) the entry and both briefs.
        /// </summary>
        public void Reload()
        {
            string id = _SubjectId() ?? "";
            if (id.Length == 0) return;
            int generation = ++_Generation;
            Loading = true;
            Invalidate();
            BlackBoxPanelSourceEnum source = Source;
            _Screen.Call(async (c, t) =>
            {
                BlackBoxEntry? entry = source == BlackBoxPanelSourceEnum.Mission ? await c.GetMissionBlackBoxAsync(id, true, t).ConfigureAwait(false)
                    : source == BlackBoxPanelSourceEnum.Voyage ? await c.GetVoyageBlackBoxAsync(id, true, t).ConfigureAwait(false)
                    : await c.GetBlackBoxAsync(id, true, t).ConfigureAwait(false);
                string? brief = null;
                if (entry != null) brief = await c.GetBlackBoxBriefAsync(entry.Id, false, t).ConfigureAwait(false);
                return new BlackBoxPanelLoad(entry, brief);
            }, r =>
            {
                if (generation != _Generation) return;
                Loading = false;
                Loaded = true;
                Entry = r.Entry;
                Brief = r.Brief;
                NotFound = r.Entry == null;
                LoadError = null;
                Invalidate();
            }, null, ex =>
            {
                if (generation != _Generation) return;
                Loading = false;
                Loaded = true;
                Entry = null;
                Brief = null;
                NotFound = ex is ArmadaApiException api && api.StatusCode == 404;
                LoadError = NotFound ? null : _Screen.Tr("The black box could not be loaded: {{message}}", LocalizationArgs.Of("message", ex.Message));
                Invalidate();
            });
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyHint>? GetKeyHints()
        {
            IReadOnlyList<KeyHint>? search = base.GetKeyHints();
            if (search != null || Entry == null) return search;
            List<KeyHint> hints = new List<KeyHint>
            {
                new KeyHint("c", "Copy FIX THIS"),
                new KeyHint("x", "Export"),
                new KeyHint("r", "Recovery"),
                new KeyHint("a", "Ask Armada"),
                new KeyHint("j", "JSON"),
                new KeyHint("s", "Resolve"),
                new KeyHint("p", Entry.Pinned ? "Unpin" : "Pin"),
                new KeyHint("n", "Note")
            };
            if (OffersOpen) hints.Add(new KeyHint("o", "Open entry"));
            if (BlackBoxResilienceText.TopChange(Entry) != null) hints.Add(new KeyHint("A", "Draft Armada fix"));
            hints.Add(new KeyHint(".", "Actions"));
            hints.Add(new KeyHint("[ ]", "Panels"));
            return hints;
        }

        /// <inheritdoc />
        public bool HandlePanelKey(KeyEvent key)
        {
            if (Entry == null || Searching) return false;
            if (key.Code != KeyCode.Character) return false;
            if (key.Rune == 'A' && (key.Modifiers == KeyModifiers.None || key.Modifiers == KeyModifiers.Shift))
            {
                DraftArmadaFix();
                return true;
            }

            if (key.Modifiers != KeyModifiers.None) return false;
            switch (key.Rune)
            {
                case 'c':
                    CopyBrief();
                    return true;
                case 'x':
                    ExportBrief();
                    return true;
                case 'r':
                    ShowRecovery();
                    return true;
                case 'a':
                    AskArmada();
                    return true;
                case 'j':
                    ShowJson();
                    return true;
                case 's':
                    Resolve();
                    return true;
                case 'p':
                    TogglePin();
                    return true;
                case 'n':
                    if (!String.IsNullOrEmpty(SearchText)) return false;
                    AddNote();
                    return true;
                case 'o':
                    if (!OffersOpen) return false;
                    OpenEntry();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Copy the FIX THIS brief to the clipboard (OSC 52).
        /// </summary>
        /// <returns>True when something was copied.</returns>
        public bool CopyBrief()
        {
            string? text = Brief;
            if (String.IsNullOrEmpty(text))
            {
                _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("The brief is not loaded yet."));
                return false;
            }

            return _Screen.Context.Clipboard.Copy(text!, "FIX THIS brief");
        }

        /// <summary>
        /// Ask for a path (default <c>blackbox-{entryId}.md</c> in the save directory) and write the FIX THIS brief there.
        /// </summary>
        public void ExportBrief()
        {
            if (Entry == null) return;
            string? text = Brief;
            if (String.IsNullOrEmpty(text))
            {
                _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("The brief is not loaded yet."));
                return;
            }

            string brief = text!;
            PathPrompt.AskSave(_Screen.Context, "Export Brief", BlackBoxText.BriefFileName(Entry.Id), path => ExportTo(path, brief));
        }

        /// <summary>
        /// Write the FIX THIS brief to a path without prompting.
        /// </summary>
        /// <param name="path">Path (<c>~</c> expands).</param>
        /// <returns>The full path, or null when the write failed (a toast says why).</returns>
        public string? ExportTo(string path)
        {
            string? text = Brief;
            if (String.IsNullOrEmpty(text)) return null;
            return ExportTo(path, text!);
        }

        /// <summary>
        /// List the recovery paths; choosing one runs its dry run.
        /// </summary>
        public void ShowRecovery()
        {
            if (Entry == null) return;
            List<BlackBoxRecoveryPath> paths = RecoveryPaths;
            if (paths.Count == 0)
            {
                _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("No recovery paths are recorded for this failure."));
                return;
            }

            List<ActionMenuItem> items = new List<ActionMenuItem>();
            foreach (BlackBoxRecoveryPath p in paths)
            {
                BlackBoxRecoveryPath path = p;
                string label = path.Rank.ToString(CultureInfo.InvariantCulture) + ". " + path.Title + (path.DataLossRisk ? "  (" + _Screen.Tr("data loss risk") + ")" : "")
                    + (String.IsNullOrEmpty(path.RunbookId) ? "" : "  [" + _Screen.Tr("runbook") + ": " + RunbookName(path) + "]");
                ActionMenuItem item = new ActionMenuItem(label, () => DryRun(path), "");
                item.Destructive = path.DataLossRisk;
                items.Add(item);
            }

            _Screen.ShowMenu("Recovery Paths", items);
        }

        /// <summary>
        /// Dry run one recovery path: shows the plan and, when the server issued a confirm token, offers to run it.
        /// </summary>
        /// <param name="path">Path.</param>
        public void DryRun(BlackBoxRecoveryPath path)
        {
            if (Entry == null || path == null) return;
            string entryId = Entry.Id;
            BlackBoxRecoveryRunRequest request = new BlackBoxRecoveryRunRequest();
            request.DryRun = true;
            request.AuthorKind = BlackBoxNoteAuthorKindEnum.Operator;
            _Screen.Call((c, t) => c.RunBlackBoxRecoveryAsync(entryId, path.Id, request, t), r =>
            {
                if (r == null) return;
                string plan = PlanText(path, r);
                if (String.IsNullOrEmpty(r.ConfirmToken))
                {
                    _Screen.ShowText(_Screen.Tr("Recovery dry run: {{title}}", LocalizationArgs.Of("title", path.Title)), plan + "\n\n" + _Screen.Tr("The server did not issue a confirm token, so this path cannot run from here."));
                    return;
                }

                string token = r.ConfirmToken!;
                bool risk = path.DataLossRisk || r.Plan.Any(s => s.DataLossRisk);
                _Screen.Confirm("Run Recovery Path", plan, () => Acknowledge(path, token, risk), "Run");
            }, "Recovery dry run failed");
        }

        /// <summary>
        /// Run a recovery path after its dry run with the confirm token.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <param name="confirmToken">Token from the dry run.</param>
        /// <param name="acknowledgeDataLoss">The user acknowledged the data-loss risk.</param>
        public void Execute(BlackBoxRecoveryPath path, string confirmToken, bool acknowledgeDataLoss)
        {
            if (Entry == null || path == null) return;
            string entryId = Entry.Id;
            BlackBoxRecoveryRunRequest request = new BlackBoxRecoveryRunRequest();
            request.DryRun = false;
            request.ConfirmToken = confirmToken;
            request.AcknowledgeDataLoss = acknowledgeDataLoss;
            request.AuthorKind = BlackBoxNoteAuthorKindEnum.Operator;
            _Screen.Call((c, t) => c.RunBlackBoxRecoveryAsync(entryId, path.Id, request, t), r =>
            {
                if (r == null) return;
                _Screen.Toast(r.Succeeded ? NotificationSeverityEnum.Success : NotificationSeverityEnum.Error, r.Succeeded
                    ? _Screen.Tr("Recovery path \"{{title}}\" ran.", LocalizationArgs.Of("title", path.Title))
                    : _Screen.Tr("Recovery path \"{{title}}\" failed.", LocalizationArgs.Of("title", path.Title)));
                _Screen.ShowText(_Screen.Tr("Recovery result: {{title}}", LocalizationArgs.Of("title", path.Title)), ResultText(r));
                AfterChange();
            }, "Recovery failed");
        }

        /// <summary>
        /// Open Ask Armada with the canonical failure prompt in the composer; without an Ask session, copy the prompt.
        /// </summary>
        public void AskArmada()
        {
            if (Entry == null) return;
            string prompt = BlackBoxText.AskPrompt(BlackBoxText.SubjectId(Entry));
            if (_Screen.Context.Ask != null) _Screen.Context.Ask.AskWithPrompt(prompt);
            else _Screen.Context.Clipboard.Copy(prompt, "Ask Armada prompt");
        }

        /// <summary>
        /// Draft an Armada fix mission: open Dispatch prefilled with the top proposed Armada change's mission brief, on the
        /// vessel that holds Armada's own source when one is known (the screen's vessel list, read first when it is not
        /// loaded yet). Without a proposed change a toast says so.
        /// </summary>
        /// <returns>True when a proposed change exists (Dispatch opens once the vessel is known).</returns>
        public bool DraftArmadaFix()
        {
            BlackBoxProposedChange? change = BlackBoxResilienceText.TopChange(Entry);
            if (Entry == null || change == null)
            {
                _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("No Armada change is proposed for this failure."));
                return false;
            }

            if (_Screen.Reference.Loaded.Contains("vessels"))
            {
                OpenDispatch(change, BlackBoxResilienceText.ArmadaVesselId(_Screen.Reference.Vessels));
                return true;
            }

            ArmadaPageQuery query = new ArmadaPageQuery();
            query.PageSize = 1000;
            _Screen.Call((c, t) => c.ListVesselsAsync(query, t), r => OpenDispatch(change, BlackBoxResilienceText.ArmadaVesselId(r?.Objects)), null, ex => OpenDispatch(change, null));
            return true;
        }

        /// <summary>
        /// Show the entry (with its record) as JSON.
        /// </summary>
        public void ShowJson()
        {
            if (Entry == null) return;
            _Screen.ShowJson(_Screen.Tr("Black Box: {{id}}", LocalizationArgs.Of("id", Entry.Id)), Entry);
        }

        /// <summary>
        /// The Resolve dialog (resolution, note, link).
        /// </summary>
        /// <returns>The dialog, or null without an entry.</returns>
        public OpsFormDialog? Resolve()
        {
            if (Entry == null) return null;
            string entryId = Entry.Id;
            OpsFormDialog dialog = _Screen.NewForm("Resolve Failure", "Resolve");
            List<SelectOption<BlackBoxResolutionEnum>> options = new List<SelectOption<BlackBoxResolutionEnum>>();
            foreach (BlackBoxResolutionEnum r in Enum.GetValues(typeof(BlackBoxResolutionEnum)))
            {
                if (r != BlackBoxResolutionEnum.Unknown) options.Add(new SelectOption<BlackBoxResolutionEnum>(r, r.ToString()));
            }

            SelectField<BlackBoxResolutionEnum> resolution = _Screen.NewSelect("Resolution", options);
            resolution.SetValue(Entry.Resolution == BlackBoxResolutionEnum.Open ? BlackBoxResolutionEnum.Resolved : Entry.Resolution);
            OpsTextArea note = new OpsTextArea();
            note.Text = Entry.ResolutionNote ?? "";
            InputField link = new InputField();
            link.Value = Entry.ResolutionLink ?? "";
            link.Placeholder = "msn_... or a URL";
            dialog.AddField("Resolution", resolution);
            dialog.AddField("Note", note, null, 3);
            dialog.AddField("Link", link);
            dialog.Submit = d =>
            {
                BlackBoxResolveRequest request = new BlackBoxResolveRequest();
                request.Resolution = resolution.Value;
                request.Note = String.IsNullOrWhiteSpace(note.Text) ? null : note.Text.Trim();
                request.Link = String.IsNullOrWhiteSpace(link.Value) ? null : link.Value.Trim();
                _Screen.Call((c, t) => c.ResolveBlackBoxAsync(entryId, request, t), e =>
                {
                    d.Complete();
                    _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Black box entry marked {{resolution}}.", LocalizationArgs.Of("resolution", request.Resolution.ToString())));
                    AfterChange();
                }, null, ex => d.Fail(ex, _Screen.Tr("Resolve failed.")));
                return false;
            };
            _Screen.Context.Modals.Show(dialog);
            return dialog;
        }

        /// <summary>
        /// Pin the entry (kept by garbage collection), or unpin a pinned one.
        /// </summary>
        public void TogglePin()
        {
            if (Entry == null) return;
            string entryId = Entry.Id;
            bool pin = !Entry.Pinned;
            _Screen.Call((c, t) => pin ? c.PinBlackBoxAsync(entryId, t) : c.UnpinBlackBoxAsync(entryId, t), e =>
            {
                _Screen.Toast(NotificationSeverityEnum.Success, pin ? _Screen.Tr("Black box entry pinned.") : _Screen.Tr("Black box entry unpinned."));
                AfterChange();
            }, pin ? "Pin failed" : "Unpin failed");
        }

        /// <summary>
        /// The Add Note dialog (author Operator).
        /// </summary>
        /// <returns>The dialog, or null without an entry.</returns>
        public OpsFormDialog? AddNote()
        {
            if (Entry == null) return null;
            string entryId = Entry.Id;
            OpsFormDialog dialog = _Screen.NewForm("Add Note", "Add");
            OpsTextArea body = new OpsTextArea();
            body.Validator = v => String.IsNullOrWhiteSpace(v) ? "A note is required." : null;
            body.ExternalEditor = (text, done) => _Screen.EditExternally(text, done);
            dialog.AddField("Note", body, null, 5);
            dialog.Submit = d =>
            {
                BlackBoxNoteAddRequest request = new BlackBoxNoteAddRequest();
                request.Body = body.Text.Trim();
                request.AuthorKind = BlackBoxNoteAuthorKindEnum.Operator;
                _Screen.Call((c, t) => c.AddBlackBoxNoteAsync(entryId, request, t), n =>
                {
                    d.Complete();
                    _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Note added."));
                    AfterChange();
                }, null, ex => d.Fail(ex, _Screen.Tr("The note could not be added.")));
                return false;
            };
            _Screen.Context.Modals.Show(dialog);
            return dialog;
        }

        /// <summary>
        /// Open the entry's own screen.
        /// </summary>
        public void OpenEntry()
        {
            if (Entry == null) return;
            _Screen.Context.Navigate("/blackbox/" + Uri.EscapeDataString(Entry.Id));
        }

        #endregion

        #region Private-Methods

        private void OpenDispatch(BlackBoxProposedChange change, string? vesselId)
        {
            _Screen.Context.Navigate(OpsHandoff.Dispatch(OpsHandoff.FromBlackBox, vesselId, null, BlackBoxResilienceText.DraftBrief(change), change.Title, null, null));
        }

        private string? ExportTo(string path, string brief)
        {
            try
            {
                string full = _Screen.Context.External.SaveText(path, brief);
                _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Brief saved to {{path}}.", LocalizationArgs.Of("path", full)));
                return full;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                _Screen.Toast(NotificationSeverityEnum.Error, _Screen.Tr("The brief could not be saved: {{message}}", LocalizationArgs.Of("message", ex.Message)));
                return null;
            }
        }

        private void Acknowledge(BlackBoxRecoveryPath path, string token, bool risk)
        {
            if (!risk)
            {
                Execute(path, token, false);
                return;
            }

            _Screen.Confirm("Data Loss Risk", _Screen.Tr("\"{{title}}\" can delete work that is not saved anywhere else. Type {{word}} to run it anyway.", LocalizationArgs.Of("title", path.Title, "word", BlackBoxText.DataLossWord)),
                () => Execute(path, token, true), "Run anyway", BlackBoxText.DataLossWord);
        }

        private string PlanText(BlackBoxRecoveryPath path, BlackBoxRecoveryRunResult result)
        {
            List<string> lines = new List<string>();
            lines.Add(_Screen.Tr("Dry run of \"{{title}}\": nothing has run yet.", LocalizationArgs.Of("title", path.Title)));
            lines.Add("");
            if (!String.IsNullOrEmpty(path.RunbookId))
            {
                lines.Add(_Screen.Tr("Runs runbook {{runbook}} ({{id}}) instead of the steps below.", LocalizationArgs.Of("runbook", RunbookName(path), "id", path.RunbookId)));
                lines.Add("");
            }

            List<BlackBoxRecoveryStep> steps = result.Plan.Count > 0 ? result.Plan : path.Steps;
            foreach (BlackBoxRecoveryStep s in steps.OrderBy(s => s.Order))
            {
                string tool = s.Action != null && !String.IsNullOrEmpty(s.Action.Tool) ? "  [" + s.Action.Tool + "]" : "";
                lines.Add(s.Order.ToString(CultureInfo.InvariantCulture) + ". " + s.Description + tool + (s.DataLossRisk ? "  (" + _Screen.Tr("data loss risk") + ")" : ""));
            }

            if (!String.IsNullOrEmpty(path.ExpectedOutcome))
            {
                lines.Add("");
                lines.Add(_Screen.Tr("Expected: {{outcome}}", LocalizationArgs.Of("outcome", path.ExpectedOutcome)));
            }

            if (!String.IsNullOrEmpty(result.Message))
            {
                lines.Add("");
                lines.Add(result.Message!);
            }

            return String.Join("\n", lines);
        }

        private static string ResultText(BlackBoxRecoveryRunResult result)
        {
            List<string> lines = new List<string>();
            lines.Add(result.Succeeded ? "Succeeded." : "Failed.");
            if (!String.IsNullOrEmpty(result.Message)) lines.Add(result.Message!);
            lines.Add("");
            foreach (BlackBoxRecoveryStepResult s in result.StepResults.OrderBy(s => s.Order))
            {
                lines.Add(s.Order.ToString(CultureInfo.InvariantCulture) + ". " + (s.Succeeded ? "ok" : "failed") + (String.IsNullOrEmpty(s.Tool) ? "" : "  [" + s.Tool + "]") + (String.IsNullOrEmpty(s.Message) ? "" : "  " + s.Message));
            }

            return String.Join("\n", lines);
        }

        private static string RunbookName(BlackBoxRecoveryPath path)
        {
            return String.IsNullOrEmpty(path.RunbookTitle) ? (path.RunbookId ?? "") : path.RunbookTitle!;
        }

        private void AfterChange()
        {
            Reload();
            Changed?.Invoke();
        }

        private OpsDocument Build(OpsDocument doc)
        {
            if (Loading && !Loaded)
            {
                doc.Note("Loading the black box...");
                return doc;
            }

            if (!Loaded) return doc;
            if (LoadError != null)
            {
                doc.Text(LoadError, doc.Theme.Error);
                doc.Note("F5 Retry");
                return doc;
            }

            BlackBoxEntry? e = Entry;
            if (e == null)
            {
                doc.Note(Source == BlackBoxPanelSourceEnum.Voyage ? "No black box entry for this voyage."
                    : Source == BlackBoxPanelSourceEnum.Mission ? "No black box entry for this mission." : "Black box entry not found.");
                doc.Note("The black box records a failure when one happens; nothing has been recorded here.");
                return doc;
            }

            doc.Note("c " + _Screen.Tr("Copy FIX THIS") + "   x " + _Screen.Tr("Export") + "   r " + _Screen.Tr("Recovery") + "   a " + _Screen.Tr("Ask Armada")
                + "   j JSON   s " + _Screen.Tr("Resolve") + "   p " + _Screen.Tr(e.Pinned ? "Unpin" : "Pin") + "   n " + _Screen.Tr("Note") + (OffersOpen ? "   o " + _Screen.Tr("Open entry") : "")
                + (BlackBoxResilienceText.TopChange(e) != null ? "   A " + _Screen.Tr("Draft Armada fix") : ""), doc.Theme.Accent);
            doc.Section("Black Box");
            doc.Field("Entry", e.Id);
            doc.Field("Where", BlackBoxText.Where(e.Phase, e.Step));
            doc.Field("Kind", e.FailureKind?.ToString());
            doc.Field("Status", (e.StatusBefore ?? "-") + " -> " + (e.StatusAfter ?? "-"));
            doc.Field("Triage", e.Triage.ToString() + (BlackBoxResilienceText.IsSuspected(e) ? "  (" + _Screen.Tr("suspected Armada defect") + ")" : ""), BlackBoxResilienceText.IsSuspected(e) ? doc.Theme.Warning : null);
            doc.Field("Resolution", e.Resolution.ToString() + (String.IsNullOrEmpty(e.ResolutionNote) ? "" : "  " + e.ResolutionNote));
            doc.Field("Pinned", _Screen.Tr(e.Pinned ? "Yes" : "No"));
            doc.Field("Attempt", e.Attempt.ToString(CultureInfo.InvariantCulture));
            doc.Field("Captured", _Screen.Context.Loc.FormatDateTime(e.CreatedUtc));
            if (!String.IsNullOrEmpty(e.ReasonExcerpt)) doc.Text(e.ReasonExcerpt, doc.Theme.Error);
            BlackBoxResilienceText.Append(doc, e, BlackBoxResilienceText.TopChange(e) != null ? "A" : null);

            List<BlackBoxRecoveryPath> paths = RecoveryPaths;
            doc.Section("Recovery Paths", "  (r)");
            if (paths.Count == 0) doc.Note("No recovery paths are recorded for this failure.");
            foreach (BlackBoxRecoveryPath p in paths)
            {
                string rate = p.SuccessRate.HasValue ? "  " + _Screen.Tr("{{rate}}% of {{runs}} tracked runs succeeded", LocalizationArgs.Of("rate", Math.Round(p.SuccessRate.Value * 100).ToString(CultureInfo.InvariantCulture), "runs", p.TrackedRuns)) : "";
                doc.Text(p.Rank.ToString(CultureInfo.InvariantCulture) + ". " + p.Title + "  [" + p.Risk + "]" + (p.DataLossRisk ? "  " + _Screen.Tr("data loss risk") : "") + rate, p.DataLossRisk ? doc.Theme.Warning : doc.Theme.Text);
                if (!String.IsNullOrEmpty(p.RunbookId)) doc.Text("   " + _Screen.Tr("Runbook") + ": " + RunbookName(p) + "  (" + p.RunbookId + ")", doc.Theme.Info);
            }

            doc.Section("Brief");
            if (String.IsNullOrEmpty(Brief)) doc.Note("The brief is not available.");
            else doc.Markdown(Brief);
            return doc;
        }

        #endregion
    }
}
