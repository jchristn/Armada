$ErrorActionPreference = 'Stop'

$armadaHome = Join-Path $env:USERPROFILE '.armada'
$binDir = Join-Path $armadaHome 'bin'

# Prefer the executable named by the self-rebuild slot pointer so a rebuild's new binary is picked up on the
# next launch; fall back to the classic non-slot publish at bin\Armada.Server.exe. See docs/SERVER_REBUILD.md.
$serverExe = Join-Path $binDir 'Armada.Server.exe'
$currentPointer = Join-Path $binDir 'current'
if (Test-Path -LiteralPath $currentPointer) {
    $slotName = (Get-Content -LiteralPath $currentPointer -Raw -ErrorAction SilentlyContinue)
    if ($slotName) { $slotName = $slotName.Trim() }
    if ($slotName) {
        $slotExe = Join-Path $binDir (Join-Path 'slots' (Join-Path $slotName 'Armada.Server.exe'))
        if (Test-Path -LiteralPath $slotExe) { $serverExe = $slotExe }
    }
}

$targetPath = [System.IO.Path]::GetFullPath($serverExe)

if (-not (Test-Path -LiteralPath $targetPath)) {
    Write-Error "Published server executable not found at $targetPath"
}

$existing = Get-CimInstance Win32_Process -Filter "Name = 'Armada.Server.exe'" |
    Where-Object {
        $_.ExecutablePath -and
        [System.StringComparer]::OrdinalIgnoreCase.Equals($_.ExecutablePath, $targetPath)
    }

if ($existing) {
    exit 0
}

Start-Process -FilePath $targetPath -WorkingDirectory $armadaHome -WindowStyle Hidden | Out-Null
exit 0
