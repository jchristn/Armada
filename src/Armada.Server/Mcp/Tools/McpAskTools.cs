namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Ask;
    using Armada.Server.Ask;

    /// <summary>
    /// MCP tools for Ask Armada conversations, matching the REST <c>/api/v1/ask</c> routes: list, read, create, rename,
    /// pin, archive, and delete conversations; read and send messages (optionally waiting for the reply); cancel or
    /// summarize a turn; mark read; run quick actions; approve or reject proposed actions; and read tracked work.
    /// Conversations are private: every tool acts as the caller and sees only the caller's own conversations.
    /// </summary>
    public static class McpAskTools
    {
        #region Private-Members

        private const int _MaxWaitSeconds = 600;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Registers the Ask Armada MCP tools.
        /// </summary>
        /// <param name="register">Tool registration delegate.</param>
        /// <param name="threads">Conversation service, or null where Ask Armada does not run (the stdio server).</param>
        /// <param name="turns">Turn coordinator (sending, cancelling, summarizing), or null.</param>
        /// <param name="actions">Action service (quick actions and proposals), or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public static void Register(RegisterToolDelegate register, AskThreadService? threads, AskTurnCoordinator? turns, AskActionService? actions)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));
            if (threads == null || turns == null || actions == null)
            {
                // Same tool list on every transport; without the services each tool answers Unavailable.
                RegisterToolDelegate outer = register;
                register = (name, description, inputSchema, handler) => outer(name, description, inputSchema,
                    _ => Task.FromResult<object>(McpToolError.Unavailable("Ask Armada conversations are not available on this MCP server; use the Admiral's HTTP MCP endpoint.")));
            }

            RegisterTools(register, threads!, turns!, actions!);
        }

        #endregion

        #region Private-Methods

        private static void RegisterTools(RegisterToolDelegate register, AskThreadService threads, AskTurnCoordinator turns, AskActionService actions)
        {

            object threadId = new { type = "string", description = "Ask Armada conversation ID" };

            register(
                "list_ask_threads",
                "List the caller's Ask Armada conversations, newest first, with titles, captain, pinned and archived state, and unread counts. Use search to match titles and includeArchived to include archived ones.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        search = new { type = "string", description = "Text to match in conversation titles" },
                        includeArchived = new { type = "boolean", description = "Include archived conversations (default false)" },
                        pageNumber = new { type = "integer", description = "Page number, 1-based (default 1)" },
                        pageSize = new { type = "integer", description = "Results per page (default 25)" }
                    }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    AskThreadEnumerateRequest request = new AskThreadEnumerateRequest { Search = a.Search, IncludeArchived = a.IncludeArchived ?? false };
                    if (a.PageNumber.HasValue) request.PageNumber = a.PageNumber.Value;
                    if (a.PageSize.HasValue) request.PageSize = a.PageSize.Value;
                    return (object)await threads.EnumerateThreadsAsync(McpToolHelpers.ResolveCallerContext(), request).ConfigureAwait(false);
                });

            register(
                "get_ask_thread",
                "Get one Ask Armada conversation with counts of its tracked work, pending proposals, and pending CLI permission requests, and the running turn's progress (with its tool call count). Read the collections with list_ask_work, list_ask_proposals, and list_ask_tool_calls.",
                new { type = "object", properties = new { threadId }, required = new[] { "threadId" } },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    AskThreadDetail? detail = await threads.GetThreadDetailAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId).ConfigureAwait(false);
                    return detail == null ? (object)McpToolError.NotFound("Ask conversation not found") : detail;
                });

            register(
                "create_ask_thread",
                "Start a new Ask Armada conversation. Optionally give it a title, choose the captain that answers, and set whether its proposed actions run without asking (autoApprove).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Conversation title (optional; Armada names it from the first message otherwise)" },
                        captainId = new { type = "string", description = "Captain that answers (optional; the server's Ask captain otherwise)" },
                        autoApprove = new { type = "boolean", description = "Run proposed actions without asking (optional)" }
                    }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    try
                    {
                        AskThreadCreateRequest request = new AskThreadCreateRequest { Title = a.Title, CaptainId = a.CaptainId, AutoApprove = a.AutoApprove };
                        return (object)await threads.CreateThreadAsync(McpToolHelpers.ResolveCallerContext(), request).ConfigureAwait(false);
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)FromArgumentException(ex);
                    }
                });

            register(
                "update_ask_thread",
                "Rename, pin or unpin, archive or unarchive an Ask Armada conversation, change its captain (or clearCaptain to use the default), or change autoApprove. Only supplied fields change.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        title = new { type = "string", description = "New title" },
                        captainId = new { type = "string", description = "Captain that answers from now on" },
                        clearCaptain = new { type = "boolean", description = "True to use the server's default Ask captain" },
                        autoApprove = new { type = "boolean", description = "Run proposed actions without asking" },
                        pinned = new { type = "boolean", description = "Pin or unpin" },
                        archived = new { type = "boolean", description = "Archive or unarchive" }
                    },
                    required = new[] { "threadId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    AskThreadUpdateRequest request = new AskThreadUpdateRequest
                    {
                        Title = a.Title,
                        AutoApprove = a.AutoApprove,
                        Pinned = a.Pinned,
                        Archived = a.Archived
                    };
                    if (a.ClearCaptain == true)
                    {
                        request.CaptainId = null;
                        request.CaptainIdSpecified = true;
                    }
                    else if (a.CaptainId != null)
                    {
                        request.CaptainId = a.CaptainId;
                        request.CaptainIdSpecified = true;
                    }

                    try
                    {
                        AskThread? thread = await threads.UpdateThreadAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, request).ConfigureAwait(false);
                        return thread == null ? (object)McpToolError.NotFound("Ask conversation not found") : thread;
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)FromArgumentException(ex);
                    }
                });

            register(
                "delete_ask_thread",
                "Permanently delete an Ask Armada conversation and its messages, cancelling its turn if one is running. This cannot be undone.",
                new { type = "object", properties = new { threadId }, required = new[] { "threadId" } },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    bool deleted = await DeleteAsync(threads, turns, a.ThreadId).ConfigureAwait(false);
                    return deleted ? (object)new { status = "Deleted", threadId = a.ThreadId } : McpToolError.NotFound("Ask conversation not found");
                });

            register(
                "delete_ask_threads",
                "Permanently delete up to 1000 Ask Armada conversations by ID (cancelling running turns); more is InvalidArgument CollectionLimitExceeded. Returns which were deleted and which were not found (both bounded by the request). This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new { threadIds = new { type = "array", items = new { type = "string" }, description = "Conversation IDs" } },
                    required = new[] { "threadIds" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (a.ThreadIds == null || a.ThreadIds.Count == 0) return (object)McpToolError.InvalidArgument("threadIds is required.");
                    CollectionLimits.RequireRequestItems(a.ThreadIds, "threadIds");
                    List<string> deleted = new List<string>();
                    List<string> notFound = new List<string>();
                    foreach (string id in a.ThreadIds.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal))
                    {
                        if (await DeleteAsync(threads, turns, id).ConfigureAwait(false)) deleted.Add(id);
                        else notFound.Add(id);
                    }

                    return (object)new { status = "Deleted", deletedCount = deleted.Count, deleted, notFound };
                });

            register(
                "list_ask_messages",
                "Read an Ask Armada conversation's messages, newest page first (1 to 200 per page). Pass beforeSequence (the lowest sequence you have) to read older messages. Each message carries toolCallCount; read its tool calls with list_ask_tool_calls.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        beforeSequence = new { type = "integer", description = "Return messages before this sequence number" },
                        pageSize = new { type = "integer", description = "Messages per page, 1 to 200 (default 50)" }
                    },
                    required = new[] { "threadId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    AskMessageEnumerateRequest request = new AskMessageEnumerateRequest { BeforeSequence = a.BeforeSequence };
                    if (a.PageSize.HasValue) request.PageSize = a.PageSize.Value;
                    AskMessagePage? page = await threads.EnumerateMessagesAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, request).ConfigureAwait(false);
                    return page == null ? (object)McpToolError.NotFound("Ask conversation not found") : page;
                });

            register(
                "send_ask_message",
                "Send a message in an Ask Armada conversation; its captain answers in the background. Set waitSeconds (up to 600) to wait for the reply and get the latest messages back; otherwise read the reply later with list_ask_messages. Fails with Conflict while a turn is already running.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        content = new { type = "string", description = "Message text" },
                        showThinking = new { type = "boolean", description = "Include the captain's reasoning in the reply (optional)" },
                        waitSeconds = new { type = "integer", description = "Seconds to wait for the reply (0 to 600, default 0)" }
                    },
                    required = new[] { "threadId", "content" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    if (String.IsNullOrWhiteSpace(a.Content)) return (object)McpToolError.InvalidArgument("content is required.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    AskTurnStart start = await turns.SendMessageAsync(caller, a.ThreadId, new AskMessageSendRequest { Content = a.Content, ShowThinking = a.ShowThinking }).ConfigureAwait(false);
                    if (start.StatusCode != 202) return (object)ToError(start.StatusCode, start.Message);
                    int wait = Math.Clamp(a.WaitSeconds ?? 0, 0, _MaxWaitSeconds);
                    if (wait == 0) return (object)start.Response!;

                    DateTime deadline = DateTime.UtcNow.AddSeconds(wait);
                    while (turns.ActiveTurnId(a.ThreadId) != null && DateTime.UtcNow < deadline)
                        await Task.Delay(1000).ConfigureAwait(false);
                    AskMessagePage? page = await threads.EnumerateMessagesAsync(caller, a.ThreadId, new AskMessageEnumerateRequest { PageSize = 10 }).ConfigureAwait(false);
                    return (object)new { completed = turns.ActiveTurnId(a.ThreadId) == null, turn = start.Response, recentMessages = page };
                });

            register(
                "cancel_ask_turn",
                "Stop the turn running in an Ask Armada conversation.",
                new { type = "object", properties = new { threadId }, required = new[] { "threadId" } },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    int status = await turns.CancelAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId).ConfigureAwait(false);
                    if (status == 404) return (object)McpToolError.NotFound("Ask conversation not found");
                    if (status == 409) return (object)McpToolError.Conflict("No turn is running in this conversation");
                    return (object)new { cancelled = true };
                });

            register(
                "summarize_ask_thread",
                "Ask the conversation's captain to summarize the conversation so far (runs as a turn in the background).",
                new { type = "object", properties = new { threadId }, required = new[] { "threadId" } },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    AskTurnStart start = await turns.SummarizeAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId).ConfigureAwait(false);
                    return start.StatusCode == 202 ? (object)start.Response! : ToError(start.StatusCode, start.Message);
                });

            register(
                "mark_ask_thread_read",
                "Mark an Ask Armada conversation as read (clears its unread count).",
                new { type = "object", properties = new { threadId }, required = new[] { "threadId" } },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    AskThread? thread = await threads.MarkReadAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId).ConfigureAwait(false);
                    return thread == null ? (object)McpToolError.NotFound("Ask conversation not found") : thread;
                });

            register(
                "list_ask_quick_actions",
                "List the quick actions an Ask Armada conversation can run directly (each is an Armada tool with its argument form).",
                new { type = "object", properties = new { } },
                (args) => Task.FromResult((object)AskQuickActionCatalog.All().Where(q => actions.HasTool(q.ToolName)).ToList()));

            register(
                "run_ask_quick_action",
                "Propose a quick action in an Ask Armada conversation (toolName from list_ask_quick_actions, with its arguments). It becomes an action proposal that runs when approved, or at once when the conversation auto-approves.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        toolName = new { type = "string", description = "Quick action tool name" },
                        arguments = new { type = "object", description = "The tool's arguments" }
                    },
                    required = new[] { "threadId", "toolName" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    if (String.IsNullOrWhiteSpace(a.ToolName)) return (object)McpToolError.InvalidArgument("toolName is required.");
                    AskProposalDecision decision = await actions.SubmitQuickActionAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, new AskActionRequest { ToolName = a.ToolName, Arguments = a.Arguments }).ConfigureAwait(false);
                    return decision.StatusCode == 200 ? (object)decision.Proposal! : ToError(decision.StatusCode, decision.Message);
                });

            register(
                "approve_ask_proposal",
                "Approve an action Armada proposed in an Ask Armada conversation, so it runs now.",
                new
                {
                    type = "object",
                    properties = new { threadId, proposalId = new { type = "string", description = "Proposal ID" } },
                    required = new[] { "threadId", "proposalId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId) || String.IsNullOrWhiteSpace(a.ProposalId)) return (object)McpToolError.InvalidArgument("threadId and proposalId are required.");
                    AskProposalDecision decision = await actions.ApproveAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, a.ProposalId).ConfigureAwait(false);
                    return decision.StatusCode == 200 ? (object)decision.Proposal! : ToError(decision.StatusCode, decision.Message);
                });

            register(
                "reject_ask_proposal",
                "Reject an action Armada proposed in an Ask Armada conversation; it does not run.",
                new
                {
                    type = "object",
                    properties = new { threadId, proposalId = new { type = "string", description = "Proposal ID" } },
                    required = new[] { "threadId", "proposalId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId) || String.IsNullOrWhiteSpace(a.ProposalId)) return (object)McpToolError.InvalidArgument("threadId and proposalId are required.");
                    AskProposalDecision decision = await actions.RejectAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, a.ProposalId).ConfigureAwait(false);
                    return decision.StatusCode == 200 ? (object)decision.Proposal! : ToError(decision.StatusCode, decision.Message);
                });

            register(
                "get_ask_work",
                "Get the current state of work an Ask Armada conversation is tracking (a voyage, mission, or fleet action run it started): status and counts, with missionCount and targetCount. Read the rows with list_ask_work_missions and list_ask_work_targets.",
                new
                {
                    type = "object",
                    properties = new { threadId, workId = new { type = "string", description = "Tracked work ID" } },
                    required = new[] { "threadId", "workId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId) || String.IsNullOrWhiteSpace(a.WorkId)) return (object)McpToolError.InvalidArgument("threadId and workId are required.");
                    AskWorkSnapshot? snapshot = await threads.GetWorkSnapshotAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, a.WorkId).ConfigureAwait(false);
                    return snapshot == null ? (object)McpToolError.NotFound("Tracked work not found") : snapshot;
                });
            register(
                "list_ask_work",
                "Read one page of the work an Ask Armada conversation tracks (paged in the database), newest first by default, each item with a counts-only snapshot. status filters by state: Active, Succeeded, Failed, or Cancelled.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        status = new { type = "string", description = "Optional state filter: Active, Succeeded, Failed, Cancelled" },
                        pageNumber = new { type = "integer", description = "1-based page number (default 1)" },
                        pageSize = new { type = "integer", description = "Items per page, 1 to 1000 (default 10)" },
                        order = new { type = "string", description = "CreatedDescending (newest first, default) or CreatedAscending" }
                    },
                    required = new[] { "threadId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    EnumerationQuery query = ToQuery(a);
                    query.Status = a.Status;
                    EnumerationResult<AskTrackedWork>? page = await threads.EnumerateTrackedWorkAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, query).ConfigureAwait(false);
                    return page == null ? (object)McpToolError.NotFound("Ask conversation not found") : page;
                });
            register(
                "list_ask_work_missions",
                "Read one page of the mission rows of tracked work (a voyage's missions, or the mission itself), paged in the database. Newest first by default; pass order CreatedAscending for creation order. Fleet action runs have targets instead (list_ask_work_targets).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        workId = new { type = "string", description = "Tracked work ID (atw_ prefix)" },
                        pageNumber = new { type = "integer", description = "1-based page number (default 1)" },
                        pageSize = new { type = "integer", description = "Rows per page, 1 to 1000 (default 10)" },
                        order = new { type = "string", description = "CreatedDescending (newest first, default) or CreatedAscending" }
                    },
                    required = new[] { "threadId", "workId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId) || String.IsNullOrWhiteSpace(a.WorkId)) return (object)McpToolError.InvalidArgument("threadId and workId are required.");
                    EnumerationResult<AskWorkMissionSnapshot>? page = await threads.EnumerateWorkMissionsAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, a.WorkId, ToQuery(a)).ConfigureAwait(false);
                    return page == null ? (object)McpToolError.NotFound("Tracked work not found") : page;
                });
            register(
                "list_ask_work_targets",
                "Read one page of the target rows of a tracked fleet action run (one per vessel, by vessel name), paged in the database.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        workId = new { type = "string", description = "Tracked work ID (atw_ prefix)" },
                        pageNumber = new { type = "integer", description = "1-based page number (default 1)" },
                        pageSize = new { type = "integer", description = "Rows per page, 1 to 1000 (default 10)" }
                    },
                    required = new[] { "threadId", "workId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId) || String.IsNullOrWhiteSpace(a.WorkId)) return (object)McpToolError.InvalidArgument("threadId and workId are required.");
                    EnumerationResult<AskWorkTargetSnapshot>? page = await threads.EnumerateWorkTargetsAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, a.WorkId, ToQuery(a)).ConfigureAwait(false);
                    return page == null ? (object)McpToolError.NotFound("Tracked work not found") : page;
                });
            register(
                "list_ask_proposals",
                "Read one page of an Ask Armada conversation's action proposals (paged in the database), newest first by default. status filters by proposal status, for example Pending.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        status = new { type = "string", description = "Optional status filter, for example Pending" },
                        pageNumber = new { type = "integer", description = "1-based page number (default 1)" },
                        pageSize = new { type = "integer", description = "Proposals per page, 1 to 1000 (default 10)" },
                        order = new { type = "string", description = "CreatedDescending (newest first, default) or CreatedAscending" }
                    },
                    required = new[] { "threadId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    EnumerationQuery query = ToQuery(a);
                    query.Status = a.Status;
                    EnumerationResult<AskActionProposal>? page = await threads.EnumerateProposalsAsync(McpToolHelpers.ResolveCallerContext(), a.ThreadId, query).ConfigureAwait(false);
                    return page == null ? (object)McpToolError.NotFound("Ask conversation not found") : page;
                });
            register(
                "list_ask_tool_calls",
                "Read one page of tool calls in an Ask Armada conversation: the calls of one message (messageId, paged in the database), or without messageId the calls the running turn has made so far. Newest first by default; pass order CreatedAscending for the order they were made in.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        threadId,
                        messageId = new { type = "string", description = "Message ID (amg_ prefix); omit for the running turn" },
                        pageNumber = new { type = "integer", description = "1-based page number (default 1)" },
                        pageSize = new { type = "integer", description = "Calls per page, 1 to 1000 (default 10)" },
                        order = new { type = "string", description = "CreatedDescending (newest first, default) or CreatedAscending" }
                    },
                    required = new[] { "threadId" }
                },
                async (args) =>
                {
                    AskToolArgs a = Parse(args);
                    if (String.IsNullOrWhiteSpace(a.ThreadId)) return (object)McpToolError.InvalidArgument("threadId is required.");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    EnumerationResult<AskMessageToolCall>? page = String.IsNullOrWhiteSpace(a.MessageId)
                        ? await threads.EnumerateActiveTurnToolCallsAsync(caller, a.ThreadId, ToQuery(a)).ConfigureAwait(false)
                        : await threads.EnumerateToolCallsAsync(caller, a.ThreadId, a.MessageId!, ToQuery(a)).ConfigureAwait(false);
                    return page == null ? (object)McpToolError.NotFound(String.IsNullOrWhiteSpace(a.MessageId) ? "Ask conversation not found" : "Message not found") : page;
                });
        }

        

        private static McpToolError FromArgumentException(ArgumentException ex)
        {
            // A captain the caller cannot see is reported the same way as one that does not exist.
            if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return McpToolError.NotFound(ex.Message.Split(" (Parameter", StringSplitOptions.None)[0]);
            return McpToolError.FromException(ex);
        }

        private static EnumerationQuery ToQuery(AskToolArgs a)
        {
            EnumerationQuery query = new EnumerationQuery();
            if (a.PageNumber.HasValue) query.PageNumber = a.PageNumber.Value;
            if (a.PageSize.HasValue) query.PageSize = a.PageSize.Value;
            if (!String.IsNullOrWhiteSpace(a.Order) && Enum.TryParse<EnumerationOrderEnum>(a.Order, true, out EnumerationOrderEnum order)) query.Order = order;
            return query;
        }

        private static AskToolArgs Parse(JsonElement? args)
        {
            if (!args.HasValue) return new AskToolArgs();
            return JsonSerializer.Deserialize<AskToolArgs>(args.Value, _JsonOptions) ?? new AskToolArgs();
        }

        private static async Task<bool> DeleteAsync(AskThreadService threads, AskTurnCoordinator turns, string threadId)
        {
            AuthContext caller = McpToolHelpers.ResolveCallerContext();
            if (turns.ActiveTurnId(threadId) != null) await turns.CancelAsync(caller, threadId).ConfigureAwait(false);
            return await threads.DeleteThreadAsync(caller, threadId).ConfigureAwait(false);
        }

        private static McpToolError ToError(int statusCode, string? message)
        {
            string text = String.IsNullOrWhiteSpace(message) ? "Request failed" : message!;
            switch (statusCode)
            {
                case 400: return McpToolError.InvalidArgument(text);
                case 403: return McpToolError.Forbidden(text);
                case 404: return McpToolError.NotFound(text);
                case 409: return McpToolError.Conflict(text);
                case 503: return McpToolError.Unavailable(text);
                default: return McpToolError.Failed(text);
            }
        }

        #endregion
    }
}
