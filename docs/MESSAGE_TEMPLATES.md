# Message Templates

Armada supports configurable message templates for git commit messages, pull request descriptions, and local merge commits. Templates use placeholder parameters that are replaced with actual values at runtime.

## Overview

| Surface | Mechanism | Deterministic? |
|---------|-----------|----------------|
| Agent commits | Template rendered into agent prompt as instructions | No - agent may not always comply |
| PR descriptions | Server appends rendered template to PR body | Yes |
| Merge commits | Server passes rendered message to `git merge` | Yes |

Agent commit metadata works by injecting instructions into the agent's prompt asking it to append Git trailers to every commit. Because agents are autonomous, compliance is best-effort. PR and merge commit metadata are fully controlled by the server and always applied.

## Placeholder Reference

| Placeholder | Description | Example |
|-------------|-------------|---------|
| `{MissionId}` | Mission identifier | `msn_abc123def456` |
| `{MissionTitle}` | Mission title text | `Fix login bug` |
| `{VoyageId}` | Parent voyage identifier | `vyg_abc123def456` |
| `{VoyageTitle}` | Voyage title text | `Sprint 42` |
| `{CaptainId}` | Assigned captain identifier | `cpt_abc123def456` |
| `{CaptainName}` | Captain display name | `Captain-1` |
| `{VesselId}` | Target vessel identifier | `vsl_abc123def456` |
| `{VesselName}` | Vessel display name | `my-api` |
| `{FleetId}` | Fleet identifier | `flt_abc123def456` |
| `{DockId}` | Dock (worktree) identifier | `dck_abc123def456` |
| `{BranchName}` | Git branch name | `armada/fix-login-bug` |
| `{Timestamp}` | UTC timestamp (ISO 8601) | `2026-03-07T14:30:00.0000000Z` |

Placeholders that cannot be resolved (e.g., no voyage assigned) are replaced with an empty string. Unknown placeholders are left as-is in the output. The PR description and merge commit are rendered without a captain, so `{CaptainName}` is empty there; `{CaptainId}` still comes from the mission.

These are the message-template placeholders only. Prompt templates (Configuration > Prompts) use their own set; see [PERSONAS_GUIDE.md](PERSONAS_GUIDE.md#available-placeholders).

## Commit Message Template

**Setting:** `messageTemplates.commitMessageTemplate`

**Default:**
```
\nArmada-Mission-Id: {MissionId}
Armada-Voyage-Id: {VoyageId}
Armada-Captain-Id: {CaptainId}
Armada-Vessel-Id: {VesselId}
```

Every mission prompt asks the agent for a descriptive commit message (the `commit.instructions_preamble` prompt template, always sent). When `enableCommitMetadata` is true, this template is rendered and appended after the `commit.trailers_preamble` prompt template, asking the agent to add these Git trailers to every commit message it creates. Both preamble templates can be edited under Configuration > Prompts in the dashboard.

**Example commit produced by an agent:**
```
fix: handle null vessel state

Armada-Mission-Id: msn_01jq7xyz
Armada-Voyage-Id: vyg_01jq7abc
Armada-Captain-Id: cpt_01jq7def
Armada-Vessel-Id: vsl_01jq7ghi
```

Git trailers are machine-parseable via `git log --format='%(trailers)'`.

> **Note:** Because agents are autonomous, they may not always include the trailers. This is inherent to prompt-based metadata injection. For guaranteed metadata, use PR descriptions or merge commits.

## PR Description Template

**Setting:** `messageTemplates.prDescriptionTemplate`

**Default:**
```markdown
---
Committed by [Armada](https://github.com/jchristn/armada)
- Mission ID : {MissionId}
- Voyage ID  : {VoyageId}
- Captain ID : {CaptainId}
- Vessel ID  : {VesselId}
```

When `enablePrMetadata` is true, this template is appended to the pull request body. The body itself comes from the `landing.pr_body` prompt template (by default the mission title and description).

**Example PR body:**
```markdown
## Mission
**Fix login bug**

Handle the case where user session is null during OAuth callback.

---
Committed by [Armada](https://github.com/jchristn/armada)
- Mission ID : msn_01jq7xyz
- Voyage ID  : vyg_01jq7abc
- Captain ID : cpt_01jq7def
- Vessel ID  : vsl_01jq7ghi
```

## Merge Commit Template

**Setting:** `messageTemplates.mergeCommitTemplate`

**Default:**
```
Merge armada mission: {BranchName}

Armada-Mission-Id: {MissionId}
Armada-Voyage-Id: {VoyageId}
```

Used for local merge commits when Armada merges a captain's branch into the vessel's working directory (non-PR flow). It is applied only when `enableCommitMetadata` is true; otherwise git's default merge message is used.

## Configuration

### Via CLI

```bash
# Toggle commit metadata on/off
armada config set messageTemplates.enableCommitMetadata true
armada config set messageTemplates.enableCommitMetadata false

# Toggle PR metadata on/off
armada config set messageTemplates.enablePrMetadata true
armada config set messageTemplates.enablePrMetadata false

# Custom commit template
armada config set messageTemplates.commitMessageTemplate "\nArmada: {MissionId}"

# Custom PR template
armada config set messageTemplates.prDescriptionTemplate "\n\n---\nAutomated by Armada | Mission {MissionId}"

# Custom merge commit template
armada config set messageTemplates.mergeCommitTemplate "Merge {BranchName} (mission {MissionId})"

# View current settings
armada config show
```

### Via settings.json

Edit `settings.json` in the Armada data directory (`~/.armada/settings.json` by default):

```json
{
  "messageTemplates": {
    "enableCommitMetadata": true,
    "enablePrMetadata": true,
    "commitMessageTemplate": "\nArmada-Mission-Id: {MissionId}\nArmada-Voyage-Id: {VoyageId}\nArmada-Captain-Id: {CaptainId}\nArmada-Vessel-Id: {VesselId}",
    "prDescriptionTemplate": "\n\n---\nCommitted by [Armada](https://github.com/jchristn/armada)\n- Mission ID : {MissionId}\n- Voyage ID  : {VoyageId}\n- Captain ID : {CaptainId}\n- Vessel ID  : {VesselId}",
    "mergeCommitTemplate": "Merge armada mission: {BranchName}\n\nArmada-Mission-Id: {MissionId}\nArmada-Voyage-Id: {VoyageId}"
  }
}
```

## Examples

### Minimal: mission ID only

```json
{
  "messageTemplates": {
    "commitMessageTemplate": "\nArmada-Mission: {MissionId}",
    "prDescriptionTemplate": "\n\n---\n*Mission {MissionId}*",
    "mergeCommitTemplate": "Merge {BranchName}"
  }
}
```

### Verbose: all IDs with names

```json
{
  "messageTemplates": {
    "commitMessageTemplate": "\nArmada-Mission-Id: {MissionId}\nArmada-Mission-Title: {MissionTitle}\nArmada-Voyage-Id: {VoyageId}\nArmada-Captain-Id: {CaptainId}\nArmada-Captain-Name: {CaptainName}\nArmada-Vessel-Id: {VesselId}\nArmada-Vessel-Name: {VesselName}\nArmada-Fleet-Id: {FleetId}\nArmada-Dock-Id: {DockId}",
    "prDescriptionTemplate": "\n\n---\nCommitted by [Armada](https://github.com/jchristn/armada)\n| Field | Value |\n|-------|-------|\n| Mission | {MissionTitle} (`{MissionId}`) |\n| Voyage | {VoyageTitle} (`{VoyageId}`) |\n| Captain | `{CaptainId}` |\n| Vessel | {VesselName} (`{VesselId}`) |\n| Branch | `{BranchName}` |"
  }
}
```

### Disabled: no metadata

```json
{
  "messageTemplates": {
    "enableCommitMetadata": false,
    "enablePrMetadata": false
  }
}
```
