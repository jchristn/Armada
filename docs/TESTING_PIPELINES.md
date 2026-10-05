# Testing Pipelines and Stages

This document walks through concrete examples for testing Armada pipelines end-to-end. Each example starts from scratch and can be followed step by step using MCP tools, REST API, or the dashboard.

---

## Prerequisites

- Armada server running (v1.0.0)
- At least one vessel registered with a valid git repository
- At least one captain running (idle state)
- Built-in personas and pipelines are seeded automatically on startup
- Someone available to resolve review gates: the built-in Reviewed, Tested, FullPipeline, and Recorded pipelines stop
  each gated stage in `Review` until it is approved (dashboard or TUI mission page **Resolve Review**, or
  `POST /api/v1/missions/{id}/review/approve`). See [PIPELINES.md](PIPELINES.md#review-gates).

Verify setup:

```
// MCP: check built-in personas exist
enumerate({ entityType: "personas" })

// Expected: Worker, Architect, Product Manager, Usability Engineer, Judge, Test Engineer, Linter, Recorder

// MCP: check built-in pipelines exist
enumerate({ entityType: "pipelines" })

// Expected: WorkerOnly, Reviewed, Tested, FullPipeline, Recorded
```

---

## Example 1: Simple Dispatch with WorkerOnly Pipeline (Baseline)

This confirms the default behavior is unchanged. A single Worker mission is created and assigned.

```
// Dispatch a voyage with no pipeline specified (defaults to WorkerOnly)
dispatch({
  title: "Fix typo in README",
  vesselId: "<your_vessel_id>",
  missions: [
    { title: "Fix typo in README", description: "Fix the spelling of 'recieve' to 'receive' in README.md line 42." }
  ]
})
```

**What to verify:**
1. One voyage is created
2. One mission is created with `persona: null` (defaults to Worker behavior), assuming the vessel and its fleet have no
   default pipeline
3. Mission is assigned to any idle captain
4. No `dependsOnMissionId` is set

```
// Check the voyage
voyage_status({ voyageId: "<voyage_id>", summary: false, includeMissions: true })
```

---

## Example 2: Reviewed Pipeline (Worker + Judge)

This tests a two-stage pipeline where a Worker implements changes, then a Judge reviews the diff.

```
// Dispatch with the built-in "Reviewed" pipeline
dispatch({
  title: "Add input validation to UserService",
  vesselId: "<your_vessel_id>",
  pipeline: "Reviewed",
  missions: [
    {
      title: "Add input validation",
      description: "Add null checks and length validation to all public methods in UserService.cs. Throw ArgumentNullException for null strings, ArgumentException for strings over 500 characters."
    }
  ]
})
```

**What to verify:**
1. One voyage is created
2. **Two missions** are created:
   - `"[Worker] Add input validation"` with `persona: "Worker"`, `dependsOnMissionId: null`
   - `"[Judge] Add input validation"` with `persona: "Judge"`, `dependsOnMissionId: <worker_mission_id>`
3. Only the Worker mission is assigned immediately
4. The Judge mission stays in Pending status

```
// Check the mission chain
enumerate({
  entityType: "missions",
  voyageId: "<voyage_id>",
  includeDescription: true
})
```

**After the Worker completes:**
5. The Worker mission moves to `Review` (the Worker stage has a review gate); approve it to continue
6. The Judge mission's description is updated with the Worker's output and diff
7. The Judge mission's `branchName` is set to the Worker's branch
8. The Judge mission is automatically assigned to an idle captain
9. The Judge captain reviews the diff and must end with a standalone `[ARMADA:VERDICT] PASS|FAIL|NEEDS_REVISION` line.
   `FAIL` or `NEEDS_REVISION` fails the Judge mission; `PASS` sends it to its own review gate, and approving it
   continues to landing

---

## Example 3: FullPipeline (Product Manager through Recorder)

This tests the complete eight-stage pipeline (Product Manager, Architect, Worker, Usability Engineer, Test Engineer,
Linter, Judge, Recorder), including the Architect's special handling.

```
dispatch({
  title: "Add caching to the API layer",
  vesselId: "<your_vessel_id>",
  pipeline: "FullPipeline",
  missions: [
    {
      title: "Add caching to the API layer",
      description: "Implement response caching for GET endpoints in the API. Use an in-memory cache with configurable TTL. Cache should respect Cache-Control headers."
    }
  ]
})
```

**What to verify:**

**Stages 1-2 -- Product Manager, then Architect:**
1. Eight missions are created initially, one per stage, each depending on the one before it:
   - `"[Product Manager] Add caching..."` -- no dependency, assigned immediately
   - `"[Architect] Add caching..."` -- depends on Product Manager
   - `"[Worker] Add caching..."` -- depends on Architect
   - `"[Usability Engineer] ..."`, `"[Test Engineer] ..."`, `"[Linter] ..."`, `"[Judge] ..."`, `"[Recorder] ..."` -- each depends on the stage before it
2. Only the Product Manager mission is assigned; the Architect runs after it completes and its review gate is approved
   (every FullPipeline stage except the Recorder has a review gate)

**After the Architect completes (with mission definitions):**

The Architect should output an `armada-plan` block (see [PIPELINES.md](PIPELINES.md#9-architect-special-handling));
the legacy `[ARMADA:MISSION]` marker format is still accepted as a fallback:
```
[ARMADA:MISSION] Add CacheService with TTL support
Implement CacheService in src/Services/CacheService.cs with Get, Set, Remove methods.
Add ICacheService interface. Use ConcurrentDictionary with timer-based expiry.
Files: src/Services/CacheService.cs, src/Services/Interfaces/ICacheService.cs

[ARMADA:MISSION] Add caching middleware to GET endpoints
Add CacheMiddleware that checks the cache before executing the handler.
Wire into the request pipeline for all GET routes.
Files: src/Middleware/CacheMiddleware.cs, src/Startup.cs
```

3. The original Worker mission is updated with the first parsed mission's title and description (title
   `"Add CacheService with TTL support [Worker]"`)
4. A second Worker mission is created for the second parsed mission
5. Each additional Worker mission gets its own copy of the downstream chain (Usability Engineer, Test Engineer, Linter, Judge, Recorder)
6. Worker missions are assigned to idle captains

**After each Worker completes (and its review gate is approved):**
7. The corresponding Usability Engineer mission receives the Worker's output, diff, and branch, then the Test Engineer and
   Linter stages commit to the same branch
8. The Judge reviews the combined diff and must end with a standalone `[ARMADA:VERDICT] PASS|FAIL|NEEDS_REVISION` line
9. The Recorder distills durable memories from the voyage

```
// Monitor the full pipeline
voyage_status({ voyageId: "<voyage_id>", summary: true })

// Check individual mission status and dependencies
enumerate({
  entityType: "missions",
  voyageId: "<voyage_id>",
  includeDescription: true
})
```

---

## Example 4: Custom Pipeline with a Security Auditor

This tests creating a custom persona and pipeline from scratch.

**Step 1: Create the prompt template**

```
update_prompt_template({
  name: "persona.security_auditor",
  content: "You are a security auditor reviewing code changes for vulnerabilities.\n\nReview the diff from the previous pipeline stage and check for:\n1. SQL injection vulnerabilities\n2. Cross-site scripting (XSS)\n3. Authentication/authorization bypass\n4. Hardcoded secrets or credentials\n5. Insecure deserialization\n6. Path traversal vulnerabilities\n\nFor each finding, report:\n- Severity (Critical, High, Medium, Low)\n- File and line number\n- Description of the vulnerability\n- Recommended fix\n\nIf no vulnerabilities are found, output: SECURITY REVIEW: PASS\nIf vulnerabilities are found, output: SECURITY REVIEW: FAIL followed by your findings.",
  description: "Security auditor persona for reviewing code changes"
})
```

**Step 2: Create the persona**

```
create_persona({
  name: "SecurityAuditor",
  description: "Reviews code changes for OWASP-style security vulnerabilities",
  promptTemplateName: "persona.security_auditor"
})
```

**Step 3: Create the pipeline**

```
create_pipeline({
  name: "SecureReview",
  description: "Worker then SecurityAuditor then Judge",
  stages: [
    { personaName: "Worker" },
    { personaName: "SecurityAuditor" },
    { personaName: "Judge" }
  ]
})
```

**Step 4: Dispatch with the custom pipeline**

```
dispatch({
  title: "Add user login endpoint",
  vesselId: "<your_vessel_id>",
  pipeline: "SecureReview",
  missions: [
    {
      title: "Add user login endpoint",
      description: "Add POST /api/v1/login endpoint that accepts email and password, validates credentials against the database, and returns a JWT token."
    }
  ]
})
```

**What to verify:**
1. Three missions created: Worker, SecurityAuditor, Judge (chained by dependency). Pipelines created through MCP have
   no review gates, so the stages run back to back
2. Worker executes first, implements the login endpoint
3. SecurityAuditor receives the diff and reviews for vulnerabilities (its mission prompt uses
   `persona.security_auditor`, the template name derived from the persona name `SecurityAuditor`)
4. Judge reviews the final result

---

## Example 5: Captain Persona Routing

This tests that the Admiral routes missions to the right captains based on persona preferences.

**Setup: Configure captain preferences**

```
// Dedicate a captain to Architect work
update_captain({
  captainId: "<captain_1_id>",
  preferredPersona: "Architect"
})

// Restrict a captain to Worker only
update_captain({
  captainId: "<captain_2_id>",
  allowedPersonas: "[\"Worker\", \"Test Engineer\"]"
})

// Leave a third captain unrestricted (can fill any role)
// No changes needed -- null AllowedPersonas means any role
```

**Dispatch with FullPipeline:**

```
dispatch({
  title: "Refactor database layer",
  vesselId: "<your_vessel_id>",
  pipeline: "FullPipeline",
  missions: [
    { title: "Refactor database layer", description: "Extract common query patterns into a base repository class." }
  ]
})
```

**What to verify:**
1. The Architect mission is preferably assigned to captain_1 (PreferredPersona match)
2. The Worker mission is assigned to any idle captain (all three are eligible)
3. The Judge mission is NOT assigned to captain_2 (AllowedPersonas excludes Judge)
4. If only captain_2 is idle when the Judge stage is ready, the Judge mission stays `Pending` until captain_1 or
   captain_3 is idle (`AllowedPersonas` is a hard filter; the mission page's Why This Mission Is Waiting shows
   `NoEligibleCaptain`)

```
// Check which captain got which mission
enumerate({
  entityType: "missions",
  voyageId: "<voyage_id>"
})
// Look at captainId on each mission
```

---

## Example 6: Setting a Default Pipeline on a Vessel

This tests that vessel-level pipeline defaults work correctly.

```
// Set "Reviewed" as the default pipeline for a vessel
update_vessel({
  vesselId: "<your_vessel_id>",
  defaultPipelineId: "<reviewed_pipeline_id>"
})
```

Get the pipeline ID first:
```
get_pipeline({ name: "Reviewed" })
// Note the id field
```

Now dispatch WITHOUT specifying a pipeline:

```
dispatch({
  title: "Update documentation",
  vesselId: "<your_vessel_id>",
  missions: [
    { title: "Update API docs", description: "Update REST_API.md with the new endpoint parameters." }
  ]
})
```

**What to verify:**
1. Even though no `pipeline` or `pipelineId` was passed to dispatch, the vessel's default kicks in
2. Two missions are created (Worker + Judge, both with review gates) because "Reviewed" is the default
3. The dependency chain is set up correctly

**Override the default for one dispatch:**

```
dispatch({
  title: "Quick hotfix",
  vesselId: "<your_vessel_id>",
  pipeline: "WorkerOnly",
  missions: [
    { title: "Fix null check", description: "Add null check on line 42 of Handler.cs" }
  ]
})
```

4. Only one Worker mission is created (the explicit pipeline overrides the vessel default)

---

## Monitoring and Troubleshooting

### Check pipeline progress

```
// Voyage summary shows mission counts by status
voyage_status({ voyageId: "<voyage_id>", summary: true })

// Full mission list with dependencies visible
enumerate({
  entityType: "missions",
  voyageId: "<voyage_id>",
  includeDescription: true
})
```

### Common issues

**Mission stuck in Pending:**
- Open the mission in the dashboard or TUI: the **Why This Mission Is Waiting** card shows the server's reason (the
  `AssignmentBlocker` on `GET /api/v1/missions/{id}`)
- Check `dependsOnMissionId` -- the dependency mission may not have completed yet, or may be waiting in `Review` for
  its review gate to be approved
- Check if any idle captains are available
- If `AllowedPersonas` is set on all captains, verify at least one can fill the required persona

**Architect didn't produce multiple missions:**
- The Architect must output an `armada-plan` block (or legacy `[ARMADA:MISSION]` markers) in its response
- If no valid mission definitions are found, the Architect mission is marked `Failed` with failure kind `InvalidOutput`
- Check the Architect mission's agent output for the plan block or markers

**Judge always sees empty diff:**
- The diff is injected from the prior stage's `diffSnapshot` field
- Verify the Worker mission has a non-null `diffSnapshot` after completion
- The diff capture runs before the handoff -- check logs for "error capturing diff"

### Dashboard monitoring

The dashboard shows persona and dependency information on:
- **Mission detail page** -- `Persona` field, `Depends On` link, and **Resolve Review** for a gated stage
- **Voyage detail page** -- all missions listed with their status
- **Captain detail page** -- `Preferred Persona` and `Allowed Personas` fields
- **Vessel detail page** -- `Default Pipeline` field
