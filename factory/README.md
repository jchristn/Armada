# Factory Reset

Gold reference configuration and scripts for resetting Armada back to a
fresh installation. Use these when you want to wipe all runtime state
(missions, voyages, captains, vessels, docks, logs, database) and boot
Armada as if for the first time.

## Contents

- `settings.json` — gold copy of the default `ArmadaSettings` shape.
  Generated from `new ArmadaSettings()` with machine-specific absolute
  paths stripped so the file is portable. When Armada loads this file,
  missing path keys fall back to `Constants.DefaultDataDirectory` on
  the target machine (typically `~/.armada`).
- `reset.bat` — Windows reset script.
- `reset.sh` — Linux/macOS reset script.

No `armada.db` is shipped. The database is re-created from migrations
on first boot by `SqliteDatabaseDriver.InitializeAsync` and seeded with
the default tenant/user/credential and the built-in prompt templates,
personas, and pipelines by `PromptTemplateService.SeedDefaultsAsync`
and `PersonaSeedService.SeedAsync`. Deleting the file is sufficient to
trigger a clean rebuild.

## What gets wiped

Relative to `~/.armada`:

- `armada.db` (and `armada.db-wal`, `armada.db-shm`)
- `logs/`
- `docks/`
- `repos/`
- `settings.json`

The `~/.armada` directory itself is preserved (re-created if missing).

## Usage

### Windows

```
factory\reset.bat
```

### Linux/macOS

```
./factory/reset.sh
```

Both scripts require typing `RESET` (capitals) at the prompt to
proceed. Anything else aborts without touching anything.

After the reset, the scripts print the command to restart Armada. On
next boot, Armada will:

1. Load `settings.json` from the gold copy.
2. Recreate `armada.db` via migrations.
3. Re-seed the default tenant, admin user, credential, prompt
   templates, personas, and pipelines.

## Refreshing the gold copy

If you intentionally change `ArmadaSettings` defaults and want the
gold `settings.json` to reflect them, regenerate it by serializing a
fresh `new ArmadaSettings()` (a throwaway console program referencing
`Armada.Core` is the simplest path — strip the absolute-path keys so
the file stays portable).
