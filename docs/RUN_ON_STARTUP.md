# Running Armada Server on System Startup

This guide covers how to configure Armada Server (the Admiral process) to start automatically when your system boots.

## Prerequisites

- .NET 10.0 SDK installed
- Armada built: `dotnet build src/Armada.sln`
- Settings configured in `~/.armada/settings.json` (optional; defaults are used if absent)

## Publish a Self-Contained Binary

For startup services, publish a standalone binary rather than using `dotnet run`:

```bash
dotnet publish src/Armada.Server -c Release -f net10.0 -o ~/.armada/bin
```

This produces an executable at `~/.armada/bin/Armada.Server` (Linux/macOS) or `~/.armada/bin/Armada.Server.exe` (Windows).

---

## Linux (systemd)

Create a service unit file:

```bash
sudo nano /etc/systemd/system/armada.service
```

Paste the following, replacing `<YOUR_USER>` with your username:

```ini
[Unit]
Description=Armada Admiral Server
After=network.target

[Service]
Type=simple
User=<YOUR_USER>
ExecStart=%h/.armada/bin/Armada.Server
WorkingDirectory=%h/.armada
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable armada
sudo systemctl start armada
```

Check status:

```bash
sudo systemctl status armada
journalctl -u armada -f
```

---

## macOS (launchd)

Create a plist file:

```bash
nano ~/Library/LaunchAgents/com.armada.admiral.plist
```

Paste the following:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.armada.admiral</string>
    <key>ProgramArguments</key>
    <array>
        <string>/Users/YOUR_USER/.armada/bin/Armada.Server</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/Users/YOUR_USER/.armada/logs/launchd-stdout.log</string>
    <key>StandardErrorPath</key>
    <string>/Users/YOUR_USER/.armada/logs/launchd-stderr.log</string>
</dict>
</plist>
```

Replace `YOUR_USER` with your macOS username, then load:

```bash
launchctl load ~/Library/LaunchAgents/com.armada.admiral.plist
```

To unload:

```bash
launchctl unload ~/Library/LaunchAgents/com.armada.admiral.plist
```

---

## Windows (Task Scheduler)

### Option A: Command Line (schtasks)

Open an elevated Command Prompt or PowerShell and run:

```powershell
schtasks /create /tn "Armada Admiral" /tr "%USERPROFILE%\.armada\bin\Armada.Server.exe" /sc onlogon /rl highest
```

### Option B: Task Scheduler GUI

1. Open **Task Scheduler** (`taskschd.msc`).
2. Click **Create Task**.
3. **General** tab: name it `Armada Admiral`, check **Run with highest privileges**.
4. **Triggers** tab: add a trigger for **At log on**.
5. **Actions** tab: add an action **Start a program**, set the path to `%USERPROFILE%\.armada\bin\Armada.Server.exe`.
6. **Settings** tab: check **Allow task to be run on demand** and **Restart on failure** (1 minute interval).
7. Click **OK**.

### Option C: Windows Service (sc.exe)

If you want Armada to run as a true Windows service (starts before user logon):

```powershell
sc.exe create ArmadaAdmiral binPath= "%USERPROFILE%\.armada\bin\Armada.Server.exe" start= auto
sc.exe start ArmadaAdmiral
```

> **Note:** Running as a Windows service requires the executable to support the Windows Service lifecycle. You may need to wrap it with a service host or use a tool like [NSSM](https://nssm.cc/).

---

## Verifying the Server is Running

After startup, verify the Admiral is listening:

```bash
curl http://localhost:7890/dashboard
```

Or check the log file at `~/.armada/logs/admiral.log`.

## Default Ports

| Service       | Port |
|---------------|------|
| REST API      | 7890 |
| MCP Server    | 7891 |
| WebSocket Hub | 7892 |

Ports are configurable in `~/.armada/settings.json`.
