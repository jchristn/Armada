namespace Armada.Server
{
    using System;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
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
        private readonly Func<string?, string?, string?, Task<AuthContext>>? _Authenticate;
        private readonly bool _ListenerIsLoopback;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="manager">Harbor connection manager.</param>
        /// <param name="settings">Harbor server settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="authenticate">Credential validator (Authorization header, X-Token, X-Api-Key), as for REST.</param>
        /// <param name="listenerIsLoopback">Whether the Admiral's listener is bound to a loopback hostname.</param>
        public HarborLinkEndpoint(
            HarborConnectionManager manager,
            HarborServerSettings settings,
            LoggingModule logging,
            Func<string?, string?, string?, Task<AuthContext>>? authenticate = null,
            bool listenerIsLoopback = true)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Authenticate = authenticate;
            _ListenerIsLoopback = listenerIsLoopback;
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
            HarborSendDelegate? link = null;

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

                        HarborLinkIdentity identity = await AuthorizeAsync(ctx, session.RemoteIp).ConfigureAwait(false);
                        if (identity.DenyReason != null)
                        {
                            _Logging.Warn(_Header + "refused link from " + session.RemoteIp + ": " + identity.DenyReason);
                            await SendAsync(session, new HarborHandshakeAck { CorrelationId = handshake.CorrelationId, Accepted = false, Reason = identity.DenyReason }).ConfigureAwait(false);
                            break;
                        }

                        HarborSendDelegate send = (outbound, token) => SendAsync(session, outbound);
                        link = send;
                        HarborHandshakeAck ack = await _Manager.OnHandshakeAsync(handshake, identity.TenantId, identity.UserId, send, ctx.Token).ConfigureAwait(false);
                        await SendAsync(session, ack).ConfigureAwait(false);

                        // Only an accepted link owns the id: a refused link must not mark the registered Harbor
                        // disconnected when it closes.
                        if (!ack.Accepted) break;
                        harborId = handshake.HarborId;
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
                // Pass this socket's link so a close that arrives after the Harbor already reconnected on a new socket
                // does not tear down the new connection.
                if (harborId != null)
                    await _Manager.OnDisconnectedAsync(harborId, link, CancellationToken.None).ConfigureAwait(false);
                _Logging.Info(_Header + "link closed from " + session.RemoteIp + ":" + session.RemotePort);
            }
        }

        #endregion

        #region Private-Methods

        private static Task SendAsync(WebSocketSession session, HarborMessage message)
        {
            return session.SendTextAsync(HarborProtocol.Serialize(message));
        }

        /// <summary>
        /// Authenticate the Harbor on the link upgrade. A presented credential (x-access-key, or an Authorization
        /// header) must be a valid Armada credential (bearer token, session token, or the local API key); the Harbor
        /// then registers under that credential's tenant and user (a global admin may name a tenant with
        /// x-tenant-guid). Without a credential the link is accepted only when Harbor.RequireAuth is off, the Admiral
        /// listens on a loopback hostname, and the Harbor connects from loopback (the local Harbor app); it then keeps
        /// the tenant it names.
        /// </summary>
        private async Task<HarborLinkIdentity> AuthorizeAsync(HttpContextBase ctx, string? remoteIp)
        {
            string? accessKey = ctx.Request.Headers.Get("x-access-key");
            string? authorization = ctx.Request.Headers.Get("Authorization");
            string? requestedTenant = ctx.Request.Headers.Get("x-tenant-guid");
            bool presented = !String.IsNullOrWhiteSpace(accessKey) || !String.IsNullOrWhiteSpace(authorization);

            if (presented)
            {
                if (_Authenticate == null) return HarborLinkIdentity.Deny("This Admiral cannot validate Harbor credentials.");
                AuthContext auth = !String.IsNullOrWhiteSpace(authorization)
                    ? await _Authenticate(authorization, null, null).ConfigureAwait(false)
                    : await _Authenticate("Bearer " + accessKey!.Trim(), accessKey, accessKey).ConfigureAwait(false);
                if (!auth.IsAuthenticated || String.IsNullOrEmpty(auth.UserId) || !String.IsNullOrEmpty(auth.AskThreadId) || !String.IsNullOrEmpty(auth.MissionId))
                    return HarborLinkIdentity.Deny("The Harbor credential (x-access-key) is not a valid Armada credential.");

                string? tenantId = auth.IsAdmin && !String.IsNullOrWhiteSpace(requestedTenant) ? requestedTenant : auth.TenantId;
                return HarborLinkIdentity.Allow(tenantId, auth.UserId);
            }

            if (_Settings.RequireAuth)
                return HarborLinkIdentity.Deny("Authentication is required on this Admiral: present an Armada credential as x-access-key.");

            bool remoteIsLoopback = System.Net.IPAddress.TryParse(remoteIp ?? String.Empty, out System.Net.IPAddress? address) && System.Net.IPAddress.IsLoopback(address);
            if (!_ListenerIsLoopback || !remoteIsLoopback)
                return HarborLinkIdentity.Deny("A Harbor connecting from another host must present an Armada credential as x-access-key.");

            return HarborLinkIdentity.Allow(requestedTenant, null);
        }

        #endregion
    }
}
