# TokenRelay Docker Deployment Script for Windows
param(
    [Parameter(Position=0)]
    [ValidateSet("start", "start-nginx", "stop", "restart", "logs", "status", "cleanup", "setup")]
    [string]$Command = "help"
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectRoot = Split-Path -Parent $ScriptDir

# Functions
function Write-Info($Message) {
    Write-Host "[INFO] $Message" -ForegroundColor Blue
}

function Write-Success($Message) {
    Write-Host "[SUCCESS] $Message" -ForegroundColor Green
}

function Write-Warning($Message) {
    Write-Host "[WARNING] $Message" -ForegroundColor Yellow
}

function Write-Error($Message) {
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Test-Prerequisites {
    Write-Info "Checking prerequisites..."
    
    try {
        docker --version | Out-Null
    } catch {
        Write-Error "Docker is not installed or not in PATH"
        exit 1
    }
    
    try {
        docker-compose --version | Out-Null
    } catch {
        Write-Error "Docker Compose is not installed or not in PATH"
        exit 1
    }
    
    Write-Success "Prerequisites check passed"
}

function Initialize-Environment {
    Write-Info "Setting up environment..."
    
    Set-Location $ScriptDir
    
    if (-not (Test-Path ".env")) {
        if (Test-Path "env.template") {
            Copy-Item "env.template" ".env"
            Write-Success "Created .env file from template"
            Write-Warning "Please edit .env file with your configuration before proceeding"
            return $false
        } else {
            Write-Error "env.template not found"
            exit 1
        }
    }
    
    # Create necessary directories
    $null = New-Item -ItemType Directory -Force -Path "uploads", "logs"
    
    # Check for configuration file
    $configPath = "./config/tokenrelay.json"
    if (-not (Test-Path $configPath)) {
        Write-Warning "Configuration file not found: $configPath"
        
        if (Test-Path "./config/tokenrelay.template.json") {
            Write-Info "Template configuration file found."
            Write-Warning "SECURITY NOTICE:"
            Write-Host "  The Docker image does NOT include tokenrelay.json for security reasons." -ForegroundColor Red
            Write-Host "  You must create your own configuration file with proper credentials." -ForegroundColor Red
            Write-Host ""
            Write-Host "Options:" -ForegroundColor Cyan
            Write-Host "  1. Copy template: Copy-Item './config/tokenrelay.template.json' '$configPath'" -ForegroundColor Gray
            Write-Host "  2. Use environment config: Set TOKENRELAY_CONFIG_MODE=env in .env" -ForegroundColor Gray
            Write-Host ""
            
            $choice = Read-Host "Copy template configuration? (y/N)"
            if ($choice -eq 'y' -or $choice -eq 'Y') {
                Copy-Item "./config/tokenrelay.template.json" $configPath
                Write-Success "Copied template configuration to $configPath"
                Write-Warning "IMPORTANT: Edit $configPath with your actual credentials and endpoints!"
                return $false  # Don't proceed automatically, let user edit first
            } else {
                Write-Warning "Please create $configPath or use environment-based configuration"
                return $false
            }
        } else {
            Write-Error "No configuration template found"
            return $false
        }
    }
    
    Write-Success "Environment setup completed"
    return $true
}

function Start-Services($WithNginx = $false) {
    Write-Info "Building and starting TokenRelay services..."
    
    Set-Location $ScriptDir
    
    if ($WithNginx) {
        docker-compose --profile with-nginx up -d --build
        Write-Success "TokenRelay started with Nginx reverse proxy"
        Write-Info "Access URLs:"
        Write-Info "  - TokenRelay: http://localhost:5163"
        Write-Info "  - Nginx Proxy: http://localhost:8080"
    } else {
        docker-compose up -d --build
        Write-Success "TokenRelay started"
        Write-Info "Access URL: http://localhost:5163"
    }
    
    Write-Info "Health check: http://localhost:5163/health"
}

function Stop-Services {
    Write-Info "Stopping TokenRelay services..."
    
    Set-Location $ScriptDir
    docker-compose down
    
    Write-Success "TokenRelay services stopped"
}

function Show-Logs {
    Set-Location $ScriptDir
    docker-compose logs -f tokenrelay
}

function Show-Status {
    Set-Location $ScriptDir
    docker-compose ps
    
    Write-Info "Testing health endpoint..."
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5163/health" -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            Write-Success "TokenRelay is healthy"
        } else {
            Write-Warning "TokenRelay health check returned status: $($response.StatusCode)"
        }
    } catch {
        Write-Warning "TokenRelay health check failed: $($_.Exception.Message)"
    }
}

function Invoke-Cleanup {
    Write-Info "Cleaning up TokenRelay deployment..."
    
    Set-Location $ScriptDir
    docker-compose down -v --rmi local
    docker system prune -f
    
    Write-Success "Cleanup completed"
}

function Show-Usage {
    Write-Host @"
Usage: .\deploy.ps1 [COMMAND]

Commands:
  start         Start TokenRelay (without Nginx)
  start-nginx   Start TokenRelay with Nginx reverse proxy
  stop          Stop TokenRelay services
  restart       Restart TokenRelay services
  logs          Show TokenRelay logs (follow mode)
  status        Show service status and health
  cleanup       Remove containers, volumes, and images
  setup         Initial setup (create .env file)
"@
}

# Main script logic
switch ($Command) {
    "start" {
        Test-Prerequisites
        if (Initialize-Environment) {
            Start-Services $false
        }
    }
    "start-nginx" {
        Test-Prerequisites
        if (Initialize-Environment) {
            Start-Services $true
        }
    }
    "stop" {
        Stop-Services
    }
    "restart" {
        Stop-Services
        Test-Prerequisites
        if (Initialize-Environment) {
            Start-Services $false
        }
    }
    "logs" {
        Show-Logs
    }
    "status" {
        Show-Status
    }
    "cleanup" {
        Invoke-Cleanup
    }
    "setup" {
        Initialize-Environment
    }
    default {
        Show-Usage
        exit 1
    }
}
