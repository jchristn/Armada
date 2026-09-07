# Fast-Track Setup

This guide walks through onboarding a coding agent (Claude Code or Codex) to your local code library and using it to bootstrap Armada with fleets, vessels, default context, and playbooks. Everything below is something the agent can do for you in one session if you have it talking to Armada over MCP.

The instructions below are agent-agnostic — every prompt works against either harness. The only step that differs is **how you wire the MCP server into the agent's config**, covered in Step 1.

## What you get

By the end of the guide:

- Armada is reachable from Claude Code as an MCP server.
- Every codebase you care about is registered as a vessel in the appropriate fleet.
- Each vessel has a sensible default `ProjectContext` and `StyleGuide` so missions do not start blind.
- Reusable engineering standards (code style, repo requirements, architecture references, etc.) are loaded as playbooks.
- New repos that you've created on GitHub but not yet initialized locally have been git-init'd and pushed.

## Prerequisites

1. **Armada is running locally** and the MCP HTTP server is reachable at `http://localhost:7891/mcp` (the default `McpPort`). All examples below assume this URL.
2. **A coding agent is installed** — either:
   - **Claude Code** (`claude`) with the model of your choice, or
   - **Codex CLI** (`codex`) wired to your OpenAI account
   This guide assumes Windows + bash but every step is platform-agnostic.
3. **Your repos live under a known root directory** (`<code-root>` in the prompts below — substitute the actual path on your machine). Sub-folders may contain umbrella directories with multiple repos — that's fine.
4. **Git is configured globally** (`user.name`, `user.email`, and credentials for HTTPS pushes via Git Credential Manager). The agent uses your credentials when it pushes.

## Step 1 — Connect Armada via MCP

Wire Armada into whichever agent you're driving. Use the HTTP transport — it's the primary MCP transport in Armada and avoids stdio's "Armada must be a child process" coupling, so a single Armada server can serve every agent on the box.

### Claude Code

Edit `.mcp.json` at the project root or `~/.claude/mcp.json`:

```json
{
  "mcpServers": {
    "armada": {
      "transport": "http",
      "url": "http://localhost:7891/mcp"
    }
  }
}
```

Or, equivalent CLI:

```bash
claude mcp add --transport http --scope user armada http://localhost:7891/mcp
```

Restart Claude Code. The tools surface as `mcp__armada__*` — they're loaded lazily, so the first call may pause briefly.

### Codex CLI

Edit `~/.codex/config.toml` (or `%USERPROFILE%\.codex\config.toml` on Windows). MCP servers are declared under `[mcp_servers.<name>]`:

```toml
[mcp_servers.armada]
url = "http://localhost:7891/mcp"
transport = "http"
```

Codex auto-loads tools from configured MCP servers on session start; no restart needed beyond exiting the current session. Tools surface with the prefix configured by Codex (typically `armada__<tool>`).

### Authenticated MCP (multi-user or remote)

MCP is authenticated end-to-end. For a local single-user server the bare URL above is enough -- an unauthenticated caller is treated as the default tenant admin, so everything works out of the box. For a shared/multi-user or remote deployment, present a credential so the server scopes every MCP call to your user (the same per-user scoping the REST API enforces): mint an access key + secret in the dashboard (Server hub -> Credentials tab) and pass it as a header in your agent's MCP config, e.g. add an `Authorization` (or `X-Token`) header alongside the `url`. Without a credential the tools still load, but user-scoped data (your inbox, Ask history, captain chat) resolves against the default admin context rather than your user.

### Verify the connection

In whichever agent you're using, ask:

> Do you have access to Armada via MCP?

Expected: the agent confirms a long list of Armada tools (fleets, vessels, captains, missions, voyages, merge queue, personas, playbooks, signals, status, backup, restore). If it can't see them, confirm Armada is running locally (`armada status` or check `http://localhost:7891/mcp` returns a JSON-RPC error rather than a connection refused) and that the URL in your agent config matches.

## Step 2 — Point the agent at your code

Tell the agent where your code lives and ask for a manifest of potential vessels — but **do not let it create anything yet**. A read-only inventory pass is much cheaper to revise than a fleet of misregistered vessels.

> List all potential codebases under `<code-root>` and subdirectories that could be Armada vessels. Group them into proposed fleets. Do not make any changes — just a manifest.

What the agent will produce:

- One row per `.git` directory found at depth ≤ 4.
- Non-git codebases (directories with `src/` and `README.md` but no `.git`) flagged separately.
- A proposed fleet grouping (umbrella product, language, lifecycle stage — whatever fits).
- Excluded items: scratch folders, single-file directories, top-level metadata files.

Review the manifest and trim. Common edits: drop archived/legacy fleets, prune `Misc/`-style umbrellas to actively-maintained packages, separate work repos from personal repos.

## Step 3 — Create fleets and vessels

Reply with the final fleet/vessel layout in plain text (one fleet per heading, vessels listed below). The agent creates the fleets first via `create_fleet`, then registers vessels in parallel via `add_vessel`. The vessel registration uses:

- **`name`** — the vessel name you give in the list
- **`repoUrl`** — read from the local repo's `git config --get remote.origin.url`. For non-git directories where the GitHub repo exists but `git init` hasn't run, the agent uses the conventional URL pattern (`https://github.com/<owner>/<name>.git`) and you confirm.
- **`fleetId`** — from the create_fleet response
- **`workingDirectory`** — the local path so post-merge pulls land correctly

A fleet of ~70 vessels takes ~1 minute end-to-end with parallel registration.

## Step 4 — Add default `ProjectContext` and `StyleGuide`

Ask the agent to populate context for each vessel. Two strategies:

**A. Fleet-uniform style guide + per-vessel project context**

The cleanest default. The agent embeds your standard style guide on every vessel in a fleet and writes a 1-3 sentence project description per vessel. Example prompt:

> Add default context to every vessel. For C# vessels, use my standard C# style guide (see memory: `coding-standards.md`). For Python vessels, use a Python-equivalent. For each vessel write a short project description and include the GitHub URL and local path.

**B. Per-vessel deep context**

More expensive but useful for the active 5-10 repos. The agent reads each vessel's README and sketches a context that names the key files, dependencies, and architectural decisions.

Vessel context updates use `update_vessel_context` (does not touch other fields). Updates dispatch in parallel — ~70 vessels take ~2 minutes.

## Step 5 — Initialize new repos (optional)

If you've created GitHub repos but not yet pushed local content (e.g. `mkdir new-project && create the GitHub repo on the web → forgot to git init`), have the agent do it:

> Initialize the listed local directories as git repos and push them to their corresponding GitHub URLs.

The agent runs the equivalent of:

```bash
cd <path>
git init -b main
git remote add origin <repo url>
git add .
git -c user.name="..." -c user.email="..." commit -m "Initial commit"
git push -u origin main
```

The agent uses your global git config when available and trusts your local `.gitignore`. **Confirm the repo URL is correct before saying yes** — pushing to the wrong remote leaks code.

## Step 6 — Add captains

A **captain** in Armada is a registered AI agent that can be dispatched to vessels to execute missions. The captain record tells Armada which runtime, model, and system instructions to use when work is routed to that agent. You can register one captain per agent harness you actually run, or several variants (different models, different system instructions) of the same harness.

The supported runtimes are `ClaudeCode`, `Codex`, `Gemini`, `Cursor`, and `Custom`. Tell the agent which ones to add. Example prompt:

> Register four captains: Claude Code (Opus), Codex, Gemini, and Cursor. Each gets a system-instructions block that tells the captain to follow the vessel's StyleGuide and ProjectContext, reference the fleet-wide playbooks, and compile clean before reporting complete.

Under the hood the agent calls `create_captain` once per captain. The fields that matter:

| Field | Notes |
|-------|-------|
| `name` | Display name. Pick something operators will recognize: "Claude Code (Opus)", "Codex", "Gemini", "Cursor". |
| `runtime` | One of `ClaudeCode`, `Codex`, `Gemini`, `Cursor`, `Custom`. Required for dispatch routing. |
| `model` | Specific model identifier (e.g. `claude-opus-4-8`, `gpt-5`). Optional — runtime defaults if omitted. |
| `systemInstructions` | Text injected into every mission prompt for this captain. The right place to enforce playbook usage and compile-clean discipline. |
| `preferredPersona` | Optional; routing prefers this captain when missions match the persona (e.g. `Worker`, `Architect`, `Judge`). |
| `allowedPersonas` | JSON array; if set, the captain only takes missions whose persona is on the list. |

### Recommended system instructions

The same body of instructions works for all four runtimes. It tells the captain to honor the vessel context, reference the fleet-wide playbooks by name, and verify before reporting complete:

```text
You are a software engineering agent operating as part of the Armada fleet.

When dispatched to a vessel, follow that vessel's StyleGuide and ProjectContext exactly. Reference fleet-wide playbooks for cross-cutting standards:
- CODE_STYLE — mandatory C# style rules
- REPOSITORY_REQUIREMENTS — required filesystem layout
- AUTHENTICATION — multi-tenant AAA / RBAC reference
- BACKEND_ARCHITECTURE — Watson 7 + provider-neutral DB + typed routes
- BACKEND_TEST_ARCHITECTURE — Touchstone descriptor pattern
- FRONTEND_ARCHITECTURE — React 19 / Vite 6 / fetch-based ApiClient
- I18N — locale registry, formatters, RTL/CJK layout

Compile clean (no errors, no warnings) before reporting work complete. Prefer existing patterns in the codebase over introducing new abstractions. When SQL is hand-written it is deliberate — do not silently rewrite to ORM helpers. Match the vessel's existing conventions (private field naming, region structure, async signatures) before introducing your own.
```

### Per-runtime defaults to consider

- **Claude Code** — model `claude-opus-4-8` for heavy work, `claude-sonnet-5` for cheaper passes, `claude-haiku-4-5-20251001` for fast triage. Multiple captains is fine: e.g. one named "Claude Code (Opus)" and another "Claude Code (Sonnet, fast)".
- **Codex** — runs against OpenAI models (typically `gpt-5` family). Codex auto-selects without an explicit model identifier; pass it only if you want to pin behavior.
- **Gemini** — runtime `Gemini`; pin a specific Gemini model if you care about which version answers (e.g. `gemini-3-pro` for code, `gemini-3-flash` for quick passes). Note that Gemini's tool-calling shape differs from Claude/OpenAI — your system instructions don't need to change but expect different cadence.
- **Cursor** — runtime `Cursor`; useful when you want missions to land in someone's IDE for review rather than running fully autonomously. Pair with a tighter `allowedPersonas` (e.g. only `Reviewer`) so it doesn't pick up bulk work.

Captains do not require unique runtimes — register as many variants as you need. They share the fleet's vessels and playbooks; only the dispatch policy decides who gets a given mission.

### Verifying

After registration, ask the agent for an `enumerate entityType=captains` summary. You should see one row per captain you registered with the right `Runtime`, `Model`, and a non-empty system-instructions length.

## Step 7 — Load playbooks

Playbooks are reusable markdown documents (architecture references, code style, security checklists, runbooks) that any mission can pull into context.

Point the agent at a folder of markdown files:

> Add every `.md` file in `<playbook-dir>` as an Armada playbook. Use the filename as `fileName` and write a one-line description from the file's heading.

The agent calls `create_playbook` once per file with the markdown content. Skip "header-only" READMEs and any files you'd consider drafts.

After this step you can reference the playbooks in mission descriptions: "Implement the user-management endpoints following the AUTHENTICATION playbook."

## Agent differences worth knowing

The prompts below work against either Claude Code or Codex, but a few practical things differ:

- **Tool name prefix.** Claude Code surfaces Armada tools as `mcp__armada__*`; Codex typically shows them as `armada__*` or under whatever prefix you configured. The agent figures this out from its tool list — your prompts don't need to name tools.
- **Memory.** Claude Code has a built-in auto-memory system at `~/.claude/projects/<slug>/memory/` that this guide leans on for storing coding standards. Codex doesn't have a direct equivalent; replicate the pattern by saving your standards to a stable location (e.g. `~/.config/coding-standards.md`) and pointing the agent at it explicitly each session, or commit the standards into a shared repo.
- **Permission prompts.** Both agents prompt for confirmation on tool calls by default. Claude Code uses `.claude/settings.json` to allowlist tools; Codex uses `~/.codex/config.toml`'s `[approval]` section. Either way, leave destructive operations (git push, repo creation) requiring confirmation until you trust the run.
- **Slash commands and skills.** Claude Code's `/loop`, `/schedule`, and skill system don't exist in Codex. Replace them with explicit polling prompts or external schedulers when needed.
- **Background work.** Claude Code can run agents in the background and notify you on completion; Codex runs synchronously by default. For multi-hour runs, drive Codex through a wrapper that handles backgrounding.

## End-to-end prompt

If you'd rather hand the whole thing to the agent in one shot, the minimal prompt is:

> You have Armada available via MCP. Search the following directories for git repositories: `<code-root-1>`, `<code-root-2>`, `<code-root-N>` (list every base directory you want considered — the agent will recurse into each one). My playbook markdown files live under `<playbook-dir>`. (1) Build a manifest of vessels found across those directories and propose fleets — wait for my confirmation. (2) Create the fleets and vessels I confirm. (3) Register default `ProjectContext` and `StyleGuide` for every vessel using my coding standards from auto-memory. (4) Register captains for the agent harnesses I actually run (Claude Code, Codex, Gemini, Cursor) with system instructions that point them at fleet playbooks. (5) Add every `.md` file in `<playbook-dir>` as a playbook. Report counts at the end. Ask me before any destructive action.

## Tips

- **Run the manifest pass first.** Having the agent enumerate 200+ git repos and propose fleets takes a couple of minutes; revising the proposal in plain text takes seconds. Skipping straight to creation makes you fix vessels one at a time later.
- **Persist your style guides in a known location.** With Claude Code, save them to the auto-memory store (`coding-standards.md`, `user-preferences.md`) once and future sessions inherit them automatically. With Codex (or any agent that lacks built-in memory), keep them at a stable path like `~/.config/coding-standards.md` and reference them in your session prompt.
- **Set `EnableModelContext: true`** (the default for `add_vessel`) so missions can accumulate per-repo learnings into `ModelContext` over time. The agent will not blow this away when it updates `ProjectContext` and `StyleGuide`.
- **Keep `ProjectContext` short.** A vessel's project context should be a 1-2 sentence orientation + the repo URL + the local path. Detailed architecture goes in playbooks where it can be reused across vessels.
- **Don't try to push 60+ vessels' context updates serially.** Parallel registration is fast; sequential is painful.
- **Watch for repo name vs. package name mismatches.** Some libraries are published under a different name than their repo. Make sure the vessel's `repoUrl` matches the actual git remote, not the published package name. The agent reads `git config --get remote.origin.url` to avoid getting this wrong.
- **For non-git codebases**, the conventional URL pattern is `https://github.com/<owner>/<repo>.git`. The agent will guess and ask you to confirm before registering. Don't merge code into a vessel until the local directory is actually wired to the right remote.

## What "done" looks like

- `enumerate entityType=fleets` returns your fleets.
- `enumerate entityType=vessels pageSize=100` returns every vessel with non-zero `ProjectContextLength` and `StyleGuideLength`.
- `enumerate entityType=captains` returns one row per agent harness you registered (Claude Code, Codex, Gemini, Cursor as appropriate), each with a `Runtime` and non-empty system-instructions length.
- `enumerate entityType=playbooks` returns the markdown set you loaded.
- Any new repos that needed initialization show a `main` branch on GitHub with the initial commit.

From here, you can dispatch missions, open voyages, and route work between captains.

## Where captains execute (local vs. Harbor)

By default the Admiral launches captains in-process on its own machine (local mode) -- the setup above is all you need. If the Admiral runs in a container or on a remote host, or you set `RequireHarborForLaunch`, captains instead execute on a host-side **Harbor** runner that dials out to the Admiral and runs the CLIs where your logins and repos already live. In that case, install and start the Harbor app on your machine, point it at the Admiral, and mint it a credential (see `docs/HARBOR.md`); dispatched missions then run on your Harbor rather than the Admiral. Each user's launches require that user's own connected Harbor.
