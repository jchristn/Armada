namespace Armada.Server
{
    using System;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;
    using Armada.Core.Harbor;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The server-side Harbor link endpoint. Accepts the client-to-server WebSocket a Harbor dials, reads
    /// its handshake, registers it with the connection manager, acknowledges with the advertised MCP URL,
    /// and then feeds every subsequent message to the manager until the link closes.
    /// </summary>
    public class HarborLinkEndpoint
    {
        #region Private-Members

        private readonly string _Header = "[HarborLink] ";
        private readonly HarborConnectionManager _Manager;
        private readonly HarborServerSettings _Settings;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="manager">Harbor connection manager.</param>
        /// <param name="settings">Harbor server settings.</param>
        /// <param name="logging">Logging module.</param>
        public HarborLinkEndpoint(HarborConnectionManager manager, HarborServerSettings settings, LoggingModule logging)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Watson WebSocket handler for the Harbor link. Registered on the main server at the configured
        /// link path.
        /// </summary>
        /// <param name="ctx">HTTP context of the upgrade.</param>
        /// <param name="session">WebSocket session.</param>
        public async Task HandleWebSocketAsync(HttpContextBase ctx, WebSocketSession session)
        {
            _Logging.Info(_Header + "link opened from " + session.RemoteIp + ":" + session.RemotePort);
            string? harborId = null;

            try
            {
                await foreach (WebSocketMessage message in session.ReadMessagesAsync(ctx.Token))
                {
                    if (message.MessageType != WebSocketMessageType.Text || String.IsNullOrEmpty(message.Text)) continue;

                    HarborMessage parsed;
                    try
                    {
                        parsed = HarborProtocol.Deserialize(message.Text);
                    }
                    catch (FormatException e)
                    {
                        _Logging.Warn(_Header + "dropping malformed message: " + e.ToString());
                        continue;
                    }

                    if (harborId == null)
                    {
                        if (parsed is not HarborHandshake handshake)
                        {
                            await SendAsync(session, new HarborError { Message = "The first message on a Harbor link must be a handshake." }).ConfigureAwait(false);
                            break;
                        }

                        string? denyReason = Authorize(ctx);
                        if (denyReason != null)
                        {
                            await SendAsync(session, new HarborHandshakeAck { CorrelationId = handshake.CorrelationId, Accepted = false, Reason = denyReason }).ConfigureAwait(false);
                            break;
                        }

                        harborId = handshake.HarborId;
                        HarborSendDelegate send = (outbound, token) => SendAsync(session, outbound);
                        HarborHandshakeAck ack = await _Manager.OnHandshakeAsync(handshake, ResolveTenantId(ctx), ResolveUserId(ctx), send, ctx.Token).ConfigureAwait(false);
                        await SendAsync(session, ack).ConfigureAwait(false);
                        if (!ack.Accepted) break;
                        continue;
                    }

                    await _Manager.OnMessageAsync(harborId, parsed, ctx.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Server shutting down or link closed.
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "link error: " + e.ToString());
            }
            finally
            {
                if (harborId != null)
                    await _Manager.OnDisconnectedAsync(harborId, CancellationToken.None).ConfigureAwait(false);
                _Logging.Info(_Header + "link closed from " + session.RemoteIp + ":" + session.RemotePort);
            }
        }

        #endregion

        #region Private-Methods

        private static Task SendAsync(WebSocketSession session, HarborMessage message)
        {
            return session.SendTextAsync(HarborProtocol.Serialize(message));
        }

        private string? Authorize(HttpContextBase ctx)
        {
            if (!_Settings.RequireAuth) return null;

            // Full credential/signed-request validation on the upgrade is a follow-up; for now require the
            // presence of an access key when authentication is enabled.
            string? accessKey = ctx.Request.Headers.Get("x-access-key");
            if (String.IsNullOrWhiteSpace(accessKey))
                return "Authentication is required on this Admiral: present a Harbor credential (x-access-key).";
            return null;
        }

        private static string? ResolveTenantId(HttpContextBase ctx)
        {
            return ctx.Request.Headers.Get("x-tenant-guid");
        }

        private static string? ResolveUserId(HttpContextBase ctx)
        {
            return null;
        }

        #endregion
    }
}
