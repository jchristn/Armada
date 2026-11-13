# Stops the Armada MCP stdio hosts (dotnet Armada.Helm.dll mcp stdio) that run from a repository checkout.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\windows\stop-repo-mcp-hosts.ps1 [-RepoRoot <dir>]
#   -RepoRoot   the checkout to match; defaults to the one this script is in
param(
    [string]$RepoRoot = (Join-Path $PSScriptRoot '..\..')
)

$resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

$targets = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
    Where-Object {
        $_.CommandLine -like '*Armada.Helm.dll* mcp stdio*' -and
        $_.CommandLine -like ('*' + $resolvedRepoRoot + '*')
    }

if (-not $targets) {
    Write-Host '[update] No repo-backed MCP stdio hosts found.'
    exit 0
}

foreach ($target in $targets) {
    Write-Host ('[update] Stopping MCP stdio host PID ' + $target.ProcessId + '...')
    Stop-Process -Id $target.ProcessId -Force
}
