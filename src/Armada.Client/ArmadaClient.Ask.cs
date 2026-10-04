namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Ask API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>askArmada</c>: POST '/api/v1/ask'.
        /// </summary>
        /// <param name="message">message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskResponse?> AskArmadaAsync(string message, CancellationToken token = default)
        {
            return PostAsync<AskResponse>("/api/v1/ask", new { Message = message }, null, token);
        }

        /// <summary>
        /// Dashboard <c>chatWithCaptain</c>: POST /api/v1/captains/{captainId}/chat (5.5 minute timeout; cancel the token to stop the turn).
        /// </summary>
        /// <param name="captainId">Captain id.</param>
        /// <param name="body">Chat request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CaptainChatResponse?> ChatWithCaptainAsync(string captainId, CaptainChatRequest body, CancellationToken token = default)
        {
            return PostAsync<CaptainChatResponse>($"/api/v1/captains/{E(captainId)}/chat", body, ArmadaRequestOptions.WithTimeout(330000), token);
        }

        /// <summary>
        /// Dashboard <c>enumerateAskThreads</c>: POST /api/v1/ask/threads/enumerate (pinned first, then most recent message).
        /// </summary>
        /// <param name="query">Query, or null for page 1 of 50.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<AskThread>?> EnumerateAskThreadsAsync(AskThreadEnumerateQuery? query = null, CancellationToken token = default)
        {
            AskThreadEnumerateQuery q = query ?? new AskThreadEnumerateQuery();
            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["PageNumber"] = q.PageNumber;
            body["PageSize"] = q.PageSize;
            if (!String.IsNullOrEmpty(q.Search)) body["Search"] = q.Search;
            body["IncludeArchived"] = q.IncludeArchived;
            return PostAsync<EnumerationResult<AskThread>>("/api/v1/ask/threads/enumerate", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>createAskThread</c>: POST /api/v1/ask/threads.
        /// </summary>
        /// <param name="data">Optional title, captain, and auto-approve.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskThread?> CreateAskThreadAsync(AskThreadCreateRequest? data = null, CancellationToken token = default)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?>();
            if (data != null && !String.IsNullOrEmpty(data.Title)) body["Title"] = data.Title;
            if (data != null && !String.IsNullOrEmpty(data.CaptainId)) body["CaptainId"] = data.CaptainId;
            if (data != null && data.AutoApprove.HasValue) body["AutoApprove"] = data.AutoApprove.Value;
            return PostAsync<AskThread>("/api/v1/ask/threads", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>getAskThread</c>: GET /api/v1/ask/threads/{id}.
        /// </summary>
        /// <param name="id">Thread id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskThreadDetail?> GetAskThreadAsync(string id, CancellationToken token = default)
        {
            return GetAsync<AskThreadDetail>($"/api/v1/ask/threads/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>updateAskThread</c>: PUT /api/v1/ask/threads/{id}. Only set fields are sent; set <see cref="AskThreadUpdateRequest.CaptainIdSpecified"/> with a null captain to clear it.
        /// </summary>
        /// <param name="id">Thread id.</param>
        /// <param name="data">Fields to change.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskThread?> UpdateAskThreadAsync(string id, AskThreadUpdateRequest data, CancellationToken token = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Dictionary<string, object?> body = new Dictionary<string, object?>();
            if (data.Title != null) body["Title"] = data.Title;
            if (data.CaptainId != null || data.CaptainIdSpecified) body["CaptainId"] = data.CaptainId;
            if (data.AutoApprove.HasValue) body["AutoApprove"] = data.AutoApprove.Value;
            if (data.Pinned.HasValue) body["Pinned"] = data.Pinned.Value;
            if (data.Archived.HasValue) body["Archived"] = data.Archived.Value;
            return PutAsync<AskThread>($"/api/v1/ask/threads/{E(id)}", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteAskThread</c>: DELETE /api/v1/ask/threads/{id}.
        /// </summary>
        /// <param name="id">Thread id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteAskThreadAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/ask/threads/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateAskMessages</c>: POST /api/v1/ask/threads/{id}/messages/enumerate (newest page when no sequence is given).
        /// </summary>
        /// <param name="id">Thread id.</param>
        /// <param name="query">Paging, or null for the newest 30.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskMessagePage?> EnumerateAskMessagesAsync(string id, AskMessageEnumerateQuery? query = null, CancellationToken token = default)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?>();
            if (query != null && query.BeforeSequence.HasValue) body["BeforeSequence"] = query.BeforeSequence.Value;
            body["PageSize"] = query != null ? query.PageSize : 30;
            return PostAsync<AskMessagePage>($"/api/v1/ask/threads/{E(id)}/messages/enumerate", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>sendAskMessage</c>: POST /api/v1/ask/threads/{id}/messages (the captain turn runs in the background).
        /// </summary>
        /// <param name="id">Thread id.</param>
        /// <param name="content">Message text.</param>
        /// <param name="showThinking">Stream thinking for this turn.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskSendMessageResult?> SendAskMessageAsync(string id, string content, bool showThinking = false, CancellationToken token = default)
        {
            return PostAsync<AskSendMessageResult>($"/api/v1/ask/threads/{E(id)}/messages", new AskSendMessageRequest { Content = content ?? "", ShowThinking = showThinking }, null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelAskTurn</c>: POST `${askThreadPath(id)}/cancel`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task CancelAskTurnAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, $"/api/v1/ask/threads/{E(id)}/cancel", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>summarizeAskThread</c>: POST `${askThreadPath(id)}/summarize`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task SummarizeAskThreadAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, $"/api/v1/ask/threads/{E(id)}/summarize", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>markAskThreadRead</c>: POST `${askThreadPath(id)}/read`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task MarkAskThreadReadAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, $"/api/v1/ask/threads/{E(id)}/read", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>runAskQuickAction</c>: POST `${askThreadPath(id)}/actions`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="toolName">toolName.</param>
        /// <param name="args">args.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskActionProposal?> RunAskQuickActionAsync(string id, string toolName, ArmadaRawJson args, CancellationToken token = default)
        {
            return PostAsync<AskActionProposal>($"/api/v1/ask/threads/{E(id)}/actions", new { ToolName = toolName, Arguments = args }, ArmadaRequestOptions.WithTimeout(120000), token);
        }

        /// <summary>
        /// Dashboard <c>approveAskProposal</c>: POST `${askThreadPath(id)}/proposals/${encodeURIComponent(proposalId)}/approve`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="proposalId">proposalId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskActionProposal?> ApproveAskProposalAsync(string id, string proposalId, CancellationToken token = default)
        {
            return PostAsync<AskActionProposal>($"/api/v1/ask/threads/{E(id)}/proposals/{E(proposalId)}/approve", null, ArmadaRequestOptions.WithTimeout(120000), token);
        }

        /// <summary>
        /// Dashboard <c>rejectAskProposal</c>: POST `${askThreadPath(id)}/proposals/${encodeURIComponent(proposalId)}/reject`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="proposalId">proposalId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskActionProposal?> RejectAskProposalAsync(string id, string proposalId, CancellationToken token = default)
        {
            return PostAsync<AskActionProposal>($"/api/v1/ask/threads/{E(id)}/proposals/{E(proposalId)}/reject", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>getAskWorkSnapshot</c>: GET `${askThreadPath(id)}/work/${encodeURIComponent(workId)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="workId">workId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<AskWorkSnapshot?> GetAskWorkSnapshotAsync(string id, string workId, CancellationToken token = default)
        {
            return GetAsync<AskWorkSnapshot>($"/api/v1/ask/threads/{E(id)}/work/{E(workId)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getAskQuickActions</c>: GET /api/v1/ask/quick-actions. Tolerates a bare array or a QuickActions/Actions/Objects wrapper.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task<List<AskQuickAction>> GetAskQuickActionsAsync(CancellationToken token = default)
        {
            ArmadaRawJson? raw = await GetAsync<ArmadaRawJson>("/api/v1/ask/quick-actions", null, token).ConfigureAwait(false);
            string json = raw?.Json?.Trim() ?? "";
            if (json.StartsWith("[")) return ArmadaJson.Deserialize<List<AskQuickAction>>(json) ?? new List<AskQuickAction>();
            if (json.StartsWith("{"))
            {
                AskQuickActionEnvelope? envelope = ArmadaJson.Deserialize<AskQuickActionEnvelope>(json);
                if (envelope != null) return envelope.QuickActions ?? envelope.Actions ?? envelope.Objects ?? new List<AskQuickAction>();
            }

            return new List<AskQuickAction>();
        }

        #endregion
    }
}
