#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes TokenRelay for standalone deployment (without Docker)

.DESCRIPTION
    This script builds and publishes the TokenRelay application for various deployment scenarios:
    - Self-contained deployment (includes .NET runtime)
    - Framework-dependent deployment (requires .NET runtime installed)
    - Platform-specific builds (Windows, Linux, macOS)

.PARAMETER Runtime
    Target runtime identifier (e.g., win-x64, linux-x64, osx-x64)

.PARAMETER SelfContained
    Whether to create a self-contained deployment (includes .NET runtime)

.PARAMETER Configuration
    Build configuration (Debug, Release)

.PARAMETER OutputPath
    Output directory for published files

.EXAMPLE
    .\publish-standalone.ps1 -Runtime win-x64 -SelfContained $true
    .\publish-standalone.ps1 -Runtime linux-x64 -SelfContained $false -Configuration Release
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$Runtime = "portable",
    
    [Parameter(Mandatory = $false)]
    [bool]$SelfContained = $false,
    
    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "./publish"
)

# Set error action preference
$ErrorActionPreference = "Stop"

Write-Host "🚀 TokenRelay Standalone Publisher" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

# Determine project directory
$ProjectDir = Join-Path $PSScriptRoot "TokenRelay"
$OutputDir = if ($Runtime -eq "portable") { Join-Path $OutputPath "portable" } else { Join-Path $OutputPath $Runtime }

Write-Host "📁 Project Directory: $ProjectDir" -ForegroundColor Cyan
Write-Host "📁 Output Directory: $OutputDir" -ForegroundColor Cyan
Write-Host "⚙️  Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "🎯 Runtime: $Runtime" -ForegroundColor Cyan
Write-Host "📦 Self-Contained: $SelfContained" -ForegroundColor Cyan

# Clean output directory
if (Test-Path $OutputDir) {
    Write-Host "🧹 Cleaning output directory..." -ForegroundColor Yellow
    Remove-Item $OutputDir -Recurse -Force
}

# Build publish command
$PublishArgs = @(
    "publish"
    $ProjectDir
    "--configuration", $Configuration
    "--output", $OutputDir
    "--verbosity", "minimal"
)

if ($Runtime -ne "portable") {
    $PublishArgs += "--runtime", $Runtime
}

if ($SelfContained) {
    $PublishArgs += "--self-contained", "true"
} else {
    $PublishArgs += "--self-contained", "false"
}

# Execute publish
Write-Host "🔨 Publishing application..." -ForegroundColor Yellow
try {
    & dotnet @PublishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed with exit code $LASTEXITCODE"
    }
} catch {
    Write-Host "❌ Publish failed: $_" -ForegroundColor Red
    exit 1
}

# Copy configuration template
Write-Host "📋 Copying configuration template..." -ForegroundColor Yellow
$ConfigTemplate = Join-Path $PSScriptRoot "docker/config/tokenrelay.template.json"
$ConfigDestination = Join-Path $OutputDir "tokenrelay.template.json"

if (Test-Path $ConfigTemplate) {
    Copy-Item $ConfigTemplate $ConfigDestination
} else {
    Write-Host "⚠️  Configuration template not found at $ConfigTemplate" -ForegroundColor Yellow
}

# Create directories
Write-Host "📁 Creating necessary directories..." -ForegroundColor Yellow
$UploadDir = Join-Path $OutputDir "uploads"
$LogDir = Join-Path $OutputDir "logs"
New-Item -ItemType Directory -Path $UploadDir -Force | Out-Null
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

# Create startup scripts
Write-Host "📜 Creating startup scripts..." -ForegroundColor Yellow

# Determine the command to start the application
if ($SelfContained -or $Runtime -ne "portable") {
    $WindowsStartCommand = "TokenRelay.exe"
    $UnixStartCommand = "./TokenRelay"
    $UnixMakeExecutable = "chmod +x TokenRelay"
} else {
    $WindowsStartCommand = "dotnet TokenRelay.dll"
    $UnixStartCommand = "dotnet TokenRelay.dll"
    $UnixMakeExecutable = ""
}

# Windows startup script
$WindowsStartupScript = @"
@echo off
echo Starting TokenRelay...
echo.

REM Set environment variables
set ASPNETCORE_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://localhost:5163;https://localhost:7102
set ConfigPath=tokenrelay.json

REM Check if configuration exists
if not exist "tokenrelay.json" (
    echo Warning: tokenrelay.json not found. Copy from tokenrelay.template.json and configure.
    echo.
    if exist "tokenrelay.template.json" (
        echo Template found. Would you like to copy it? [Y/N]
        set /p choice=
        if /i "%choice%"=="Y" (
            copy "tokenrelay.template.json" "tokenrelay.json"
            echo Configuration template copied. Please edit tokenrelay.json before starting.
            pause
            exit /b 1
        )
    )
    echo.
    echo Please create tokenrelay.json configuration file.
    pause
    exit /b 1
)

REM Start the application
$WindowsStartCommand
"@

$WindowsStartupPath = Join-Path $OutputDir "start.bat"
$WindowsStartupScript | Out-File -FilePath $WindowsStartupPath -Encoding ASCII

# Linux/macOS startup script
$UnixStartupScript = @"
#!/bin/bash
echo "Starting TokenRelay..."
echo

# Set environment variables
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://localhost:5163;https://localhost:7102
export ConfigPath=tokenrelay.json

# Check if configuration exists
if [ ! -f "tokenrelay.json" ]; then
    echo "Warning: tokenrelay.json not found. Copy from tokenrelay.template.json and configure."
    echo
    if [ -f "tokenrelay.template.json" ]; then
        echo "Template found. Would you like to copy it? [y/N]"
        read -r choice
        if [[ `$choice =~ ^[Yy]`$ ]]; then
            cp "tokenrelay.template.json" "tokenrelay.json"
            echo "Configuration template copied. Please edit tokenrelay.json before starting."
            exit 1
        fi
    fi
    echo
    echo "Please create tokenrelay.json configuration file."
    exit 1
fi

# Make executable if self-contained
$UnixMakeExecutable

# Start the application
$UnixStartCommand
"@

$UnixStartupPath = Join-Path $OutputDir "start.sh"
$UnixStartupScript | Out-File -FilePath $UnixStartupPath -Encoding UTF8

# Create service files for systemd (Linux)
if ($Runtime -like "linux*" -or $Runtime -eq "portable") {
    Write-Host "🐧 Creating systemd service file..." -ForegroundColor Yellow
    
    # Determine ExecStart command for systemd
    if ($SelfContained -and $Runtime -like "linux*") {
        $SystemdExecStart = "/opt/tokenrelay/TokenRelay"
    } else {
        $SystemdExecStart = "/usr/bin/dotnet /opt/tokenrelay/TokenRelay.dll"
    }
    
    $ServiceFile = @"
[Unit]
Description=TokenRelay Proxy Service
Documentation=https://github.com/your-org/TokenRelay
After=network.target

[Service]
Type=notify
TimeoutStartSec=30
TimeoutStopSec=30
User=tokenrelay
Group=tokenrelay
WorkingDirectory=/opt/tokenrelay
ExecStart=$SystemdExecStart
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=tokenrelay
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5163
Environment=ConfigPath=/opt/tokenrelay/tokenrelay.json

[Install]
WantedBy=multi-user.target
"@

    $ServicePath = Join-Path $OutputDir "tokenrelay.service"
    $ServiceFile | Out-File -FilePath $ServicePath -Encoding UTF8
}

# Create Windows service install script
if ($Runtime -like "win*" -or $Runtime -eq "portable") {
    Write-Host "🪟 Creating Windows service script..." -ForegroundColor Yellow
    
    # Determine the service binPath
    if ($SelfContained -or $Runtime -ne "portable") {
        $ServiceBinPath = 'sc create TokenRelay binPath= "\"%~dp0TokenRelay.exe\"" start= auto'
    } else {
        $ServiceBinPath = 'sc create TokenRelay binPath= "\"%~dp0dotnet.exe\" \"%~dp0TokenRelay.dll\"" start= auto'
    }
    
    $WindowsServiceScript = @"
@echo off
echo TokenRelay Windows Service Management
echo ===================================
echo.

if "%1"=="install" goto install
if "%1"=="uninstall" goto uninstall
if "%1"=="start" goto start
if "%1"=="stop" goto stop

echo Usage: service.bat [install^|uninstall^|start^|stop]
echo.
echo install   - Install TokenRelay as Windows service
echo uninstall - Remove TokenRelay service
echo start     - Start TokenRelay service  
echo stop      - Stop TokenRelay service
goto end

:install
echo Installing TokenRelay service...
$ServiceBinPath
sc description TokenRelay "TokenRelay Proxy Service"
echo Service installed. Use 'service.bat start' to start it.
goto end

:uninstall
echo Stopping and removing TokenRelay service...
sc stop TokenRelay
sc delete TokenRelay
echo Service removed.
goto end

:start
echo Starting TokenRelay service...
sc start TokenRelay
goto end

:stop
echo Stopping TokenRelay service...
sc stop TokenRelay
goto end

:end
pause
"@

    $WindowsServicePath = Join-Path $OutputDir "service.bat"
    $WindowsServiceScript | Out-File -FilePath $WindowsServicePath -Encoding ASCII
}

# Create README for this deployment
Write-Host "📖 Creating deployment README..." -ForegroundColor Yellow

# Helper function to create README content
function New-ReadmeContent {
    param($Runtime, $SelfContained, $Configuration)
    
    $content = @"
# TokenRelay Standalone Deployment

This directory contains a standalone deployment of TokenRelay.

## Configuration
- **Runtime**: $Runtime
- **Self-Contained**: $SelfContained
- **Configuration**: $Configuration

## Quick Start

### 1. Configure the application
Copy tokenrelay.template.json to tokenrelay.json and edit it with your settings.

### 2. Start the application

"@

    # Add Windows instructions
    if ($Runtime -like "win*" -or $Runtime -eq "portable") {
        $content += @"
**Windows:**
```
start.bat
```

**Windows Service:**
```
service.bat install
service.bat start
```

"@
    }

    # Add Linux/macOS instructions
    if ($Runtime -like "linux*" -or $Runtime -like "osx*" -or $Runtime -eq "portable") {
        $content += @"
**Linux/macOS:**
```
chmod +x start.sh
./start.sh
```

**Linux systemd service:**
```
sudo cp tokenrelay.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable tokenrelay
sudo systemctl start tokenrelay
```

"@
    }

    $content += @"
## Default URLs
- HTTP: http://localhost:5163
- HTTPS: https://localhost:7102
- Swagger UI: http://localhost:5163 or https://localhost:7102
- Health Check: http://localhost:5163/health

## Requirements
"@

    # Add requirements
    if ($SelfContained) {
        $content += "`n✅ **Self-contained deployment** - No additional requirements needed`n"
    } else {
        $content += @"

📦 **.NET Runtime Required:**
- .NET 8.0 Runtime or later
- Download from: https://dotnet.microsoft.com/download/dotnet/8.0
"@
    }

    $content += @"

## Directory Structure
- uploads/ - File upload storage
- logs/ - Application logs (if file logging is configured)
- tokenrelay.json - Main configuration file
- tokenrelay.template.json - Configuration template

## Support
For issues and documentation, visit: https://github.com/your-org/TokenRelay
"@

    return $content
}

$ReadmeContent = New-ReadmeContent -Runtime $Runtime -SelfContained $SelfContained -Configuration $Configuration
$DeploymentReadmePath = Join-Path $OutputDir "README.md"
$ReadmeContent | Out-File -FilePath $DeploymentReadmePath -Encoding UTF8

Write-Host "✅ Publication completed successfully!" -ForegroundColor Green
Write-Host "📁 Output location: $OutputDir" -ForegroundColor Green
Write-Host "" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Copy the contents of '$OutputDir' to your target server" -ForegroundColor White
Write-Host "2. Create tokenrelay.json from the template" -ForegroundColor White
Write-Host "3. Run the appropriate startup script" -ForegroundColor White
Write-Host "" -ForegroundColor Green

# Optionally open output directory
if ($IsWindows -or $env:OS -eq "Windows_NT") {
    $OpenChoice = Read-Host "Open output directory? [Y/n]"
    if ($OpenChoice -ne "n" -and $OpenChoice -ne "N") {
        Start-Process "explorer.exe" -ArgumentList $OutputDir
    }
}
