# Running the Admiral on Another Machine

This guide covers running the Armada Admiral on one machine (a server, a VM, a container host, a spare desktop) and
using it from others: the dashboard, the terminal UI, the `armada` CLI, MCP clients such as Claude Code, and Harbors
that run captains on the machines where your code lives.

How this guide was checked: every command and snippet below was run against a throwaway Admiral on a macOS host
(Armada 1.0.0, temporary `ARMADA_DATA_DIR`, ports 23210/23211 bound to `0.0.0.0` and to the host's LAN address, and a
loopback Admiral behind Caddy 2.10.2 with an internal TLS certificate), unless it is marked **Not run here**. The
examples use the default ports 7890/7891 and the placeholder name `armada.example.com`; substitute your own.

Contents:

1. [When to run the Admiral remotely](#1-when-to-run-the-admiral-remotely)
2. [Prepare the server](#2-prepare-the-server)
3. [TLS with a reverse proxy](#3-tls-with-a-reverse-proxy)
4. [Create credentials](#4-create-credentials)
5. [Connect each client](#5-connect-each-client)
6. [Docker](#6-docker)
7. [Armada.Proxy instead of open ports](#7-armadaproxy-instead-of-open-ports)
8. [Troubleshooting](#8-troubleshooting)
9. [CLI command reference for remote targets](#9-cli-command-reference-for-remote-targets)

## 1. When to run the Admiral remotely

Run the Admiral on your own machine (the default, `rest.hostname: localhost`) unless one of these applies:

- Several people, or several of your machines, should share one set of fleets, vessels, missions, and history.
- The Admiral should keep running (and keep dispatching, merging, and recording) while your laptop sleeps.
- You want the Admiral in Docker or on a server, with captains running on developer machines through Harbors
  ([HARBOR.md](HARBOR.md), split mode).

Keep in mind where work happens. A captain needs the code, git credentials, and the agent CLI's own login on the
machine that launches it. A remote Admiral launches captains on its own host (which then needs those), or on a
connected Harbor. The CLI and MCP clients only send requests; they do not run captains themselves.

If you only need a browser view of a machine behind NAT, [Armada.Proxy](#7-armadaproxy-instead-of-open-ports) avoids
opening any inbound port.

## 2. Prepare the server

### Change the default admin password first

The Admiral refuses to listen on a non-loopback address while the seeded `admin@armada` password (`password`) or the
seeded bearer token `default` is in use. The log line and console say:

```
Refusing to listen on non-loopback hostname '0.0.0.0' while default credentials are in use (user admin@armada in
tenant default still uses the default password; the seeded bearer token "default" is active). Set the
ARMADA_INITIAL_ADMIN_PASSWORD environment variable (first start), or start on localhost and change the admin password
(dashboard or PUT /api/v1/account/password), or set AllowDefaultCredentialsOnNetwork to true to accept the risk.
```

Pick one:

- First start of a new install: set `ARMADA_INITIAL_ADMIN_PASSWORD` (8 or more characters) in the server's
  environment. Changing the default password also retires the seeded `default` bearer token.
- Existing install: start it once on `localhost`, sign in to the dashboard as `admin@armada`, and change the password
  (or `PUT /api/v1/account/password`), then switch the hostname.

Do not set `AllowDefaultCredentialsOnNetwork` on a reachable machine.

### Set the hostname and ports

In the Admiral's `settings.json` (`~/.armada/settings.json`, or `$ARMADA_DATA_DIR/settings.json`):

```json
{
  "admiralPort": 7890,
  "mcpPort": 7891,
  "rest": { "hostname": "0.0.0.0" }
}
```

`rest.hostname` applies to both listeners: REST, the dashboard, and the WebSockets on `admiralPort`, and MCP on
`mcpPort`. The choices:

| `rest.hostname` | Reachable from | MCP `Host` header the listener accepts (tested) |
|---|---|---|
| `localhost` (default) | this machine only | `localhost:<port>` only. On macOS the MCP listener for `localhost` bound `[::1]` only, so `127.0.0.1:7891` was refused. |
| `127.0.0.1` | this machine only | `127.0.0.1:<port>` only (`localhost` gets 404) |
| `0.0.0.0`, `*`, or `+` | every interface | any `Host` (tested: the LAN IP, `localhost`, `armada.example.com`, and an arbitrary name all returned 200) |
| a specific address, for example `192.168.1.20` | that interface only | that address only, with or without the port; `localhost` and other names get 404. The REST listener on that address accepted any `Host`. |

`0.0.0.0` is the simplest choice for a server that clients reach directly. Behind a reverse proxy on the same host,
keep `localhost` and follow [section 3](#3-tls-with-a-reverse-proxy).

The settings file is read at start; restart the Admiral after editing it.

### Close the MCP loopback exemption when anything proxies to it

An MCP request without a credential is accepted (as the default tenant's tenant admin) when
`mcp.allowUnauthenticatedLoopback` is true (the default), `rest.hostname` is a loopback name, and the caller connects
from loopback. A reverse proxy on the same host connects from loopback, so every request it forwards qualifies. Tested
through Caddy: an MCP `initialize` with no credential returned **200** until this was set, and 401 after:

```json
{
  "rest": { "hostname": "localhost" },
  "mcp": { "allowUnauthenticatedLoopback": false },
  "harbor": { "requireAuth": true }
}
```

`harbor.requireAuth: true` closes the matching Harbor exemption (a credential-less Harbor connecting from loopback is
otherwise accepted, without an owner). With `0.0.0.0` and no proxy neither exemption applies to remote callers, but
setting both is harmless and recommended on any shared Admiral.

### Firewall

Open only what clients need: the proxy's HTTPS port(s) when you use one (section 3), otherwise `admiralPort` and
`mcpPort`. Keep `9464` (Prometheus metrics, only when telemetry is on) private. **Not run here** (firewall changes
were not made on the test host); typical commands:

```bash
# Ubuntu (ufw)
sudo ufw allow 443/tcp
sudo ufw allow 7891/tcp
# RHEL/Fedora (firewalld)
sudo firewall-cmd --permanent --add-port=443/tcp --add-port=7891/tcp && sudo firewall-cmd --reload
```

### Run it as a service

Use `armada-server --install-service` (systemd, launchd, or a Windows service; see
[OPERATIONS.md](OPERATIONS.md#service-and-startup-registration)) or the scripts in
[RUN_ON_STARTUP.md](RUN_ON_STARTUP.md). Put `ARMADA_INITIAL_ADMIN_PASSWORD` (first start only) and `ARMADA_DATA_DIR`
(if used) in the service's environment. Check it from another machine with:

```bash
curl -fsS http://armada.example.com:7890/api/v1/status/health
```

The health endpoint needs no credential and reports the ports (`"Ports":{"Admiral":7890,"Mcp":7891}`).

## 3. TLS with a reverse proxy

The Admiral has no usable TLS of its own (see [OPERATIONS.md](OPERATIONS.md#tls)). Terminate TLS in a reverse proxy
on the same host, keep the Admiral on `localhost`, and set the two settings from
[section 2](#close-the-mcp-loopback-exemption-when-anything-proxies-to-it).

Caddy (obtains a certificate for the name automatically):

```caddyfile
armada.example.com {
	reverse_proxy localhost:7890
}

armada.example.com:7891 {
	reverse_proxy localhost:7891 {
		header_up Host localhost:7891
	}
}
```

What each part is for (each verified through Caddy with `tls internal` on ports 23253/23254):

- REST, the dashboard (`/dashboard`), and the WebSockets share the first site. Caddy forwards WebSocket upgrades
  without extra configuration: `GET /ws` and `GET /v1.0/harbor/connect` with `Upgrade: websocket` returned
  `101 Switching Protocols` through the proxy.
- MCP gets its own TLS port, 7891, so `armada mcp install` (which defaults the MCP URL to the Admiral's host on the
  MCP port) and the URLs in this guide line up. Any other port or a separate hostname works too; pass it with
  `--mcp-url`.
- `header_up Host localhost:7891` is required. The MCP listener answers only the `Host` it is bound to; without the
  rewrite the forwarded `Host: armada.example.com:7891` got **404**, with it **200**.
- Upstream `localhost`, not `127.0.0.1`: on the macOS test host the MCP listener for `localhost` bound only `[::1]`,
  and `reverse_proxy 127.0.0.1:7891` failed with `connection refused` (502).

The test configuration differed only in the site addresses and `tls internal` (plus `admin off` and
`skip_install_trust` in the global block, so the test did not touch the machine's trust store). Automatic public
certificates were **not run here**. For nginx or another proxy, apply the same rules: pass `Upgrade`/`Connection`
headers for `/ws` and `/v1.0/harbor/connect`, set the upstream `Host` for MCP to `localhost:7891`, and allow long-lived
connections (the dashboard WebSocket and Harbor link stay open; MCP uses streaming responses).

Behind the proxy, clients use `https://armada.example.com` for REST and `https://armada.example.com:7891/mcp` for MCP.

## 4. Create credentials

Give every person and every machine its own bearer token, so each can be revoked alone and actions are attributed to
a user. Tokens belong to a user; the user's role (admin, tenant admin, member) decides what the token may do.

Dashboard: sign in, open **Server**, tab **Credentials** (`/dashboard/server?tab=credentials`), and create a
credential; copy the bearer token when it is shown. (The page and route exist in the dashboard source; creating a
credential through the browser was **not run here**.)

REST (tested): sign in, then create a credential with the session token. Members create their own; an admin may set
`UserId` and `TenantId` to create one for someone else.

```bash
# 1. Sign in (returns a session token valid for 24 hours)
curl -sS -X POST https://armada.example.com/api/v1/authenticate \
  -H 'Content-Type: application/json' \
  -d '{"Email":"dev@example.com","TenantId":"default","Password":"..."}'
# -> {"Success":true,"Token":"<session token>","ExpiresUtc":"...","PasswordChangeRequired":false}

# 2. Create a bearer token for this laptop
curl -sS -X POST https://armada.example.com/api/v1/credentials \
  -H 'X-Token: <session token>' -H 'Content-Type: application/json' \
  -d '{"Name":"dev-laptop","Active":true}'
# -> {"Id":"crd_...","UserId":"usr_...","Name":"dev-laptop","BearerToken":"<bearer token>","Active":true,...}
```

Use it as `Authorization: Bearer <bearer token>`. Deactivate or delete the credential (dashboard, or
`PUT`/`DELETE /api/v1/credentials/{id}`) to revoke it. The Admiral's own `apiKey` from its `settings.json` is a
system-wide admin key: do not hand it out.

## 5. Connect each client

### Dashboard

Browse to `https://armada.example.com/dashboard` (or `http://<host>:7890/dashboard` without a proxy) and sign in.
Tested: `GET /dashboard` from another address on the LAN returned the dashboard (200).

### Terminal UI

```bash
armada tui --server https://armada.example.com
armada tui --profile prod
ARMADA_SERVER_URL=https://armada.example.com ARMADA_TOKEN=<bearer token> armada tui
```

`--server` saves a profile named after the host; sign in with email and password or an API key/bearer token. The TUI
and the CLI share profiles (`tui.json`) and stored tokens (OS keychain, or a 0600 file), so a profile added with
`armada profile add` opens in the TUI, and a TUI sign-in is available to the CLI. See [TUI.md](TUI.md). (The TUI was
**not run interactively here**; the shared store is covered by the automated tests.)

### The `armada` CLI

Every command that talks to the Admiral takes `--server`, `--token`, and `--profile`. The target is resolved in this
order:

1. `--server <url>` or `--profile <name>` on the command line (`--profile local` means this machine's Admiral);
2. `ARMADA_SERVER_URL` (or the TUI's `ARMADA_URL`) in the environment;
3. the active profile, shared with the TUI (`armada profile use <name>`);
4. this machine's Admiral at `http://127.0.0.1:<admiralPort>` with the local API key (the behavior before profiles).

The credential follows the same order: `--token`, then `ARMADA_TOKEN`, then the token stored for the profile. For
this machine's Admiral the CLI keeps sending the local API key unless you pass `--token`.

One-off commands:

```bash
armada mission list --server https://armada.example.com --token <bearer token>

export ARMADA_SERVER_URL=https://armada.example.com
export ARMADA_TOKEN=<bearer token>
armada status
```

Saved profiles (the token goes to the OS keychain or a 0600 file, never into `tui.json`; omit `--token` on a
terminal to be prompted for it):

```bash
armada profile add prod --server https://armada.example.com --token <bearer token>
armada profile list
armada mission list --profile prod      # one command
armada profile use prod                 # make it the default target for the CLI and the TUI
armada config show                      # the "CLI Target" table shows url, source, profile, and credential source
armada profile use local                # back to this machine's Admiral
armada profile remove prod              # forgets the profile and its stored token
```

Tested output (`profile list`, `config show` against the throwaway Admiral; table borders abridged):

```
Admiral Profiles
| * | local | this machine's Admiral      | local  | local API key |
|   | lab   | http://192.168.86.151:23210 | remote | stored        |

CLI Target
| url        | http://192.168.86.151:23210    |
| source     | Profile                        |
| profile    | lab                            |
| local      | no                             |
| credential | token stored for profile 'lab' |
```

What the CLI does differently for a remote target:

- When the target comes from the environment or the active profile, each command prints `armada: target is ...` on
  stderr, so you always see which Admiral you are acting on. A credential sent over plain `http://` to a non-loopback
  host prints a one-line warning to use HTTPS. Tokens are never printed.
- It never starts the embedded local server. An unreachable Admiral is an error:
  `Error: Cannot reach the Admiral at ... Armada never starts a local server for a remote target.`
- It never creates data implicitly (no default fleet, no auto-created captains, no local working directory on
  vessels). `armada go` registers the current repository by its `origin` URL; add captains on the remote with
  `armada captain add`.
- Commands that only make sense for this machine (`server start`, `reset`, `config set`, `config init`, `mcp stdio`)
  refuse a remote target instead of silently acting locally:
  `Error: 'armada server start' acts only on this machine's Admiral (...), but the target is profile 'prod' (...).
  Remove --server/--profile, unset ARMADA_SERVER_URL, pass --profile local, or run 'armada profile use local'.`
- Targeting errors exit with code 2. The full per-command list is in [section 9](#9-cli-command-reference-for-remote-targets).

### MCP clients (Claude Code, Codex, Gemini CLI, Cursor, Mux, OpenCode)

Remote MCP needs a credential on every call. Let `armada mcp install` write the client configs:

```bash
armada mcp install --server https://armada.example.com --token <bearer token>
armada mcp install --profile prod --mcp-url https://mcp.example.com/mcp   # MCP somewhere else
armada mcp install --server https://armada.example.com --token <bearer token> --dry-run   # show, do not write
```

The MCP URL defaults to the target's scheme and host on the Admiral's MCP port (read from its health endpoint, else
7891) at `/mcp`; `--mcp-url` overrides it. Printed snippets show `<token>` instead of the token. The files that hold
the token are set to mode 0600 on macOS and Linux; keep them private, and revoke the credential if one leaks.

What it writes, per client (Claude Code and Codex were verified with their real CLIs against the throwaway Admiral;
the others were verified as files only):

Claude Code, `~/.claude.json` (verified: `claude mcp list` reported `armada: http://...:23211/mcp (HTTP) - Connected`,
and `claude mcp get armada` showed the header):

```json
{ "mcpServers": { "armada": {
  "type": "http",
  "url": "https://armada.example.com:7891/mcp",
  "headers": { "Authorization": "Bearer <token>" } } } }
```

or by hand: `claude mcp add --transport http --scope user armada https://armada.example.com:7891/mcp --header "Authorization: Bearer <token>"`.

Codex reads the token from the environment, so it is not written to disk (verified: `codex mcp add` wrote the entry
below to `config.toml`; a Codex session against it was **not run here**):

```bash
codex mcp add armada --url https://armada.example.com:7891/mcp --bearer-token-env-var ARMADA_TOKEN
export ARMADA_TOKEN=<bearer token>   # in the environment Codex runs in
```

```toml
[mcp_servers.armada]
url = "https://armada.example.com:7891/mcp"
bearer_token_env_var = "ARMADA_TOKEN"
```

Gemini CLI, `~/.gemini/settings.json` (Gemini CLI **not run here**):

```json
{ "mcpServers": { "armada": {
  "httpUrl": "https://armada.example.com:7891/mcp",
  "headers": { "Authorization": "Bearer <token>" } } } }
```

Cursor, `.cursor/mcp.json` in the project (Cursor **not run here**):

```json
{ "mcpServers": { "armada": {
  "url": "https://armada.example.com:7891/mcp",
  "headers": { "Authorization": "Bearer <token>" } } } }
```

Mux, `~/.mux/mcp-servers.json` (or `$MUX_CONFIG_DIR`; Mux **not run here**):

```json
{ "servers": [ {
  "name": "armada", "transport": "http",
  "url": "https://armada.example.com:7891", "mcpPath": "/mcp",
  "auth": { "type": "apikey", "apiKeyHeader": "Authorization", "apiKeyValue": "Bearer <token>" } } ] }
```

OpenCode, `~/.config/opencode/opencode.json` (OpenCode **not run here**):

```json
{ "mcp": { "armada": {
  "type": "remote", "url": "https://armada.example.com:7891/mcp", "enabled": true,
  "headers": { "Authorization": "Bearer <token>" } } } }
```

The `Host` rule, precisely: when the Admiral binds `0.0.0.0` (or `*`, `+`), the MCP listener accepts any `Host`, so
clients may use whatever name or address reaches the machine. When it binds a specific address, clients must use
exactly that address in the URL. Behind a proxy, the proxy must send the `Host` the Admiral is bound to (section 3).
A mismatch is an HTTP 404, not a 401.

### Harbors on other machines

A Harbor runs captains on a developer machine and dials out to the Admiral, so the Harbor machine needs no open port.
In the Harbor app's settings ([HARBOR.md](HARBOR.md#installing-and-running-the-harbor-app)):

| Harbor setting | Value |
|---|---|
| `ServerLinkUrl` | `wss://armada.example.com/v1.0/harbor/connect` (or `ws://<host>:7890/v1.0/harbor/connect` without TLS) |
| `AccessKey` | a bearer token of the user whose missions this Harbor should run |

On the Admiral (`settings.json`):

```json
{
  "harbor": {
    "requireAuth": true,
    "advertisedMcpBaseUrl": "https://armada.example.com:7891/mcp"
  },
  "requireHarborForLaunch": true
}
```

- `advertisedMcpBaseUrl` is the MCP URL captains launched by Harbors call home to. It must be reachable from the
  Harbor machines, so never leave it at a `127.0.0.1` address for a remote Harbor.
- `requireHarborForLaunch: true` keeps missions Pending until an eligible Harbor owned by the mission's user is
  connected, instead of running them on the Admiral host.
- A Harbor's owner comes only from its `AccessKey`; see [HARBOR.md](HARBOR.md) and [HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md).

Verified here: the Harbor link upgrades to a WebSocket through the TLS proxy (`101`). The Harbor app itself, and a
Harbor mission end to end, were **not run here**. With `harbor.requireAuth: true` a Harbor without a credential is
refused during the link handshake (after the WebSocket opens), per `HarborLinkEndpoint`; that rejection was read in
the code, not observed.

## 6. Docker

The Docker images bind `0.0.0.0` and need `ARMADA_INITIAL_ADMIN_PASSWORD` before the first start. `compose.yaml`
runs an all-in-one Admiral; `compose.split.yaml` runs the Admiral for Harbors (split mode). For a Harbor on another
machine, change `harbor.advertisedMcpBaseUrl` in `docker/armada/armada.split.json` from `http://127.0.0.1:7891/mcp` to
a URL that machine can reach, and put the TLS proxy in front of the published ports. Everything in sections 4 and 5
applies unchanged. See [DOCKER.md](DOCKER.md), including
[split mode](DOCKER.md#split-mode-admiral-in-docker-captains-on-the-host). (The Docker stack was **not run here**.)

## 7. Armada.Proxy instead of open ports

When the Admiral sits behind NAT, or you want browser access without opening any inbound port, run Armada.Proxy: the
Admiral opens an outbound tunnel and remote browsers use the relayed dashboard. The relay blocks some administrative
routes, and it does not carry MCP, the CLI's REST calls, or Harbor links, so it complements this guide rather than
replacing it. Setup is in [REMOTE_MGMT.md](REMOTE_MGMT.md); operations in [TUNNEL_OPERATIONS.md](TUNNEL_OPERATIONS.md).

### Native clients through Armada.Proxy

Native clients (the Armada mobile app, scripts, `Armada.Client` with `ProxySessionToken`) do not use the browser
cookie. They hold two separate tokens and send each in its own place (full rules in
[PROXY_API.md](PROXY_API.md#native-clients-bearer-sessions)):

- the **proxy session token**, from the proxy's login, in `X-Armada-Proxy-Session` (or `Authorization: Bearer` on
  `/proxy-api/*` only, or an `armada-proxy-session.<base64url>` subprotocol entry on `/ws`);
- the **Admiral credential** (session token, bearer credential, or API key) in `X-Token`, `Authorization: Bearer`, or
  `X-Api-Key` on relayed `/api/v1/*`, and `?token=` or `armada-token.<base64url>` on `/ws`. The proxy relays these
  untouched and never relays its own token.

Request sequence (`P` is the proxy session token, `A` the Admiral token):

```text
1. GET  /proxy-api/v1/auth/challenge
   -> {"nonce":"<nonce>","expiresUtc":"..."}
2. POST /proxy-api/v1/auth/login
   Content-Type: application/json
   {"nonce":"<nonce>","proofSha256":"<sha256hex('proxy-browser-login:proxy:' + nonce + ':' + sha256hex(trim(password)))>","setCookie":false}
   -> {"token":"<P>","expiresUtc":"...","selectedInstanceId":null}   (Cache-Control: no-store; no Set-Cookie; 429 + Retry-After when locked out)
3. GET  /proxy-api/v1/instances
   Authorization: Bearer <P>
   -> {"count":1,"instances":[{"instanceId":"armada-...","state":"connected","capabilities":[...]}]}
4. POST /proxy-api/v1/session/instance
   Authorization: Bearer <P>
   {"instanceId":"armada-..."}
   -> session context; the selection is stored on the proxy session (409 if not connected or not relay-capable)
5. POST /api/v1/authenticate                         (sign in to the selected Admiral through the relay)
   X-Armada-Proxy-Session: <P>
   {"Email":"dev@example.com","TenantId":"default","Password":"..."}
   -> {"Success":true,"Token":"<A>",...}            (POST /api/v1/tenants/lookup is also allowed)
6. GET|POST|PUT|DELETE /api/v1/...
   X-Armada-Proxy-Session: <P>
   X-Token: <A>                                     (or Authorization: Bearer <A> for a bearer credential)
7. WS   /ws
   Sec-WebSocket-Protocol: armada, armada-proxy-session.<base64url(P)>, armada-token.<base64url(A)>
   (or: header X-Armada-Proxy-Session: <P> on the upgrade and /ws?token=<percent-encoded A>)
8. POST /proxy-api/v1/session/logout-instance        (switch deployment; Authorization: Bearer <P>)
   POST /proxy-api/v1/auth/logout                    (Authorization: Bearer <P>; P is rejected everywhere afterwards)
```

`401` from `/proxy-api/*` or from a relayed route without a valid `P` means the proxy session expired (24 hours) or was
logged out: sign in to the proxy again. `409` means no deployment is selected or it disconnected. A `401` with a valid
`P` comes from the Admiral and means `A` is missing or expired. The relay blocks writes to `settings`, `tenants`,
`users`, and `credentials`, so create bearer credentials for a device directly on the Admiral or in the dashboard.

For a single user, an SSH tunnel is another way to reach a `localhost`-bound Admiral without changing anything on it
(**not run here**):

```bash
ssh -N -L 7890:localhost:7890 -L 7891:localhost:7891 user@server
armada mission list --server http://127.0.0.1:7890 --token <bearer token>
```

If this machine runs its own Admiral on 7890, pick other local ports (`-L 17890:localhost:7890`).

## 8. Troubleshooting

**401 versus 403.** `401` means no credential, or one the Admiral does not accept (wrong, deactivated, or an expired
24-hour session token). `403` means the credential is valid but its user lacks the role: for example a member's token
on `armada server stop`, which needs a global admin. The CLI says which one, and where the credential came from:

```
Error: HTTP 401 on GET /api/v1/missions: the Admiral at http://...:23210 (from --server) requires a credential and none
was sent. Pass --token <bearer>, set ARMADA_TOKEN, or store one with 'armada profile add <name> --server ... --token <bearer>'.

Error: HTTP 403 on POST /api/v1/server/stop: the Admiral at http://...:23210 (from --server) accepted the credential
(token from --token) but its user may not do this. Ask an administrator for the needed role, or use a credential of a
user who has it.
```

`armada doctor --server ...` checks reachability and the credential. A TUI profile created by password sign-in holds
a session token, which expires; store a bearer token instead (`armada profile add <name> --server ... --token ...`).

**MCP returns 404.** The `Host` header does not match the address the MCP listener is bound to. Bind `0.0.0.0`, use
the exact bound address in the client URL, or set the upstream `Host` in your proxy (`header_up Host localhost:7891`).
REST on the same Admiral answering normally is the tell-tale sign.

**MCP returns 401 with `WWW-Authenticate`.** The client sent no credential, or a bad one. Remote MCP always needs a
credential; re-run `armada mcp install --server ... --token ...` or add the `Authorization` header by hand.

**The Admiral will not start on the network.** It refuses non-loopback binding while default credentials are in use
(the message is quoted in [section 2](#change-the-default-admin-password-first)). Set `ARMADA_INITIAL_ADMIN_PASSWORD`
for the first start, or change the admin password while bound to `localhost`.

**Captains cannot call home.** A captain reaches MCP at the URL the Admiral gives it. For captains on the Admiral host
that is the local MCP URL; for Harbor captains it is `harbor.advertisedMcpBaseUrl`, which must resolve and be
reachable from the Harbor machine. From that machine,
`curl -sS -o /dev/null -w "%{http_code}\n" -X POST -H "Content-Type: application/json" -d "{}" <url>` should print
`401` (reachable, credential required), not a connection error or `404`. (Without a body the listener answers `411`.)

**Dashboard live updates or Harbors fail through the proxy.** The proxy is not passing WebSocket upgrades for `/ws`
or `/v1.0/harbor/connect`, or it closes idle connections. With nginx, set `proxy_http_version 1.1`, the `Upgrade` and
`Connection` headers, and a long `proxy_read_timeout`. Check with:

```bash
curl -sS --http1.1 -o /dev/null -w "%{http_code}\n" --max-time 3 \
  -H "Connection: Upgrade" -H "Upgrade: websocket" -H "Sec-WebSocket-Version: 13" \
  -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" -H "Authorization: Bearer <token>" \
  https://armada.example.com/ws
```

`101` (curl then times out, which is expected) means the upgrade works; `400`, `426`, or `502` means the proxy is
not forwarding it. Without a credential `/ws` answers `401`.

**`armada server restart` on a remote Admiral does not come back.** The restart endpoint starts a replacement of the
running executable. Run as a service, or as the native `armada-server`/`Armada.Server` executable, it came back
(tested). Started as `dotnet Armada.Server.dll`, the replacement was launched as a bare `dotnet` and the Admiral
stayed down (observed); start it again on its host, and prefer the service or native executable.

**The CLI acts on the wrong Admiral.** `armada config show` prints the resolved target and its source. An exported
`ARMADA_SERVER_URL` or an active profile (possibly chosen in the TUI) wins over the local Admiral; `--profile local`
or `armada profile use local` goes back.

## 9. CLI command reference for remote targets

Every command was evaluated for whether it can act on a remote Admiral. Summary: 50 commands work remotely through
REST, 5 are local-only and refuse a remote target, 4 manage local client files (and use the target where it matters),
and 4 manage profiles.

"How to target" means `--server <url>` with `--token <bearer>`, or `--profile <name>`, or `ARMADA_SERVER_URL` with
`ARMADA_TOKEN`, or the active profile (section 5).

| Command | Works remotely | How to target | Notes |
|---|---|---|---|
| `go` | Yes | target options | Registers the current repo by its `origin` URL (no local working directory is sent). Does not auto-create or auto-scale captains on a remote; add them with `captain add`. |
| `status` | Yes | target options | Filters by the current repo's `origin` when it matches a vessel. |
| `watch` | Yes | target options | Polls REST. Bell and notification settings come from this machine's settings. |
| `log` | Yes | target options | Reads `GET /api/v1/missions/{id}/log` or `/captains/{id}/log` instead of local files; `--follow` polls every second. |
| `diff` | Yes | target options | |
| `inbox` | Yes | target options | |
| `health` | Yes | target options | `--evaluate` runs on the Admiral host. |
| `doctor` | Yes | target options | Checks reachability, transport, and the credential on the remote; skips this machine's settings, database, and runtimes. |
| `mission list`, `create`, `show`, `cancel`, `restart`, `retry` | Yes | target options | `show` tails the log through REST. |
| `voyage list`, `create`, `show`, `cancel`, `retry` | Yes | target options | |
| `action list`, `run`, `status`, `cancel` | Yes | target options | Command actions run on the Admiral host or Harbor, not here. |
| `playbook list`, `add`, `show`, `remove` | Yes | target options | `add --from-file` reads the local file and uploads its content. |
| `backlog list`, `show`, `create`, `update`, `delete`, `reorder` | Yes | target options | |
| `vessel list`, `add`, `remove` | Yes | target options | The repo URL must be clonable from the Admiral host (or Harbor). |
| `vessel import` | Yes | target options | Paths and `--root` are directories on the Admiral host, not on this machine. |
| `vessel history` | Yes | target options | Git runs on the Admiral host against the vessel's repository; days are grouped in this machine's time zone. `--all` follows the server's cursor. |
| `captain list`, `add`, `update`, `stop`, `remove`, `stop-all` | Yes | target options | The runtime must be installed and signed in on the Admiral host or a Harbor. |
| `fleet list`, `add`, `remove` | Yes | target options | |
| `server status` | Yes | target options | Unauthenticated health check of the target. |
| `server stop` | Yes, through `POST /api/v1/server/stop` | target options with a global admin's token | 403 for other users. A service manager may restart it. |
| `server restart` | Yes, through `POST /api/v1/server/restart` | target options with a global admin's token | Waits for the Admiral to answer again. See troubleshooting for `dotnet Armada.Server.dll`. |
| `server start` | No (local-only) | n/a | Refuses a remote target: it starts a process on this machine. Start the remote Admiral on its host or as a service. |
| `reset` | No (local-only) | n/a | Refuses a remote target: it deletes this machine's data. |
| `config set` | No (local-only) | n/a | Refuses a remote target: it edits this machine's `settings.json`. Change remote settings in the dashboard (Settings) or with `PUT /api/v1/settings`. |
| `config init` | No (local-only) | n/a | Refuses a remote target. |
| `mcp stdio` | No (local-only) | n/a | Refuses a remote target: it opens the local database. Use `mcp install --server` to point clients at the remote HTTP MCP endpoint. |
| `config show` | Local file, shows target | target options | Shows this machine's settings and the resolved CLI target (never the token). |
| `mcp install` | Writes local client configs for the target | target options, `--mcp-url` | Remote target: MCP URL plus `Authorization: Bearer` per client (Codex via `ARMADA_TOKEN`). Local target: unchanged. |
| `mcp remove` | Local files only | n/a | Removes the `armada` entry whatever URL it points at; the target options are ignored. |
| `tui` | Yes | `--server`, `--profile`, `--token`, `ARMADA_SERVER_URL`, `ARMADA_URL`, `ARMADA_TOKEN` | Same profiles and token store as the CLI. |
| `profile list`, `add`, `use`, `remove` | n/a (manage targets) | n/a | Edit the shared profile store (`tui.json`) and token store. `local` is reserved for this machine's Admiral. |
