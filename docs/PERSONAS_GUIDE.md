# Personas, Pipelines, and Prompt Templates -- User Guide

This guide covers how to use Armada's persona system to specialize agent behavior,
chain work through multi-stage pipelines, and customize every prompt the system generates.

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Built-in Personas](#2-built-in-personas)
3. [Pipelines](#3-pipelines)
4. [Configuring Pipelines](#4-configuring-pipelines)
5. [Captain Persona Capabilities](#5-captain-persona-capabilities)
6. [Prompt Templates](#6-prompt-templates)
7. [Creating Custom Personas](#7-creating-custom-personas)
8. [API Reference (Quick Reference)](#8-api-reference-quick-reference)

---

## 1. Introduction

### What Are Personas?

A **persona** is a named agent role that determines what a captain does during a mission.
Without personas, every captain behaves the same way -- reads a description, makes code
changes, commits, and exits. Personas let you assign specialized roles so that different
captains perform different tasks: planning, implementing, testing, or reviewing.

Each persona points to a **prompt template** containing the instructions given to the
agent. Changing the persona changes the instructions, which changes the behavior.

### How Personas Fit into the Architecture

```
Fleet
  └── Vessel (repository)
        └── Voyage (batch of work)
              └── Mission (single task)
                    └── Persona (agent role for this mission)
                          └── Prompt Template (instructions text)
```

A persona is a property of the **mission**, not the captain. Any captain can fill any
persona role (unless restricted via `AllowedPersonas`). The Admiral assigns captains to
missions based on availability and persona preferences.

---

## 2. Built-in Personas

Armada ships with four built-in personas, seeded on first startup. They cannot be deleted
but their prompt templates can be customized.

### Worker

The default persona. If no persona is specified on a mission, it uses Worker.

- **Purpose:** Execute code changes, write implementations, fix bugs.
- **Prompt template:** `persona.worker`
- **When to use:** Any mission that produces code changes.

### Architect

Plans work and decomposes goals into right-sized missions.

- **Purpose:** Analyze a codebase, understand scope and dependencies, break a high-level
  goal into concrete missions. Outputs structured mission definitions using
  `[ARMADA:MISSION]` markers.
- **Prompt template:** `persona.architect`
- **When to use:** Large feature requests where you want AI-driven task decomposition.
  Typically the first stage in a multi-stage pipeline.

### Judge

Reviews completed work for correctness and completeness.

- **Purpose:** Examine the Worker's diff against the mission description. Check
  completeness, correctness, scope, and style. Produce a verdict: PASS, FAIL, or
  NEEDS_REVISION.
- **Prompt template:** `persona.judge`
- **When to use:** Quality gate after Worker and/or TestEngineer stages.

### TestEngineer

Writes tests for the changes produced by a Worker.

- **Purpose:** Analyze the Worker's diff, identify missing test coverage, and write
  unit/integration tests following the repository's existing patterns.
- **Prompt template:** `persona.test_engineer`
- **When to use:** After a Worker stage when you want automated test generation.

---

## 3. Pipelines

### What Is a Pipeline?

A **pipeline** is an ordered sequence of persona stages that a dispatch goes through.
For a single-stage pipeline (like WorkerOnly), Armada behaves as it always has. For
multi-stage pipelines, the Admiral creates one mission per stage, each depending on the
previous one.

### Built-in Pipelines

| Pipeline | Stages | Description |
|---|---|---|
| **WorkerOnly** | Worker | The default. Single-stage, backward-compatible behavior. |
| **Reviewed** | Worker -> Judge | Work is implemented, then reviewed. |
| **Tested** | Worker -> TestEngineer -> Judge | Work is implemented, tests are written, then everything is reviewed. |
| **FullPipeline** | Architect -> Worker -> TestEngineer -> Judge | Full lifecycle: plan, implement, test, review. |

### Pipeline Resolution Order (Precedence)

When a voyage is dispatched, the Admiral determines which pipeline to use. **Highest priority wins:**

| Priority | Source | Set Via |
|----------|--------|---------|
| 1 (highest) | **Explicit dispatch parameter** | `pipelineId` or `pipeline` on dispatch |
| 2 | **Vessel default** | `DefaultPipelineId` on the target vessel (dashboard, MCP, REST) |
| 3 | **Fleet default** | `DefaultPipelineId` on the vessel's parent fleet (dashboard, MCP, REST) |
| 4 (lowest) | **System fallback** | WorkerOnly (no configuration needed) |

This means a fleet-level default applies to all vessels in that fleet unless overridden at the vessel level, and any explicit pipeline on a dispatch overrides both. If a referenced pipeline has been deleted, the stale reference is automatically cleared.

### How Missions Chain

When a pipeline has multiple stages:

1. The Admiral creates one mission per stage, all within the same voyage.
2. Each mission after the first has `DependsOnMissionId` pointing to the previous stage.
3. The first mission is assigned immediately; subsequent ones stay `Pending`.
4. On completion, **stage handoff** injects context (persona, title, branch, diff) into
   the next mission, sets the same branch, and attempts assignment.
5. All stages work on the **same branch**, building on prior work.

### The Architect Special Case

The Architect outputs structured mission definitions using `[ARMADA:MISSION]` markers.
Currently this output is injected as context for the next stage; full automatic parsing
into new Worker missions is a planned enhancement.

---

## 4. Configuring Pipelines

### Setting a Default Pipeline on a Vessel

**Via MCP:**

```json
// armada_update_vessel
{
  "vesselId": "vsl_abc123",
  "defaultPipelineId": "ppl_xyz789"
}
```

**Via REST:**

```bash
curl -X PUT http://localhost:7890/api/v1/vessels/vsl_abc123 \
  -H "Content-Type: application/json" -H "Authorization: Bearer TOKEN" \
  -d '{"DefaultPipelineId": "ppl_xyz789"}'
```

**Via Dashboard:** Open the vessel detail page, click Edit, and select a default pipeline.

### Setting a Default Pipeline on a Fleet

```json
// armada_update_fleet
{
  "fleetId": "flt_abc123",
  "defaultPipelineId": "ppl_xyz789"
}
```

### Overriding Per-Dispatch

Pass `pipelineId` to `armada_dispatch` to override for a single voyage:

```json
// armada_dispatch
{
  "title": "Add authentication",
  "vesselId": "vsl_abc123",
  "pipelineId": "ppl_xyz789",
  "missions": [
    {
      "title": "Add JWT middleware",
      "description": "Create middleware that validates JWT tokens"
    }
  ]
}
```

### Creating a Custom Pipeline

**Via MCP:**

```json
// armada_create_pipeline
{
  "name": "SecurityReview",
  "description": "Implement then run security audit",
  "stages": [
    { "personaName": "Worker", "description": "Implement the feature" },
    { "personaName": "SecurityAuditor", "description": "Audit for vulnerabilities" },
    { "personaName": "Judge", "description": "Final review" }
  ]
}
```

**Via Dashboard:** Navigate to Pipelines in the sidebar, click Create, and use the
dynamic stage editor to add stages in order.

Note: `SecurityAuditor` in this example is a custom persona you would create first
(see [Section 7](#7-creating-custom-personas)).

---

## 5. Captain Persona Capabilities

### AllowedPersonas

By default, any captain can fill any persona role (`AllowedPersonas` is null). You can
restrict a captain to specific personas:

```json
// armada_update_captain
{
  "captainId": "cpt_abc123",
  "allowedPersonas": ["Worker", "TestEngineer"]
}
```

When set, the Admiral will only assign this captain to missions requiring one of the
listed personas. Missions with other personas will be assigned to other captains.

### PreferredPersona

A soft routing preference. The Admiral prefers captains whose `PreferredPersona` matches
the mission's persona, but will fall back to any available captain if no preferred match
exists.

```json
// armada_update_captain
{
  "captainId": "cpt_abc123",
  "preferredPersona": "Architect"
}
```

### Example: Dedicated Captain Roles

Dedicate a powerful model for Architect work and faster models for Worker tasks:

```json
// Create an Opus captain for architecture and review
// armada_create_captain
{
  "name": "opus-architect",
  "runtime": "ClaudeCode",
  "allowedPersonas": ["Architect", "Judge"],
  "preferredPersona": "Architect"
}

// Create Sonnet captains for implementation
// armada_create_captain
{
  "name": "sonnet-worker-1",
  "runtime": "ClaudeCode",
  "allowedPersonas": ["Worker", "TestEngineer"],
  "preferredPersona": "Worker"
}
```

---

## 6. Prompt Templates

### What They Are

Every instruction Armada gives to an agent is driven by a **prompt template** -- text
with `{Placeholder}` parameters substituted at runtime. Armada ships with built-in
defaults for all templates. You can customize any template and reset at any time.

### Template Categories

| Category | Purpose | Examples |
|---|---|---|
| **persona** | Core persona instructions | `persona.worker`, `persona.architect`, `persona.judge`, `persona.test_engineer` |
| **mission** | Mission-level rules and constraints | `mission.rules`, `mission.context_conservation`, `mission.merge_conflict_avoidance`, `mission.progress_signals`, `mission.model_context_updates` |
| **structure** | Layout wrappers for CLAUDE.md sections | `mission.metadata`, `mission.captain_instructions_wrapper`, `mission.project_context_wrapper`, `mission.code_style_wrapper`, `mission.model_context_wrapper`, `mission.existing_instructions_wrapper` |
| **commit** | Commit message and trailer instructions | `commit.instructions_preamble` |
| **landing** | PR creation templates | `landing.pr_body` |
| **agent** | Agent launch prompts | `agent.launch_prompt` |

### How Resolution Works

1. Check the database for a template with the given name.
2. If found, use the database version (which may be user-customized).
3. If not found, fall back to the embedded default shipped with Armada.

### Available Placeholders

Placeholders are grouped by context. Not all placeholders are available in all templates --
they depend on what data is available at render time.

**Mission Context:**
`{MissionId}`, `{MissionTitle}`, `{MissionDescription}`, `{MissionPersona}`,
`{VoyageId}`, `{VoyageTitle}`, `{BranchName}`

**Vessel Context:**
`{VesselId}`, `{VesselName}`, `{DefaultBranch}`, `{ProjectContext}`, `{StyleGuide}`,
`{ModelContext}`, `{FleetId}`

**Captain Context:**
`{CaptainId}`, `{CaptainName}`, `{CaptainInstructions}`

**Pipeline Context:**
`{PipelineName}`, `{StageNumber}`, `{TotalStages}`, `{PreviousStageDiff}`,
`{PreviousStageOutput}`

**System:**
`{Timestamp}`, `{ExistingClaudeMd}`

### Editing via Dashboard

Navigate to **Prompt Templates** in the sidebar. The detail page has a two-column editor:
the left panel is a monospace text editor with save/reset buttons, and the right panel
shows available placeholders grouped by context with click-to-insert. Built-in templates
display a badge and offer "Reset to Default" in the action menu.

### Editing via MCP Tools

**Get a template:**

```json
// armada_get_prompt_template
{ "name": "mission.rules" }
```

**Update a template:**

```json
// armada_update_prompt_template
{
  "name": "mission.rules",
  "content": "## Rules\n- Work only within this worktree\n- {MyCustomRule}\n- Commit all changes\n"
}
```

**Reset to default:**

```json
// armada_reset_prompt_template
{ "name": "mission.rules" }
```

### Editing via REST API

```bash
# Get
curl http://localhost:7890/api/v1/prompt-templates/mission.rules -H "Authorization: Bearer TOKEN"

# Update
curl -X PUT http://localhost:7890/api/v1/prompt-templates/mission.rules \
  -H "Content-Type: application/json" -H "Authorization: Bearer TOKEN" \
  -d '{"Content": "## Rules\n- Custom rules here\n"}'

# Reset to default
curl -X POST http://localhost:7890/api/v1/prompt-templates/mission.rules/reset \
  -H "Authorization: Bearer TOKEN"
```

### Example: Adding a Project-Specific Rule

```json
// armada_update_prompt_template
{
  "name": "mission.rules",
  "content": "## Rules\n- Work only within this worktree directory\n- All database queries must use parameterized statements\n- Commit all changes to the current branch\n- Exit with code 0 on success\n"
}
```

Now every mission (regardless of persona) will include your custom rule.

---

## 7. Creating Custom Personas

Follow these steps to create and use a custom persona.

### Step 1: Create a Prompt Template

```json
// armada_update_prompt_template
{
  "name": "persona.security_auditor",
  "content": "You are a security auditor. Review the diff and identify vulnerabilities.\n\n## Instructions\n- Check for SQL injection, XSS, CSRF, authentication bypasses\n- Check for hardcoded secrets or credentials\n- Produce a report with severity ratings\n- Output FAIL with details if critical issues found, PASS with recommendations otherwise\n",
  "description": "Security auditor persona - reviews diffs for vulnerabilities"
}
```

### Step 2: Create a Persona

```json
// armada_create_persona
{
  "name": "SecurityAuditor",
  "description": "Reviews code changes for security vulnerabilities",
  "promptTemplateName": "persona.security_auditor"
}
```

### Step 3: Add the Persona to a Pipeline

```json
// armada_create_pipeline
{
  "name": "SecureWorkflow",
  "description": "Implement, audit for security, then review",
  "stages": [
    { "personaName": "Worker", "description": "Implement the feature" },
    { "personaName": "SecurityAuditor", "description": "Security audit" },
    { "personaName": "Judge", "description": "Final review" }
  ]
}
```

### Step 4: Test by Dispatching

```json
// armada_dispatch
{
  "title": "Add payment processing",
  "vesselId": "vsl_abc123",
  "pipelineId": "ppl_the_id_returned_from_step_3",
  "missions": [
    {
      "title": "Implement Stripe integration",
      "description": "Add payment processing via Stripe API"
    }
  ]
}
```

The Admiral will create three missions in sequence: Worker -> SecurityAuditor -> Judge.
Each stage sees the diff from the previous stage.

---

## 8. API Reference (Quick Reference)

### MCP Tools

| Tool | Description |
|---|---|
| **Personas** | |
| `armada_create_persona` | Create a custom persona (name, promptTemplateName required) |
| `armada_get_persona` | Get a persona by name |
| `armada_update_persona` | Update persona description or prompt template |
| `armada_delete_persona` | Delete a custom persona (built-in personas cannot be deleted) |
| **Pipelines** | |
| `armada_create_pipeline` | Create a pipeline with ordered stages |
| `armada_get_pipeline` | Get a pipeline by name (includes stages) |
| `armada_update_pipeline` | Update pipeline description or replace stages |
| `armada_delete_pipeline` | Delete a custom pipeline (built-in pipelines cannot be deleted) |
| **Prompt Templates** | |
| `armada_get_prompt_template` | Get a template by name |
| `armada_update_prompt_template` | Update template content and/or description |
| `armada_reset_prompt_template` | Reset a template to its built-in default |
| **Enumeration** | |
| `armada_enumerate` | Use `entityType: "persona"`, `"pipeline"`, or `"prompt_template"` to list/filter/paginate |
| **Related** | |
| `armada_dispatch` | Pass `pipelineId` to override the default pipeline |
| `armada_update_vessel` | Set `defaultPipelineId` on a vessel |
| `armada_update_fleet` | Set `defaultPipelineId` on a fleet |
| `armada_create_captain` | Set `allowedPersonas` and `preferredPersona` |
| `armada_update_captain` | Update `allowedPersonas` and `preferredPersona` |

### REST Endpoints

All three entity types follow the same pattern. Replace `{entity}` with `personas`,
`pipelines`, or `prompt-templates`:

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/v1/{entity}` | List all |
| POST | `/api/v1/{entity}/enumerate` | Paginated enumeration with filters |
| GET | `/api/v1/{entity}/{name}` | Get by name |
| POST | `/api/v1/{entity}` | Create (personas and pipelines only) |
| PUT | `/api/v1/{entity}/{name}` | Update |
| DELETE | `/api/v1/{entity}/{name}` | Delete (personas and pipelines only, built-in protected) |
| POST | `/api/v1/prompt-templates/{name}/reset` | Reset template to built-in default |

### WebSocket Commands

The WebSocket API provides equivalent commands via the `command` route. Actions follow
the pattern `get_persona`, `create_persona`, `update_persona`, `delete_persona` (same
for `pipeline` and `prompt_template`). Use the `enumerate` action with `entityType` set
to `persona`, `pipeline`, or `prompt_template` for listing.
