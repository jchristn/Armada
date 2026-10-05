# Captain Routing and Per-Step Captain Selection

Armada assigns each mission to a captain when the health-check loop runs. By default that assignment is capability-based: a mission tagged with a persona goes to any idle captain that is allowed to serve that persona, optionally narrowed by a capability tier. Per-step captain selection sits on top of that model and lets an operator dictate which captain runs a given pipeline step, while keeping the pool-based fallback that stops one busy captain from stalling the whole voyage.

## The routing model

Three things decide where a mission runs.

A **persona** names the role a step plays (Architect, Worker, TestEngineer, Judge, or one you define). Every pipeline stage is a persona, and every mission carries the persona of the stage that produced it.

A **captain** declares what it can do: its runtime and model, an `AllowedPersonas` whitelist, a `PreferredPersona` bias, and a capability **tier** - Economy, Standard, or Premium, ordered cheapest to strongest. A captain with no tier set is classified from its model name. Tier is the lever that makes fallback meaningful, so set it on every captain you expect to route by capability.

A **preferred captain** is the operator's dictate for a step. It can come from three places, resolved in this order when a mission is created or first assigned:

1. an explicit captain on the dispatch payload (`MissionDescription.RequestedCaptainId`),
2. the voyage's per-persona override chosen at dispatch (an override whose persona is empty or `*` applies to every step that has no exact match), then
3. the persona's default captain (`Persona.DefaultCaptainId`).

Whatever wins becomes the mission's `RequestedCaptainId`. If no fallback tier was given, the mission's fallback tier defaults to the preferred captain's own tier. The preferred captain applies to every mission of that persona in the voyage - including the extra Worker missions an Architect stage fans out, which inherit the persona default rather than falling back to unrouted.

## How assignment resolves

When the Admiral tries to assign a mission it checks the preferred captain first. If that captain is idle, the mission runs on it, and the `AllowedPersonas` fence is deliberately ignored: you chose this captain, so Armada honors the choice even for a persona the captain would not normally accept. If the preferred captain is busy, assignment falls back to an idle captain at or above the mission's fallback tier, preferring the lowest eligible tier so a Premium captain is not spent on routine work. If the preferred captain no longer exists, Armada logs it and routes the mission normally. And if nothing idle satisfies the fallback tier, the mission stays Pending and is retried on the next tick rather than misassigned.

The mission detail page shows both sides of this: the **Preferred Captain** you asked for and the captain that actually ran it, with "(fell back to tier)" when they differ. While a mission is still Pending, the dashboard mission page and the TUI mission screen add a **Why This Mission Is Waiting** section from the server's assignment blocker (`assignmentBlocker` on `GET /api/v1/missions/{id}` and on voyage reads): the reason (for example `NoIdleCaptain`, `NoEligibleCaptain`, `VesselConcurrencyLimit`, or `DependencyNotFinished`), a summary, when it is expected to clear, and what each relevant captain or blocking mission is doing. That is usually the fastest way to see whether your fleet has enough of the right captains idle at the right moments.

## Setting it up

Give each persona a default captain from the persona detail page (the actions menu, Edit, then **Default Captain**). That default pre-fills the per-step **Captain Assignments** picker every time you dispatch a pipeline through Dispatch, where you can accept it, swap in a different captain, or set only a fallback tier. The setup wizard captures each captain's tier during first-run so a fresh install already routes sensibly; it does not ask you to bind persona defaults there - that is a deliberate second step you do once in the dashboard.

For cost and accuracy, the pattern that holds up is roles times tiers. Fence cheap Economy captains to the mechanical, parallelizable roles (Worker, TestEngineer) and run several of them; reserve one or two strong Premium captains for the reasoning-heavy roles (Architect, Judge) and gate those stages with review. Leave routine work untiered so it drains to the cheapest capable captain, and reach for a preferred captain or a Premium fallback tier only where the work genuinely warrants it.

## API surface

Persona create/update (REST `POST /api/v1/personas` and `PUT /api/v1/personas/{name}`, MCP `create_persona` / `update_persona`) accept `defaultCaptainId`, validated against an existing captain. Dispatch (REST `POST /api/v1/voyages`, MCP `dispatch`) accepts a `captainAssignments` array of `{ persona, captainId, fallbackTier }` and an optional per-mission `requestedCaptainId` / `tier`. Mission reads expose both `requestedCaptainId` and `captainId`, and voyage reads carry the stored overrides. The schema columns backing all of this are added by startup migration 55 across SQLite, PostgreSQL, MySQL, and SQL Server.
