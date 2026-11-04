namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Client;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Captain Selection tab of the mission screen, the TUI counterpart of the Dashboard selection card. It shows
    /// the current attempt (selected captain, intended and runtime-reported model, primary reason), the recovery
    /// permissions, and one of three tables: the comparison of the shown decision, the paged decision history, or a
    /// read-only preview of current selection. While the panel has focus its keys run before the screen's and are
    /// listed in the status bar: <c>c</c> comparison, <c>h</c> history, <c>v</c> preview, <c>&lt;</c>/<c>&gt;</c>
    /// page, <c>f</c> history outcome filter, <c>o</c> open the highlighted history decision, <c>j</c> JSON, <c>a</c>
    /// recovery permissions (administrators, Pending missions), <c>g</c> refresh. Reloads on a matching
    /// <c>captain-selection.changed</c> event. Not thread-safe; use on the UI loop.
    /// </summary>
    public class MissionSelectionPanel : OpsDocumentView, IPanelKeyHandler
    {
        #region Public-Members

        /// <summary>Rows per comparison, history or preview page.</summary>
        public const int PageSize = 10;

        /// <summary>Loaded summary, or null.</summary>
        public CaptainSelectionSummary? Summary { get; private set; } = null;

        /// <summary>Detail of the decision shown in the comparison view, or null.</summary>
        public CaptainSelectionDecisionDetail? Detail { get; private set; } = null;

        /// <summary>Loaded history page, or null.</summary>
        public EnumerationResult<CaptainSelectionDecisionView>? History { get; private set; } = null;

        /// <summary>Loaded preview, or null.</summary>
        public CaptainSelectionPreview? Preview { get; private set; } = null;

        /// <summary>The table currently shown.</summary>
        public MissionSelectionViewEnum View { get; private set; } = MissionSelectionViewEnum.Comparison;

        /// <summary>One-based page of the current table.</summary>
        public int Page { get; private set; } = 1;

        /// <summary>History outcome filter (<c>Admitted</c>, <c>Waiting</c>), or null for all.</summary>
        public string? OutcomeFilter { get; private set; } = null;

        /// <summary>True while a load is in flight.</summary>
        public bool Loading { get; private set; } = false;

        /// <summary>True once a load finished.</summary>
        public bool Loaded { get; private set; } = false;

        /// <summary>Load error (translated), or null.</summary>
        public string? LoadError { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly OpsScreen _Screen;
        private readonly Func<string> _MissionId;
        private readonly Func<string?> _MissionStatus;
        private string? _SelectedDecisionId = null;
        private string? _SelectedTenantId = null;
        private int _HistoryCursor = 0;
        private int _Generation = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="screen">Owning screen (calls, dialogs, toasts).</param>
        /// <param name="missionId">Mission id (read at load time).</param>
        /// <param name="missionStatus">Current mission status, or null while the mission loads.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public MissionSelectionPanel(OpsScreen screen, Func<string> missionId, Func<string?> missionStatus)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            _MissionId = missionId ?? throw new ArgumentNullException(nameof(missionId));
            _MissionStatus = missionStatus ?? throw new ArgumentNullException(nameof(missionStatus));
            Builder = Build;
            Empty = "usageSelection.tuiEmpty";
        }

        #endregion

        #region Public-Methods

        /// <summary>Load unless already loaded or loading.</summary>
        public void Ensure()
        {
            if (Loaded || Loading) return;
            Reload();
        }

        /// <summary>Reload when <paramref name="message"/> is a selection change for this mission.</summary>
        /// <param name="message">Socket message.</param>
        public void OnSocketMessage(ArmadaSocketMessage message)
        {
            if (message == null || !Loaded) return;
            CaptainSelectionChange? change = message.GetData<CaptainSelectionChange>();
            if (change != null && change.MissionId == _MissionId()) Reload();
        }

        /// <summary>Load (or reload) the summary and the current table.</summary>
        public void Reload()
        {
            string id = _MissionId() ?? "";
            if (id.Length == 0) return;
            int generation = ++_Generation;
            Loading = true;
            Invalidate();
            MissionSelectionViewEnum view = View;
            int page = Page;
            string? outcome = OutcomeFilter;
            string? decisionId = _SelectedDecisionId;
            string? tenantId = _SelectedTenantId;
            _Screen.Call(async (c, t) =>
            {
                MissionSelectionLoad load = new MissionSelectionLoad();
                load.Summary = await c.GetCaptainSelectionAsync(id, t).ConfigureAwait(false);
                if (view == MissionSelectionViewEnum.History)
                {
                    CaptainSelectionHistoryQuery query = new CaptainSelectionHistoryQuery { PageNumber = page, PageSize = PageSize, Outcome = outcome };
                    load.History = await c.GetCaptainSelectionHistoryAsync(id, query, t).ConfigureAwait(false);
                }
                else if (view == MissionSelectionViewEnum.Preview)
                {
                    load.Preview = await c.PreviewCaptainSelectionAsync(id, page, PageSize, t).ConfigureAwait(false);
                }
                else
                {
                    CaptainSelectionDecisionView? target = load.Summary?.CurrentAttempt ?? load.Summary?.LatestDecision;
                    string? detailId = decisionId ?? target?.Id;
                    string? detailTenant = decisionId != null ? tenantId : target?.TenantId;
                    if (detailId != null) load.Detail = await c.GetCaptainSelectionDecisionAsync(detailId, detailTenant, page, PageSize, t).ConfigureAwait(false);
                }
                return load;
            }, r =>
            {
                if (generation != _Generation) return;
                Loading = false;
                Loaded = true;
                LoadError = null;
                Summary = r.Summary;
                if (view == MissionSelectionViewEnum.History) History = r.History;
                else if (view == MissionSelectionViewEnum.Preview) Preview = r.Preview;
                else Detail = r.Detail;
                _HistoryCursor = Math.Clamp(_HistoryCursor, 0, Math.Max(0, (History?.Objects.Count ?? 1) - 1));
                Invalidate();
            }, null, ex =>
            {
                if (generation != _Generation) return;
                Loading = false;
                Loaded = true;
                // Authorization loss must not leave previously visible evidence on screen.
                if (ex is ArmadaApiException api && (api.StatusCode == 401 || api.StatusCode == 403 || api.StatusCode == 404))
                {
                    Summary = null;
                    Detail = null;
                    History = null;
                    Preview = null;
                }

                LoadError = _Screen.Tr("usageSelection.historyLoadFailed");
                Invalidate();
            });
        }

        /// <summary>Switch the table, starting at page one.</summary>
        /// <param name="view">Table to show.</param>
        public void ShowView(MissionSelectionViewEnum view)
        {
            if (view == MissionSelectionViewEnum.Comparison && View == MissionSelectionViewEnum.Comparison)
            {
                _SelectedDecisionId = null;
                _SelectedTenantId = null;
            }

            View = view;
            Page = 1;
            _HistoryCursor = 0;
            Reload();
        }

        /// <summary>Move to another page of the current table when it exists.</summary>
        /// <param name="delta">-1 or +1.</param>
        public void Turn(int delta)
        {
            int pages = TotalPages();
            int next = Math.Clamp(Page + delta, 1, Math.Max(1, pages));
            if (next == Page) return;
            Page = next;
            _HistoryCursor = 0;
            Reload();
        }

        /// <summary>Cycle the history outcome filter: all, Admitted, Waiting.</summary>
        public void CycleOutcomeFilter()
        {
            OutcomeFilter = OutcomeFilter == null ? CaptainSelectionOutcomes.Admitted
                : OutcomeFilter == CaptainSelectionOutcomes.Admitted ? CaptainSelectionOutcomes.Waiting : null;
            View = MissionSelectionViewEnum.History;
            Page = 1;
            _HistoryCursor = 0;
            Reload();
        }

        /// <summary>Open the highlighted history decision in the comparison view.</summary>
        public void OpenHighlighted()
        {
            if (View != MissionSelectionViewEnum.History || History == null || History.Objects.Count == 0) return;
            CaptainSelectionDecisionView row = History.Objects[Math.Clamp(_HistoryCursor, 0, History.Objects.Count - 1)];
            _SelectedDecisionId = row.Id;
            _SelectedTenantId = row.TenantId;
            View = MissionSelectionViewEnum.Comparison;
            Page = 1;
            Reload();
        }

        /// <summary>Show the loaded data of the current table as JSON.</summary>
        public void ShowJson()
        {
            object? data = View == MissionSelectionViewEnum.History ? History
                : View == MissionSelectionViewEnum.Preview ? Preview
                : (object?)Detail ?? Summary;
            if (data == null) return;
            _Screen.ShowJson(_Screen.Tr("usageSelection.inspectionView"), data);
        }

        /// <summary>Open the recovery permissions form (administrators, Pending missions).</summary>
        public void EditRecovery()
        {
            CaptainSelectionSummary? summary = Summary;
            if (summary == null || !summary.CanManageRecovery || _MissionStatus() != MissionStatusEnum.Pending.ToString())
            {
                _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("usageSelection.recovery.unavailable"));
                return;
            }

            UsageSelectionRecovery? current = summary.Recovery;
            string missionId = _MissionId();
            OpsFormDialog dialog = _Screen.NewForm("usageSelection.recovery.title", "usageSelection.recovery.save");
            dialog.WidthRatio = 0.7;
            // Help text goes in the wrapped notes; single-line field hints would truncate it.
            dialog.Notes.Add(_Screen.Tr("usageSelection.recovery.scope"));
            dialog.Notes.Add(_Screen.Tr("usageSelection.recovery.fallback") + ": " + _Screen.Tr("usageSelection.recovery.fallbackHelp"));
            dialog.Notes.Add(_Screen.Tr("usageSelection.recovery.reserve") + ": " + _Screen.Tr("usageSelection.recovery.reserveHelp"));
            dialog.Notes.Add(_Screen.Tr("usageSelection.recovery.audit"));
            ToggleField fallback = dialog.AddField("usageSelection.recovery.permissions", new ToggleField(current?.AllowFallback ?? false, _Screen.Tr("usageSelection.recovery.fallback")));
            ToggleField reserve = dialog.AddField("", new ToggleField(current?.ReserveException ?? false, _Screen.Tr("usageSelection.recovery.reserve")));
            TextAreaField reason = new TextAreaField("");
            reason.Validator = value => String.IsNullOrWhiteSpace(value) ? "This field is required."
                : value.Trim().Length > 512 ? "Enter at most 512 characters." : null;
            dialog.AddField("usageSelection.recovery.reason", reason, null, 4);
            dialog.Submit = d =>
            {
                UsageSelectionRecoveryRequest request = new UsageSelectionRecoveryRequest
                {
                    ExpectedRevision = current?.Revision ?? 0,
                    AllowFallback = fallback.Value,
                    ReserveException = reserve.Value,
                    Reason = reason.Value.Trim()
                };
                _Screen.Call((c, t) => c.AuthorizeCaptainSelectionRecoveryAsync(missionId, request, t), r =>
                {
                    d.Complete();
                    _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("usageSelection.recovery.record", LocalizationArgs.Of("revision", r?.Revision ?? 0)));
                    Reload();
                }, null, ex =>
                {
                    // The draft reason stays in the form; a conflict means another administrator changed the record.
                    bool conflict = ex is ArmadaApiException api && api.StatusCode == 409;
                    d.Fail(conflict ? _Screen.Tr("usageSelection.recovery.conflict") : OpsFormDialog.ServerMessage(ex) ?? _Screen.Tr("usageSelection.recovery.saveFailed"));
                    if (conflict) Reload();
                });
                return false;
            };
            _Screen.Context.Modals.Show(dialog);
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyHint>? GetKeyHints()
        {
            IReadOnlyList<KeyHint>? search = base.GetKeyHints();
            if (search != null) return search;
            List<KeyHint> hints = new List<KeyHint>
            {
                new KeyHint("c", "usageSelection.compare"),
                new KeyHint("h", "usageSelection.history"),
                new KeyHint("v", "usageSelection.preview")
            };
            if (TotalPages() > 1) hints.Add(new KeyHint("< >", "Page"));
            if (View == MissionSelectionViewEnum.History)
            {
                hints.Add(new KeyHint("f", "usageSelection.historyFilter"));
                if (History != null && History.Objects.Count > 0)
                {
                    hints.Add(new KeyHint("Up/Down", "Select"));
                    hints.Add(new KeyHint("o", "Open"));
                }
            }

            hints.Add(new KeyHint("j", "JSON"));
            if (Summary?.CanManageRecovery == true && _MissionStatus() == MissionStatusEnum.Pending.ToString())
                hints.Add(new KeyHint("a", "usageSelection.recovery.title"));
            hints.Add(new KeyHint("g", "Refresh"));
            hints.Add(new KeyHint("[ ]", "Panels"));
            return hints;
        }

        /// <inheritdoc />
        public bool HandlePanelKey(KeyEvent key)
        {
            if (Searching) return false;
            if (View == MissionSelectionViewEnum.History && key.Modifiers == KeyModifiers.None && History != null && History.Objects.Count > 0)
            {
                if (key.Code == KeyCode.Up) { MoveCursor(-1); return true; }
                if (key.Code == KeyCode.Down) { MoveCursor(1); return true; }
                if (key.Code == KeyCode.Enter) { OpenHighlighted(); return true; }
            }

            if (key.Code != KeyCode.Character) return false;
            if (key.Modifiers != KeyModifiers.None && key.Modifiers != KeyModifiers.Shift) return false;
            switch (key.Rune)
            {
                case 'c': ShowView(MissionSelectionViewEnum.Comparison); return true;
                case 'h': ShowView(MissionSelectionViewEnum.History); return true;
                case 'v': ShowView(MissionSelectionViewEnum.Preview); return true;
                case '<': Turn(-1); return true;
                case '>': Turn(1); return true;
                case 'f': CycleOutcomeFilter(); return true;
                case 'o':
                    if (View != MissionSelectionViewEnum.History) return false;
                    OpenHighlighted();
                    return true;
                case 'j': ShowJson(); return true;
                case 'a': EditRecovery(); return true;
                case 'g': Reload(); return true;
                default: return false;
            }
        }

        #endregion

        #region Private-Methods

        private void MoveCursor(int delta)
        {
            int count = History?.Objects.Count ?? 0;
            if (count == 0) return;
            _HistoryCursor = Math.Clamp(_HistoryCursor + delta, 0, count - 1);
            Invalidate();
        }

        private int TotalPages()
        {
            if (View == MissionSelectionViewEnum.History) return History?.TotalPages ?? 1;
            if (View == MissionSelectionViewEnum.Preview) return Preview?.Candidates?.TotalPages ?? 1;
            return Detail?.Candidates?.TotalPages ?? 1;
        }

        private OpsDocument Build(OpsDocument doc)
        {
            if (Loading && !Loaded) { doc.Note("Loading..."); return doc; }
            if (LoadError != null)
            {
                doc.Text(LoadError, doc.Theme.Error);
                doc.Note("g " + _Screen.Tr("Refresh"));
            }

            CaptainSelectionSummary? summary = Summary;
            if (summary == null) return doc;
            doc.LabelWidth = 26;
            doc.Note(KeyLine(), doc.Theme.Accent);

            doc.Section("usageSelection.title");
            doc.Field("usageSelection.mode", _Screen.Tr(ModeKey(summary.CurrentMode)));
            doc.Field("usageSelection.strategy", _Screen.Tr(StrategyKey(summary.CurrentStrategy)));
            CaptainSelectionDecisionView? shown = View == MissionSelectionViewEnum.Comparison && Detail != null ? Detail.Decision
                : summary.CurrentAttempt ?? summary.LatestDecision;
            if (shown == null) doc.Note("usageSelection.noHistory");
            else
            {
                if (summary.CurrentAttempt == null || shown.Id != summary.CurrentAttempt.Id) doc.Note("usageSelection.latestRetained");
                DecisionFields(doc, shown);
            }

            RecoveryFields(doc, summary.Recovery);
            if (View == MissionSelectionViewEnum.History) HistoryTable(doc);
            else if (View == MissionSelectionViewEnum.Preview) PreviewTable(doc);
            else ComparisonTable(doc);
            return doc;
        }

        private void DecisionFields(OpsDocument doc, CaptainSelectionDecisionView decision)
        {
            string captain = decision.SelectedCaptainName ?? decision.SelectedCaptainId ?? _Screen.Tr("usageSelection.notSelected");
            if (decision.SelectedCaptainId != null && !decision.SelectedCaptainAvailable) captain += "  (" + _Screen.Tr("usageSelection.captainDeleted") + ")";
            doc.Field("usageSelection.selectedCaptain", captain);
            doc.Field("usageSelection.intendedModel", decision.IntendedModel ?? _Screen.Tr("Unknown"));
            doc.Field("usageSelection.reportedModel", decision.ReportedModel ?? _Screen.Tr("Unknown"));
            doc.Field("Outcome", _Screen.Tr(OutcomeKey(decision.Outcome)));
            doc.Field("Reason", _Screen.Tr(Armada.Core.Services.UsageSelectionText.ReasonKey(decision.ReasonCode)));
            doc.Field("Evaluated", _Screen.Context.Loc.FormatDateTime(decision.EvaluatedUtc));
            if (decision.NextReconsiderUtc.HasValue)
                doc.Text(_Screen.Tr("usageSelection.reconsiderAt", LocalizationArgs.Of("time", _Screen.Context.Loc.FormatDateTime(decision.NextReconsiderUtc.Value))), doc.Theme.Muted);
            if (decision.Mode == UsageSelectionModeEnum.Observe && decision.SelectedCaptainId != null) doc.Text(_Screen.Tr("usageSelection.observeChoice"), doc.Theme.Muted);
        }

        private void RecoveryFields(OpsDocument doc, UsageSelectionRecovery? recovery)
        {
            if (recovery == null) return;
            doc.Section("usageSelection.recovery.title", "  " + _Screen.Tr("usageSelection.recovery.record", LocalizationArgs.Of("revision", recovery.Revision)));
            doc.Field("usageSelection.recovery.permissions", _Screen.Tr(recovery.AllowFallback ? "usageSelection.recovery.fallbackOn" : "usageSelection.recovery.fallbackOff")
                + "  |  " + _Screen.Tr(recovery.ReserveException ? "usageSelection.recovery.reserveOn" : "usageSelection.recovery.reserveOff"));
            doc.Field("usageSelection.recovery.reason", recovery.Reason);
            doc.Field("Updated", _Screen.Context.Loc.FormatDateTime(recovery.CreatedUtc));
        }

        private void ComparisonTable(OpsDocument doc)
        {
            doc.Section("usageSelection.compare", PageSuffix(Detail?.Candidates));
            List<CaptainSelectionCandidateView> rows = Detail?.Candidates?.Objects ?? new List<CaptainSelectionCandidateView>();
            if (rows.Count == 0) { doc.Note("usageSelection.noCandidates"); return; }
            CandidateTable(doc, rows);
            List<CaptainSelectionOmittedReason> omitted = Detail?.OmittedCandidates ?? new List<CaptainSelectionOmittedReason>();
            if (omitted.Count == 0) return;
            doc.Blank().Text(_Screen.Tr("usageSelection.omitted", LocalizationArgs.Of("count", omitted.Sum(row => row.Count))), doc.Theme.Accent.WithAttribute(CellAttributes.Bold, true));
            foreach (CaptainSelectionOmittedReason row in omitted)
                doc.Field(row.Count.ToString(CultureInfo.InvariantCulture), _Screen.Tr(Armada.Core.Services.UsageSelectionText.ReasonKey(row.ReasonCode)));
            doc.Note("usageSelection.omittedHint");
        }

        private void PreviewTable(OpsDocument doc)
        {
            doc.Section("usageSelection.preview", PageSuffix(Preview?.Candidates));
            if (Preview == null) return;
            doc.Text(_Screen.Tr(Armada.Core.Services.UsageSelectionText.ReasonKey(Preview.ReasonCode)), doc.Theme.Muted);
            List<CaptainSelectionCandidateView> rows = Preview.Candidates?.Objects ?? new List<CaptainSelectionCandidateView>();
            if (rows.Count == 0) { doc.Note("usageSelection.noCandidates"); return; }
            CandidateTable(doc, rows);
        }

        private void CandidateTable(OpsDocument doc, List<CaptainSelectionCandidateView> rows)
        {
            List<IList<string>> body = rows.Select(row => (IList<string>)new List<string>
            {
                row.Rank.ToString(CultureInfo.InvariantCulture),
                row.CaptainName + (row.Model == null ? "" : " / " + row.Model),
                _Screen.Tr(row.ActualSelection ? "usageSelection.selected" : row.HypotheticalSelection ? "usageSelection.proposed"
                    : row.CanSelect ? "usageSelection.eligible" : row.Band == UsageSelectionBandEnum.Unknown ? "usageSelection.unknownState" : "usageSelection.blocked"),
                row.AvailablePercent.HasValue ? Percent(row.AvailablePercent.Value) : "-"
            }).ToList();
            List<string?> details = rows.Select(row => (string?)_Screen.Tr(Armada.Core.Services.UsageSelectionText.ReasonKey(row.ReasonCode))).ToList();
            doc.TableWithDetails(new List<string> { "#", "Captain", "State", "usageSelection.headroom" }, body, details, 48);
        }

        private void HistoryTable(OpsDocument doc)
        {
            string filter = OutcomeFilter == null ? _Screen.Tr("All") : _Screen.Tr(OutcomeKey(OutcomeFilter));
            doc.Section("usageSelection.history", "  " + filter + PageSuffix(History));
            List<CaptainSelectionDecisionView> rows = History?.Objects ?? new List<CaptainSelectionDecisionView>();
            if (rows.Count == 0) { doc.Note(OutcomeFilter == null ? "usageSelection.noHistory" : "usageSelection.noMatchingHistory"); return; }
            List<IList<string>> body = new List<IList<string>>();
            List<string?> details = new List<string?>();
            for (int index = 0; index < rows.Count; index++)
            {
                CaptainSelectionDecisionView row = rows[index];
                body.Add(new List<string>
                {
                    (index == _HistoryCursor ? "> " : "  ") + _Screen.Context.Loc.FormatDateTime(row.EvaluatedUtc),
                    _Screen.Tr(OutcomeKey(row.Outcome)) + (row.Trigger == CaptainSelectionTriggers.Redispatch ? " (" + _Screen.Tr("usageSelection.redispatch") + ")" : ""),
                    row.SelectedCaptainName ?? row.SelectedCaptainId ?? "-"
                });
                details.Add(_Screen.Tr(Armada.Core.Services.UsageSelectionText.ReasonKey(row.ReasonCode)));
            }

            doc.TableWithDetails(new List<string> { "Evaluated", "Outcome", "Captain" }, body, details, 48);
        }

        private string PageSuffix<T>(EnumerationResult<T>? page)
        {
            if (page == null || page.TotalPages <= 1) return "";
            return "  " + _Screen.Tr("usageSelection.page", LocalizationArgs.Of("page", page.PageNumber, "pages", page.TotalPages));
        }

        // The status bar truncates on narrow terminals, so the panel repeats its own keys inline (see GetKeyHints).
        private string KeyLine()
        {
            List<string> parts = new List<string>
            {
                "c " + _Screen.Tr("usageSelection.compare"),
                "h " + _Screen.Tr("usageSelection.history"),
                "v " + _Screen.Tr("usageSelection.preview")
            };
            if (TotalPages() > 1) parts.Add("< > " + _Screen.Tr("Page"));
            if (View == MissionSelectionViewEnum.History)
            {
                parts.Add("f " + _Screen.Tr("usageSelection.historyFilter"));
                if (History != null && History.Objects.Count > 0) parts.Add("Enter " + _Screen.Tr("Open"));
            }

            parts.Add("j JSON");
            if (Summary?.CanManageRecovery == true && _MissionStatus() == MissionStatusEnum.Pending.ToString()) parts.Add("a " + _Screen.Tr("usageSelection.recovery.title"));
            parts.Add("g " + _Screen.Tr("Refresh"));
            return String.Join("   ", parts);
        }

        private string Percent(double value)
            => (value / 100).ToString("P1", _Screen.Context.Loc.Culture);

        private static string ModeKey(UsageSelectionModeEnum mode) => mode switch
        {
            UsageSelectionModeEnum.Disabled => "usageSelection.disabled",
            UsageSelectionModeEnum.Enforce => "usageSelection.enforce",
            _ => "usageSelection.observe"
        };

        private static string StrategyKey(CaptainSelectionStrategyEnum strategy) => strategy switch
        {
            CaptainSelectionStrategyEnum.MostRemainingAllowance => "usageSelection.mostRemaining",
            CaptainSelectionStrategyEnum.MostUsedAllowance => "usageSelection.mostUsed",
            _ => "usageSelection.firstAvailable"
        };

        private static string OutcomeKey(string? outcome) => outcome switch
        {
            CaptainSelectionOutcomes.Admitted => "usageSelection.admitted",
            CaptainSelectionOutcomes.Waiting => "usageSelection.waiting",
            _ => outcome ?? "Unknown"
        };

        #endregion
    }
}
