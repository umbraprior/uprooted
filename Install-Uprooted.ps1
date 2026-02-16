#Requires -Version 5.1
param(
    [ValidateSet("Profiler", "StartupHooks")]
    [string]$Method = "Profiler"
)

$ErrorActionPreference = "Stop"

$RootExePath = Join-Path $env:LOCALAPPDATA "Root\current\Root.exe"
$HookProjectDir = Join-Path $PSScriptRoot "hook"
$ToolsDir = Join-Path $PSScriptRoot "tools"
$InstallDir = Join-Path $env:LOCALAPPDATA "Root\uprooted"
$DllName = "UprootedHook.dll"
$ProfilerDll = "uprooted_profiler.dll"
$ProfilerGuid = "{D1A6F5A0-1234-4567-89AB-CDEF01234567}"

function Write-Step($msg) { Write-Host "[*] $msg" -ForegroundColor Cyan }
function Write-OK($msg) { Write-Host "[+] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[!] $msg" -ForegroundColor Yellow }
function Write-Err($msg) { Write-Host "[-] $msg" -ForegroundColor Red }

Write-Host ""
Write-Host "  +---------------------------------+" -ForegroundColor Green
Write-Host "  |    Uprooted Installer v0.1.95    |" -ForegroundColor Green
Write-Host "  +---------------------------------+" -ForegroundColor Green
Write-Host "  Method: $Method" -ForegroundColor Gray
Write-Host ""

Write-Step "Checking for Root.exe..."
if (-not (Test-Path $RootExePath)) {
    Write-Err "Root.exe not found at: $RootExePath"
    Write-Err "Make sure Root Communications is installed."
    exit 1
}
$exeSize = (Get-Item $RootExePath).Length
Write-OK "Found Root.exe ($([math]::Round($exeSize / 1MB))MB)"

$rootProcess = Get-Process -Name "Root" -ErrorAction SilentlyContinue
if ($rootProcess) {
    Write-Warn "Root is currently running. Please close it before installing."
    $response = Read-Host "Close Root now? (y/n)"
    if ($response -eq 'y') {
        Stop-Process -Name "Root" -Force
        Start-Sleep -Seconds 2
        Write-OK "Root closed"
    } else {
        Write-Err "Installation cancelled. Close Root and try again."
        exit 1
    }
}

if ($Method -eq "StartupHooks") {
    Write-Step "Patching Root.exe to enable startup hooks..."

    $SearchBytes = [System.Text.Encoding]::UTF8.GetBytes('"System.StartupHookProvider.IsSupported": false')
    $ReplaceBytes = [System.Text.Encoding]::UTF8.GetBytes('"System.StartupHookProvider.IsSupported": true ')

    if ($SearchBytes.Length -ne $ReplaceBytes.Length) {
        Write-Err "Internal error: patch byte length mismatch"
        exit 1
    }

    $exeBytes = [System.IO.File]::ReadAllBytes($RootExePath)

    $found = -1
    for ($i = 0; $i -le ($exeBytes.Length - $SearchBytes.Length); $i++) {
        $match = $true
        for ($j = 0; $j -lt $SearchBytes.Length; $j++) {
            if ($exeBytes[$i + $j] -ne $SearchBytes[$j]) {
                $match = $false
                break
            }
        }
        if ($match) {
            $found = $i
            break
        }
    }

    if ($found -eq -1) {
        $alreadyPatched = $false
        for ($i = 0; $i -le ($exeBytes.Length - $ReplaceBytes.Length); $i++) {
            $match = $true
            for ($j = 0; $j -lt $ReplaceBytes.Length; $j++) {
                if ($exeBytes[$i + $j] -ne $ReplaceBytes[$j]) {
                    $match = $false
                    break
                }
            }
            if ($match) {
                $alreadyPatched = $true
                break
            }
        }

        if ($alreadyPatched) {
            Write-OK "Root.exe already patched (startup hooks enabled)"
        } else {
            Write-Err "Could not find startup hook flag in Root.exe"
            Write-Err "This version of Root may not be supported."
            exit 1
        }
    } else {
        $backupPath = "$RootExePath.uprooted.bak"
        if (-not (Test-Path $backupPath)) {
            Write-Step "Creating backup at $backupPath..."
            Copy-Item $RootExePath $backupPath
            Write-OK "Backup created"
        } else {
            Write-OK "Backup already exists"
        }

        for ($j = 0; $j -lt $ReplaceBytes.Length; $j++) {
            $exeBytes[$found + $j] = $ReplaceBytes[$j]
        }
        [System.IO.File]::WriteAllBytes($RootExePath, $exeBytes)
        Write-OK "Patched at offset 0x$($found.ToString('X8'))"
    }
} else {
    Write-Step "Checking for profiler DLL..."
    $profilerSource = Join-Path $ToolsDir $ProfilerDll
    if (-not (Test-Path $profilerSource)) {
        Write-Err "Profiler DLL not found at: $profilerSource"
        Write-Err "Build it first: cl.exe /LD /O2 uprooted_profiler.c /link kernel32.lib"
        exit 1
    }
    Write-OK "Found $ProfilerDll"
}

Write-Step "Building UprootedHook.dll..."

if (-not (Test-Path $HookProjectDir)) {
    Write-Err "Hook project not found at: $HookProjectDir"
    exit 1
}

$buildResult = & dotnet build $HookProjectDir -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err "Build failed:"
    $buildResult | ForEach-Object { Write-Host "  $_" }
    exit 1
}

$builtDll = Join-Path $HookProjectDir "bin\Release\net10.0\$DllName"
if (-not (Test-Path $builtDll)) {
    Write-Err "Built DLL not found at: $builtDll"
    exit 1
}
Write-OK "Build successful"

Write-Step "Installing to $InstallDir..."

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item $builtDll $InstallDir -Force

$depsJson = Join-Path $HookProjectDir "bin\Release\net10.0\UprootedHook.deps.json"
if (Test-Path $depsJson) {
    Copy-Item $depsJson $InstallDir -Force
}

if ($Method -eq "Profiler") {
    $profilerSource = Join-Path $ToolsDir $ProfilerDll
    Copy-Item $profilerSource $InstallDir -Force
    Write-OK "Profiler DLL copied"
}

$installedDll = Join-Path $InstallDir $DllName
Write-OK "DLL installed to $installedDll"

Write-Step "Setting environment variables..."

if ($Method -eq "Profiler") {
    [System.Environment]::SetEnvironmentVariable("CORECLR_ENABLE_PROFILING", "1", "User")
    [System.Environment]::SetEnvironmentVariable("CORECLR_PROFILER", $ProfilerGuid, "User")
    $installedProfiler = Join-Path $InstallDir $ProfilerDll
    [System.Environment]::SetEnvironmentVariable("CORECLR_PROFILER_PATH", $installedProfiler, "User")
    [System.Environment]::SetEnvironmentVariable("DOTNET_ReadyToRun", "0", "User")

    [System.Environment]::SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", $null, "User")

    Write-OK "CORECLR_ENABLE_PROFILING = 1"
    Write-OK "CORECLR_PROFILER = $ProfilerGuid"
    Write-OK "CORECLR_PROFILER_PATH = $installedProfiler"
    Write-OK "DOTNET_ReadyToRun = 0"
    Write-Warn "Note: These env vars affect all .NET apps. The profiler has a process guard for Root.exe only."
} else {
    [System.Environment]::SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", $installedDll, "User")

    [System.Environment]::SetEnvironmentVariable("CORECLR_ENABLE_PROFILING", $null, "User")
    [System.Environment]::SetEnvironmentVariable("CORECLR_PROFILER", $null, "User")
    [System.Environment]::SetEnvironmentVariable("CORECLR_PROFILER_PATH", $null, "User")
    [System.Environment]::SetEnvironmentVariable("DOTNET_ReadyToRun", $null, "User")

    Write-OK "DOTNET_STARTUP_HOOKS = $installedDll"
}

Write-Host ""
Write-Host "  +---------------------------------+" -ForegroundColor Green
Write-Host "  |   Installation Complete!         |" -ForegroundColor Green
Write-Host "  +---------------------------------+" -ForegroundColor Green
Write-Host ""
Write-Host "  Launch Root to activate Uprooted." -ForegroundColor White
Write-Host "  Open Settings to see the UPROOTED section." -ForegroundColor White
Write-Host "  Logs: $env:LOCALAPPDATA\Root Communications\Root\profile\default\uprooted-hook.log" -ForegroundColor Gray
Write-Host ""
