# Running Armada Server on System Startup

This guide covers the scripted startup workflows for the Admiral process. The scripts publish `Armada.Server` into `~/.armada/bin`, deploy the dashboard into `~/.armada/dashboard`, register the platform-specific service definition, and verify health on startup.

The server can also register itself: `armada-server --install-service` (or `Armada.Server --install-service` for a published binary) writes a systemd unit, launchd agent, or Windows Service directly, and the WiX, Inno, `.pkg`, and Deb/Rpm installers use it. See "Service and startup registration" in [OPERATIONS.md](OPERATIONS.md). Use one mechanism per install, not both: the scripts below and `--install-service` use different definitions (for example the launchd labels `com.armada.admiral` and `com.joelchristner.armada.server`), and two registered Admirals race for the same ports.

## Prerequisites

- .NET SDK installed for the framework you plan to publish, such as `net8.0` or `net10.0`
- Settings configured in `~/.armada/settings.json` if you are not using the default ports and paths
- Platform service manager available:
  - Windows: PowerShell and the current-user `Run` registry key
  - Linux: `systemd --user`, or a desktop session that runs XDG autostart entries
  - macOS: `launchd`
  - WSL: Windows interop (`cmd.exe`, `wscript.exe`), for the Windows Startup folder

## Shared Helpers

The shell implementations now live in `scripts/common/`, with Linux and macOS wrappers in their respective platform folders. Windows entrypoints live in `scripts/windows/`.

Canonical helpers:

- Windows: `scripts/windows/publish-server.bat`, `scripts/windows/healthcheck-server.bat`
- Shared shell implementation: `scripts/common/publish-server.sh`, `scripts/common/healthcheck-server.sh`
- Linux wrappers: `scripts/linux/publish-server.sh`, `scripts/linux/healthcheck-server.sh`
- macOS wrappers: `scripts/macos/publish-server.sh`, `scripts/macos/healthcheck-server.sh`
- WSL: `scripts/wsl2/` and `scripts/wsl/` carry their own WSL implementations of the same scripts

`publish-server` publishes `src/Armada.Server` in `Release` mode for `net10.0` by default to `~/.armada/bin` and then attempts to deploy the React dashboard. Override the framework with a leading argument (`net8.0`, `-f net8.0`, or `--framework net8.0`), for example `scripts\windows\publish-server.bat net8.0` or `./scripts/linux/publish-server.sh --framework net8.0`, or with the `ARMADA_TARGET_FRAMEWORK` environment variable. The start-at-login install and update scripts on every platform (`install-startup` and `update-startup`, below) publish the same way and take the same arguments, so a framework argument or `--insecure` works with them too. `publish-server` refuses to run while an Armada server is running, so it never overwrites a running binary; the startup scripts stop the server first.

If the dashboard deploy fails, `publish-server` (shell and `.bat` alike) keeps going with a warning only when a previously deployed dashboard exists in `~/.armada/dashboard`, and otherwise stops with an error and a non-zero exit, so the install or update script that called it stops too.

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

## Script names

Every OS has the same three start-at-login scripts:

| Action | Linux | macOS | WSL 2 / WSL 1 | Windows |
|--------|-------|-------|---------------|---------|
| Publish, register at login, start, health-check | `scripts/linux/install-startup.sh` | `scripts/macos/install-startup.sh` | `scripts/wsl2/install-startup.sh` / `scripts/wsl/install-startup.sh` | `scripts/windows/install-startup.bat` |
| Republish from the checkout and restart | `scripts/linux/update-startup.sh` | `scripts/macos/update-startup.sh` | `scripts/wsl2/update-startup.sh` / `scripts/wsl/update-startup.sh` | `scripts/windows/update-startup.bat` |
| Unregister and stop | `scripts/linux/remove-startup.sh` | `scripts/macos/remove-startup.sh` | `scripts/wsl2/remove-startup.sh` / `scripts/wsl/remove-startup.sh` | `scripts/windows/remove-startup.bat` |

The rules are the same everywhere. `install-startup` fails when Armada is already registered and points at `update-startup`; it stops any running Armada server before publishing. `update-startup` fails when Armada is not registered and points at `install-startup`; it rewrites the registration (unit, plist, autostart entry, Run value or launcher), so it also picks up a changed `PATH`. `remove-startup` unregisters first and then stops the server, and succeeds with "nothing to remove" when nothing is registered. `publish-server` (which all of them use) clears the self-rebuild slot pointer `~/.armada/bin/current`, so the freshly published server is the one that starts.

All three refuse to work next to the server's own registration from `Armada.Server --install-service` (also used by the installers): the launchd label `com.joelchristner.armada.server` on macOS, a `~/.config/systemd/user/armada.service` that runs `--run-service` or `/etc/systemd/system/armada.service` on Linux and WSL, and the Windows service `armada`. Two registrations would start two Admirals on the same ports, so the scripts name the conflicting registration and tell you to remove it with `Armada.Server --uninstall-service` first. Each takes an optional framework (`net8.0`, `-f net8.0`, `--framework net8.0`) and `--insecure`, rejects other arguments, and prints its usage with `-h`.

The earlier per-OS names still work and forward to the new scripts (the old install names, like before, update an existing registration instead of failing): `install-windows-task.bat`, `update-windows-task.bat`, `remove-windows-task.bat` (Windows), `install-systemd-user.sh`, `update-systemd-user.sh`, `remove-systemd-user.sh` (Linux) and `install-launchd-agent.sh`, `update-launchd-agent.sh`, `remove-launchd-agent.sh` (macOS).

## Windows (Current-User Startup)

The supported Windows path is a current-user startup registration under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; it does not depend on Task Scheduler.

Install and start:

```powershell
scripts\windows\install-startup.bat
```

Or publish and install against a specific SDK target:

```powershell
scripts\windows\install-startup.bat net8.0
```

Update from source and restart:

```powershell
scripts\windows\update-startup.bat --framework net8.0
```

Remove the startup entry:

```powershell
scripts\windows\remove-startup.bat
```

This installs a current-user startup entry named `ArmadaAdmiral` that runs `scripts\windows\start-armada-server.ps1` from your checkout (hidden PowerShell window) at logon in your normal user context, then starts the server immediately. The start script launches the slot named by `%USERPROFILE%\.armada\bin\current` when a self-rebuild has created one (see [SERVER_REBUILD.md](SERVER_REBUILD.md)) and `%USERPROFILE%\.armada\bin\Armada.Server.exe` otherwise, and does nothing when that executable is already running. Because the entry points at the script in your checkout, keep the checkout where it is (run `update-startup.bat` after moving it). It does not require elevation.

`install-startup.bat` and `update-startup.bat` stop every running `Armada.Server` (including `dotnet`-hosted instances and builds run from the repository) before republishing, so the ports and file locks are released. `remove-startup.bat` stops the server and deletes the entry.

> **Note:** For SCM-managed startup before anyone logs on, register a real Windows Service with `Armada.Server.exe --install-service` from an elevated prompt instead (the WiX installer does this). See "Service and startup registration" in [OPERATIONS.md](OPERATIONS.md).

## Linux (`systemd --user`, or XDG autostart)

Install and start:

```bash
./scripts/linux/install-startup.sh
```

Update from source and restart:

```bash
./scripts/linux/update-startup.sh
```

Remove the registration:

```bash
./scripts/linux/remove-startup.sh
```

When a systemd user manager is running, the installer writes `~/.config/systemd/user/armada.service` (under `$XDG_CONFIG_HOME` when it is set), a `Type=simple` unit that runs `~/.armada/bin/Armada.Server` with `Restart=on-failure`, with `Environment="PATH=..."` set to the `PATH` of the shell that ran the installer (captains are started by bare command names such as `claude`, `codex` and `git`, and a systemd user manager has a minimal `PATH`), then runs `systemctl --user enable --now armada.service`. Without one (some containers and init systems), it writes the XDG autostart entry `~/.config/autostart/armada.desktop` instead (`Exec=env "PATH=..." .../Armada.Server`), which a desktop session runs at graphical login, and starts the server now; that mode has no restart on failure. The update script stops the server, republishes and starts it again the same way; the remove script disables and deletes whichever exists and stops the server.

> **Note:** `systemd --user` services normally start when your user session starts. If you want Armada to come up at boot before interactive login, enable linger for your account:
>
> `sudo loginctl enable-linger $USER`

## WSL (`scripts/wsl2`, `scripts/wsl`)

Use `scripts/wsl2/` on WSL 2 and `scripts/wsl/` on WSL 1; each refuses to run on the other WSL version. They are full WSL implementations of the same scripts, not wrappers around `scripts/linux`. A systemd unit or desktop entry only runs while the distro is up, never at Windows login, and WSL stops an idle distro, so the installer puts a hidden launcher in the Windows Startup folder (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\armada-wsl-<distro>.vbs`). At Windows login it runs the server in that distro through `wsl.exe` (from `~/.armada`: the self-rebuild slot named by `~/.armada/bin/current` when it holds a server, else `~/.armada/bin/Armada.Server`; output appended to `~/.armada/logs/wsl-startup-stdout.log` and `wsl-startup-stderr.log`), which keeps the distro running; the installer also starts the server through the launcher right away, so it keeps running after the terminal closes. The launcher passes the installing shell's Linux `PATH` (without the Windows entries) and `DOTNET_ROOT` to the server. This mode has no automatic restart: run `update-startup.sh` after a crash. A systemd unit or desktop entry left by the Linux scripts is removed, so the server does not start twice. No admin rights are needed.

On WSL 2, Windows reaches the server at `http://localhost:7890` through WSL localhost forwarding. WSL 1 shares the Windows network, so `localhost` works directly.

## macOS (`launchd`)

Install and start:

```bash
./scripts/macos/install-startup.sh
```

Update from source and restart:

```bash
./scripts/macos/update-startup.sh
```

Remove the agent:

```bash
./scripts/macos/remove-startup.sh
```

The installer writes `~/Library/LaunchAgents/com.armada.admiral.plist` (label `com.armada.admiral`, `RunAtLoad` and `KeepAlive`, running `~/.armada/bin/Armada.Server` with the installing shell's `PATH` in `EnvironmentVariables`, since launchd itself only provides `/usr/bin:/bin:/usr/sbin:/sbin`, and stdout and stderr in `~/.armada/logs/launchd-stdout.log` and `launchd-stderr.log`) and loads it with `launchctl bootstrap gui/<uid>`. The update script unloads the agent, republishes and loads it again; the remove script unloads it and deletes the plist. Because of `KeepAlive`, `stop-armada-server.sh` unloads the agent before it signals the server (otherwise launchd restarts it at once); it loads again at the next login or with `update-startup.sh`.

The Linux and macOS definitions always start `~/.armada/bin/Armada.Server`; unlike the Windows start script they do not follow the self-rebuild slot pointer. (In 1.0 the self-rebuild resolves a slot's executable as `Armada.Server.exe` on every platform, so slots are Windows-only for now.)

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
