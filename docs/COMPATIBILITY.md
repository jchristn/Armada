# Compatibility Policy

> **Type:** policy and reference. Applies from Armada 1.0.0 onward. Keep it in sync with
> `src/Armada.Core/ApiSurface/ExperimentalSurface.cs`, `docs/api-surface-1.0.json`, and the `E2E.ApiContract` test.

Armada follows [Semantic Versioning 2.0.0](https://semver.org). From 1.0.0 on, a minor or patch release does not break
a client, script, or settings file that works against an earlier release with the same major version. This document
says exactly what that promise covers, what it does not, how a covered item is deprecated and removed, and how
experimental items are marked and graduated.

## Contents

- [What the promise covers](#what-the-promise-covers)
- [What is not covered](#what-is-not-covered)
- [Compatible and breaking changes](#compatible-and-breaking-changes)
- [Deprecation](#deprecation)
- [Experimental surfaces](#experimental-surfaces)
- [The surface file and the contract test](#the-surface-file-and-the-contract-test)

## What the promise covers

The frozen 1.0 surface is listed item by item in [API_SURFACE_1.0.md](API_SURFACE_1.0.md) (generated from the running
code; machine-readable copy: `docs/api-surface-1.0.json`). It covers five surfaces:

| Surface | Covered | Reference |
|---|---|---|
| REST API (`/api/v1`) | Methods and route templates; the authorization level each route requires (not tightened); request body fields and whether the body is required; response body fields and their types; error bodies (`ApiErrorResponse`) and the stable `Error` codes; status codes, including `404` for cross-tenant access; pagination parameters and the `EnumerationResult<T>` shape; PascalCase JSON | [REST_API.md](REST_API.md) |
| MCP tools | Tool names; argument names, types, and which arguments are required; the authorization level each tool requires; result fields; the `{ "Error": ... }` failure convention | [MCP_API.md](MCP_API.md) |
| WebSocket | The `/ws` endpoint and its authentication; client routes (`subscribe`, `command`) and message fields; command actions and their reply envelope; event types, their recipients, and payload field names; camelCase JSON | [WEBSOCKET_API.md](WEBSOCKET_API.md) |
| CLI (`armada`) | Command names; positional arguments; option long names and short aliases; which arguments and options are required; exit status (0 on success, non-zero on failure); field names of `--json` output | `armada help` |
| Settings | `settings.json` keys (camelCase dotted paths), their types, and their meaning; enum values accepted by a key | [API_SURFACE_1.0.md](API_SURFACE_1.0.md#settings) |

Data is covered by the upgrade promise rather than this document: a 1.x Admiral upgrades the database of any earlier 1.x
(and 0.9.x) release in place on every supported provider, and backs it up first ([UPGRADING.md](UPGRADING.md)).

## What is not covered

- **Experimental items** (see [Experimental surfaces](#experimental-surfaces)). At 1.0 these are Harbor split mode (the
  Harbor link WebSocket and its protocol, the `/api/v1/harbors` routes, the `*_harbor` MCP tools, and the `harbor.*`,
  `deploymentMode`, and `requireHarborForLaunch` settings) and self-rebuild (`/api/v1/server/rebuild`,
  `/api/v1/server/rebuild/status`, `/api/v1/server/rollback`, and the `selfVesselId`, `rebuildSlotRetentionCount`, and
  `rebuildSupervisorHarborId` settings).
- **Human-readable text**: `Message` and `Description` fields of errors, OpenAPI summaries and descriptions, MCP tool
  descriptions, CLI table and help output, log lines, event `message` text, and dashboard text. Do not parse them.
- **Defaults** of settings and of optional parameters may change in a minor release when the old default is unsafe or
  wrong; the change is listed in the CHANGELOG. Set a value explicitly if you depend on it.
- **Event log types that are not WebSocket events**: `ArmadaEvent.EventType` values that the Admiral writes only to the
  event log (for example `captain.quarantined`, `mission.redispatched`) are informational. The WebSocket event types listed
  in the surface file are covered.
- **Ordering** of results that are not explicitly sorted, timing fields (`TotalMs`, `elapsedMs`), and the contents of
  free-form `Data`, `Metadata`, and JSON-text fields.
- **Internal interfaces**: the C# types and methods of `Armada.Core`, `Armada.Server`, and `Armada.Runtimes` (they are
  not a supported library API), the database schema (use the APIs), prompt template and persona text, and the files under
  the data directory other than `settings.json`.
- **Not yet in the surface file**: the proxy API (`/proxy-api/v1`, [PROXY_API.md](PROXY_API.md)) and the remote-control
  tunnel protocol ([TUNNEL_PROTOCOL.md](TUNNEL_PROTOCOL.md)) are documented and versioned with their own protocol
  version but are not checked by the contract test at 1.0; the TUI is decided at the first release candidate (decision
  D5 in V1_READINESS.md).
- **Security fixes** may tighten behavior in a minor or patch release (for example requiring a stronger role for a
  route, or rejecting input that was accepted by mistake). Such changes are called out under a Security heading in the
  CHANGELOG.

## Compatible and breaking changes

A minor release may add to the surface. Clients must therefore ignore JSON fields, WebSocket event types, and enum values
they do not recognize.

| Allowed in a minor release | Breaking (major release only, after deprecation) |
|---|---|
| New routes, MCP tools, WebSocket commands, event types, CLI commands | Removing or renaming any covered route, tool, command, event type, CLI command |
| New optional request fields, MCP arguments, CLI options | Removing or renaming a field, argument, option, or short alias; changing its type |
| New response and event payload fields | Removing or renaming a response or payload field |
| New enum values | Removing an enum value |
| New settings keys (with defaults that keep current behavior) | Removing, renaming, or retyping a settings key |
| Loosening an authorization requirement | Tightening an authorization requirement (except as a security fix) |
| Making a required argument optional | Making an optional argument or body required; adding a required argument |

## Deprecation

A covered item is removed only in a major release, and only after it has been deprecated for at least one minor release:

1. **Announce** in a minor release: a `Deprecated` section in the CHANGELOG names the item, the replacement, and the
   major release that removes it.
2. **Mark** it everywhere it is visible: `deprecated: true` on the OpenAPI operation and a `[Deprecated]` prefix on its
   summary; a `[Deprecated]` prefix on the MCP tool description; a deprecation banner in the reference doc; for a CLI
   command or option, a warning on standard error when it is used; for a settings key, a warning in the Admiral log at
   startup when the key is present.
3. **Keep it working** unchanged until the next major release.
4. **Remove** it in the next major release, together with the new surface file for that major.

## Experimental surfaces

An experimental item is shipped and usable but excluded from the promise: it may change or be removed in any minor
release, and the CHANGELOG says so when it does.

- **Single list.** `src/Armada.Core/ApiSurface/ExperimentalSurface.cs` lists experimental REST route prefixes, MCP tools,
  settings key prefixes, and WebSocket endpoints.
- **Markers.** The Admiral prefixes the OpenAPI summary of every experimental route with `[Experimental] ` and adds the
  `Experimental` tag; the MCP description of every experimental tool starts with `[Experimental] `; reference docs carry
  an "Experimental" banner; the surface file records `"experimental": true`, and the contract test ignores changes to
  those items.
- **New surfaces** may ship experimental: add them to `ExperimentalSurface` in the same change.
- **Graduating** an item: remove it from `ExperimentalSurface` in a minor release, remove the doc banners, regenerate the
  surface file, and note the graduation in the CHANGELOG. From then on it is covered.
- A covered item never becomes experimental again within a major release (the contract test fails if it does).

## The surface file and the contract test

- `scripts/common/generate-api-surface.sh` (Windows: `scripts\windows\generate-api-surface.bat`) boots a throwaway
  Admiral and rewrites `docs/api-surface-1.0.json` and `docs/API_SURFACE_1.0.md` from the running code: REST routes from
  the web server's route table and OpenAPI metadata joined with `RouteAuthorizationRegistry`, MCP tools and input schemas
  as registered joined with `McpToolAuthorizationRegistry`, the WebSocket contract from `WebSocketSurface`, the CLI
  command model from Helm, and settings keys by reflection over `ArmadaSettings`.
- The `E2E.ApiContract` suite builds the same surface from the code under test and compares it to
  `docs/api-surface-1.0.json`. It fails on any removal or incompatible change of a non-experimental item and prints a
  diff (`BREAKING`, `ADDED`, `NOTES`); additions pass. It also checks that experimental items carry their markers, that
  every declared WebSocket command reaches a handler, that every event type the server broadcasts is declared, and that
  `API_SURFACE_1.0.md` matches the JSON file.
- **When you add** a route, tool, argument, command, event, CLI option, or settings key: declare authorization for routes
  and tools (`RouteAuthorizationRegistry`, `McpToolAuthorizationRegistry`), declare WebSocket commands and events in
  `WebSocketSurface`, then run the generator and commit the regenerated files in the same change so the addition is
  frozen.
- **When the test fails** with a breaking change: restore the item, or follow [Deprecation](#deprecation) and make the
  change in the next major release. A major release starts a new surface file (`api-surface-2.0.json`) and leaves the
  1.0 file as history.
- Only a deliberate, reviewed change edits the surface files; review their diff like code.
