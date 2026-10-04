# Security Policy

## Reporting a vulnerability

Please do not report security vulnerabilities in public issues, discussions, or pull requests.

Report them privately through GitHub: open the repository's **Security** tab at
<https://github.com/jchristn/armada/security> and choose **Report a vulnerability**. Include:

- the Armada version (`GET /api/v1/status` reports it) and how it is installed (Docker, installer,
  NuGet tool, source);
- the entry point involved (REST route, MCP tool, WebSocket command, Harbor link, proxy) and the configuration that
  matters (hostname binding, database provider, Harbor or proxy use);
- steps to reproduce, and what an attacker gains.

You will get an acknowledgement within five business days. Fixes are released as patch versions and credited in the
CHANGELOG unless you ask otherwise. Please give us a reasonable window to ship a fix before disclosing publicly.

## Supported versions

Security fixes are made on the latest minor release. Before 1.0, upgrade to the latest release to receive fixes.

## Security model summary

The full model, the surface inventory, and the list of open and fixed findings are in
[docs/SECURITY_REVIEW.md](docs/SECURITY_REVIEW.md). In short:

- **Authentication everywhere.** REST, the WebSocket (`/ws`), the Harbor link, and MCP require a credential: a
  session token (`X-Token`), a bearer token (`Authorization: Bearer`), or the local API key (`X-Api-Key`). MCP accepts
  unauthenticated calls only when its listener is bound to loopback and `Mcp.AllowUnauthenticatedLoopback` is on (the
  default), which is what the local Claude Code setup uses.
- **Explicit authorization.** Every REST route and MCP tool declares a resource type, an operation, and a permission
  level (`Authenticated`, `TenantAdmin`, `AdminOnly`) in a central registry, checked before the handler runs.
  Undeclared routes and tools fail closed, and a test fails the build when one is missing.
- **Tenant isolation.** Users see and change only their own tenant's data (regular users: their own records);
  global admins see everything.
- **Safe defaults.** The Admiral refuses to listen on a non-loopback address while the default credentials
  (`admin@armada` / `password`, bearer token `default`) are in use, unless `AllowDefaultCredentialsOnNetwork` is set.
  The first dashboard sign-in with the default password requires changing it, which also disables the `default`
  bearer token. Headless installs set `ARMADA_INITIAL_ADMIN_PASSWORD` before the first start. Self-registration is off
  by default.
- **Passwords and guessing.** Passwords are stored as salted PBKDF2-HMAC-SHA256 (600,000 iterations); hashes from
  earlier releases are upgraded at startup or on the next login. Repeated failed logins lock the account, and repeated
  failed credentials lock the client address, with exponential backoff (429 with `Retry-After`; `loginRateLimit`
  settings, see [REST_API.md](docs/REST_API.md#login-rate-limiting)).
- **Agents run with your permissions.** Captains are CLI agents launched with auto-approve flags by default. Read
  [Running agents safely](docs/SECURITY_REVIEW.md#running-agents-safely) and turn `autoApprove` off for captains that do
  not need unattended shell access.
- **Accounting.** Every shell command Armada runs for a user (workspace exec, fleet action commands, check runs,
  Harbor probes, merge queue tests) writes an `audit.command` event that only a global admin can delete.
- **Secrets.** Read endpoints never return tokens, passwords, or API keys (a new bearer token is shown once, at
  creation); request history and logs redact secret-bearing fields.
- **Supply chain.** CI scans NuGet and npm dependencies for known vulnerabilities; container images run as non-root
  users on pinned base images.

## Hardening checklist

1. Keep the Admiral bound to `localhost` unless you need remote access; when exposing it, terminate TLS in front of it.
2. Change the default admin password before anything else (the Admiral enforces this off loopback).
3. Run Armada under a dedicated, unprivileged account or in the container.
4. Turn off `autoApprove` for captains that do not need it, and use Harbors on separate machines for untrusted
   repositories.
5. Leave `AllowSelfRegistration` off unless you want open sign-up.
6. Change the remote tunnel and proxy passwords from their defaults if you use the proxy.
