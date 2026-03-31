namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using SwiftStack;
    using SwiftStack.Websockets;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// WebSocket hub for real-time event broadcasting.
    /// Supports subscribe/command routes and broadcasts mission/captain state changes.
    /// Provides full command parity with the REST API and MCP tools.
    /// </summary>
    public class ArmadaWebSocketHub
    {
        #region Private-Members

        private string _Header = "[WebSocketHub] ";
        private LoggingModule _Logging;
        private SwiftStackApp _App;
        private WebsocketsApp _WsApp;
        private IAdmiralService _Admiral;
        private WebSocketCommandHandler _CommandHandler;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the WebSocket hub.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="app">SwiftStack application instance.</param>
        /// <param name="port">WebSocket port.</param>
        /// <param name="ssl">Whether to enable SSL/TLS.</param>
        /// <param name="admiral">Admiral service for command handling.</param>
        /// <param name="database">Database driver for data access.</param>
        /// <param name="mergeQueue">Merge queue service.</param>
        /// <param name="settings">Optional Armada settings for log/diff paths.</param>
        /// <param name="git">Optional git service for diff generation.</param>
        /// <param name="onStop">Optional callback invoked when stop_server is requested.</param>
        public ArmadaWebSocketHub(LoggingModule logging, SwiftStackApp app, int port, bool ssl, IAdmiralService admiral, DatabaseDriver database, IMergeQueueService mergeQueue, ArmadaSettings? settings = null, IGitService? git = null, Action? onStop = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _App = app ?? throw new ArgumentNullException(nameof(app));

            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));

            _CommandHandler = new WebSocketCommandHandler(
                _Admiral,
                database ?? throw new ArgumentNullException(nameof(database)),
                mergeQueue ?? throw new ArgumentNullException(nameof(mergeQueue)),
                settings,
                git,
                onStop,
                _JsonOptions,
                BroadcastMissionChange,
                BroadcastVoyageChange);

            _WsApp = new WebsocketsApp(_App);
            _WsApp.WebsocketSettings = new WatsonWebsocket.WebsocketSettings
            {
                Hostnames = new List<string> { "localhost" },
                Port = port,
                Ssl = ssl
            };
            _WsApp.QuietStartup = true;

            _WsApp.OnConnection += (sender, args) =>
            {
                _Logging.Info(_Header + "client connected: " + args.Client.IpPort);
            };

            _WsApp.OnDisconnection += (sender, args) =>
            {
                _Logging.Info(_Header + "client disconnected: " + args.Client.IpPort);
            };

            RegisterRoutes();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the WebSocket server.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task StartAsync(CancellationToken token = default)
        {
            await _WsApp.Run(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Broadcast a mission state change to all connected clients.
        /// </summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="status">New status.</param>
        /// <param name="title">Mission title.</param>
        public void BroadcastMissionChange(string missionId, string status, string? title = null)
        {
            object payload = new
            {
                type = "mission.changed",
                data = new
                {
                    id = missionId,
                    title = title,
                    status = status
                },
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a voyage state change to all connected clients.
        /// </summary>
        /// <param name="voyageId">Voyage ID.</param>
        /// <param name="status">New status.</param>
        /// <param name="title">Voyage title.</param>
        public void BroadcastVoyageChange(string voyageId, string status, string? title = null)
        {
            object payload = new
            {
                type = "voyage.changed",
                data = new
                {
                    id = voyageId,
                    title = title,
                    status = status
                },
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a captain state change to all connected clients.
        /// </summary>
        /// <param name="captainId">Captain ID.</param>
        /// <param name="state">New state.</param>
        /// <param name="name">Captain name.</param>
        public void BroadcastCaptainChange(string captainId, string state, string? name = null)
        {
            object payload = new
            {
                type = "captain.changed",
                data = new
                {
                    id = captainId,
                    name = name,
                    state = state
                },
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a generic event to all connected clients.
        /// </summary>
        /// <param name="eventType">Event type string.</param>
        /// <param name="message">Event message.</param>
        /// <param name="data">Optional additional data.</param>
        public void BroadcastEvent(string eventType, string message, object? data = null)
        {
            object payload = new
            {
                type = eventType,
                message = message,
                data = data,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        #endregion

        #region Private-Methods

        private void RegisterRoutes()
        {
            // Subscribe route — clients connect here to receive broadcasts.
            // On connect, send current status as initial payload.
            _WsApp.AddRoute("subscribe", async (msg, token) =>
            {
                try
                {
                    ArmadaStatus status = await _Admiral.GetStatusAsync().ConfigureAwait(false);
                    object initial = new
                    {
                        type = "status.snapshot",
                        data = status,
                        timestamp = DateTime.UtcNow
                    };

                    string json = JsonSerializer.Serialize(initial, _JsonOptions);
                    await msg.RespondAsync(json).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error sending initial status: " + ex.Message);
                }
            });

            // Command route — clients can send commands to the Admiral.
            _WsApp.AddRoute("command", async (msg, token) =>
            {
                try
                {
                    string body = msg.DataAsString();
                    WebSocketCommand command = JsonSerializer.Deserialize<WebSocketCommand>(body, _JsonOptions) ?? new WebSocketCommand();
                    string action = command.Action;

                    object result = await _CommandHandler.HandleCommandAsync(action, command, body).ConfigureAwait(false);

                    string json = JsonSerializer.Serialize(result, _JsonOptions);
                    await msg.RespondAsync(json).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error handling command: " + ex.Message);
                    string errorJson = JsonSerializer.Serialize(
                        new { type = "command.error", error = ex.Message }, _JsonOptions);
                    await msg.RespondAsync(errorJson).ConfigureAwait(false);
                }
            });

            // Default route for unspecified routes
            _WsApp.DefaultRoute = (sender, msg) =>
            {
                string json = JsonSerializer.Serialize(
                    new { type = "error", message = "Send a message with route 'subscribe' or 'command'" }, _JsonOptions);
                msg.RespondAsync(json).Wait();
            };

            // Not-found route
            _WsApp.NotFoundRoute = (sender, msg) =>
            {
                string json = JsonSerializer.Serialize(
                    new { type = "error", message = "Unknown route: " + (msg.Route ?? "null") }, _JsonOptions);
                msg.RespondAsync(json).Wait();
            };
        }

        private void BroadcastEvent(object payload)
        {
            try
            {
                if (_WsApp.WebsocketServer == null) return;

                string json = JsonSerializer.Serialize(payload, _JsonOptions);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

                List<WatsonWebsocket.ClientMetadata> clients = _WsApp.WebsocketServer.ListClients().ToList();
                foreach (WatsonWebsocket.ClientMetadata client in clients)
                {
                    try
                    {
                        _WsApp.WebsocketServer.SendAsync(client.Guid, bytes).Wait();
                    }
                    catch
                    {
                        // Client may have disconnected
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "broadcast error: " + ex.Message);
            }
        }

        #endregion
    }
}
