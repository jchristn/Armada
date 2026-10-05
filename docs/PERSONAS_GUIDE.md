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

Each persona points to a **prompt template** (`PromptTemplateName`) containing the instructions given to
the agent. Changing the persona changes the instructions, which changes the behavior. When the named
template does not exist, mission prompts fall back to `persona.<name in snake_case>` and log a warning.

### How Personas Fit into the Architecture

```
Fleet
  +-- Vessel (repository)
        +-- Voyage (batch of work)
              +-- Mission (single task)
                    +-- Persona (agent role for this mission)
                          +-- Prompt Template (instructions text)
```

A persona is a property of the **mission**, not the captain. Any captain can fill any
persona role (unless restricted via `AllowedPersonas`). The Admiral assigns captains to
missions based on availability, persona restrictions and preferences, preferred captains, and
capability tiers (see [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md)).

---

## 2. Built-in Personas

Armada ships with eight built-in personas, seeded on startup: Worker, Architect, Product
Manager, Usability Engineer, Judge, Test Engineer, Linter, and Recorder. They cannot be
deleted but their prompt templates can be customized. The older name `TestEngineer` is
still accepted for Test Engineer.

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

### Product Manager

Frames the work before it is planned.

- **Purpose:** Turn the dispatched work into a whole-product picture: user value, success
  criteria, user personas and workflows, the concrete experience, validation expectations,
  and future-facing requirements, staying inside the dispatched scope.
- **Prompt template:** `persona.product_manager`
- **When to use:** First stage of `FullPipeline`, ahead of the Architect.

### Usability Engineer

Refines the implemented work from the user's side.

- **Purpose:** Improve usability, edge-case experience, and consistency with the
  surrounding product.
- **Prompt template:** `persona.usability_engineer`
- **When to use:** After the Worker stage, before tests and review (as in `FullPipeline`).

### Judge

Reviews completed work through a bounded three-lens contract.

- **Purpose:** Examine the Worker's diff against the mission description through
  exactly three lenses -- **Correctness** (is the change logically correct for the
  inputs it can receive?), **Blast Radius** (what else could it break, and how far
  do its effects reach?), and **Source Fidelity** (does it faithfully implement the
  mission, stay in scope, and match the real codebase without inventing behavior?).
  Produce a verdict: PASS, FAIL, or NEEDS_REVISION.
- **Bounded blocking:** To block (FAIL or NEEDS_REVISION) the Judge MUST include a
  `## Affected Case` section exhibiting one concrete affected case as a labeled
  field line: `File: <path>[:line]` or `Scenario: <inputs and the wrong result>`.
  Only those labeled lines are read; a blocking verdict without one is not
  accepted.
- **Verdict signal:** The verdict is read only from a standalone
  `[ARMADA:VERDICT] PASS|FAIL|NEEDS_REVISION` line outside any code block. Prose
  such as "Verdict: PASS", bare PASS/FAIL lines (for example test-runner output),
  and conflicting verdict lines are not verdicts; a Judge without exactly one
  structured verdict blocks landing. A PASS must fill all three lens sections with real reasoning; a
  shallow or verdict-only PASS is rejected and the mission fails terminally rather
  than silently re-running.
- **Prompt template:** `persona.judge`
- **When to use:** Quality gate after Worker and/or Test Engineer stages.

### Test Engineer

Writes tests for the changes produced by a Worker.

- **Purpose:** Analyze the Worker's diff, identify missing test coverage, and write
  unit/integration tests following the repository's existing patterns.
- **Prompt template:** `persona.test_engineer`
- **When to use:** After a Worker stage when you want automated test generation.

### Linter

Evaluates the mission's changed **code and documentation** for style and correctness.

- **Purpose:** Review the diff for code style (naming, formatting, import ordering,
  adherence to the project's style guide and idioms), code correctness (typos, obvious
  defects, unhandled edge paths, mismatched signatures), documentation style (Markdown
  formatting, headings, spelling/grammar, code-fence tags), and documentation correctness
  (broken/stale links, examples or commands that no longer match the code). It corrects
  clear, safe, in-scope violations directly and flags judgment calls as findings, staying
  strictly within the files the mission changed.
- **Prompt template:** `persona.linter`
- **When to use:** As a quality gate after the work exists (typically after the Worker and
  Test Engineer stages) and before the final Judge review.

### Recorder

Captures what the voyage learned.

- **Purpose:** Review the voyage conversation and distill durable memories (episodic,
  semantic, procedural) into the vessel's model context, the Armada memory store, and
  external memory facilities.
- **Prompt template:** `persona.recorder`
- **When to use:** As the last stage, after review (`FullPipeline` and `Recorded`).

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
| **Tested** | Worker -> Test Engineer -> Judge | Work is implemented, tests are written, then everything is reviewed. |
| **FullPipeline** | Product Manager -> Architect -> Worker -> Usability Engineer -> Test Engineer -> Linter -> Judge -> Recorder | Full lifecycle: frame, plan, implement, refine, test, lint, review, and record durable memory. |
| **Recorded** | Worker -> Recorder | Do the work, then distill durable memories from it. |

### Pipeline Resolution Order (Precedence)

When a voyage is dispatched, the Admiral determines which pipeline to use. **Highest priority wins:**

| Priority | Source | Set Via |
|----------|--------|---------|
| 1 (highest) | **Explicit dispatch parameter** | `pipelineId` or `pipeline` on dispatch |
| 2 | **Vessel default** | `DefaultPipelineId` on the target vessel (dashboard, MCP, REST) |
| 3 | **Fleet default** | `DefaultPipelineId` on the vessel's parent fleet (dashboard, MCP, REST) |
| 4 (lowest) | **System fallback** | WorkerOnly (no configuration needed) |

This means a fleet-level default applies to all vessels in that fleet unless overridden at the vessel level, and any explicit pipeline on a dispatch overrides both. An explicit pipeline that does not exist rejects the
dispatch (REST 404, MCP `PipelineNotFound`) instead of falling back to a default. If a vessel or fleet default
references a pipeline that has been deleted, the stale reference is automatically cleared.

### How Missions Chain

When a pipeline has multiple stages:

1. The Admiral creates one mission per stage, all within the same voyage.
2. Each mission after the first has `DependsOnMissionId` pointing to the previous stage.
3. The first mission is assigned immediately; subsequent ones stay `Pending`.
4. On completion, **stage handoff** injects context (persona, title, branch, diff) into
   the next mission, sets the same branch, and attempts assignment.
5. All stages work on the **same branch**, building on prior work.

### The Architect Special Case

The Architect outputs its plan as one fenced code block whose info string is `armada-plan`:

````
```armada-plan
{"missions": [
  {"title": "Add core model properties", "description": "Update Captain.cs and Mission.cs.", "dependsOn": null},
  {"title": "Extend secondary backends", "description": "Update PostgreSQL and MySQL.", "dependsOn": 1},
  {"title": "Document the final behavior", "description": "Update README.md.", "waitForOtherMissions": true}
]}
```
````

The Admiral deserializes the block and creates one Worker mission (plus the downstream
stages) per entry. `dependsOn` is the 1-based number of an earlier entry whose full
Worker -> Test Engineer -> Judge chain must finish first; `waitForOtherMissions` holds the
mission until every other Worker mission in the voyage has settled. Sequencing comes only
from these fields (persisted as `DependsOnMissionId` and `WaitForVoyageWorkers`), never from
description wording.

Fallback: when the output has no valid `armada-plan` block, the older `[ARMADA:MISSION]`
marker format is still read (title on the marker line, description below, an optional
standalone `Depends on: Mission N` line). For that format only, deferral wording such as
"after both implementation missions complete" is converted once, at parse time, into
`WaitForVoyageWorkers`; a mission created any other way is never deferred by its wording.

### Captain Status Signals

A captain may print `[ARMADA:STATUS] Testing` on a line of its own while it runs its tests,
and `[ARMADA:STATUS] InProgress` to switch back. This is informational and is the only status
change a captain can make:

- Only the captain's own **stdout** counts. Agent CLIs print tool and command output (for
  example a file the agent printed with `cat`) on stderr in text mode, so a status line inside
  such output never changes the mission.
- Only the InProgress/Testing toggle is applied. Review, WorkProduced, Complete, Failed, and
  Cancelled are decided by the Admiral from the process exit and the completion pipeline
  (diff capture, boundary scan, Definition-of-Done gate, Judge verdict, landing); a status line
  naming them is recorded as a progress signal and otherwise ignored.

---

## 4. Configuring Pipelines

### Setting a Default Pipeline on a Vessel

**Via MCP:**

```json
// update_vessel
{
  "vesselId": "vsl_abc123",
  "defaultPipelineId": "ppl_xyz789"
}
```

**Via REST:** `PUT /api/v1/vessels/{id}` replaces the whole vessel record, so read the
vessel, change the field, and send the full object back:

```bash
curl -s http://localhost:7890/api/v1/vessels/vsl_abc123 -H "Authorization: Bearer TOKEN" \
  | jq '.defaultPipelineId = "ppl_xyz789"' \
  | curl -X PUT http://localhost:7890/api/v1/vessels/vsl_abc123 \
      -H "Content-Type: application/json" -H "Authorization: Bearer TOKEN" -d @-
```

**Via Dashboard:** Open the vessel detail page, choose Edit from its actions menu, and set
**Default Pipeline**.

### Setting a Default Pipeline on a Fleet

In the dashboard, the fleet form has the same **Default Pipeline** field. Via MCP:

```json
// update_fleet
{
  "fleetId": "flt_abc123",
  "defaultPipelineId": "ppl_xyz789"
}
```

### Overriding Per-Dispatch

Pass `pipelineId` (or `pipeline`, the pipeline's name) to `dispatch` to override for a
single voyage:

```json
// dispatch
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
// create_pipeline
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

**Via Dashboard:** Open **Configuration**, choose the **Pipelines** tab, click **+ Pipeline**,
and use the stage editor to add stages in order. The dashboard form also sets each stage's
review gate (`requiresReview`, `reviewDenyAction`), which the `create_pipeline` MCP tool
does not expose; see [PIPELINES.md](PIPELINES.md).

Note: `SecurityAuditor` in this example is a custom persona you would create first
(see [Section 7](#7-creating-custom-personas)).

---

## 5. Captain Persona Capabilities

### AllowedPersonas

By default, any captain can fill any persona role (`AllowedPersonas` is null). You can
restrict a captain to specific personas:

`allowedPersonas` is a string holding a JSON array:

```json
// update_captain
{
  "captainId": "cpt_abc123",
  "allowedPersonas": "[\"Worker\", \"Test Engineer\"]"
}
```

When set, the Admiral will only assign this captain to missions requiring one of the
listed personas. This is a hard filter: if no idle captain is allowed to serve a mission's
persona, the mission stays `Pending` until one is. The one exception is a preferred captain
(a dispatch override or the persona's `defaultCaptainId`), which is used whenever it is idle;
see [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md).

### PreferredPersona

A soft routing preference. Among the captains eligible for a mission, the Admiral prefers
those whose `PreferredPersona` matches the mission's persona, and falls back to any other
eligible captain if none matches.

```json
// update_captain
{
  "captainId": "cpt_abc123",
  "preferredPersona": "Architect"
}
```

### Example: Dedicated Captain Roles

Dedicate a powerful model for Architect work and faster models for Worker tasks:

```json
// Create an Opus captain for architecture and review
// create_captain
{
  "name": "opus-architect",
  "runtime": "ClaudeCode",
  "allowedPersonas": "[\"Architect\", \"Judge\"]",
  "preferredPersona": "Architect"
}

// Create Sonnet captains for implementation
// create_captain
{
  "name": "sonnet-worker-1",
  "runtime": "ClaudeCode",
  "allowedPersonas": "[\"Worker\", \"Test Engineer\"]",
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
| **persona** | Core persona instructions | `persona.worker`, `persona.architect`, `persona.product_manager`, `persona.usability_engineer`, `persona.judge`, `persona.test_engineer`, `persona.linter`, `persona.recorder` |
| **mission** | Mission-level rules and constraints | `mission.rules`, `mission.context_conservation`, `mission.merge_conflict_avoidance`, `mission.progress_signals`, `mission.model_context_updates` |
| **structure** | Layout wrappers for CLAUDE.md sections | `mission.metadata`, `mission.captain_instructions_wrapper`, `mission.project_context_wrapper`, `mission.code_style_wrapper`, `mission.model_context_wrapper`, `mission.playbooks_wrapper`, `mission.existing_instructions_wrapper` |
| **commit** | Commit message instructions (always sent) and the sentence introducing the Armada trailers (sent only when commit metadata is enabled) | `commit.instructions_preamble`, `commit.trailers_preamble` |
| **landing** | PR creation templates | `landing.pr_body` |
| **agent** | Agent launch prompts | `agent.launch_prompt` |
| **ask** | Ask Armada system prompt | `ask.system` |
| **vessel** | Vessel "Build Context" action | `vessel.build_context` |
| **import** | Fleet recommendations for bulk-imported repositories | `import.fleet_categorization` |

### How Resolution Works

1. Check the database for a template with the given name.
2. If found, use the database version (which may be user-customized).
3. If not found, fall back to the embedded default shipped with Armada.

### Available Placeholders

Rendering replaces each `{Name}` with its value; a placeholder Armada does not fill is left in
the text unchanged. These are filled for mission prompt templates (`mission.*`, `persona.*`,
and the structure wrappers):

**Mission Context:**
`{MissionId}`, `{MissionTitle}`, `{MissionDescription}`, `{MissionPersona}`,
`{VoyageId}`, `{BranchName}`, `{PersonaPrompt}`, `{SelectedPlaybooksMarkdown}`

**Vessel Context:**
`{VesselId}`, `{VesselName}`, `{DefaultBranch}`, `{ProjectContext}`, `{StyleGuide}`,
`{ModelContext}`, `{FleetId}`

**Captain Context:**
`{CaptainId}`, `{CaptainName}`, `{CaptainInstructions}`

**Pipeline Context:**
`{Diff}` (the prior stage's diff, capped at 20000 characters), `{PreviousStageOutput}` (the prior
stage's agent output, capped at 8000 characters). The prior stage is the mission this one depends on;
without one they hold a short "not available" note. The built-in `persona.judge`,
`persona.test_engineer`, and `persona.linter` templates use them.

**System:**
`{Timestamp}`, `{ExistingClaudeMd}`

Prior-stage context (persona, title, branch, agent output, and full diff) also reaches the next stage
through its mission description, which the stage handoff rewrites, so `{MissionDescription}` carries
it too. The dashboard and TUI placeholder panels list exactly these placeholders. The commit and PR message templates use a different
set, including `{VoyageTitle}` and `{DockId}`; see [MESSAGE_TEMPLATES.md](MESSAGE_TEMPLATES.md).

### Editing via Dashboard

Open **Configuration** and choose the **Prompts** tab. The detail page has a two-column editor:
the left panel is a monospace text editor with save/reset buttons, and the right panel
shows available placeholders grouped by context with click-to-insert. Built-in templates
display a badge and offer "Reset to Default" in the action menu.

### Editing via MCP Tools

`list_prompt_templates` lists templates (optionally by `category`), and
`create_prompt_template` creates a new one.

**Get a template:**

```json
// get_prompt_template
{ "name": "mission.rules" }
```

**Update a template:**

```json
// update_prompt_template
{
  "name": "mission.rules",
  "content": "## Rules\n- Work only within this worktree\n- {MyCustomRule}\n- Commit all changes\n"
}
```

**Reset to default:**

```json
// reset_prompt_template
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
// update_prompt_template
{
  "name": "mission.rules",
  "content": "## Rules\n- Work only within this worktree directory\n- All database queries must use parameterized statements\n- Commit all changes to the current branch\n- Exit with code 0 on success\n"
}
```

Now every mission (regardless of persona) will include your custom rule. Placeholders you
invent, such as `{MyCustomRule}` above, are not filled; they appear in the prompt as written.

---

## 7. Creating Custom Personas

Follow these steps to create and use a custom persona.

### Step 1: Create a Prompt Template

```json
// update_prompt_template
{
  "name": "persona.security_auditor",
  "content": "You are a security auditor. Review the diff and identify vulnerabilities.\n\n## Instructions\n- Check for SQL injection, XSS, CSRF, authentication bypasses\n- Check for hardcoded secrets or credentials\n- Produce a report with severity ratings\n- Output FAIL with details if critical issues found, PASS with recommendations otherwise\n",
  "description": "Security auditor persona - reviews diffs for vulnerabilities"
}
```

### Step 2: Create a Persona

```json
// create_persona
{
  "name": "SecurityAuditor",
  "description": "Reviews code changes for security vulnerabilities",
  "promptTemplateName": "persona.security_auditor"
}
```

### Step 3: Add the Persona to a Pipeline

```json
// create_pipeline
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
// dispatch
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
| `create_persona` | Create a custom persona (name, promptTemplateName required; optional `defaultCaptainId`) |
| `get_persona` | Get a persona by name |
| `update_persona` | Update persona description, prompt template, or `defaultCaptainId` |
| `delete_persona` | Delete a custom persona (built-in personas cannot be deleted) |
| **Pipelines** | |
| `create_pipeline` | Create a pipeline with ordered stages |
| `get_pipeline` | Get a pipeline by name (includes stages) |
| `update_pipeline` | Update pipeline description or replace stages |
| `delete_pipeline` | Delete a custom pipeline (built-in pipelines cannot be deleted) |
| **Prompt Templates** | |
| `list_prompt_templates` | List templates, optionally by category |
| `create_prompt_template` | Create a template |
| `get_prompt_template` | Get a template by name |
| `update_prompt_template` | Update template content and/or description (creates it if missing) |
| `reset_prompt_template` | Reset a template to its built-in default |
| **Enumeration** | |
| `enumerate` | Use `entityType: "personas"`, `"pipelines"`, or `"prompt_templates"` to list/filter/paginate |
| **Related** | |
| `dispatch` | Pass `pipelineId` or `pipeline` (name) to override the default pipeline |
| `update_vessel` | Set `defaultPipelineId` on a vessel |
| `update_fleet` | Set `defaultPipelineId` on a fleet |
| `create_captain` | Set `allowedPersonas` and `preferredPersona` |
| `update_captain` | Update `allowedPersonas` and `preferredPersona` |

### REST Endpoints

All three entity types follow the same pattern. Replace `{entity}` with `personas`,
`pipelines`, or `prompt-templates`:

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/v1/{entity}` | List all |
| POST | `/api/v1/{entity}/enumerate` | Paginated enumeration with filters |
| GET | `/api/v1/{entity}/{name}` | Get by name |
| POST | `/api/v1/{entity}` | Create |
| PUT | `/api/v1/{entity}/{name}` | Update |
| DELETE | `/api/v1/{entity}/{name}` | Delete (personas and pipelines only, built-in protected) |
| POST | `/api/v1/prompt-templates/{name}/reset` | Reset template to built-in default |

### WebSocket Commands

The WebSocket API accepts `get_persona`, `create_persona`, `update_persona`,
`delete_persona`, `get_pipeline`, `create_pipeline`, `update_pipeline`, `delete_pipeline`,
`get_prompt_template`, and `update_prompt_template` as `command` actions. Listing,
creating, and resetting prompt templates, and enumerating any of the three, are available
through MCP and REST only.
