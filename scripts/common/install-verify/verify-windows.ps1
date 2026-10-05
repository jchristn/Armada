# =====================================================================
# verify-windows.ps1 -- install verification on Windows (V1 readiness W5.5).
# UNVERIFIED: written without a Windows machine; the first CI run is the real check.
#
# Part 1, NuGet global tool (Armada.Helm, command "armada"):
#   packs src/Armada.Helm, installs it with "dotnet tool install --tool-path"
#   into a temp directory, runs "armada server start" with ARMADA_DATA_DIR in
#   the temp directory and the Admiral on IV_PORT_BASE+60/+61, runs the REST
#   smoke test (login, dashboard, fleet, vessel from a temp bare repo, one
#   mission on a stub-inference ApiEndpoint captain), then "armada server stop".
# Part 2, server service registration:
#   publishes Armada.Server self-contained for win-x64 the way the WiX channel
#   does and runs "Armada.Server.exe --install-service --dry-run", which prints
#   the Windows service definition and the sc.exe commands without changing
#   anything (exit 4 without elevation is reported, not failed).
#
# Usage: pwsh scripts/common/install-verify/verify-windows.ps1 [-SkipTool] [-SkipService] [-Keep]
# Env:   IV_PORT_BASE (default 34000)
# Requires: dotnet SDK, git, python (3.8+) on PATH.
# =====================================================================
[CmdletBinding()]
param(
    [switch]$SkipTool,
    [switch]$SkipService,
    [switch]$Keep
)

# Native tools write progress to stderr; failures are detected from exit codes and explicit checks instead.
$ErrorActionPreference = 'Continue'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '..\..\..')).Path
$PortBase = if ($env:IV_PORT_BASE) { [int]$env:IV_PORT_BASE } else { 34000 }
$RestPort = $PortBase + 60
$McpPort = $PortBase + 61
$StubPort = $PortBase + 53
$Password = 'Install-Verify-Win-' + (Get-Random -Minimum 1000 -Maximum 9999) + '-Pw1'
$Work = Join-Path ([System.IO.Path]::GetTempPath()) ('armada-iv-win-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $Work | Out-Null
$Started = New-Object System.Collections.Generic.List[int]
$Failures = New-Object System.Collections.Generic.List[string]

function Log([string]$Message) { Write-Host "[install-verify] $Message" }

function Check([string]$Label, [bool]$Ok) {
    if ($Ok) { Write-Host "PASS  $Label" } else { Write-Host "FAIL  $Label"; $script:Failures.Add($Label) }
}

function Get-Python {
    foreach ($name in @('python', 'python3', 'py')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    throw 'python is required'
}

function Assert-PortFree([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        throw "port $Port is already in use; set a different IV_PORT_BASE"
    } catch [System.Net.Sockets.SocketException] {
        # Connection refused: the port is free.
    } finally {
        $client.Dispose()
    }
}

function Stop-Started {
    for ($i = $script:Started.Count - 1; $i -ge 0; $i--) {
        $id = $script:Started[$i]
        $proc = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($proc) {
            Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
            $proc.WaitForExit(10000) | Out-Null
        }
    }
    $script:Started.Clear()
}

$Python = Get-Python
$ExitCode = 0
try {
    foreach ($p in @($RestPort, $McpPort, $StubPort)) { Assert-PortFree $p }

    if (-not $SkipTool) {
        # ---- Part 1: dotnet tool -------------------------------------------------
        $Nupkg = Join-Path $Work 'nupkg'
        Log "packing src/Armada.Helm into $Nupkg"
        & dotnet pack (Join-Path $RepoRoot 'src\Armada.Helm') -c Release -o $Nupkg --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed' }
        $pkg = Get-ChildItem $Nupkg -Filter 'Armada.Helm.*.nupkg' | Where-Object { $_.Name -notlike '*.snupkg' } | Select-Object -First 1
        if (-not $pkg) { throw 'no Armada.Helm nupkg produced' }
        $Version = $pkg.BaseName.Substring('Armada.Helm.'.Length)
        Log "package: $($pkg.Name)"

        $ToolPath = Join-Path $Work 'tools'
        & dotnet tool install Armada.Helm --version $Version --tool-path $ToolPath --add-source $Nupkg --ignore-failed-sources
        if ($LASTEXITCODE -ne 0) { throw 'dotnet tool install failed' }
        $Tool = Join-Path $ToolPath 'armada.exe'
        Check 'tool shim armada.exe installed' (Test-Path $Tool)

        $env:ARMADA_DATA_DIR = Join-Path $Work 'data'
        $env:ARMADA_INITIAL_ADMIN_PASSWORD = $Password
        New-Item -ItemType Directory -Path $env:ARMADA_DATA_DIR | Out-Null
        $settings = @{
            admiralPort   = $RestPort
            mcpPort       = $McpPort
            telemetry     = @{ enabled = $false; prometheusEnabled = $false }
            syslogServers = @()
        } | ConvertTo-Json -Depth 4
        [System.IO.File]::WriteAllText((Join-Path $env:ARMADA_DATA_DIR 'settings.json'), $settings)

        # Bare origin repository with one commit on main.
        $Seed = Join-Path $Work 'seed'
        $Origin = Join-Path $Work 'origin.git'
        & git init -q -b main $Seed
        Set-Content -Path (Join-Path $Seed 'README.md') -Value '# install smoke'
        & git -C $Seed add README.md
        & git -C $Seed -c "user.name=Armada Install Verify" -c "user.email=install-verify@armada.invalid" commit -q -m 'Initial commit'
        & git clone -q --bare $Seed $Origin
        if ($LASTEXITCODE -ne 0) { throw 'could not create the origin repository' }

        $stub = Start-Process -FilePath $Python -ArgumentList @((Join-Path $ScriptDir 'stub_inference.py'), '--host', '127.0.0.1', '--port', "$StubPort") `
            -PassThru -WindowStyle Hidden -RedirectStandardError (Join-Path $Work 'stub.log')
        $Started.Add($stub.Id)
        Start-Sleep -Seconds 2

        # Run from the temp directory so the CLI cannot find a source checkout and build from it.
        Push-Location $Work
        try {
            Log 'armada server start'
            $startOutput = & $Tool server start 2>&1 | Out-String
            Write-Host $startOutput
            $match = [regex]::Match($startOutput, '\(PID: (\d+)\)')
            Check 'armada server start launched the Admiral' ($LASTEXITCODE -eq 0 -and $match.Success)
            if ($match.Success) { $Started.Add([int]$match.Groups[1].Value) }

            & $Python (Join-Path $ScriptDir 'smoke.py') --base-url "http://127.0.0.1:$RestPort" --password $Password `
                --stub-url "http://127.0.0.1:$StubPort/v1" --repo-url $Origin
            Check 'REST smoke test (login, dashboard, fleet, vessel, mission)' ($LASTEXITCODE -eq 0)

            Log 'armada server stop'
            & $Tool server stop 2>&1 | Out-String | Write-Host
            if ($match.Success) {
                $serverProc = Get-Process -Id ([int]$match.Groups[1].Value) -ErrorAction SilentlyContinue
                if ($serverProc) { $exited = $serverProc.WaitForExit(30000) } else { $exited = $true }
                Check 'armada server stop stopped the Admiral' $exited
            }
        } finally {
            Pop-Location
        }

        if ($Failures.Count -gt 0) {
            Log 'admiral log tail:'
            Get-ChildItem (Join-Path $env:ARMADA_DATA_DIR 'logs') -Filter 'admiral.log*' -ErrorAction SilentlyContinue |
                ForEach-Object { Get-Content $_.FullName -Tail 60 }
        }
        Stop-Started
    }

    if (-not $SkipService) {
        # ---- Part 2: --install-service --dry-run ---------------------------------
        $Publish = Join-Path $Work 'server-win-x64'
        Log "publishing Armada.Server (win-x64, self-contained, single file) to $Publish"
        & dotnet publish (Join-Path $RepoRoot 'src\Armada.Server') -c Release -f net10.0 -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $Publish --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
        $ServerExe = Join-Path $Publish 'Armada.Server.exe'
        Check 'published Armada.Server.exe' (Test-Path $ServerExe)
        Check 'publish carries the React dashboard' (Test-Path (Join-Path $Publish 'dashboard\index.html'))

        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        $elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        Log "armada-server --install-service --dry-run (elevated: $elevated)"
        $dry = & $ServerExe --install-service --dry-run 2>&1 | Out-String
        $dryCode = $LASTEXITCODE
        Write-Host $dry
        if ($elevated) {
            Check '--install-service --dry-run exits 0' ($dryCode -eq 0)
            Check 'dry run describes the armada service' ($dry -match 'armada')
            Check 'dry run names --run-service' ($dry -match '--run-service')
            Check 'dry run created no service' ($null -eq (Get-Service -Name 'armada' -ErrorAction SilentlyContinue))
        } else {
            Check '--install-service without elevation is refused with exit 4' ($dryCode -eq 4 -or $dryCode -eq 0)
        }
    }
} catch {
    Write-Host "[install-verify] ERROR: $($_.Exception.Message)"
    $ExitCode = 1
} finally {
    Stop-Started
    if (-not $Keep) {
        Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue
    } else {
        Log "work directory kept: $Work"
    }
}

if ($Failures.Count -gt 0) { $ExitCode = 1 }
if ($ExitCode -eq 0) { Log 'Windows install verification: PASS' } else { Log "Windows install verification: FAIL ($($Failures -join '; '))" }
exit $ExitCode
