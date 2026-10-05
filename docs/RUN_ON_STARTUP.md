# Running Armada Server on System Startup

This guide covers the scripted startup workflows for the Admiral process. The scripts publish `Armada.Server` into `~/.armada/bin`, deploy the dashboard into `~/.armada/dashboard`, register the platform-specific service definition, and verify health on startup.

The server can also register itself: `armada-server --install-service` (or `Armada.Server --install-service` for a published binary) writes a systemd unit, launchd agent, or Windows Service directly, and the WiX, Inno, `.pkg`, and Deb/Rpm installers use it. See "Service and startup registration" in [OPERATIONS.md](OPERATIONS.md). Use one mechanism per install, not both: the scripts below and `--install-service` use different definitions (for example the launchd labels `com.armada.admiral` and `com.joelchristner.armada.server`), and two registered Admirals race for the same ports.

## Prerequisites

- .NET SDK installed for the framework you plan to publish, such as `net8.0` or `net10.0`
- Settings configured in `~/.armada/settings.json` if you are not using the default ports and paths
- Platform service manager available:
  - Windows: PowerShell and the current-user `Run` registry key
  - Linux: `systemd --user`
  - macOS: `launchd`

## Shared Helpers

The shell implementations now live in `scripts/common/`, with Linux and macOS wrappers in their respective platform folders. Windows entrypoints live in `scripts/windows/`.

Canonical helpers:

- Windows: `scripts/windows/publish-server.bat`, `scripts/windows/healthcheck-server.bat`
- Shared shell implementation: `scripts/common/publish-server.sh`, `scripts/common/healthcheck-server.sh`
- Linux wrappers: `scripts/linux/publish-server.sh`, `scripts/linux/healthcheck-server.sh`
- macOS wrappers: `scripts/macos/publish-server.sh`, `scripts/macos/healthcheck-server.sh`

`publish-server` publishes `src/Armada.Server` in `Release` mode for `net10.0` by default to `~/.armada/bin` and then attempts to deploy the React dashboard. Override the framework with a leading argument (`net8.0`, `-f net8.0`, or `--framework net8.0`), for example `scripts\windows\publish-server.bat net8.0` or `./scripts/linux/publish-server.sh --framework net8.0`, or with the `ARMADA_TARGET_FRAMEWORK` environment variable. The install and update scripts on every platform (`install-windows-task.bat`, `update-windows-task.bat`, `install-systemd-user.sh`, `update-systemd-user.sh`, `install-launchd-agent.sh`, `update-launchd-agent.sh`) forward their arguments to `publish-server`, so a framework argument or `--insecure` works with them too.

If the dashboard deploy fails, the shell `publish-server.sh` prints a warning and continues; `publish-server.bat` continues only when a previously deployed dashboard exists in `~/.armada/dashboard` and fails otherwise.

Behind an enterprise proxy that performs TLS inspection, the dashboard deploy step can fail with `npm error code SELF_SIGNED_CERT_IN_CHAIN`. Append `--insecure` (or `-k`) to disable strict TLS validation for npm/Node for that run, for example `scripts\windows\publish-server.bat net8.0 --insecure` (framework first, then the flag). If Node.js is not installed at all, `publish-server` deploys the pre-built dashboard bundle that ships in the repository instead of building it. See the README's "Behind an enterprise proxy or firewall" section for details.

`healthcheck-server` probes `http://localhost:7890/api/v1/status/health` once a second for up to 30 attempts. It also accepts the base URL as its first argument. If your Admiral port is not `7890`, pass the URL or set `ARMADA_BASE_URL` before invoking the platform wrapper:

```bash
ARMADA_BASE_URL=http://localhost:9000 ./scripts/linux/healthcheck-server.sh
```

On Windows:

```powershell
set ARMADA_BASE_URL=http://localhost:9000
scripts\windows\healthcheck-server.bat
```

## Windows (Current-User Startup)

The supported Windows path is a current-user startup registration under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The script names still use `*-windows-task.bat` for compatibility, but they no longer depend on Task Scheduler.

Scripts:

- `scripts/windows/install-windows-task.bat`
- `scripts/windows/update-windows-task.bat`
- `scripts/windows/remove-windows-task.bat`

Install and start:

```powershell
scripts\windows\install-windows-task.bat
```

Or publish and install against a specific SDK target:

```powershell
scripts\windows\install-windows-task.bat net8.0
```

Update from source and restart:

```powershell
scripts\windows\update-windows-task.bat
```

With an explicit framework override:

```powershell
scripts\windows\update-windows-task.bat --framework net8.0
```

Remove the startup entry:

```powershell
scripts\windows\remove-windows-task.bat
```

This installs a current-user startup entry named `ArmadaAdmiral` that runs `scripts\windows\start-armada-server.ps1` from your checkout (hidden PowerShell window) at logon in your normal user context, then starts the server immediately. The start script launches the slot named by `%USERPROFILE%\.armada\bin\current` when a self-rebuild has created one (see [SERVER_REBUILD.md](SERVER_REBUILD.md)) and `%USERPROFILE%\.armada\bin\Armada.Server.exe` otherwise, and does nothing when that executable is already running. Because the entry points at the script in your checkout, keep the checkout where it is (re-run the installer after moving it). It does not require elevation.

`update-windows-task.bat` stops every running `Armada.Server` (including `dotnet`-hosted instances and builds run from the repository) before republishing, so the ports and file locks are released. `remove-windows-task.bat` stops the server and deletes the entry, and exits successfully when the entry is not installed.

> **Note:** For SCM-managed startup before anyone logs on, register a real Windows Service with `Armada.Server.exe --install-service` from an elevated prompt instead (the WiX installer does this). See "Service and startup registration" in [OPERATIONS.md](OPERATIONS.md).

## Linux (`systemd --user`)

The supported Linux path is a user-scoped `systemd` service.

Scripts:

- `scripts/linux/install-systemd-user.sh`
- `scripts/linux/update-systemd-user.sh`
- `scripts/linux/remove-systemd-user.sh`

Install and start:

```bash
./scripts/linux/install-systemd-user.sh
```

Update from source and restart:

```bash
./scripts/linux/update-systemd-user.sh
```

Remove the user service:

```bash
./scripts/linux/remove-systemd-user.sh
```

The installer writes `~/.config/systemd/user/armada.service` (under `$XDG_CONFIG_HOME` when it is set), a `Type=simple` unit that runs `~/.armada/bin/Armada.Server` with `Restart=on-failure`, then runs `systemctl --user enable --now armada.service`. The update script stops the unit and re-runs the installer; the remove script disables and stops the unit and deletes the file.

> **Note:** `systemd --user` services normally start when your user session starts. If you want Armada to come up at boot before interactive login, enable linger for your account:
>
> `sudo loginctl enable-linger $USER`

## macOS (`launchd`)

The supported macOS path is a user-scoped `LaunchAgent`.

Scripts:

- `scripts/macos/install-launchd-agent.sh`
- `scripts/macos/update-launchd-agent.sh`
- `scripts/macos/remove-launchd-agent.sh`

Install and start:

```bash
./scripts/macos/install-launchd-agent.sh
```

Update from source and restart:

```bash
./scripts/macos/update-launchd-agent.sh
```

Remove the agent:

```bash
./scripts/macos/remove-launchd-agent.sh
```

The installer writes `~/Library/LaunchAgents/com.armada.admiral.plist` (label `com.armada.admiral`, `RunAtLoad` and `KeepAlive`, running `~/.armada/bin/Armada.Server` with stdout and stderr in `~/.armada/logs/launchd-stdout.log` and `launchd-stderr.log`) and loads it with `launchctl bootstrap gui/<uid>`. The update script unloads the agent and re-runs the installer; the remove script unloads it and deletes the plist.

The Linux and macOS definitions always start `~/.armada/bin/Armada.Server`; unlike the Windows start script they do not follow the self-rebuild slot pointer.

> **Note:** `LaunchAgent` runs in your user session. If you need machine-level startup before user login, you would need a separate `LaunchDaemon` flow and a service-compatible runtime context for Armada's repos, agent binaries, and credentials.

## Verifying the Server Is Running

All install and update scripts run a health check automatically. You can also verify the Admiral manually:

```bash
curl http://localhost:7890/api/v1/status/health
```

Or check the main log file at `~/.armada/logs/admiral.log`.

## Default Paths and Ports

| Item | Default |
|------|---------|
| Data directory | `~/.armada` |
| Published server binary | `~/.armada/bin/Armada.Server` or `Armada.Server.exe` |
| React dashboard deploy | `~/.armada/dashboard` |
| REST API (+ WebSocket at `/ws`) | `7890` |
| MCP server | `7891` |

Ports are configurable in `~/.armada/settings.json`. When you use a non-default Admiral port, set `ARMADA_BASE_URL` before running the health-check or install/update scripts so the post-start verification targets the correct endpoint.
