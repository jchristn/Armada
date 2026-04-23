# Armada Remote Control

> Archived on `2026-04-23` after the `v0.7.0` tunnel/proxy MVP shipped. Checked items reflect landed work; unchecked items remain deferred follow-on scope.

## Purpose

Enable a remote user on any network to securely monitor and control their privately deployed Armada instance from a phone or browser, without VPNs, port forwarding, or exposing the instance directly to the internet.

Core architecture:

```text
[Phone / Browser] -> [SaaS Proxy] <= outbound WSS => [Private Armada Instance]
```

The Armada instance initiates and maintains the outbound tunnel. The proxy brokers user identity, routes requests and events, and hosts the remote UX.

---

## How To Use This Plan

This document is the execution plan, not just a design note.

- Treat every phase and workstream as a checklist that can be annotated in place.
- Add owner, branch/PR, and date information directly under the item being worked.
- Mark work complete only when code, tests, docs, and contract artifacts are all updated.
- If scope changes, update this document first so the plan remains the source of truth.

Suggested annotation format:

```md
- [ ] Implement tunnel handshake
  Owner: @name
  Branch/PR: feature/remote-tunnel / #123
  Status: In progress
  Notes: waiting on token format review
```

Status legend:

- `[ ]` not started
- `[-]` in progress / partially complete
- `[x]` complete
- `[!]` blocked

---

## Product Position

The MVP is not full dashboard parity through a generic proxy.

The MVP is a mobile-first remote operations shell with:

- instance list and connectivity status
- tunnel health and latency
- recent activity and notifications
- mission, voyage, and captain summaries
- logs and diffs for focused investigation
- a small set of guarded remote actions

Full remote desktop/dashboard compatibility comes later, after trust, identity, transport, notifications, and mobile workflows are stable.

---

## Guiding Principles

1. Security is foundational.
   This is a public bridge into a private system. Authn, authz, audit, and device trust cannot be deferred.

2. Preserve local Armada meaning.
   Remote users must map to locally meaningful Armada identities so permissions and audit attribution remain correct.

3. Use one outbound tunnel per instance.
   No inbound ports. No customer-managed reverse proxy requirement.

4. Treat events as first-class.
   Remote monitoring depends on live state, subscriptions, and notification delivery, not just REST proxying.

5. Optimize for mobile-first operator workflows.
   Notification-first, summary-first, low-bandwidth, low-latency-feeling UX is more important than rendering every admin page on a phone.

6. Reuse infrastructure and components, not the entire local shell.
   Shared providers, hooks, cards, badges, viewers, and action components should be extracted, but the remote shell should have its own top-level information architecture.

7. Contracts are product surface area.
   REST, MCP, WebSocket, Postman, README guidance, and tunnel-specific docs must stay current as implementation lands.

---

## Why The Old Order Was Wrong

The earlier draft underweighted several first-order concerns:

- security and authorization were too late in the plan
- the identity model between proxy users and local Armada users was missing
- version and capability negotiation across Armada releases was underspecified
- notification state was treated like local UI state instead of a server-backed remote feature
- the plan leaned too hard on generic dashboard reuse despite the current dashboard being tightly coupled to:
  - one local session
  - one local instance
  - one direct WebSocket
  - one local notification stream
  - broad, chatty local data loading patterns

That makes "just make the dashboard responsive" an architectural trap for the phone-first use case.

---

## Target Architecture

### 1. Proxy

Cloud-hosted service responsible for:

- user authentication
- account and instance management
- tunnel termination and routing
- delegated identity/session brokerage
- event fan-out
- notification storage and delivery
- audit logging
- hosting the remote web app

### 2. Tunnel Client In Armada

Armada.Server runs a persistent outbound `wss` connection to the proxy and:

- registers the instance and capabilities
- receives proxied requests
- forwards local events
- reports health and tunnel latency
- reconnects automatically with backoff and jitter

### 3. Two Frontend Shells, Shared Building Blocks

- Local dashboard
  - current Armada-local admin experience
- Remote operations shell
  - mobile-first proxy-hosted app for remote use

Both should share extracted primitives where possible:

- provider interfaces
- entity cards
- status badges
- log/diff viewers
- action controls
- common query and formatting utilities

Later, the proxy can also support remote desktop/dashboard compatibility using the REST proxy and remote-aware providers.

---

## Identity And Authorization Model

This is a core requirement, not a later enhancement.

### Requirements

- proxy users authenticate to the SaaS proxy
- each remote user must map to a locally meaningful Armada identity or delegated local session
- local Armada authorization remains authoritative for local actions
- audit logs must attribute actions to the human remote operator, the mapped local identity, the instance, and the action performed

### Design Targets

- proxy account/user model for SaaS access
- per-instance enrollment and ownership
- delegated session issuance from instance to proxy
- short-lived local session or service-principal tokens scoped to the mapped role
- support for session revocation and lost-device response
- per-device trust model with explicit device/session inventory

### Non-Goals

- flattening all remote users into a single shared instance token
- bypassing Armada's native authz model with a proxy-only role check

---

## Tunnel Protocol

The tunnel is message-oriented and must support both compatibility and native remote workflows.

### Envelope Types

- `request`
- `response`
- `event`
- `subscribe`
- `unsubscribe`
- `ping`
- `pong`
- `error`

### Required Characteristics

- correlation IDs
- request/response multiplexing
- subscription and event streaming
- backpressure handling
- reconnect and resumable subscriptions where practical
- version and capability handshake on connect
- tunnel health telemetry
- chunked and streaming semantics for large payloads

### Compatibility Lanes

- REST proxying remains supported for dashboard compatibility and deep-link scenarios
- event forwarding remains supported for existing live-update patterns
- the mobile-first remote shell should prefer aggregated proxy APIs over raw endpoint passthrough

---

## Remote UX Strategy

### MVP: Remote Operations Shell

Top-level concepts:

- instances
- activity
- alerts and notifications
- approvals and guarded actions
- instance health and tunnel status

Primary views:

- instance list
- instance summary
- mission summary and recent mission activity
- voyage summary and recent voyage activity
- fleet and vessel management
- voyage dispatch
- mission create and edit
- captain status
- logs and diffs for focused inspection
- notification inbox and read state

Initial guarded actions:

- restart mission
- stop captain
- cancel voyage

### Explicitly Deferred

- full parity for all existing dashboard pages on mobile
- unrestricted remote execution of destructive operations
- broad desktop-style table workflows as MVP blockers

---

## Cross-Cutting Definition Of Done

No remote-control milestone is complete until all of the following are true:

- implementation is merged
- automated coverage for the changed behavior exists
- failure, auth, and offline behaviors are tested
- docs are updated
- `Armada.postman_collection.json` is updated for any REST-exposed surface change
- any contract changes are reflected in the relevant API references
- tunnel-specific behavior is documented in tunnel-specific docs, not just buried in this plan

Required artifact set for this effort:

- `README.md`
- `docs/REST_API.md`
- `docs/MCP_API.md`
- `docs/WEBSOCKET_API.md`
  - Note: the current repository file is singular `WEBSOCKET_API.md`, not `WEBSOCKETS_API.md`
- `Armada.postman_collection.json`
- new tunnel/remote-control docs as they become real product surface area:
  - `docs/TUNNEL_PROTOCOL.md`
  - `docs/PROXY_API.md`
  - `docs/TUNNEL_OPERATIONS.md`

If a tunnel feature lands and one of those docs still does not exist, create it before calling the phase complete.

---

## Workstream 0: Planning, Scope Control, And Contracts

Goal: convert architecture intent into implementation contracts that teams can execute and track.

### Checklist

- [x] Freeze MVP scope for the remote operations shell
- [x] Freeze non-goals for MVP and record them here
- [x] Define capability manifest schema and versioning rules
- [ ] Define delegated identity model and token/session lifecycle
- [x] Define tunnel message schema, handshake, and error model
- [-] Define proxy aggregated API surface for remote shell MVP
- [ ] Define remote action policy matrix: allowed, guarded, blocked
- [ ] Define audit event taxonomy for enrollment, auth, tunnel, and remote actions
- [ ] Define rollout plan for mixed-version Armada instances

### Exit Criteria

- a developer can pick up any checklist item and know the expected output
- the contract docs to be authored or updated are identified before implementation begins
- unresolved design questions are listed in the open questions section with named decision owners

---

## Workstream 1: Trust, Enrollment, And Identity

Goal: establish the security model before remote control exists in practice.

### Checklist

- [ ] Implement proxy account and user model
- [ ] Implement instance enrollment and pairing flow
- [ ] Implement per-instance ownership and revocation
- [ ] Implement delegated local identity mapping
- [ ] Implement short-lived session or service-principal issuance
- [ ] Implement device trust inventory
- [ ] Implement lost-device and session revocation flows
- [ ] Implement audit records for enrollment, login, token issuance, revocation, and remote action attribution
- [ ] Enforce authorization boundaries so local Armada authz remains authoritative

### Tests

- [ ] unit tests for token/session creation, expiration, revocation, and mapping rules
- [ ] automated tests for enrollment happy path and rejection cases
- [ ] automated tests for unauthorized remote action attempts
- [ ] automated tests for cross-tenant and cross-instance isolation
- [ ] automated tests for audit attribution correctness

### Documentation

- [ ] update `README.md` with remote control overview and security caveats when feature is user-visible
- [ ] update `docs/REST_API.md` for any enrollment/auth endpoints added on the Armada side
- [ ] update `docs/MCP_API.md` if MCP gains remote-control-aware tools or constraints
- [ ] create or update `docs/PROXY_API.md` for account, enrollment, identity, and action policy endpoints
- [ ] create or update `docs/TUNNEL_OPERATIONS.md` with enrollment, credential rotation, and revocation procedures
- [ ] update `Armada.postman_collection.json` for any new REST endpoints

### Exit Criteria

- a remote user can be mapped to a meaningful local identity
- revocation works without restarting the instance
- audit output can answer who acted, through which device/session, against which instance, as which local identity

---

## Workstream 2: Tunnel Foundation

Goal: establish the secure outbound transport between private Armada instances and the proxy.

### Checklist

- [x] implement persistent outbound `wss` tunnel client in Armada.Server
- [x] implement proxy tunnel termination
- [x] implement handshake with protocol version and capability manifest
- [x] implement request/response multiplexing with correlation IDs
- [-] implement event forwarding and subscription lifecycle
- [x] implement heartbeat, stall detection, reconnect, and jittered backoff
- [x] implement tunnel latency and health telemetry
- [x] implement explicit offline and stale-instance semantics
- [ ] implement payload chunking/streaming for large logs, diffs, backups, or restores
- [ ] implement capability-based behavior gates for mixed-version instances
- [x] define fallback behavior when proxy is unavailable

### Tests

- [x] unit tests for handshake parsing, capability negotiation, and routing rules
- [-] unit tests for reconnect, heartbeat timeout, and backpressure behavior
- [ ] automated tests for request/response over the tunnel
- [ ] automated tests for event subscription and resumable behavior where supported
- [ ] automated tests for offline handling and stale-state detection
- [ ] load or soak tests for multiplexed requests and large streaming payloads

### Documentation

- [x] create `docs/TUNNEL_PROTOCOL.md` with envelopes, handshake, capability negotiation, error semantics, and streaming rules
- [x] create or update `docs/TUNNEL_OPERATIONS.md` with deployment, observability, reconnect, and failure-mode guidance
- [x] update `README.md` with high-level tunnel architecture once public-facing
- [x] update `docs/REST_API.md` if tunnel health/status or enrollment endpoints are exposed via REST
- [x] update `docs/WEBSOCKET_API.md` if local or proxied event semantics change
- [x] update `Armada.postman_collection.json` for any new REST-accessible tunnel endpoints

### Exit Criteria

- a private Armada instance can enroll and maintain a secure outbound tunnel
- the proxy can send an authenticated request through the tunnel and receive a response
- events flow from Armada to the proxy
- tunnel health, latency, and offline state are observable

---

## Workstream 3: Proxy Aggregated APIs

Goal: expose remote-control APIs designed for remote workflows, not raw local-dashboard coupling.

### Checklist

- [x] define instance summary endpoint(s)
- [x] define recent activity and alert rollup endpoint(s)
- [x] define focused detail endpoint(s) for missions, voyages, captains, logs, and diffs
- [ ] define notification inbox and read-state endpoint(s)
- [x] define guarded action endpoint(s)
- [ ] define action confirmation and policy evaluation responses
- [-] define pagination, filtering, stale-data markers, and capability-dependent field rules
- [ ] define audit and rate-limit behavior for each endpoint family

### Tests

- [-] automated tests for each aggregated endpoint
- [ ] automated tests for authorization and instance isolation
- [ ] automated tests for stale/offline instance responses
- [ ] automated tests for guarded action confirmation flows
- [ ] contract tests to detect drift between proxy API responses and remote UI expectations

### Documentation

- [x] create or update `docs/PROXY_API.md`
- [x] update `README.md` if any public remote shell API usage is documented there
- [x] update `Armada.postman_collection.json` to cover all REST endpoints that are part of this remote surface
- [x] ensure request/response examples stay current with the latest schema

### Exit Criteria

- the remote shell can load its core views without depending on raw passthrough of the full Armada REST surface
- API responses expose enough metadata for stale/offline/guarded states without UI guesswork

---

## Workstream 4: Remote Operations Shell MVP

Goal: deliver the first high-value remote experience for phone and browser users.

### Checklist

- [x] implement instance list with online/offline/stale state
- [x] implement instance detail summary
- [x] implement activity feed
- [x] implement mission and voyage focused views
- [x] implement captain status view
- [x] implement log and diff viewers tuned for mobile inspection
- [ ] implement notification inbox with synced read state
- [-] implement guarded remote actions with confirmations
- [-] implement auth/session UX for mobile web
- [-] implement clear degraded/offline UI states

### Tests

- [ ] component and integration tests for mobile-first layouts and degraded states
- [ ] end-to-end tests for sign-in, instance selection, summary inspection, and guarded action flows
- [ ] notification read-state sync tests across sessions/devices
- [ ] tests for low-bandwidth, reconnect, and stale-data indicators

### Documentation

- [x] update `README.md` with remote operations shell scope and usage once available
- [ ] create a user-facing remote-shell doc if the UX surface becomes large enough to warrant it
- [x] update `docs/TUNNEL_OPERATIONS.md` with operator troubleshooting for disconnected or stale instances

### Exit Criteria

- a user can open the app on a phone and understand instance status within seconds
- a user can investigate recent failures without a desktop workflow
- a user can perform a small set of safe remote actions confidently
- notifications remain consistent across devices and sessions

---

## Workstream 5: Mobile-First Reliability And Notifications

Goal: make remote operation reliable on real devices and real networks.

### Checklist

- [ ] implement web push or equivalent remote notification delivery
- [ ] implement offline last-known snapshot behavior
- [ ] implement bandwidth-aware loading strategies
- [ ] implement pull-to-refresh and reconnect feedback
- [ ] implement stronger confirmations for dangerous actions
- [ ] implement stale-data indicators everywhere remote state can age

### Tests

- [ ] notification delivery tests with app open and app backgrounded
- [ ] offline snapshot tests
- [ ] reconnect tests after sleep/resume and network interruption
- [ ] UX tests for stale-data and approval flows

### Documentation

- [ ] update `README.md` or dedicated remote-shell docs for notification setup if user action is required
- [ ] update `docs/TUNNEL_OPERATIONS.md` with mobile-network and reconnect troubleshooting

### Exit Criteria

- operators can rely on remote monitoring even with intermittent mobile connectivity
- the product makes stale or last-known data impossible to misread as live state

---

## Workstream 6: Remote Desktop And Dashboard Compatibility

Goal: support broader remote use of existing dashboard flows after remote primitives are stable.

### Checklist

- [ ] implement REST proxy compatibility path
- [ ] implement tunnel-multiplexed WebSocket compatibility path
- [ ] implement remote-aware auth provider
- [ ] implement remote-aware notification provider
- [ ] implement instance-aware routing and context where needed
- [ ] extract shared components and provider interfaces instead of cloning the shell
- [ ] document unsupported or degraded workflows explicitly

### Tests

- [ ] automated tests for proxied REST compatibility
- [ ] automated tests for proxied WebSocket/event compatibility
- [ ] regression tests for action guardrails under compatibility mode
- [ ] mixed-version compatibility tests using capability negotiation

### Documentation

- [ ] update `README.md` for remote desktop/dashboard capabilities and limitations
- [ ] update `docs/REST_API.md` for any proxy-facing REST changes
- [ ] update `docs/WEBSOCKET_API.md` for any proxy-facing WebSocket changes
- [ ] update `docs/MCP_API.md` if remote compatibility affects MCP semantics or supported workflows
- [ ] update `Armada.postman_collection.json` for compatibility endpoints

### Exit Criteria

- desktop users can reach deeper dashboard workflows remotely
- compatibility mode does not weaken authz, audit, or action guardrails

---

## Workstream 7: Multi-Instance And SaaS Expansion

Goal: grow from single-instance remote control into a broader SaaS operating surface.

### Checklist

- [ ] implement cross-instance overview
- [ ] implement tags and grouping
- [ ] implement account-level alerting views
- [ ] implement bandwidth metering and cost attribution
- [ ] implement billing hooks
- [ ] implement custom domains
- [ ] implement HA scale-out and cross-node tunnel routing as needed

### Tests

- [ ] multi-instance aggregation tests
- [ ] HA routing and failover tests
- [ ] metering accuracy tests
- [ ] billing integration tests if billing ships

### Documentation

- [ ] update `README.md` for SaaS-level capabilities that are user-visible
- [ ] update `docs/PROXY_API.md` for account-level APIs
- [ ] update `docs/TUNNEL_OPERATIONS.md` for HA/routing behavior

---

## Shared Frontend Refactors Worth Doing In Parallel

These help both the local dashboard and the remote experience:

- [ ] replace oversized full-collection fetches with summary endpoints and proper pagination
- [ ] move away from reload-everything-on-any-event behavior
- [ ] support targeted event-driven updates
- [ ] extract reusable cards, status components, and viewers
- [ ] define stable provider interfaces for auth, websocket/event transport, and notifications

These are good investments, but they should not force the remote MVP to inherit the local dashboard's full shell and routing model.

---

## Documentation And Contract Maintenance Checklist

This workstream is mandatory and should be reviewed at the end of every PR in this effort.

### Core Docs

- [x] `README.md` reflects the current user-visible remote-control story
- [x] `docs/REST_API.md` reflects all Armada-side REST changes
- [x] `docs/MCP_API.md` reflects all MCP-side changes or explicit non-support
- [x] `docs/WEBSOCKET_API.md` reflects all WebSocket/event changes
- [x] `Armada.postman_collection.json` matches the current REST surface and examples

### New Docs Required For This Endeavor

- [x] `docs/TUNNEL_PROTOCOL.md` exists and matches the shipped tunnel behavior
- [x] `docs/PROXY_API.md` exists and matches the shipped proxy API behavior
- [x] `docs/TUNNEL_OPERATIONS.md` exists and covers enrollment, rotation, observability, reconnect, and failure handling

### Rules

- [x] no REST endpoint ships without docs and Postman updates
- [x] no MCP-facing tunnel or remote-control behavior ships without `docs/MCP_API.md` review
- [x] no WebSocket/event behavior change ships without `docs/WEBSOCKET_API.md` review
- [x] no tunnel behavior ships with only code comments and this plan as documentation
- [x] examples, payloads, and version numbers in docs are verified against the current implementation before release

---

## Test Strategy Checklist

This work must be covered across multiple layers.

### Unit

- [ ] identity mapping and policy logic
- [ ] token/session lifecycle
- [x] handshake parsing and capability negotiation
- [-] routing, correlation, retry, and backpressure logic

### Automated / Integration

- [ ] enrollment and pairing
- [ ] tunnel request/response and streaming behavior
- [ ] event forwarding and subscription behavior
- [ ] guarded action execution and rejection
- [ ] offline, stale, reconnect, and mixed-version behaviors

### End-To-End

- [ ] remote user authenticates, selects instance, inspects status, and performs a guarded action
- [ ] notification delivery and read-state synchronization work across devices/sessions
- [ ] failure paths are covered: revoked session, offline instance, stale data, tunnel reconnect, denied action

### Non-Functional

- [ ] soak/load testing for multiplexed tunnels
- [ ] latency and degraded-network testing
- [ ] security testing for instance isolation, token misuse, and unauthorized action attempts

### Existing Armada Test Surfaces To Extend

- [x] `test/Armada.Test.Unit`
- [ ] `test/Armada.Test.Automated`
- [ ] `test/Armada.Test.Runtimes` if runtime or MCP orchestration behavior changes
- [ ] add dedicated remote/tunnel suites if the existing projects become too overloaded

---

## Security Checklist

These are MVP-level concerns, not backlog polish:

- [ ] delegated identity with preserved local authz meaning
- [ ] audit logging for auth, enrollment, tunnel activity, and remote actions
- [ ] rate limiting and abuse controls
- [ ] per-device trust and explicit session revocation
- [ ] short-lived credentials and token rotation
- [ ] strict account and instance isolation
- [-] guarded handling for destructive commands
- [ ] TLS everywhere
- [x] explicit path and capability restrictions on proxied requests

---

## Reliability Checklist

- [x] exponential backoff reconnect with jitter
- [x] heartbeat and stall detection
- [x] tunnel health and latency measurement
- [ ] graceful disconnect/reconnect during deploys
- [ ] browser/event backpressure handling
- [ ] chunked streaming for large logs, diffs, backups, or restores
- [ ] support for enterprise egress realities:
  - outbound proxies
  - captive network interruption
  - reconnect after sleep/resume

---

## Missing Dimensions That Must Stay In Scope

- [-] version and capability negotiation across Armada releases
- [ ] notification-first workflow, not dashboard-first workflow
- [ ] server-backed notification history and read state
- [ ] offline last-known state for mobile
- [ ] device/session inventory and lost-device response
- [ ] MCP proxying considerations where Armada's remote tool surface overlaps existing command flows
- [ ] bandwidth metering for later billing and operational visibility
---

## Recommended Build Order

1. Complete Workstream 0 and freeze the contract boundaries.
2. Deliver Workstream 1 and Workstream 2 together.
3. Deliver Workstream 3 so the remote shell has purpose-built APIs.
4. Deliver Workstream 4 for the first usable mobile-first product.
5. Deliver Workstream 5 to harden real-device reliability and notifications.
6. Deliver Workstream 6 only after the remote primitives are stable.
7. Expand with Workstream 7 when single-instance remote control is proven.

---

## Open Questions

- [ ] What is the exact delegated identity shape on the Armada side: mapped user, service principal, or both?
- [ ] Which destructive actions should be blocked entirely on mobile vs allowed with stronger confirmation?
- [ ] Should approvals be local-instance policy driven, proxy policy driven, or both?
- [ ] What minimum capability manifest is required to safely support mixed-version fleets?
- [ ] What large-payload strategy is preferred for backups/restores: chunked tunnel streaming or separate upload/download channels?
- [ ] How much MCP/tool traffic should be first-class in the tunnel protocol from day one?
- [ ] At what scale do we need Redis or a dedicated tunnel gateway tier instead of single-node routing?

Each open question should eventually gain:

- an owner
- a decision deadline
- a chosen answer
- the docs/tests impacted by that answer

## Summary

The correct sequence is:

- merge trust and tunnel work up front
- preserve local identity and audit meaning
- ship a thin remote operations shell first
- keep REST proxying as infrastructure, not as the product definition
- add full remote dashboard compatibility later

This revised plan is intended to be directly tracked by developers and reviewers. If implementation advances without the matching tests, docs, and Postman updates listed here, the phase is not actually done.
