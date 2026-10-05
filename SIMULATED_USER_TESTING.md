# Simulated User Testing

> **Type:** procedure (repeatable). Run it against every release candidate and record the results in
> `docs/SIMULATED_USER_TESTING_RESULTS_<version>.md` (copy the results template at the end). Referenced by
> `V1_READINESS.md` W6.1 (dashboard and TUI) and `TUI_APP_PLAN.md` W8.7 (TUI as the primary surface).

A simulated user session is a scripted walk through Armada's main workflows by someone (or something) playing a
specific kind of user, against a release build, on an isolated stack, with nothing but the product and its docs. The
goal is to find what a real user would trip over: failures, dead ends, confusing text, and papercuts. It is not a
replacement for the automated suites; it is the check that the pieces the suites cover add up to a usable product.

## 1. Rules of the session

- **Release build only.** Use the release candidate artifact (installer, `dotnet tool`, or a `-c Release` build of
  the tagged commit), never a Debug build or a dev server with hot reload.
- **Isolated stack.** The stack is called `armada-usertest`. It never touches the tester's real Armada:
  - `ARMADA_DATA_DIR` points at a fresh temp directory (deleted at the end).
  - The Admiral and MCP ports are free ports outside 7890/7891 (for example 39300/39301).
  - Repositories are temp git repositories, each with a local bare `origin`, created for the session.
  - The TUI uses its own preferences and credentials files (`ARMADA_TUI_PREFERENCES`,
    `ARMADA_TUI_CREDENTIAL_STORE=file`, `ARMADA_TUI_CREDENTIALS`) and a temp `HOME`.
- **Start from nothing.** First run means an empty data directory: no settings file, no database, the default
  `admin@armada` account.
- **Docs only.** The tester uses only what a user has: the UI, `README.md`, `GETTING_STARTED.md`, `docs/TUI.md`,
  and in-product help. If the tester has to read source code or ask a developer to make progress, that is a finding.
- **Do not fix during the session.** Record the finding, apply a documented workaround if one exists, and continue.
  Fixes come after triage.
- **Stop every process you start** (by PID or name, never by pattern) and delete the temp directories at the end.

## 2. Setup

```bash
export UT=$(mktemp -d)/armada-usertest
mkdir -p "$UT/data" "$UT/repos/origins" "$UT/repos/seed" "$UT/tui" "$UT/findings"

# Temp repositories with bare origins (one per name)
for n in solo-app api-gateway web-frontend billing-svc auth-svc mobile-app infra-tf docs-site; do
  git init -q --bare -b main "$UT/repos/origins/$n.git"
  git init -q -b main "$UT/repos/seed/$n"
  (cd "$UT/repos/seed/$n" && echo "# $n" > README.md && git add -A && \
   git -c user.name=UT -c user.email=ut@example.com commit -qm "Initial commit" && \
   git remote add origin "$UT/repos/origins/$n.git" && git push -q origin main)
done

# Admiral (release build) on the session's ports: write the ports before the first start
echo '{"AdmiralPort":39300,"McpPort":39301}' > "$UT/data/settings.json"
ARMADA_DATA_DIR="$UT/data" dotnet <release-output>/Armada.Server.dll

# TUI with its own preferences, credentials, and data directory
HOME="$UT/tui" ARMADA_DATA_DIR="$UT/tui/data" ARMADA_TUI_PREFERENCES="$UT/tui/tui.json" \
  ARMADA_TUI_CREDENTIAL_STORE=file ARMADA_TUI_CREDENTIALS="$UT/tui/credentials.json" \
  armada tui --server http://127.0.0.1:39300
```

**Captains.** A human session uses real captains (Claude Code, Codex, or another runtime from `docs/CAPTAINS.md`)
signed in with real model accounts, because the quality of the captain's work and its streaming output are part of
the experience. An unattended session (an agent or CI acting as the user) cannot sign in to a model, so it replaces
the Claude Code runtime with the scripted stub captain from the test suites (`StubCaptainRuntime` in
`src/Test.Shared/Infrastructure`, installed through `ArmadaServer.RuntimeFactory.Override`): a small console host
that compiles the stub's source files (`StubCaptainRuntime`, `StubCaptainBehavior`, `StubCaptainTurn`,
`StubDispatchArguments`, `RecordingRuntimeToolDiscoverySource`) against `Armada.Server`, loads settings from
`ARMADA_DATA_DIR` like `Armada.Server` does, starts `ArmadaServer`, and calls `StubCaptainRuntime.Install`. Do not
reference the `Test.Shared` assembly itself: its module initializer redirects the data directory to a temporary
profile that is deleted when the process exits. The stub commits one file per mission (so landing and the
merge queue run for real), replies to planning and refinement prompts with a short plan, and in Ask Armada turns can
call the thread's MCP tools (for example `dispatch`) so proposals and approvals are real. Record which captain kind
the session used; findings about captain output quality need a real captain.

**Surfaces.** Every task is run on the **dashboard** (`http://127.0.0.1:<port>/dashboard`, in a desktop browser at
1440x900 and spot-checked at 390 px) and on the **TUI** (`armada tui --server http://127.0.0.1:<port>` in a 160x48
terminal, spot-checked at 80x24). The TUI session for `TUI_APP_PLAN.md` W8.7 runs the same scripts with the TUI as
the primary surface and falls back to the dashboard only where the TUI has no equivalent (record each fallback as a
finding). An unattended session can drive the dashboard with a headless browser (Playwright or Chrome headless) and
the TUI in a pseudo-terminal (for example Python `pty` plus `pyte`), saving a screenshot or a text frame after each
step.

## 3. Personas

| ID | Persona | Background | What they care about |
|----|---------|------------|----------------------|
| P1 | **New solo developer** | Has one repository and one agent CLI. Never used Armada. Reads the README and GETTING_STARTED, nothing else. | Getting from install to a first landed change fast; knowing what is happening; not losing work. |
| P2 | **Team lead** | Runs several repositories for a small team. Knows git and PR review well. Wants to hand batches of work to captains and review the results. | Importing many repos at once; voyages; Ask Armada; planning and backlog; reviewing and landing; the merge queue; fleet-wide actions; repository health. |
| P3 | **Operator / admin** | Owns the Admiral for a team. Cares about security and upkeep more than missions. | Sign-in and password policy; users, tenants, and credentials; server settings; diagnostics; backups; delivery records. |

## 4. Task scripts

Each task has an ID, a goal stated the way the user would state it, the steps a user is expected to find on their
own, and a success check. Do not give the tester the steps; they are there for the observer and for unattended runs.
Time each task from the first action to the success check.

### P1: New solo developer

| ID | Goal | Expected path | Success check |
|----|------|---------------|---------------|
| T1.1 | First run and sign in | Start the Admiral, open the dashboard (or `armada tui`), sign in with `admin@armada` / `password`, set a new password when asked. | Signed in with the new password; the default password and the `default` token no longer work. |
| T1.2 | First mission through the setup wizard | The wizard opens on an empty Armada: create the fleet, register `solo-app` (repository = bare origin, working directory = a clone, Landing Mode = Local Merge), create a captain, dispatch a small mission. | Mission reaches Complete; the commit is on `origin/main`; total time from sign-in under ten minutes (W6.7). |
| T1.3 | Understand what happened | Open the mission: status history, diff, captain log. | The user can say which files changed and why the mission ended where it did. |
| T1.4 | Dispatch a second mission without the wizard | Dispatch page: pick the vessel, describe the work, dispatch. | Mission lands; it appears on Home and in Missions with the right status. |
| T1.5 | Ask Armada | Open Ask Armada, ask for a change, read the proposal, approve it, follow the work card. | The proposal shows what will run; approval runs it; the card follows the voyage to landing; the thread says so. |
| T1.6 | Settings basics | Change a server setting (for example Max Captains) and the UI theme or language. | The value persists after reload; the UI change applies at once. |

### P2: Team lead

| ID | Goal | Expected path | Success check |
|----|------|---------------|---------------|
| T2.1 | Register vessels | Create a fleet and register two vessels by hand (one with Landing Mode = Local Merge, one with Merge Queue). | Both vessels listed in the fleet with the right settings. |
| T2.2 | Import many repositories | Vessels, Import: point at a folder of repositories (or paste several sources), review, import. | Every repository becomes a vessel; failures, if any, say why; the batch is visible afterwards. |
| T2.3 | Dispatch a voyage | Create Voyage (or Dispatch with several missions) across one vessel. | The voyage shows each mission; it completes when all missions land. |
| T2.4 | Ask Armada: propose, approve, track | Ask for a change on a named vessel; approve the proposal from the thread (and once from the Approvals/Inbox center); reject a second proposal. | Approved work runs and is tracked in the thread to landing; the rejected one never runs. |
| T2.5 | Planning session | Planning: pick a captain and vessel, chat, select a reply, summarize, open in Dispatch or dispatch. | A dispatch draft or a voyage created from the plan; the captain is released when the session stops. |
| T2.6 | Backlog | Backlog: create an item, refine it, promote it to work. | The item moves through its states and links to the work it produced. |
| T2.7 | Review and landing | Set a vessel to require review (or Landing Mode None), dispatch, review the diff, approve or request changes, land. | The reviewer sees the diff before landing; the decision is recorded; the change lands only after approval. |
| T2.8 | Merge queue | Dispatch to the Merge Queue vessel; open Merge Queue; process the entry. | The entry is tested and landed (or fails with a clear reason); the queue empties. |
| T2.9 | Fleet actions | Fleet Actions: run an action (for example a command or a sync) across several vessels; open the run. | The run shows a result per vessel; failures say why. |
| T2.10 | Vessel health | Vessels, Health: read the health grid; open one vessel's detail. | The user can tell which vessels need attention and why. |

### P3: Operator / admin

| ID | Goal | Expected path | Success check |
|----|------|---------------|---------------|
| T3.1 | Users | Server, Users: create a user, reset their password, disable them. | The user can sign in, then cannot after being disabled. |
| T3.2 | Credentials | Server, Credentials: create an API credential, copy it once, use it from a script or the TUI, revoke it. | The token works until revoked, then returns 401. |
| T3.3 | Server settings | Server: change settings, save, reload; read Diagnostics (Doctor). | Saved values persist; Diagnostics reports the stack's state. |
| T3.4 | Delivery basics | Delivery: create an environment, a release, and a deployment record; open a check. | Records are created and linked; the lists filter and open. |
| T3.5 | Activity and audit | Activity / Events / API Requests: find what the other personas did. | The user can trace who did what. |
| T3.6 | Backup | Server: back up, then (on the throwaway stack) restore. | The restore brings back the data. |

## 5. What to record

For every task, on every surface:

- **Result:** Pass (success check met without help), Pass with findings, Fail (success check not met), Blocked
  (could not attempt), or Not run (with the reason).
- **Time on task** from the first action to the success check (wall clock, rounded to seconds for an unattended
  run, to the nearest half minute for a human).
- **Errors:** any error text shown, failed request (HTTP 4xx/5xx from the browser console or the Admiral log), crash,
  or exception.
- **Confusion points:** where the user hesitated, picked the wrong control, or had to guess. Quote the label or text
  that misled them.
- **Papercuts:** small annoyances that do not block the task (extra clicks, missing defaults, inconsistent words,
  layout glitches).
- **Evidence:** a screenshot (dashboard) or text frame (TUI) for every finding, kept outside the repository; the
  results file references them by name.

Each finding gets an ID (`F<n>`), a severity, the persona and task, the surface, reproduction steps, expected and
actual behavior, and a suggested fix.

## 6. Severity scale

| Severity | Meaning | Release candidate rule |
|----------|---------|------------------------|
| **S1 Blocker** | A main workflow cannot be completed, data is lost or corrupted, or a security boundary fails. | Must be fixed before release. |
| **S2 Major** | A main workflow completes only with a workaround the user would not find, or the product gives wrong information about work (for example a mission shown as landed that did not land). | Fix before release, or ship with the workaround in the release notes and the maintainer's sign-off. |
| **S3 Minor** | The workflow completes but the user is confused, slowed down, or sees an error that does not stop them. | Triage; fix if cheap, else schedule. |
| **S4 Cosmetic** | Wording, alignment, spacing, inconsistent labels, a missing tooltip. | Batch into the next polish pass. |

## 7. Exit criterion for a release candidate

A release candidate passes simulated user testing when all of the following hold:

1. Every task in section 4 was run on the dashboard and on the TUI (or marked Not run with a reason the maintainer
   accepts), by a human with real captains at least once per release, and by the unattended run for every
   candidate.
2. There are **no open S1 findings** and **no open S2 findings** without a documented workaround and the maintainer's
   sign-off.
3. T1.2 (first landed mission) finishes in under ten minutes from sign-in for the human run.
4. Every task's success check was met on at least one surface, and every TUI fallback to the dashboard is recorded.
5. The results file is committed and linked from the release notes.

## 8. Results template

```markdown
# Simulated User Testing Results <version>

Build: <commit / artifact>    Date: <date>    Tester: <name or agent>    Captain: <real runtime or stub>
Stack: ARMADA_DATA_DIR=<temp>, ports <admiral>/<mcp>, repos <n> temp repos with bare origins

## Summary
<pass/fail counts per surface, S1-S4 counts, exit criterion verdict>

## Results by task
| Task | Surface | Result | Time | Findings |
|------|---------|--------|------|----------|

## Findings
### F1 (S3) <title>
- Persona / task / surface:
- Steps:
- Expected:
- Actual:
- Evidence:
- Suggested fix / status:

## Not run, and what a human still must check
```
