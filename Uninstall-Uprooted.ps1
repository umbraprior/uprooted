#Requires -Version 5.1

$ErrorActionPreference = "Stop"

$RootExePath = Join-Path $env:LOCALAPPDATA "Root\current\Root.exe"
$BackupPath = "$RootExePath.uprooted.bak"
$InstallDir = Join-Path $env:LOCALAPPDATA "Root\uprooted"

function Write-Step($msg) { Write-Host "[*] $msg" -ForegroundColor Cyan }
function Write-OK($msg) { Write-Host "[+] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[!] $msg" -ForegroundColor Yellow }

Write-Host ""
Write-Host "  +---------------------------------+" -ForegroundColor Yellow
Write-Host "  |   Uprooted Uninstaller v0.1.95   |" -ForegroundColor Yellow
Write-Host "  +---------------------------------+" -ForegroundColor Yellow
Write-Host ""

$rootProcess = Get-Process -Name "Root" -ErrorAction SilentlyContinue
if ($rootProcess) {
    Write-Warn "Root is currently running. Please close it first."
    $response = Read-Host "Close Root now? (y/n)"
    if ($response -eq 'y') {
        Stop-Process -Name "Root" -Force
        Start-Sleep -Seconds 2
        Write-OK "Root closed"
    } else {
        Write-Warn "Continuing anyway (changes take effect on next launch)"
    }
}

Write-Step "Removing environment variables..."

$envVars = @(
    "CORECLR_ENABLE_PROFILING",
    "CORECLR_PROFILER",
    "CORECLR_PROFILER_PATH",
    "DOTNET_ReadyToRun",
    "DOTNET_STARTUP_HOOKS"
)

foreach ($var in $envVars) {
    $current = [System.Environment]::GetEnvironmentVariable($var, "User")
    if ($current) {
        [System.Environment]::SetEnvironmentVariable($var, $null, "User")
        Write-OK "Removed $var"
    }
}
foreach ($var in $envVars) {
    Remove-Item "Env:\$var" -ErrorAction SilentlyContinue
}
Write-OK "Environment variables cleaned"

Write-Step "Checking for Root.exe backup..."
if (Test-Path $BackupPath) {
    Copy-Item $BackupPath $RootExePath -Force
    Remove-Item $BackupPath -Force
    Write-OK "Root.exe restored from backup"
} else {
    Write-OK "No backup found (Root.exe was not patched)"
}

Write-Step "Removing installed files..."
if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-OK "Removed $InstallDir"
} else {
    Write-OK "Install directory was already clean"
}

$profileDir = Join-Path $env:LOCALAPPDATA "Root Communications\Root\profile\default"
$logFile = Join-Path $profileDir "uprooted-hook.log"
$settingsFile = Join-Path $profileDir "uprooted-settings.json"

$hasLeftovers = (Test-Path $logFile) -or (Test-Path $settingsFile)
if ($hasLeftovers) {
    Write-Host ""
    $response = Read-Host "Remove log and settings files too? (y/n)"
    if ($response -eq 'y') {
        if (Test-Path $logFile) { Remove-Item $logFile -Force }
        if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }
        Write-OK "Log and settings files removed"
    } else {
        Write-OK "Log and settings files kept"
    }
}

Write-Host ""
Write-Host "  +---------------------------------+" -ForegroundColor Green
Write-Host "  |   Uninstall Complete!            |" -ForegroundColor Green
Write-Host "  +---------------------------------+" -ForegroundColor Green
Write-Host ""
Write-Host "  Root Communications has been restored to stock." -ForegroundColor White
Write-Host ""
