# Phase 7: documented usage contracts

The authoritative surface remains the route/tool registrars. REST, MCP and
WebSocket references now describe tenant/account/host ownership, inherited-on
capture settings, the one-time opt-out reset, host selection, read/capture errors,
completion-time attribution, retained history and native/dashboard navigation.

The existing Postman v2.1 collection is maintained in place. Usage has 16
variable-driven requests: GET reads, the three enumerate twins, capture and
negative range/foreign/auth/role/disabled/cooldown/offline fixtures. Existing
assertions and collection authentication are retained. Saved examples include
empty history, AuthMissing, queued capture and 400/401/403/404/409/429/503 errors.
Sixty saved-example assertions pass in a local JavaScript sandbox. This validates
the examples and scripts, not live execution against operator-provisioned
negative fixtures. Live REST/MCP/client contract suites provide separate runtime
coverage. No collection workspace was initialized or published.

The MCP usage_status schema now declares hostId and accountKey, which its typed
handler already accepted. The generated public surface records this additive
schema correction: 521 REST routes, 264 MCP tools, 65 WebSocket commands,
77 WebSocket events, 97 CLI commands and 325 settings keys. Every existing usage
SDK method is exercised by Client.Contract.Usage against the fixture Admiral.

TUI and mobile canonical Account Limits routes and manifests track the flattened
dashboard navigation. TUI parity passes with no planned entries; mobile manifest
consistency passes with the same 23 unrelated pre-existing planned API entries.
Native interactive-history scope is recorded in DASHBOARD_PARITY.md.

Feature verification and final regression counts are recorded in
PHASE8_OPERATIONS.md. Provider evidence remains in PROBE_INTERFACES.md; this phase
does not claim live authenticated Gemini or successful PTY scraping.
