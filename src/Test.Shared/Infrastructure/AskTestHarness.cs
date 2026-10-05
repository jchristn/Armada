namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services.Ask;
    using Armada.Core.Settings;
    using Armada.Server.Ask;
    using Voltaic.Core;

    /// <summary>
    /// In-process Ask Armada wiring over a test database: thread service, action service (approval gate) with stub MCP
    /// tools, turn coordinator with a stub captain runner, and work tracker. Events are recorded instead of sent to
    /// sockets.
    /// </summary>
    public sealed class AskTestHarness : IDisposable
    {
        /// <summary>
        /// Test database.
        /// </summary>
        public TestDatabase Db { get; }

        /// <summary>
        /// Logging module (console disabled).
        /// </summary>
        public SyslogLogging.LoggingModule Logging { get; } = CreateLogging();

        /// <summary>
        /// Settings.
        /// </summary>
        public ArmadaSettings Settings { get; } = new ArmadaSettings();

        /// <summary>
        /// Thread service.
        /// </summary>
        public AskThreadService Threads { get; }

        /// <summary>
        /// Action service (approval gate).
        /// </summary>
        public AskActionService Actions { get; }

        /// <summary>
        /// Turn coordinator.
        /// </summary>
        public AskTurnCoordinator Turns { get; }

        /// <summary>
        /// Work tracker.
        /// </summary>
        public AskWorkTracker Tracker { get; }

        /// <summary>
        /// Stub captain runner.
        /// </summary>
        public StubAskTurnRunner Runner { get; } = new StubAskTurnRunner();

        /// <summary>
        /// Recorded owner-scoped events.
        /// </summary>
        public List<AskRecordedEvent> Events { get; } = new List<AskRecordedEvent>();

        /// <summary>
        /// Invocations of stub tools.
        /// </summary>
        public List<AskToolInvocation> Invocations { get; } = new List<AskToolInvocation>();

        /// <summary>
        /// Gated handlers by tool name (what the MCP server would expose).
        /// </summary>
        public Dictionary<string, Func<JsonElement?, Task<object>>> Gated { get; } = new Dictionary<string, Func<JsonElement?, Task<object>>>(StringComparer.Ordinal);

        private AskTestHarness(TestDatabase db)
        {
            Db = db;
            Threads = new AskThreadService(db.Driver, Settings, Logging);
            Actions = new AskActionService(db.Driver, Threads, Settings, Logging);
            Turns = new AskTurnCoordinator(db.Driver, Threads, Runner, new Armada.Core.Services.SessionTokenService(), null, Settings, Logging);
            Tracker = new AskWorkTracker(db.Driver, Threads, Settings, Logging);
            Threads.OnUserEvent = (tenantId, userId, eventType, payload) =>
            {
                lock (Events) Events.Add(new AskRecordedEvent { TenantId = tenantId, UserId = userId, EventType = eventType, Payload = payload });
            };
            Threads.ActiveTurnResolver = Turns.ActiveTurnId;
            Actions.OnWorkLinked = Tracker.OnWorkLinkedAsync;
            Actions.OnProposalApproved = Turns.StartFollowUpAsync;
            Tracker.Narrate = Turns.NarrateAsync;
            Tracker.ExpireProposals = Actions.ExpireDueAsync;

            RegisterStub("status", _ => new { Captains = 0, Health = "ok" });
            RegisterStub("enumerate", _ => new { Objects = new List<object>() });
            RegisterStub("dispatch", _ => new { Id = "vyg_stub" + Guid.NewGuid().ToString("N").Substring(0, 8), Title = "stub voyage" });
            RegisterStub("cancel_voyage", _ => new { Status = "Cancelled" });
            RegisterStub("delete_vessel", _ => McpToolError.NotFound("Vessel not found"));
            RegisterStub("explode", _ => throw new InvalidOperationException("boom"));
        }

        /// <summary>
        /// Create a harness over a fresh test database.
        /// </summary>
        /// <returns>The harness.</returns>
        public static async Task<AskTestHarness> CreateAsync()
        {
            TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            return new AskTestHarness(db);
        }

        /// <summary>
        /// Register a stub tool through the gate.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="result">Result factory taking the raw arguments JSON.</param>
        public void RegisterStub(string name, Func<string, object> result)
        {
            Func<JsonElement?, Task<object>> handler = (JsonElement? args) =>
            {
                Dictionary<string, string> claims = new Dictionary<string, string>();
                RpcCallContext? ctx = RpcCallContext.Current;
                if (ctx != null) foreach (KeyValuePair<string, string> kvp in ctx.Claims) claims[kvp.Key] = kvp.Value;
                string json = args.HasValue ? args.Value.GetRawText() : "{}";
                lock (Invocations) Invocations.Add(new AskToolInvocation { ToolName = name, ArgumentsJson = json, Claims = claims });
                return Task.FromResult(result(json));
            };
            Gated[name] = Actions.WrapTool(name, handler);
        }

        /// <summary>
        /// Call a gated tool the way the MCP server would for a request authenticated with the given claims.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="argumentsJson">Arguments JSON.</param>
        /// <param name="auth">Caller.</param>
        /// <param name="threadId">Thread the token is bound to, or null for a normal MCP call.</param>
        /// <returns>The handler result.</returns>
        public async Task<object> CallAsync(string name, string argumentsJson, AuthContext auth, string? threadId)
        {
            Dictionary<string, string> claims = new Dictionary<string, string>
            {
                ["tenantId"] = auth.TenantId ?? String.Empty,
                ["userId"] = auth.UserId ?? String.Empty,
                ["isAdmin"] = auth.IsAdmin ? "true" : "false",
                ["isTenantAdmin"] = auth.IsTenantAdmin ? "true" : "false"
            };
            if (threadId != null) claims["askThreadId"] = threadId;
            using (JsonDocument doc = JsonDocument.Parse(argumentsJson))
            using (RpcCallContext.Push(new RpcCallContext(auth.UserId, claims)))
            {
                return await Gated[name](doc.RootElement.Clone()).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A user identity in the default tenant.
        /// </summary>
        /// <param name="userId">User identifier.</param>
        /// <param name="tenantAdmin">Whether the user is a tenant admin.</param>
        /// <returns>The identity.</returns>
        public static AuthContext User(string userId, bool tenantAdmin = true)
        {
            return AuthContext.Authenticated(Constants.DefaultTenantId, userId, false, tenantAdmin, "Test");
        }

        /// <summary>
        /// Events of one type addressed to one user.
        /// </summary>
        /// <param name="userId">User.</param>
        /// <param name="eventType">Event type.</param>
        /// <returns>Matching events.</returns>
        public List<AskRecordedEvent> EventsFor(string userId, string eventType)
        {
            lock (Events) return Events.Where(e => e.UserId == userId && e.EventType == eventType).ToList();
        }

        /// <summary>
        /// Wait until a condition holds or a timeout elapses.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>True when the condition held.</returns>
        public static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromMilliseconds(timeoutMs));
            while (!deadline.Passed)
            {
                if (await condition().ConfigureAwait(false)) return true;
                await Task.Delay(25).ConfigureAwait(false);
            }

            return await condition().ConfigureAwait(false);
        }

        private static SyslogLogging.LoggingModule CreateLogging()
        {
            SyslogLogging.LoggingModule logging = new SyslogLogging.LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        /// <summary>
        /// Dispose the database.
        /// </summary>
        public void Dispose()
        {
            Db.Dispose();
        }
    }
}
