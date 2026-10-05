namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text.Json.Nodes;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Canned Ask Armada server state for TUI tests: threads, messages, proposals, tracked work, captains, and the
    /// routes the Ask screen calls, plus builders for scripted <c>ask.*</c> socket events. Responses are serialized
    /// with the client's JSON options (PascalCase, string enums), like the real server.
    /// </summary>
    public sealed class AskFixtures
    {
        #region Public-Members

        /// <summary>
        /// Stub handler (signed-in server plus the Ask routes).
        /// </summary>
        public StubHttpHandler Stub { get; }

        /// <summary>
        /// Threads returned by enumerate.
        /// </summary>
        public List<AskThread> Threads { get; } = new List<AskThread>();

        /// <summary>
        /// Messages per thread.
        /// </summary>
        public Dictionary<string, List<AskMessage>> Messages { get; } = new Dictionary<string, List<AskMessage>>(StringComparer.Ordinal);

        /// <summary>
        /// Tracked work per thread.
        /// </summary>
        public Dictionary<string, List<AskTrackedWork>> Work { get; } = new Dictionary<string, List<AskTrackedWork>>(StringComparer.Ordinal);

        /// <summary>
        /// Pending proposals per thread.
        /// </summary>
        public Dictionary<string, List<AskActionProposal>> Pending { get; } = new Dictionary<string, List<AskActionProposal>>(StringComparer.Ordinal);

        /// <summary>
        /// Captains.
        /// </summary>
        public List<Captain> Captains { get; } = new List<Captain>();

        /// <summary>
        /// Older pages served for BeforeSequence requests (thread id to messages).
        /// </summary>
        public Dictionary<string, List<AskMessage>> OlderPages { get; } = new Dictionary<string, List<AskMessage>>(StringComparer.Ordinal);

        /// <summary>
        /// HasMore flag for the newest page per thread.
        /// </summary>
        public Dictionary<string, bool> HasMore { get; } = new Dictionary<string, bool>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build a stub with one captain and the Ask routes.
        /// </summary>
        public AskFixtures()
        {
            Stub = TuiFixtures.SignedInServer();
            Captain c = new Captain();
            c.Id = "cpt_1";
            c.Name = "claude-1";
            c.Runtime = AgentRuntimeEnum.ClaudeCode;
            Captains.Add(c);
            Stub.On("GET", "/api/v1/captains", body => Ok(Page(Captains)));
            Stub.On("GET", "/api/v1/captains/cpt_1/tools", body => Ok("{\"CaptainId\":\"cpt_1\",\"CaptainName\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"ArmadaToolCount\":0}"));
            Stub.On("GET", "/api/v1/ask/quick-actions", body => Ok("[]"));
            Stub.On("POST", "/api/v1/ask/threads/enumerate", body => Ok(Page(Threads)));
            Stub.On("POST", "/api/v1/ask/threads", body =>
            {
                AskThread t = Thread("ath_new", "New conversation");
                t.CaptainId = "cpt_1";
                Threads.Insert(0, t);
                return StubHttpHandler.Response(HttpStatusCode.Created, ArmadaJson.Serialize(t));
            });
            Stub.On("GET", "/api/v1/vessels", body => Ok(Page(new List<Vessel> { Vessel("vsl_b", "Beta"), Vessel("vsl_a", "Alpha") })));
            Stub.On("GET", "/api/v1/pipelines", body => Ok(Page(new List<Pipeline>())));
            Stub.On("POST", "/api/v1/fleet-actions/enumerate", body => Ok("{\"Success\":true,\"PageNumber\":1,\"PageSize\":500,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[{\"Id\":\"fa_1\",\"Name\":\"Bump deps\"}]}"));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a thread with messages and register its routes.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="messages">Newest page.</param>
        /// <returns>This.</returns>
        public AskFixtures AddThread(AskThread thread, params AskMessage[] messages)
        {
            Threads.Add(thread);
            Messages[thread.Id] = messages.ToList();
            string id = thread.Id;
            Stub.On("GET", "/api/v1/ask/threads/" + id, body =>
            {
                AskThreadDetail detail = new AskThreadDetail();
                detail.Thread = Threads.First(t => t.Id == id);
                detail.TrackedWork = Work.TryGetValue(id, out List<AskTrackedWork>? w) ? w : new List<AskTrackedWork>();
                detail.PendingProposals = Pending.TryGetValue(id, out List<AskActionProposal>? p) ? p : new List<AskActionProposal>();
                return Ok(ArmadaJson.Serialize(detail));
            });
            Stub.On("POST", "/api/v1/ask/threads/" + id + "/messages/enumerate", body =>
            {
                bool older = JsonHelper.Deserialize<AskMessageEnumerateQuery>(body).BeforeSequence.HasValue;
                AskMessagePage page = new AskMessagePage();
                if (older)
                {
                    page.Messages = OlderPages.TryGetValue(id, out List<AskMessage>? o) ? o : new List<AskMessage>();
                    page.HasMore = false;
                }
                else
                {
                    page.Messages = Messages[id];
                    page.HasMore = HasMore.TryGetValue(id, out bool more) && more;
                }

                return Ok(ArmadaJson.Serialize(page));
            });
            Stub.On("POST", "/api/v1/ask/threads/" + id + "/read", body => Ok("{}"));
            Stub.On("POST", "/api/v1/ask/threads/" + id + "/messages", body => StubHttpHandler.Response(HttpStatusCode.Accepted, "{\"MessageId\":\"amg_sent\",\"TurnId\":\"atn_1\"}"));
            Stub.On("POST", "/api/v1/ask/threads/" + id + "/cancel", body => Ok("{}"));
            Stub.On("POST", "/api/v1/ask/threads/" + id + "/summarize", body => StubHttpHandler.Response(HttpStatusCode.Accepted, "{}"));
            Stub.On("PUT", "/api/v1/ask/threads/" + id, body =>
            {
                AskThread t = Threads.First(x => x.Id == id);
                AskThreadUpdateRequest patch = JsonHelper.Deserialize<AskThreadUpdateRequest>(body);
                if (patch.Title != null) t.Title = patch.Title;
                if (patch.Pinned.HasValue) t.Pinned = patch.Pinned.Value;
                if (patch.Archived.HasValue) t.Archived = patch.Archived.Value;
                if (patch.AutoApprove.HasValue) t.AutoApprove = patch.AutoApprove.Value;
                if (JsonShape.TopLevelProperty(body, "CaptainId") != null) t.CaptainId = patch.CaptainId;
                return Ok(ArmadaJson.Serialize(t));
            });
            Stub.On("DELETE", "/api/v1/ask/threads/" + id, body =>
            {
                Threads.RemoveAll(x => x.Id == id);
                return StubHttpHandler.Response(HttpStatusCode.NoContent, "");
            });
            return this;
        }

        /// <summary>
        /// Serve approve and reject for a proposal, returning it with the decided status.
        /// </summary>
        /// <param name="proposal">Proposal.</param>
        /// <returns>This.</returns>
        public AskFixtures Decisions(AskActionProposal proposal)
        {
            string path = "/api/v1/ask/threads/" + proposal.ThreadId + "/proposals/" + proposal.Id;
            Stub.On("POST", path + "/approve", body =>
            {
                AskActionProposal p = Proposal(proposal.Id, proposal.ThreadId, proposal.ToolName, AskProposalStatusEnum.Executed);
                p.ResultText = "{\"VoyageId\":\"vyg_1\"}";
                p.ExecutedUtc = DateTime.UtcNow;
                return Ok(ArmadaJson.Serialize(p));
            });
            Stub.On("POST", path + "/reject", body => Ok(ArmadaJson.Serialize(Proposal(proposal.Id, proposal.ThreadId, proposal.ToolName, AskProposalStatusEnum.Rejected))));
            return this;
        }

        /// <summary>
        /// A thread.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="title">Title.</param>
        /// <returns>Thread.</returns>
        public static AskThread Thread(string id, string title)
        {
            AskThread t = new AskThread();
            t.Id = id;
            t.Title = title;
            t.CaptainId = "cpt_1";
            t.LastMessageUtc = DateTime.UtcNow.AddMinutes(-5);
            return t;
        }

        /// <summary>
        /// A message.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="sequence">Sequence.</param>
        /// <param name="role">Role.</param>
        /// <param name="kind">Kind.</param>
        /// <param name="text">Text.</param>
        /// <returns>Message.</returns>
        public static AskMessage Message(string id, string threadId, int sequence, AskMessageRoleEnum role, AskMessageKindEnum kind, string text)
        {
            AskMessage m = new AskMessage();
            m.Id = id;
            m.ThreadId = threadId;
            m.Sequence = sequence;
            m.Role = role;
            m.Kind = kind;
            m.ContentText = text;
            m.CreatedUtc = DateTime.UtcNow.AddMinutes(-1);
            return m;
        }

        /// <summary>
        /// A proposal.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="tool">Tool.</param>
        /// <param name="status">Status.</param>
        /// <returns>Proposal.</returns>
        public static AskActionProposal Proposal(string id, string threadId, string tool, AskProposalStatusEnum status)
        {
            AskActionProposal p = new AskActionProposal();
            p.Id = id;
            p.ThreadId = threadId;
            p.ToolName = tool;
            p.ArgumentsText = "{\"title\":\"Fix tables\",\"vesselId\":\"vsl_a\",\"missions\":[{\"title\":\"Fix tables\",\"description\":\"Fix tables\"}]}";
            p.SummaryText = "Dispatch voyage \"Fix tables\" to Alpha (1 mission)";
            p.Status = status;
            p.ExpiresUtc = status == AskProposalStatusEnum.Pending ? DateTime.UtcNow.AddMinutes(59) : (DateTime?)null;
            return p;
        }

        /// <summary>
        /// A voyage snapshot with missions in the given statuses.
        /// </summary>
        /// <param name="workId">Tracked work id.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="status">Voyage status.</param>
        /// <param name="active">Still active.</param>
        /// <param name="missionStatuses">Mission statuses.</param>
        /// <returns>Snapshot.</returns>
        public static AskWorkSnapshot VoyageSnapshot(string workId, string threadId, string status, bool active, params string[] missionStatuses)
        {
            AskWorkSnapshot s = new AskWorkSnapshot();
            s.TrackedWorkId = workId;
            s.ThreadId = threadId;
            s.EntityType = AskTrackedEntityTypeEnum.Voyage;
            s.EntityId = "vyg_1";
            s.Title = "Fix tables";
            s.Status = status;
            s.State = active ? AskTrackedWorkStateEnum.Active : AskTrackedWorkStateEnum.Succeeded;
            s.CapturedUtc = DateTime.UtcNow;
            int i = 0;
            foreach (string ms in missionStatuses)
            {
                i++;
                AskWorkMissionSnapshot m = new AskWorkMissionSnapshot();
                m.Id = "msn_" + i;
                m.Title = "Mission " + i;
                m.Status = ms;
                m.CaptainName = "claude-2";
                m.BranchName = "armada/claude-2/msn_" + i;
                if (ms == "Landed") m.LandingOutcome = "Landed";
                if (ms == "Failed") m.FailureReason = "Tests failed";
                if (i == 1) m.PrUrl = "https://example.com/pr/1";
                s.Missions.Add(m);
            }

            return s;
        }

        /// <summary>
        /// A scripted socket message.
        /// </summary>
        /// <param name="type">Event type.</param>
        /// <param name="data">Payload object (serialized with the client's options).</param>
        /// <returns>Message.</returns>
        public static ArmadaSocketMessage Event(string type, object data)
        {
            JsonObject root = new JsonObject();
            root["type"] = type;
            root["data"] = JsonNode.Parse(ArmadaJson.Serialize(data));
            return ArmadaSocketMessage.Parse(root.ToJsonString())!;
        }

        /// <summary>
        /// A scripted socket message from raw payload JSON.
        /// </summary>
        /// <param name="type">Event type.</param>
        /// <param name="json">Payload JSON.</param>
        /// <returns>Message.</returns>
        public static ArmadaSocketMessage EventJson(string type, string json)
        {
            return ArmadaSocketMessage.Parse("{\"type\":\"" + type + "\",\"data\":" + json + "}")!;
        }

        #endregion

        #region Private-Methods

        private static System.Net.Http.HttpResponseMessage Ok(string json)
        {
            return StubHttpHandler.Response(HttpStatusCode.OK, json);
        }

        private static string Page<T>(List<T> items)
        {
            EnumerationResult<T> r = new EnumerationResult<T>();
            r.Objects = items;
            r.PageNumber = 1;
            r.PageSize = 50;
            r.TotalPages = 1;
            r.TotalRecords = items.Count;
            return ArmadaJson.Serialize(r);
        }

        private static Vessel Vessel(string id, string name)
        {
            Vessel v = new Vessel();
            v.Id = id;
            v.Name = name;
            return v;
        }

        #endregion
    }
}
