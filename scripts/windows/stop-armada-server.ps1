# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\windows\stop-armada-server.ps1 [-List]
#   -List   print the PIDs of the running Admirals (one per line) and stop nothing
param(
    [switch]$List
)

$ErrorActionPreference = 'Stop'

# Stop EVERY running Armada Admiral server, no matter where it was launched from:
#   - the managed publish at %USERPROFILE%\.armada\bin\Armada.Server.exe
#   - a stray build run straight out of the repo (src\Armada.Server\bin\Debug|Release)
#   - a 'dotnet run' / 'dotnet exec' host of Armada.Server.dll
# The update scripts rely on this to fully release the Admiral/MCP ports and the build
# output file locks before republishing; leaving a stray instance alive is exactly what
# makes an update appear to "not take" (the old server keeps serving the port).

function Get-ArmadaServerProcesses {
    $byExe = @(Get-CimInstance Win32_Process -Filter "Name = 'Armada.Server.exe'" -ErrorAction SilentlyContinue)
    $byDotnet = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -match 'Armada\.Server\.dll' })
    return @($byExe + $byDotnet)
}

function Stop-ArmadaProcess([int] $TargetProcessId) {
    # Stop-Process -Force does not reliably terminate the Admiral in every environment (it can leave the
    # process alive with no error). Escalate to taskkill /F /T, which does terminate it -- otherwise the
    # server keeps the port and the SQLite database file locked, which breaks updates and factory-reset.
    try { Stop-Process -Id $TargetProcessId -Force -ErrorAction Stop } catch { }
    if (Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue) {
        # taskkill can race a process that just exited and write "process not found" to stderr; swallow it
        # (merge stderr and discard) so a benign message is not surfaced as an error.
        try { & "$env:SystemRoot\System32\taskkill.exe" /F /T /PID $TargetProcessId 2>&1 | Out-Null } catch { }
    }
}

$targets = Get-ArmadaServerProcesses
if ($List) {
    foreach ($process in $targets) { Write-Output $process.ProcessId }
    exit 0
}

if ($targets.Count -eq 0) {
    Write-Host '[stop-armada-server] No Armada.Server process is running.'
    exit 0
}

if ($targets.Count -gt 0) {
    foreach ($process in $targets) {
        $where = if ($process.ExecutablePath) { $process.ExecutablePath } else { $process.Name }
        Write-Host "[stop-armada-server] Stopping PID $($process.ProcessId) ($where)"
        Stop-ArmadaProcess -TargetProcessId $process.ProcessId
    }
}

# Wait for every instance to exit so ports and DLLs (and the database file) are released before the caller
# rebuilds or deletes state. Re-issue the force kill on each pass in case a process survived the first try.
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    $stillRunning = Get-ArmadaServerProcesses
    if ($stillRunning.Count -eq 0) {
        exit 0
    }

    foreach ($process in $stillRunning) {
        Stop-ArmadaProcess -TargetProcessId $process.ProcessId
    }

    Start-Sleep -Milliseconds 500
}

$remaining = (Get-ArmadaServerProcesses | ForEach-Object { $_.ProcessId }) -join ', '
Write-Error "Armada.Server did not stop cleanly; still running PID(s): $remaining"
exit 1
