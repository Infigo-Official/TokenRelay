#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs all TokenRelay tests via WSL (Windows Subsystem for Linux)

.DESCRIPTION
    This is a Windows wrapper script that executes the Bash test runner in WSL.
    Use this for local development on Windows machines.

    For CI/CD (GitHub Actions), use run-all-tests.sh directly.

.PARAMETER Verbose
    Enable verbose output for detailed logging

.PARAMETER WslDistro
    Specify the WSL distribution to use (default: uses default WSL distro)

.EXAMPLE
    .\Run-AllTests.ps1
    Runs all tests using default WSL distribution

.EXAMPLE
    .\Run-AllTests.ps1 -Verbose
    Runs all tests with verbose output

.EXAMPLE
    .\Run-AllTests.ps1 -WslDistro Ubuntu-22.04
    Runs tests using a specific WSL distribution

.NOTES
    Prerequisites:
    - Windows Subsystem for Linux (WSL) installed
    - A Linux distribution installed in WSL
    - .NET 10.0 SDK installed in WSL
    - Docker Desktop with WSL integration enabled
#>

param(
    [switch]$VerboseOutput,

    [Parameter(Mandatory = $false)]
    [string]$WslDistro = ""
)

$ErrorActionPreference = "Stop"

#region Helper Functions

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host "  $Title" -ForegroundColor Magenta
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host ""
}

function Write-Info {
    param([string]$Message)
    Write-Host "[i] $Message" -ForegroundColor Gray
}

function Write-Success {
    param([string]$Message)
    Write-Host "[+] $Message" -ForegroundColor Green
}

function Write-TestError {
    param([string]$Message)
    Write-Host "[-] $Message" -ForegroundColor Red
}

function Test-WslAvailable {
    try {
        $wslVersion = wsl --version 2>&1
        if ($LASTEXITCODE -eq 0) {
            return $true
        }
    }
    catch {
        # WSL not available
    }
    return $false
}

function Get-WslPath {
    param([string]$WindowsPath)

    # Convert Windows path to WSL path
    # C:\Users\... -> /mnt/c/Users/...
    $wslPath = $WindowsPath -replace '\\', '/'
    $wslPath = $wslPath -replace '^([A-Za-z]):', { '/mnt/' + $_.Groups[1].Value.ToLower() }
    return $wslPath
}

#endregion

#region Main Execution

Write-Header "TokenRelay Test Runner (WSL)"

# Check WSL availability
Write-Info "Checking WSL availability..."

if (-not (Test-WslAvailable)) {
    Write-TestError "WSL is not installed or not available."
    Write-Host ""
    Write-Host "To install WSL, run:" -ForegroundColor Yellow
    Write-Host "  wsl --install" -ForegroundColor White
    Write-Host ""
    Write-Host "Or install from Microsoft Store." -ForegroundColor Yellow
    exit 3
}

Write-Success "WSL is available"

# Get script paths
$scriptDir = $PSScriptRoot
$bashScript = Join-Path $scriptDir "run-all-tests.sh"

if (-not (Test-Path $bashScript)) {
    Write-TestError "Bash script not found: $bashScript"
    exit 3
}

Write-Success "Bash script found: $bashScript"

# Convert to WSL path
$wslScriptPath = Get-WslPath $bashScript
Write-Info "WSL path: $wslScriptPath"

# Build WSL command
$wslArgs = @()

if ($WslDistro) {
    $wslArgs += "-d"
    $wslArgs += $WslDistro
    Write-Info "Using WSL distribution: $WslDistro"
}

# Build the bash command
$bashCommand = "chmod +x '$wslScriptPath' && '$wslScriptPath'"

if ($VerboseOutput) {
    $bashCommand += " -v"
}

$wslArgs += "bash"
$wslArgs += "-c"
$wslArgs += $bashCommand

Write-Info "Running tests in WSL..."
Write-Host ""

# Execute in WSL
$process = Start-Process -FilePath "wsl" -ArgumentList $wslArgs -NoNewWindow -Wait -PassThru

# Return the exit code from the bash script
$exitCode = $process.ExitCode

Write-Host ""

switch ($exitCode) {
    0 {
        Write-Success "All tests passed!"
    }
    1 {
        Write-TestError "One or more tests failed."
    }
    2 {
        Write-TestError "Build failed."
    }
    3 {
        Write-TestError "Prerequisites check failed in WSL."
    }
    default {
        Write-TestError "Unknown error occurred (exit code: $exitCode)"
    }
}

exit $exitCode

#endregion
