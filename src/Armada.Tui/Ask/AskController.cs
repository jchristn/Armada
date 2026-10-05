namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Services;

    /// <summary>
    /// The Ask Armada session (W2): owns the thread list, the open conversation, the captain and quick-action catalogs,
    /// and every Ask API call and socket event, so the Ask screen, the Ask dock, and the Approvals center are views over
    /// one state that survives navigation. Ported from the dashboard's <c>pages/AskArmada.tsx</c> (same calls, page sizes,
    /// debounce, read-marking, reconciliation refetches, stop grace period, and error texts). Pending proposals from any
    /// thread feed <see cref="ApprovalService"/>. Public members are called on the UI loop; HTTP work runs on the thread
    /// pool and posts results back through the dispatcher.
    /// </summary>
    public class AskController
    {
        #region Public-Members

        /// <summary>
        /// Threads per page (50, like the dashboard).
        /// </summary>
        public const int ThreadPageSize = 50;

        /// <summary>
        /// Messages per page (30, like the dashboard).
        /// </summary>
        public const int MessagePageSize = 30;

        /// <summary>
        /// Snapshot fetches per thread load (20, like the dashboard).
        /// </summary>
        public const int SnapshotFetchLimit = 20;

        /// <summary>
        /// Services.
        /// </summary>
        public TuiContext Context { get; }

        /// <summary>
        /// The open conversation. Never null.
        /// </summary>
        public AskConversation Conversation { get; } = new AskConversation();

        /// <summary>
        /// Captains (for the picker and message names). Never null.
        /// </summary>
        public List<Captain> Captains { get; private set; } = new List<Captain>();

        /// <summary>
        /// Captain for a conversation that does not exist yet (remembered like the dashboard's <c>armada_ask_captain</c>).
        /// </summary>
        public string DraftCaptainId { get; private set; } = "";

        /// <summary>
        /// Quick actions (server catalog merged with the built-ins). Never null.
        /// </summary>
        public List<AskQuickAction> QuickActions { get; private set; } = AskQuickActions.Defaults();

        /// <summary>
        /// Tool access of the active captain, or null while unknown.
        /// </summary>
        public CaptainToolAccessResult? Tools { get; private set; } = null;

        /// <summary>
        /// Visible threads (pinned first, then most recent). Never null.
        /// </summary>
        public List<AskThread> Threads { get; private set; } = new List<AskThread>();

        /// <summary>
        /// The thread list is loading.
        /// </summary>
        public bool ListLoading { get; private set; } = false;

        /// <summary>
        /// Thread list error, or null.
        /// </summary>
        public string? ListError { get; private set; } = null;

        /// <summary>
        /// Search text as typed.
        /// </summary>
        public string Search { get; private set; } = "";

        /// <summary>
        /// Search text in effect (after the 300 ms debounce).
        /// </summary>
        public string Query { get; private set; } = "";

        /// <summary>
        /// Show archived threads.
        /// </summary>
        public bool IncludeArchived { get; private set; } = false;

        /// <summary>
        /// Last loaded thread page.
        /// </summary>
        public int ListPage { get; private set; } = 1;

        /// <summary>
        /// More thread pages exist.
        /// </summary>
        public bool ListHasMore { get; private set; } = false;

        /// <summary>
        /// Live per-thread activity. Never null.
        /// </summary>
        public Dictionary<string, AskThreadActivity> Activity { get; } = new Dictionary<string, AskThreadActivity>(StringComparer.Ordinal);

        /// <summary>
        /// The conversation is loading.
        /// </summary>
        public bool ConvLoading { get; private set; } = false;

        /// <summary>
        /// Conversation load error, or null.
        /// </summary>
        public string? ConvError { get; private set; } = null;

        /// <summary>
        /// Earlier messages are loading.
        /// </summary>
        public bool LoadingOlder { get; private set; } = false;

        /// <summary>
        /// Proposal whose approve or reject call is in flight, or null.
        /// </summary>
        public string? BusyProposalId { get; private set; } = null;

        /// <summary>
        /// A quick action is running.
        /// </summary>
        public bool ActionBusy { get; private set; } = false;

        /// <summary>
        /// A stop was requested for the running turn.
        /// </summary>
        public bool Stopping
        {
            get { return _Stopping && Conversation.TurnActive; }
        }

        /// <summary>
        /// Ask the captain to include its reasoning (persisted like <c>armada_ask_show_thinking</c>).
        /// </summary>
        public bool ShowThinking
        {
            get { return Context.Prefs.Current.AskShowThinking; }
        }

        /// <summary>
        /// The greeting shown on an empty new conversation.
        /// </summary>
        public string Greeting { get; private set; } = AskPhrases.RandomGreeting();

        /// <summary>
        /// Composer text kept across screen instances (and pre-filled by "Ask about this").
        /// </summary>
        public string ComposerDraft { get; set; } = "";

        /// <summary>
        /// Messages sent in this session, oldest first (composer history recall). Never null.
        /// </summary>
        public List<string> SentHistory { get; } = new List<string>();

        /// <summary>
        /// True while the Ask screen is the current screen (read-marking only happens while viewing).
        /// </summary>
        public bool Viewing { get; set; } = false;

        /// <summary>
        /// Grace period before a stopped turn is force-ended locally, in milliseconds. Default 8000; clamped to
        /// 0..60000.
        /// </summary>
        public int ForceEndMs
        {
            get { return _ForceEndMs; }
            set { _ForceEndMs = Math.Clamp(value, 0, 60000); }
        }

        /// <summary>
        /// Search debounce in milliseconds. Default 300; clamped to 0..5000.
        /// </summary>
        public int SearchDebounceMs
        {
            get { return _SearchDebounceMs; }
            set { _SearchDebounceMs = Math.Clamp(value, 0, 5000); }
        }

        /// <summary>
        /// The captain of the open conversation, or the draft captain for a new one (empty for none).
        /// </summary>
        public string ActiveCaptainId
        {
            get { return Conversation.Thread != null ? (Conversation.Thread.CaptainId ?? "") : DraftCaptainId; }
        }

        /// <summary>
        /// The active captain, or null.
        /// </summary>
        public Captain? ActiveCaptain
        {
            get { return Captains.FirstOrDefault(c => c.Id == ActiveCaptainId); }
        }

        /// <summary>
        /// No captain is selected: plain messages cannot be sent, quick actions still work.
        /// </summary>
        public bool NoCaptain
        {
            get { return String.IsNullOrEmpty(ActiveCaptainId); }
        }

        /// <summary>
        /// The captain cannot reach Armada over MCP (it can answer but not propose actions): it reports no Armada tools
        /// and its runtime is neither ApiEndpoint nor ClaudeCode (the server connects those itself).
        /// </summary>
        public bool McpMissing
        {
            get
            {
                if (NoCaptain || Tools == null || Tools.CaptainId != ActiveCaptainId) return false;
                bool serverProvides = Tools.Runtime == "ApiEndpoint" || Tools.Runtime == "ClaudeCode";
                return Tools.ArmadaToolCount <= 0 && !serverProvides;
            }
        }

        /// <summary>
        /// The rotating waiting phrase while a turn has produced no text yet (changes every 4 seconds), or empty.
        /// </summary>
        public string WaitingText
        {
            get
            {
                if (!Conversation.TurnActive) return "";
                DateTime now = Context.Clock.UtcNow;
                if (now >= _WaitingNextUtc || _Waiting.Length == 0)
                {
                    _Waiting = AskPhrases.RandomThinking(_Waiting);
                    _WaitingNextUtc = now.AddSeconds(4);
                }

                return _Waiting;
            }
        }

        /// <summary>
        /// Raised on the UI loop when the composer draft is replaced from outside the composer (Ask about this).
        /// </summary>
        public event EventHandler<string>? DraftChanged;

        /// <summary>
        /// Raised on the UI loop when a thread is created by a send or quick action (views follow it).
        /// </summary>
        public event EventHandler<string>? ThreadCreated;

        #endregion

        #region Private-Members

        private const string ArmadaSocketEventsCaptain = "captain.changed";
        private readonly Dictionary<string, CaptainToolAccessResult> _ToolsCache = new Dictionary<string, CaptainToolAccessResult>(StringComparer.Ordinal);
        private string? _ToolsRequestedFor = null;
        private bool _Stopping = false;
        private int _ForceEndMs = 8000;
        private int _SearchDebounceMs = 300;
        private int _SearchGeneration = 0;
        private string _Waiting = "";
        private DateTime _WaitingNextUtc = DateTime.MinValue;
        private bool _Started = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and subscribe to the Ask socket events, reconnects, and session changes.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public AskController(TuiContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            DraftCaptainId = context.Prefs.Current.AskDraftCaptainId ?? "";
            context.Events.Subscribe("ask.", message =>
            {
                AskEvent? e = AskEventParser.Parse(message);
                if (e != null) HandleEvent(e);
            });
            context.Events.Reconnected += (s, e) => HandleReconnect();
            context.Events.SubscribeCoalesced(ArmadaSocketEventsCaptain, () => { if (_Started) LoadCaptains(); });
            context.Session.SignedIn += (s, e) => Start();
            context.Session.SignedOut += (s, e) => Clear();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Instructions page for connecting a captain runtime to Armada over MCP (the dashboard's
        /// <c>instructionsDocUrl</c>).
        /// </summary>
        /// <param name="runtime">Runtime name, or null.</param>
        /// <returns>URL.</returns>
        public static string InstructionsUrl(string? runtime)
        {
            string file;
            switch (runtime)
            {
                case "ClaudeCode": file = "INSTRUCTIONS_FOR_CLAUDE_CODE.md"; break;
                case "Codex": file = "INSTRUCTIONS_FOR_CODEX.md"; break;
                case "Cursor": file = "INSTRUCTIONS_FOR_CURSOR.md"; break;
                case "Gemini": file = "INSTRUCTIONS_FOR_GEMINI.md"; break;
                case "Mux": file = "INSTRUCTIONS_FOR_MUX.md"; break;
                case "OpenCode": file = "INSTRUCTIONS_FOR_OPENCODE.md"; break;
                default: file = "MCP_API.md"; break;
            }

            return "https://github.com/jchristn/Armada/blob/main/docs/" + file;
        }

        /// <summary>
        /// Load the catalogs and the first thread page (called on sign-in).
        /// </summary>
        public void Start()
        {
            _Started = true;
            LoadCaptains();
            LoadQuickActions();
            LoadThreads(1);
        }

        /// <summary>
        /// Reload the captains, quick actions, thread list, and the open conversation (F5).
        /// </summary>
        public void Refresh()
        {
            _Started = true;
            LoadCaptains();
            LoadQuickActions();
            LoadThreads(1);
            if (Conversation.ThreadId != null) LoadConversation(Conversation.ThreadId, true);
        }

        /// <summary>
        /// Forget everything (called on sign-out).
        /// </summary>
        public void Clear()
        {
            _Started = false;
            Conversation.Reset(null);
            Threads = new List<AskThread>();
            Activity.Clear();
            Captains = new List<Captain>();
            Tools = null;
            _ToolsCache.Clear();
            _ToolsRequestedFor = null;
            ListError = null;
            ConvError = null;
            ComposerDraft = "";
            _Stopping = false;
        }

        /// <summary>
        /// Open a thread (or a new conversation for null), as the route <c>/ask/:threadId?</c> changes. Reopening the
        /// thread that is already open reloads it silently and keeps its state.
        /// </summary>
        /// <param name="threadId">Thread id, or null.</param>
        public void Open(string? threadId)
        {
            if (!_Started && Context.Session.IsSignedIn) Start();
            bool same = Conversation.ThreadId == threadId;
            if (!same)
            {
                Conversation.Reset(threadId);
                _Stopping = false;
                Greeting = AskPhrases.RandomGreeting();
            }

            ConvError = null;
            if (threadId != null)
            {
                Context.Prefs.Current.LastAskThreadId = threadId;
                LoadConversation(threadId, same && Conversation.Thread != null);
            }
            else
            {
                ConvLoading = false;
            }

            EnsureTools();
        }

        /// <summary>
        /// Fetch the active captain's tool access when it changed (cached per captain).
        /// </summary>
        public void EnsureTools()
        {
            string id = ActiveCaptainId;
            if (String.IsNullOrEmpty(id))
            {
                Tools = null;
                _ToolsRequestedFor = null;
                return;
            }

            if (_ToolsCache.TryGetValue(id, out CaptainToolAccessResult? cached))
            {
                Tools = cached;
                return;
            }

            if (_ToolsRequestedFor == id) return;
            _ToolsRequestedFor = id;
            Tools = null;
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    CaptainToolAccessResult? result = await client.GetCaptainToolsAsync(id).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        if (result == null) return;
                        if (String.IsNullOrEmpty(result.CaptainId)) result.CaptainId = id;
                        _ToolsCache[id] = result;
                        if (ActiveCaptainId == id) Tools = result;
                    });
                }
                catch (ArmadaApiException)
                {
                    Context.Dispatcher.Post(() => { if (_ToolsRequestedFor == id) _ToolsRequestedFor = null; });
                }
            });
        }

        /// <summary>
        /// Change the search text; the list reloads after the debounce.
        /// </summary>
        /// <param name="text">Search text.</param>
        public void SetSearch(string text)
        {
            Search = text ?? "";
            int generation = ++_SearchGeneration;
            _ = Task.Delay(_SearchDebounceMs).ContinueWith(_ => Context.Dispatcher.Post(() =>
            {
                if (generation != _SearchGeneration) return;
                string q = Search.Trim();
                if (q == Query) return;
                Query = q;
                LoadThreads(1);
            }), TaskScheduler.Default);
        }

        /// <summary>
        /// Show or hide archived threads (reloads the list).
        /// </summary>
        /// <param name="value">Show archived.</param>
        public void SetIncludeArchived(bool value)
        {
            if (IncludeArchived == value) return;
            IncludeArchived = value;
            LoadThreads(1);
        }

        /// <summary>
        /// Load a page of threads (page 1 replaces the list; later pages merge).
        /// </summary>
        /// <param name="page">Page number.</param>
        public void LoadThreads(int page)
        {
            ListLoading = true;
            ListError = null;
            AskThreadEnumerateQuery query = new AskThreadEnumerateQuery();
            query.PageNumber = Math.Max(1, page);
            query.PageSize = ThreadPageSize;
            query.Search = Query.Length > 0 ? Query : null;
            query.IncludeArchived = IncludeArchived;
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    EnumerationResult<AskThread>? result = await client.EnumerateAskThreadsAsync(query).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        ListLoading = false;
                        string? openId = Conversation.ThreadId;
                        List<AskThread> incoming = result?.Objects ?? new List<AskThread>();
                        foreach (AskThread t in incoming)
                        {
                            if (t.Id == openId) t.UnreadCount = 0;
                        }

                        if (query.PageNumber == 1) Threads = incoming;
                        else Threads = AskThreadListLogic.Sort(Threads.Where(p => !incoming.Any(i => i.Id == p.Id)).Concat(incoming));
                        ListPage = query.PageNumber;
                        ListHasMore = query.PageNumber < Math.Max(1, result?.TotalPages ?? 1);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        ListLoading = false;
                        ListError = String.IsNullOrEmpty(ex.Message) ? "Failed to load conversations." : ex.Message;
                    });
                }
            });
        }

        /// <summary>
        /// Load the next thread page.
        /// </summary>
        public void LoadMore()
        {
            if (ListHasMore && !ListLoading) LoadThreads(ListPage + 1);
        }

        /// <summary>
        /// Load a conversation (thread detail and the newest page), mark it read, and fetch missing snapshots.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="silent">Keep the current content while loading.</param>
        public void LoadConversation(string threadId, bool silent)
        {
            if (!silent) ConvLoading = true;
            ConvError = null;
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    AskMessageEnumerateQuery q = new AskMessageEnumerateQuery();
                    q.PageSize = MessagePageSize;
                    Task<AskThreadDetail?> detailTask = client.GetAskThreadAsync(threadId);
                    Task<AskMessagePage?> pageTask = client.EnumerateAskMessagesAsync(threadId, q);
                    await Task.WhenAll(detailTask, pageTask).ConfigureAwait(false);
                    AskThreadDetail? detail = detailTask.Result;
                    AskMessagePage? page = pageTask.Result;
                    Context.Dispatcher.Post(() =>
                    {
                        if (Conversation.ThreadId != threadId) return;
                        ConvLoading = false;
                        Conversation.Loaded(threadId, detail, page?.Messages ?? new List<AskMessage>(), page?.HasMore ?? false, Context.Clock.UtcNow);
                        if (detail?.Thread != null) Threads = AskThreadListLogic.ApplyUpdate(Threads, detail.Thread, Query, IncludeArchived, threadId);
                        foreach (AskActionProposal p in Conversation.PendingProposals()) TrackProposal(p, false);
                        MarkRead(threadId);
                        FetchSnapshots(threadId, detail?.TrackedWork ?? new List<AskTrackedWork>());
                        EnsureTools();
                    });
                }
                catch (Exception ex) when (ex is ArmadaApiException || ex is AggregateException)
                {
                    ArmadaApiException? api = ex as ArmadaApiException ?? (ex as AggregateException)?.InnerExceptions.OfType<ArmadaApiException>().FirstOrDefault();
                    Context.Dispatcher.Post(() =>
                    {
                        if (Conversation.ThreadId != threadId) return;
                        ConvLoading = false;
                        if (api != null && api.StatusCode == 404) ConvError = "This conversation was not found. It may have been deleted.";
                        else ConvError = api != null && !String.IsNullOrEmpty(api.Message) ? api.Message : "Failed to load the conversation.";
                    });
                }
            });
        }

        /// <summary>
        /// Refetch the newest page (reconciliation after a turn, decision, or quick action).
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <returns>A task that completes after the result is applied or ignored.</returns>
        public Task RefreshLatestAsync(string threadId)
        {
            ArmadaClient client = Context.Client;
            return Task.Run(async () =>
            {
                try
                {
                    AskMessageEnumerateQuery q = new AskMessageEnumerateQuery();
                    q.PageSize = MessagePageSize;
                    AskMessagePage? page = await client.EnumerateAskMessagesAsync(threadId, q).ConfigureAwait(false);
                    await OnUiAsync(() => Conversation.Latest(threadId, page?.Messages ?? new List<AskMessage>())).ConfigureAwait(false);
                }
                catch (ArmadaApiException)
                {
                    // The socket delivers the messages; this is only reconciliation.
                }
            });
        }

        /// <summary>
        /// Refetch the thread detail (tracked work and pending proposals).
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <returns>A task.</returns>
        public Task RefreshDetailAsync(string threadId)
        {
            ArmadaClient client = Context.Client;
            return Task.Run(async () =>
            {
                try
                {
                    AskThreadDetail? detail = await client.GetAskThreadAsync(threadId).ConfigureAwait(false);
                    await OnUiAsync(() =>
                    {
                        if (!Conversation.Detail(threadId, detail)) return;
                        foreach (AskActionProposal p in Conversation.PendingProposals()) TrackProposal(p, false);
                        FetchSnapshots(threadId, detail?.TrackedWork ?? new List<AskTrackedWork>());
                    }).ConfigureAwait(false);
                }
                catch (ArmadaApiException)
                {
                    // Reconciliation only.
                }
            });
        }

        /// <summary>
        /// Send a message to the captain (creating the thread first when needed) with an optimistic copy that is
        /// confirmed or dropped on response.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>False when nothing was sent (empty, no captain, a turn running, or a slash command).</returns>
        public bool Send(string text)
        {
            string trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0 || NoCaptain || Conversation.TurnActive || trimmed.StartsWith("/", StringComparison.Ordinal)) return false;
            SentHistory.Add(trimmed);
            TuiTelemetry.RecordAskMessage();
            bool showThinking = ShowThinking;
            string? existing = Conversation.ThreadId;
            string localId = AskConversation.LocalPrefix + Guid.NewGuid().ToString("N").Substring(0, 12);
            if (existing != null) AddOptimistic(existing, localId, trimmed);
            ArmadaClient client = Context.Client;
            string draftCaptain = DraftCaptainId;
            _ = Task.Run(async () =>
            {
                string id;
                try
                {
                    id = existing ?? await CreateThreadAsync(client, draftCaptain).ConfigureAwait(false);
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() => Fail("Failed to start a conversation.", ex));
                    return;
                }

                if (existing == null) await OnUiAsync(() => AddOptimistic(id, localId, trimmed)).ConfigureAwait(false);
                try
                {
                    AskSendMessageResult? result = await client.SendAskMessageAsync(id, trimmed, showThinking).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        if (Conversation.ThreadId == id) Conversation.ConfirmUser(localId, result?.MessageId, result?.TurnId, Context.Clock.UtcNow);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        if (Conversation.ThreadId == id) Conversation.DropOptimistic(localId);
                        Fail("The message could not be sent.", ex);
                    });
                }
            });
            return true;
        }

        /// <summary>
        /// Stop the running turn: show "Stopping...", cancel on the server, and force-end locally after
        /// <see cref="ForceEndMs"/> when no <c>ask.turn</c> arrives.
        /// </summary>
        /// <returns>True when a stop was requested.</returns>
        public bool StopTurn()
        {
            string? id = Conversation.ThreadId;
            if (id == null || !Conversation.TurnActive || _Stopping) return false;
            _Stopping = true;
            ArmadaClient client = Context.Client;
            int grace = _ForceEndMs;
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.CancelAskTurnAsync(id).ConfigureAwait(false);
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        _Stopping = false;
                        Fail("Could not stop the captain.", ex);
                    });
                    return;
                }

                await Task.Delay(grace).ConfigureAwait(false);
                Context.Dispatcher.Post(() =>
                {
                    if (Conversation.ThreadId == id && Conversation.TurnActive)
                    {
                        Conversation.TurnEnded();
                        _ = RefreshLatestAsync(id);
                    }

                    _Stopping = false;
                });
            });
            return true;
        }

        /// <summary>
        /// Run a quick action (creating the thread first when needed). <paramref name="done"/> runs on the UI loop with
        /// true when it succeeded, so a form can close.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <param name="args">MCP arguments.</param>
        /// <param name="done">Completion callback, or null.</param>
        public void RunQuickAction(AskQuickAction action, JsonObject args, Action<bool>? done = null)
        {
            if (action == null || String.IsNullOrEmpty(action.ToolName))
            {
                done?.Invoke(false);
                return;
            }

            ActionBusy = true;
            string? existing = Conversation.ThreadId;
            string draftCaptain = DraftCaptainId;
            string tool = action.ToolName;
            string command = AskQuickActions.CommandOf(action);
            string json = (args ?? new JsonObject()).ToJsonString();
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    string id = existing ?? await CreateThreadAsync(client, draftCaptain).ConfigureAwait(false);
                    AskActionProposal? proposal = await client.RunAskQuickActionAsync(id, tool, new ArmadaRawJson(json)).ConfigureAwait(false);
                    await OnUiAsync(() =>
                    {
                        if (proposal != null && Conversation.ThreadId == id) Conversation.ApplyProposal(proposal);
                    }).ConfigureAwait(false);
                    await Task.WhenAll(RefreshLatestAsync(id), RefreshDetailAsync(id)).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        ActionBusy = false;
                        if (proposal != null && proposal.Status == AskProposalStatusEnum.Failed)
                        {
                            string reason = !String.IsNullOrEmpty(proposal.ErrorText) ? proposal.ErrorText! : Context.Loc.T("unknown error");
                            Context.Notifications.Toast(NotificationSeverityEnum.Error, Context.Loc.T("{{command}} failed: {{reason}}", LocalizationArgs.Of("command", command, "reason", reason)));
                        }

                        done?.Invoke(true);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        ActionBusy = false;
                        Fail("The quick action failed.", ex);
                        done?.Invoke(false);
                    });
                }
            });
        }

        /// <summary>
        /// Approve or reject a proposal in any thread (confirm cards and the Approvals center).
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="proposalId">Proposal id.</param>
        /// <param name="approve">Approve (true) or reject (false).</param>
        /// <param name="done">Called on the UI loop with the updated proposal (null on failure), or null.</param>
        public void Decide(string threadId, string proposalId, bool approve, Action<AskActionProposal?>? done = null)
        {
            if (String.IsNullOrEmpty(threadId) || String.IsNullOrEmpty(proposalId) || BusyProposalId != null) return;
            BusyProposalId = proposalId;
            ApprovalItem? pending = Context.Approvals.Find(ApprovalKindEnum.AskProposal, proposalId);
            TuiTelemetry.RecordApproval(ApprovalKindEnum.AskProposal, approve ? "approve" : "reject", pending?.CreatedUtc, Context.Clock.UtcNow);
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    AskActionProposal? updated = approve
                        ? await client.ApproveAskProposalAsync(threadId, proposalId).ConfigureAwait(false)
                        : await client.RejectAskProposalAsync(threadId, proposalId).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        BusyProposalId = null;
                        if (updated != null)
                        {
                            if (Conversation.ThreadId == threadId) Conversation.ApplyProposal(updated);
                            TrackProposal(updated, false);
                        }

                        if (Conversation.ThreadId == threadId)
                        {
                            _ = RefreshLatestAsync(threadId);
                            _ = RefreshDetailAsync(threadId);
                        }

                        done?.Invoke(updated);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        BusyProposalId = null;
                        Fail(approve ? "The action could not be approved." : "The action could not be rejected.", ex);
                        done?.Invoke(null);
                    });
                }
            });
        }

        /// <summary>
        /// Update a thread (title, captain, auto-approve, pin, archive) and fold the result into the list and the open
        /// conversation.
        /// </summary>
        /// <param name="target">Thread.</param>
        /// <param name="patch">Changed fields.</param>
        /// <param name="done">Called on the UI loop with the merged thread (null on failure), or null.</param>
        public void UpdateThread(AskThread target, AskThreadUpdateRequest patch, Action<AskThread?>? done = null)
        {
            if (target == null || patch == null) return;
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    AskThread? updated = await client.UpdateAskThreadAsync(target.Id, patch).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        AskThread merged = updated ?? Patched(target, patch);
                        Threads = AskThreadListLogic.ApplyUpdate(Threads, merged, Query, IncludeArchived, Conversation.ThreadId);
                        Conversation.ApplyThread(merged);
                        EnsureTools();
                        done?.Invoke(merged);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        Fail("The conversation could not be updated.", ex);
                        done?.Invoke(null);
                    });
                }
            });
        }

        /// <summary>
        /// Rename a thread (ignored when unchanged or blank).
        /// </summary>
        /// <param name="target">Thread.</param>
        /// <param name="title">New title.</param>
        public void Rename(AskThread target, string title)
        {
            string next = (title ?? "").Trim();
            if (target == null || next.Length == 0 || next == target.Title) return;
            AskThreadUpdateRequest patch = new AskThreadUpdateRequest();
            patch.Title = next.Length > 200 ? next.Substring(0, 200) : next;
            UpdateThread(target, patch);
        }

        /// <summary>
        /// Pin or unpin a thread.
        /// </summary>
        /// <param name="target">Thread.</param>
        public void TogglePin(AskThread target)
        {
            if (target == null) return;
            AskThreadUpdateRequest patch = new AskThreadUpdateRequest();
            patch.Pinned = !target.Pinned;
            UpdateThread(target, patch);
        }

        /// <summary>
        /// Archive or unarchive a thread.
        /// </summary>
        /// <param name="target">Thread.</param>
        public void ToggleArchive(AskThread target)
        {
            if (target == null) return;
            AskThreadUpdateRequest patch = new AskThreadUpdateRequest();
            patch.Archived = !target.Archived;
            UpdateThread(target, patch);
        }

        /// <summary>
        /// Choose the captain: for the open thread it updates the thread (empty clears it); for a new conversation it
        /// changes the remembered draft captain.
        /// </summary>
        /// <param name="captainId">Captain id, or empty for none.</param>
        public void SetCaptain(string? captainId)
        {
            string id = captainId ?? "";
            if (Conversation.Thread == null)
            {
                DraftCaptainId = id;
                Context.Prefs.Current.AskDraftCaptainId = id;
                Context.Prefs.Save();
                EnsureTools();
                return;
            }

            if (id.Length > 0)
            {
                Context.Prefs.Current.AskDraftCaptainId = id;
                Context.Prefs.Save();
            }

            AskThreadUpdateRequest patch = new AskThreadUpdateRequest();
            patch.CaptainId = id.Length > 0 ? id : null;
            patch.CaptainIdSpecified = true;
            UpdateThread(Conversation.Thread, patch);
        }

        /// <summary>
        /// Turn auto-approve on (after the dashboard's warning, in a confirm dialog) or off for the open thread.
        /// </summary>
        /// <param name="confirm">Ask for confirmation before turning it on.</param>
        public void ToggleAutoApprove(bool confirm = true)
        {
            AskThread? thread = Conversation.Thread;
            if (thread == null) return;
            bool value = !thread.AutoApprove;
            Action apply = () =>
            {
                AskThreadUpdateRequest patch = new AskThreadUpdateRequest();
                patch.AutoApprove = value;
                UpdateThread(thread, patch, updated =>
                {
                    if (updated != null && value)
                        Context.Notifications.Toast(NotificationSeverityEnum.Warning, Context.Loc.T("Auto-approve is on. Actions the captain proposes in this conversation now run without asking."));
                });
            };
            if (value && confirm)
            {
                Context.Confirm("Auto-approve", Context.Loc.T("Auto-approve runs state-changing actions the captain proposes immediately, without a confirm card. Only turn it on for conversations you trust; every action is still recorded here."), apply, "Turn on");
                return;
            }

            apply();
        }

        /// <summary>
        /// Ask the server to summarize a thread.
        /// </summary>
        /// <param name="target">Thread.</param>
        public void Summarize(AskThread target)
        {
            if (target == null) return;
            ArmadaClient client = Context.Client;
            string title = String.IsNullOrEmpty(target.Title) ? Context.Loc.T("New conversation") : target.Title;
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.SummarizeAskThreadAsync(target.Id).ConfigureAwait(false);
                    Context.Dispatcher.Post(() => Context.Notifications.Toast(NotificationSeverityEnum.Info,
                        Context.Loc.T("Summarizing \"{{title}}\". The summary will appear in the conversation.", LocalizationArgs.Of("title", title))));
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() => Fail("The conversation could not be summarized.", ex));
                }
            });
        }

        /// <summary>
        /// Delete a thread after the dashboard's confirmation.
        /// </summary>
        /// <param name="target">Thread.</param>
        /// <returns>The dialog, or null.</returns>
        public Modals.ConfirmDialog? Delete(AskThread target)
        {
            if (target == null) return null;
            string title = String.IsNullOrEmpty(target.Title) ? Context.Loc.T("New conversation") : target.Title;
            string message = Context.Loc.T("Delete \"{{title}}\"? Its messages and action history are removed. Work it started keeps running and stays visible on the normal pages.", LocalizationArgs.Of("title", title));
            return Context.Confirm("Delete conversation", message, () => DeleteNow(target), "Delete");
        }

        /// <summary>
        /// Delete a thread without confirmation.
        /// </summary>
        /// <param name="target">Thread.</param>
        public void DeleteNow(AskThread target)
        {
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.DeleteAskThreadAsync(target.Id).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        Threads = Threads.Where(t => t.Id != target.Id).ToList();
                        foreach (ApprovalItem item in Context.Approvals.Items.Where(i => i.Kind == ApprovalKindEnum.AskProposal && i.ParentId == target.Id).ToList())
                            Context.Approvals.Remove(ApprovalKindEnum.AskProposal, item.EntityId);
                        Context.Notifications.Toast(NotificationSeverityEnum.Success, Context.Loc.T("Conversation deleted."));
                        if (target.Id == Conversation.ThreadId)
                        {
                            Conversation.Reset(null);
                            if (Viewing) Context.Navigate("/ask");
                        }
                    });
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() => Fail("The conversation could not be deleted.", ex));
                }
            });
        }

        /// <summary>
        /// Load the page before the oldest persisted message.
        /// </summary>
        /// <returns>True when a load started.</returns>
        public bool LoadOlder()
        {
            string? id = Conversation.ThreadId;
            if (id == null || LoadingOlder || !Conversation.HasMore) return false;
            AskMessage? oldest = Conversation.Messages.FirstOrDefault(m => !AskConversation.IsLocal(m));
            if (oldest == null) return false;
            LoadingOlder = true;
            ArmadaClient client = Context.Client;
            AskMessageEnumerateQuery q = new AskMessageEnumerateQuery();
            q.BeforeSequence = oldest.Sequence;
            q.PageSize = MessagePageSize;
            _ = Task.Run(async () =>
            {
                try
                {
                    AskMessagePage? page = await client.EnumerateAskMessagesAsync(id, q).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        LoadingOlder = false;
                        Conversation.Older(id, page?.Messages ?? new List<AskMessage>(), page?.HasMore ?? false);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    Context.Dispatcher.Post(() =>
                    {
                        LoadingOlder = false;
                        Fail("Failed to load earlier messages.", ex);
                    });
                }
            });
            return true;
        }

        /// <summary>
        /// Mark a thread read (never while the terminal is unfocused or the Ask screen is not showing).
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <returns>True when the read call was made.</returns>
        public bool MarkRead(string threadId)
        {
            if (!Viewing || !Context.Notifications.TerminalFocused) return false;
            foreach (AskThread t in Threads)
            {
                if (t.Id == threadId) t.UnreadCount = 0;
            }

            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                try { await client.MarkAskThreadReadAsync(threadId).ConfigureAwait(false); }
                catch (ArmadaApiException) { }
            });
            return true;
        }

        /// <summary>
        /// The terminal gained or lost focus: mark the open thread read when focus returns.
        /// </summary>
        /// <param name="focused">Focused.</param>
        public void OnTerminalFocusChanged(bool focused)
        {
            if (focused && Conversation.ThreadId != null) MarkRead(Conversation.ThreadId);
        }

        /// <summary>
        /// The context sentence "Ask about this" pre-fills for a route, for example
        /// <c>On vessel TUIKit (vsl_...): </c> (the entity name is used when known).
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="entityName">Entity name, or null.</param>
        /// <returns>Sentence, or empty for Ask itself.</returns>
        public static string ContextPrefix(Routing.RouteMatch? route, string? entityName)
        {
            if (route == null || route.Route.ScreenName == "AskScreen") return "";
            string? id = route.Param("id") ?? route.Param("runId") ?? route.Param("threadId");
            string kind = EntityKind(route.Route.Pattern);
            if (id != null && kind.Length > 0)
                return "On " + kind + " " + (String.IsNullOrEmpty(entityName) ? id : entityName + " (" + id + ")") + ": ";
            if (id != null) return "On " + route.Route.Title + " " + id + ": ";
            return "On the " + route.Route.Title + " screen (" + route.FullPath + "): ";
        }

        /// <summary>
        /// "Ask about this": open a new conversation with the current screen's context pre-filled in the composer
        /// (the entity's name is looked up for vessels, missions, voyages, captains, and fleets).
        /// </summary>
        /// <param name="route">Current route.</param>
        public void AskAbout(Routing.RouteMatch? route)
        {
            if (route == null || route.Route.ScreenName == "AskScreen")
            {
                Context.Navigate(Conversation.ThreadId != null ? "/ask/" + Uri.EscapeDataString(Conversation.ThreadId) : "/ask");
                return;
            }

            string? id = route.Param("id");
            string kind = EntityKind(route.Route.Pattern);
            ComposerDraft = ContextPrefix(route, null);
            Context.Navigate("/ask");
            if (id == null || kind.Length == 0) return;
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                string? name = null;
                try
                {
                    if (kind == "vessel") name = (await client.GetVesselAsync(id).ConfigureAwait(false))?.Name;
                    else if (kind == "mission") name = (await client.GetMissionAsync(id).ConfigureAwait(false))?.Title;
                    else if (kind == "voyage") name = (await client.GetVoyageDetailAsync(id).ConfigureAwait(false))?.Voyage?.Title;
                    else if (kind == "captain") name = (await client.GetCaptainAsync(id).ConfigureAwait(false))?.Name;
                    else if (kind == "fleet") name = (await client.GetFleetAsync(id).ConfigureAwait(false))?.Fleet?.Name;
                }
                catch (ArmadaApiException)
                {
                    name = null;
                }

                if (String.IsNullOrEmpty(name)) return;
                string plain = ContextPrefix(route, null);
                string named = ContextPrefix(route, name);
                Context.Dispatcher.Post(() =>
                {
                    if (ComposerDraft == plain) ComposerDraft = named;
                    DraftChanged?.Invoke(this, named);
                });
            });
        }

        /// <summary>
        /// Toggle "Show thinking".
        /// </summary>
        public void ToggleShowThinking()
        {
            Context.Prefs.Current.AskShowThinking = !Context.Prefs.Current.AskShowThinking;
            Context.Prefs.Save();
        }

        /// <summary>
        /// The display name of a captain id, or null.
        /// </summary>
        /// <param name="captainId">Captain id.</param>
        /// <returns>Name or null.</returns>
        public string? CaptainName(string? captainId)
        {
            if (String.IsNullOrEmpty(captainId)) return null;
            return Captains.FirstOrDefault(c => c.Id == captainId)?.Name;
        }

        /// <summary>
        /// After a socket reconnect: refetch the thread list and the open conversation so nothing missed while offline
        /// is lost.
        /// </summary>
        public void HandleReconnect()
        {
            if (!Context.Session.IsSignedIn) return;
            LoadThreads(1);
            string? id = Conversation.ThreadId;
            if (id != null) LoadConversation(id, true);
        }

        /// <summary>
        /// Handle a parsed Ask event (socket or scripted source).
        /// </summary>
        /// <param name="e">Event.</param>
        public void HandleEvent(AskEvent e)
        {
            if (e == null) return;
            AskThreadListLogic.ApplyActivity(Activity, e);
            string? openId = Conversation.ThreadId;
            if (e.Type == "ask.proposal" && e.Proposal != null) TrackProposal(e.Proposal, !IsWatching(e.ThreadId));
            if (e.Type == "ask.thread" && e.Thread != null)
            {
                bool unread = e.Thread.UnreadCount > 0;
                Threads = AskThreadListLogic.ApplyUpdate(Threads, e.Thread, Query, IncludeArchived, openId);
                if (e.ThreadId == openId && unread) MarkRead(e.ThreadId);
            }

            if (e.ThreadId != openId) return;
            AskTrackedWork? prior = e.Type == "ask.work" ? Conversation.Work(e.TrackedWorkId) : null;
            Conversation.Apply(e, Context.Clock.UtcNow);
            if (e.Type == "ask.turn" && e.State != "started")
            {
                _Stopping = false;
                _ = RefreshLatestAsync(e.ThreadId);
            }
            else if (e.Type == "ask.work" && e.Snapshot != null && prior == null)
            {
                _ = RefreshDetailAsync(e.ThreadId);
            }
        }

        #endregion

        #region Private-Methods

        private static string EntityKind(string pattern)
        {
            string p = pattern ?? "";
            if (p.StartsWith("/vessels/:id", StringComparison.Ordinal)) return "vessel";
            if (p.StartsWith("/missions/:id", StringComparison.Ordinal)) return "mission";
            if (p.StartsWith("/voyages/:id", StringComparison.Ordinal)) return "voyage";
            if (p.StartsWith("/captains/:id", StringComparison.Ordinal)) return "captain";
            if (p.StartsWith("/fleets/:id", StringComparison.Ordinal)) return "fleet";
            if (p.StartsWith("/deployments/:id", StringComparison.Ordinal)) return "deployment";
            if (p.StartsWith("/incidents/:id", StringComparison.Ordinal)) return "incident";
            if (p.StartsWith("/releases/:id", StringComparison.Ordinal)) return "release";
            if (p.StartsWith("/environments/:id", StringComparison.Ordinal)) return "environment";
            if (p.StartsWith("/backlog/:id", StringComparison.Ordinal)) return "backlog item";
            if (p.StartsWith("/checks/:id", StringComparison.Ordinal)) return "check run";
            if (p.StartsWith("/merge-queue/:id", StringComparison.Ordinal)) return "merge queue entry";
            if (p.StartsWith("/fleet-actions/runs/:id", StringComparison.Ordinal)) return "fleet action run";
            if (p.StartsWith("/runbooks/:id", StringComparison.Ordinal)) return "runbook";
            return "";
        }

        private bool IsWatching(string threadId)
        {
            return Viewing && Context.Notifications.TerminalFocused && Conversation.ThreadId == threadId;
        }

        private void TrackProposal(AskActionProposal proposal, bool notify)
        {
            if (proposal.Status != AskProposalStatusEnum.Pending)
            {
                Context.Approvals.Remove(ApprovalKindEnum.AskProposal, proposal.Id);
                return;
            }

            string threadTitle = Threads.FirstOrDefault(t => t.Id == proposal.ThreadId)?.Title
                ?? (Conversation.Thread != null && Conversation.Thread.Id == proposal.ThreadId ? Conversation.Thread.Title : Context.Loc.T("a conversation"));
            ApprovalItem item = new ApprovalItem();
            item.Kind = ApprovalKindEnum.AskProposal;
            item.EntityId = proposal.Id;
            item.ParentId = proposal.ThreadId;
            item.ToolName = proposal.ToolName;
            item.Arguments = proposal.ArgumentsText;
            item.ExpiresUtc = proposal.ExpiresUtc;
            item.EntityName = threadTitle;
            item.Title = Context.Loc.T("Approval needed in {{title}}: {{tool}}", LocalizationArgs.Of("title", threadTitle, "tool", proposal.ToolName));
            item.Detail = proposal.SummaryText;
            item.Route = "/ask/" + Uri.EscapeDataString(proposal.ThreadId);
            item.Urgency = 3;
            item.Source = "Ask Armada";
            item.CreatedUtc = proposal.CreatedUtc;
            bool isNew = Context.Approvals.Find(ApprovalKindEnum.AskProposal, proposal.Id) == null;
            Context.Approvals.Upsert(item, notify);
            if (isNew && notify)
            {
                string route = item.Route;
                Context.Notifications.Toast(NotificationSeverityEnum.Warning, item.Title + (String.IsNullOrEmpty(item.Detail) ? "" : " - " + item.Detail), "Open", () => Context.Navigate(route));
            }
        }

        private void FetchSnapshots(string threadId, List<AskTrackedWork> work)
        {
            List<AskTrackedWork> missing = work
                .Where(w => !Conversation.Snapshots.ContainsKey(w.Id) && w.Snapshot == null)
                .OrderByDescending(w => AskWorkLogic.IsActive(w) ? 1 : 0)
                .Take(SnapshotFetchLimit)
                .ToList();
            ArmadaClient client = Context.Client;
            foreach (AskTrackedWork item in missing)
            {
                string workId = item.Id;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        AskWorkSnapshot? snapshot = await client.GetAskWorkSnapshotAsync(threadId, workId).ConfigureAwait(false);
                        Context.Dispatcher.Post(() =>
                        {
                            if (snapshot != null && Conversation.ThreadId == threadId) Conversation.ApplySnapshot(workId, snapshot);
                        });
                    }
                    catch (ArmadaApiException)
                    {
                        // The card shows its loading state until an ask.work event arrives.
                    }
                });
            }
        }

        private void LoadCaptains()
        {
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                List<Captain> list;
                try
                {
                    ArmadaPageQuery q = new ArmadaPageQuery();
                    q.PageSize = 200;
                    EnumerationResult<Captain>? result = await client.ListCaptainsAsync(q).ConfigureAwait(false);
                    list = result?.Objects ?? new List<Captain>();
                }
                catch (ArmadaApiException)
                {
                    list = new List<Captain>();
                }

                Context.Dispatcher.Post(() =>
                {
                    Captains = list;
                    string current = DraftCaptainId;
                    DraftCaptainId = current.Length > 0 && list.Any(c => c.Id == current) ? current : (list.Count > 0 ? list[0].Id : "");
                    EnsureTools();
                });
            });
        }

        private void LoadQuickActions()
        {
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                List<AskQuickAction> merged;
                try
                {
                    merged = AskQuickActions.Merge(await client.GetAskQuickActionsAsync().ConfigureAwait(false));
                }
                catch (ArmadaApiException)
                {
                    merged = AskQuickActions.Defaults();
                }

                Context.Dispatcher.Post(() => QuickActions = merged);
            });
        }

        private async Task<string> CreateThreadAsync(ArmadaClient client, string draftCaptain)
        {
            AskThreadCreateRequest req = new AskThreadCreateRequest();
            req.CaptainId = String.IsNullOrEmpty(draftCaptain) ? null : draftCaptain;
            AskThread? created = await client.CreateAskThreadAsync(req).ConfigureAwait(false);
            if (created == null) throw new ArmadaApiException("The server returned no conversation.", 500, null, null, null, "POST", "/api/v1/ask/threads", null, null);
            await OnUiAsync(() =>
            {
                Conversation.Reset(created.Id);
                Conversation.ApplyThread(created);
                Threads = AskThreadListLogic.ApplyUpdate(Threads, created, Query, IncludeArchived, created.Id);
                Context.Prefs.Current.LastAskThreadId = created.Id;
                ThreadCreated?.Invoke(this, created.Id);
                if (Viewing) Context.Router.Navigate("/ask/" + Uri.EscapeDataString(created.Id), true);
            }).ConfigureAwait(false);
            return created.Id;
        }

        private void AddOptimistic(string threadId, string localId, string text)
        {
            if (Conversation.ThreadId != threadId) return;
            int maxSeq = Conversation.Messages.Count == 0 ? 0 : Conversation.Messages.Max(m => m.Sequence);
            AskMessage optimistic = new AskMessage();
            optimistic.Id = localId;
            optimistic.ThreadId = threadId;
            optimistic.Sequence = maxSeq + 1;
            optimistic.Role = AskMessageRoleEnum.User;
            optimistic.Kind = AskMessageKindEnum.Text;
            optimistic.ContentText = text;
            optimistic.CreatedUtc = Context.Clock.UtcNow;
            Conversation.OptimisticUser(optimistic);
        }

        private static AskThread Patched(AskThread target, AskThreadUpdateRequest patch)
        {
            if (patch.Title != null) target.Title = patch.Title;
            if (patch.CaptainIdSpecified || patch.CaptainId != null) target.CaptainId = patch.CaptainId;
            if (patch.AutoApprove.HasValue) target.AutoApprove = patch.AutoApprove.Value;
            if (patch.Pinned.HasValue) target.Pinned = patch.Pinned.Value;
            if (patch.Archived.HasValue) target.Archived = patch.Archived.Value;
            return target;
        }

        private Task OnUiAsync(Action action)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Context.Dispatcher.Post(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        private void Fail(string message, ArmadaApiException ex)
        {
            Context.ShowError(message, ex);
        }

        #endregion
    }
}
