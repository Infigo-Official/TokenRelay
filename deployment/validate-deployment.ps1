#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Validates TokenRelay deployment and configuration

.DESCRIPTION
    This script performs comprehensive validation of a TokenRelay deployment,
    checking configuration, connectivity, and basic functionality.

.PARAMETER BaseUrl
    Base URL for the TokenRelay service (default: http://localhost:5163)

.PARAMETER ConfigPath
    Path to the tokenrelay.json configuration file

.PARAMETER SkipConnectivity
    Skip connectivity tests to external services

.EXAMPLE
    .\validate-deployment.ps1
    .\validate-deployment.ps1 -BaseUrl "https://tokenrelay.example.com" -ConfigPath "/opt/tokenrelay/tokenrelay.json"
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$BaseUrl = "http://localhost:5163",
    
    [Parameter(Mandatory = $false)]
    [string]$ConfigPath = "tokenrelay.json",
    
    [Parameter(Mandatory = $false)]
    [switch]$SkipConnectivity
)

# Set error action preference
$ErrorActionPreference = "Continue"

Write-Host "🔍 TokenRelay Deployment Validator" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green
Write-Host ""

$ValidationResults = @()
$Errors = @()
$Warnings = @()

# Helper function to add validation result
function Add-ValidationResult {
    param(
        [string]$Test,
        [bool]$Passed,
        [string]$Message,
        [string]$Details = ""
    )
    
    $script:ValidationResults += [PSCustomObject]@{
        Test = $Test
        Passed = $Passed
        Message = $Message
        Details = $Details
    }
    
    if ($Passed) {
        Write-Host "✅ $Test`: $Message" -ForegroundColor Green
    } else {
        Write-Host "❌ $Test`: $Message" -ForegroundColor Red
        $script:Errors += "$Test`: $Message"
    }
    
    if ($Details) {
        Write-Host "   $Details" -ForegroundColor Gray
    }
}

# Helper function to add warning
function Add-Warning {
    param(
        [string]$Test,
        [string]$Message
    )
    
    Write-Host "⚠️ $Test`: $Message" -ForegroundColor Yellow
    $script:Warnings += "$Test`: $Message"
}

# Test 1: Configuration File
Write-Host "📋 Testing Configuration..." -ForegroundColor Cyan

try {
    if (Test-Path $ConfigPath) {
        $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
        Add-ValidationResult "Config File" $true "Configuration file found and readable"
        
        # Check required sections
        if ($config.proxy) {
            Add-ValidationResult "Config Structure" $true "Proxy configuration section present"
            
            # Check auth configuration
            if ($config.proxy.auth -and $config.proxy.auth.tokens) {
                Add-ValidationResult "Auth Config" $true "Authentication configuration present"
                
                if ($config.proxy.auth.tokens.Count -gt 0) {
                    Add-ValidationResult "Auth Tokens" $true "$($config.proxy.auth.tokens.Count) token(s) configured"
                } else {
                    Add-ValidationResult "Auth Tokens" $false "No authentication tokens configured"
                }
            } else {
                Add-ValidationResult "Auth Config" $false "Authentication configuration missing"
            }
            
            # Check targets
            if ($config.proxy.targets) {
                $targetCount = ($config.proxy.targets | Get-Member -MemberType NoteProperty).Count
                if ($targetCount -gt 0) {
                    Add-ValidationResult "Proxy Targets" $true "$targetCount target(s) configured"
                } else {
                    Add-ValidationResult "Proxy Targets" $false "No proxy targets configured"
                }
            } else {
                Add-ValidationResult "Proxy Targets" $false "No proxy targets section found"
            }
        } else {
            Add-ValidationResult "Config Structure" $false "Proxy configuration section missing"
        }
    } else {
        Add-ValidationResult "Config File" $false "Configuration file not found: $ConfigPath"
    }
} catch {
    Add-ValidationResult "Config File" $false "Error reading configuration: $($_.Exception.Message)"
}

Write-Host ""

# Test 2: Service Connectivity
Write-Host "🔗 Testing Service Connectivity..." -ForegroundColor Cyan

try {
    $healthResponse = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get -TimeoutSec 10
    Add-ValidationResult "Health Endpoint" $true "Service is responding to health checks"
    
    if ($healthResponse.status -eq "Healthy") {
        Add-ValidationResult "Health Status" $true "Service reports healthy status"
    } else {
        Add-ValidationResult "Health Status" $false "Service reports unhealthy status: $($healthResponse.status)"
    }
} catch {
    Add-ValidationResult "Health Endpoint" $false "Cannot connect to health endpoint: $($_.Exception.Message)"
}

try {
    $statusResponse = Invoke-RestMethod -Uri "$BaseUrl/status" -Method Get -TimeoutSec 10
    Add-ValidationResult "Status Endpoint" $true "Service status endpoint accessible"
    
    if ($statusResponse.version) {
        Add-ValidationResult "Service Version" $true "Service version: $($statusResponse.version)" "Build: $($statusResponse.build)"
    }
} catch {
    Add-ValidationResult "Status Endpoint" $false "Cannot access status endpoint: $($_.Exception.Message)"
}

# Test Swagger UI
try {
    $swaggerResponse = Invoke-WebRequest -Uri "$BaseUrl/" -Method Get -TimeoutSec 10
    if ($swaggerResponse.StatusCode -eq 200) {
        Add-ValidationResult "Swagger UI" $true "Swagger UI is accessible"
    } else {
        Add-ValidationResult "Swagger UI" $false "Swagger UI returned status: $($swaggerResponse.StatusCode)"
    }
} catch {
    Add-ValidationResult "Swagger UI" $false "Cannot access Swagger UI: $($_.Exception.Message)"
}

Write-Host ""

# Test 3: Authentication
Write-Host "🔐 Testing Authentication..." -ForegroundColor Cyan

try {
    # Test without token (should fail)
    try {
        Invoke-RestMethod -Uri "$BaseUrl/proxy/test" -Method Get -TimeoutSec 5 | Out-Null
        Add-ValidationResult "Auth Protection" $false "Service allowed access without authentication token"
    } catch {
        if ($_.Exception.Response.StatusCode -eq 401) {
            Add-ValidationResult "Auth Protection" $true "Service properly rejects requests without authentication"
        } else {
            Add-Warning "Auth Protection" "Unexpected response when testing authentication: $($_.Exception.Response.StatusCode)"
        }
    }
} catch {
    Add-Warning "Auth Test" "Could not test authentication: $($_.Exception.Message)"
}

Write-Host ""

# Test 4: Target Connectivity (if not skipped)
if (-not $SkipConnectivity -and $config -and $config.proxy.targets) {
    Write-Host "🌐 Testing Target Connectivity..." -ForegroundColor Cyan
    
    foreach ($targetName in ($config.proxy.targets | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name)) {
        $target = $config.proxy.targets.$targetName
        
        if ($target.endpoint) {
            try {
                $uri = [System.Uri]$target.endpoint
                $testUrl = "$($uri.Scheme)://$($uri.Host)"
                if ($uri.Port -and $uri.Port -ne 80 -and $uri.Port -ne 443) {
                    $testUrl += ":$($uri.Port)"
                }
                
                Invoke-WebRequest -Uri $testUrl -Method Head -TimeoutSec 10 -SkipCertificateCheck | Out-Null
                Add-ValidationResult "Target: $targetName" $true "Target is reachable" "Endpoint: $($target.endpoint)"
            } catch {
                Add-ValidationResult "Target: $targetName" $false "Target unreachable: $($_.Exception.Message)" "Endpoint: $($target.endpoint)"
            }
        } else {
            Add-ValidationResult "Target: $targetName" $false "No endpoint configured"
        }
    }
} else {
    Add-Warning "Target Connectivity" "Target connectivity tests skipped"
}

Write-Host ""

# Test 5: File System Permissions
Write-Host "📁 Testing File System..." -ForegroundColor Cyan

$uploadDir = Join-Path (Get-Location) "uploads"
$logDir = Join-Path (Get-Location) "logs"

if (Test-Path $uploadDir) {
    try {
        $testFile = Join-Path $uploadDir "test.tmp"
        "test" | Out-File $testFile
        Remove-Item $testFile -Force
        Add-ValidationResult "Upload Directory" $true "Upload directory is writable"
    } catch {
        Add-ValidationResult "Upload Directory" $false "Upload directory is not writable: $($_.Exception.Message)"
    }
} else {
    Add-ValidationResult "Upload Directory" $false "Upload directory does not exist: $uploadDir"
}

if (Test-Path $logDir) {
    try {
        $testFile = Join-Path $logDir "test.tmp"
        "test" | Out-File $testFile
        Remove-Item $testFile -Force
        Add-ValidationResult "Log Directory" $true "Log directory is writable"
    } catch {
        Add-ValidationResult "Log Directory" $false "Log directory is not writable: $($_.Exception.Message)"
    }
} else {
    Add-Warning "Log Directory" "Log directory does not exist: $logDir"
}

Write-Host ""

# Test 6: System Resources
Write-Host "💻 Testing System Resources..." -ForegroundColor Cyan

try {
    $process = Get-Process | Where-Object { $_.ProcessName -like "*TokenRelay*" -or $_.ProcessName -like "*dotnet*" }
    if ($process) {
        $totalMemory = ($process | Measure-Object WorkingSet -Sum).Sum / 1MB
        Add-ValidationResult "Process Detection" $true "TokenRelay process(es) found" "Memory usage: $([math]::Round($totalMemory, 2)) MB"
    } else {
        Add-Warning "Process Detection" "No TokenRelay processes found (service might not be running)"
    }
} catch {
    Add-Warning "Process Detection" "Could not detect processes: $($_.Exception.Message)"
}

# Check available disk space
try {
    $currentDrive = (Get-Location).Drive
    $freeSpace = $currentDrive.Free / 1GB
    if ($freeSpace -gt 1) {
        Add-ValidationResult "Disk Space" $true "Sufficient disk space available" "$([math]::Round($freeSpace, 2)) GB free"
    } else {
        Add-ValidationResult "Disk Space" $false "Low disk space" "Only $([math]::Round($freeSpace, 2)) GB free"
    }
} catch {
    Add-Warning "Disk Space" "Could not check disk space: $($_.Exception.Message)"
}

Write-Host ""

# Summary
Write-Host "📊 Validation Summary" -ForegroundColor Cyan
Write-Host "===================" -ForegroundColor Cyan

$passedTests = ($ValidationResults | Where-Object { $_.Passed }).Count
$totalTests = $ValidationResults.Count
$failedTests = $totalTests - $passedTests

Write-Host "Total Tests: $totalTests" -ForegroundColor White
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $failedTests" -ForegroundColor Red
Write-Host "Warnings: $($Warnings.Count)" -ForegroundColor Yellow

if ($failedTests -eq 0) {
    Write-Host ""
    Write-Host "🎉 All validation tests passed!" -ForegroundColor Green
    Write-Host "TokenRelay deployment appears to be working correctly." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "❌ Some validation tests failed." -ForegroundColor Red
    Write-Host "Please review the errors above and fix the issues." -ForegroundColor Red
}

if ($Warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "⚠️ Warnings detected:" -ForegroundColor Yellow
    foreach ($warning in $Warnings) {
        Write-Host "  - $warning" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "📝 Next Steps:" -ForegroundColor Cyan
if ($failedTests -gt 0) {
    Write-Host "1. Fix the failed tests listed above" -ForegroundColor White
    Write-Host "2. Check service logs for additional details" -ForegroundColor White
    Write-Host "3. Verify configuration file syntax and values" -ForegroundColor White
    Write-Host "4. Re-run this validation script" -ForegroundColor White
} else {
    Write-Host "1. Review warnings (if any) and address as needed" -ForegroundColor White
    Write-Host "2. Test with real API calls using valid tokens" -ForegroundColor White
    Write-Host "3. Monitor service performance and logs" -ForegroundColor White
    Write-Host "4. Set up monitoring and alerting for production" -ForegroundColor White
}

Write-Host ""
Write-Host "🔗 Useful Commands:" -ForegroundColor Cyan
Write-Host "- Check service status: systemctl status tokenrelay (Linux) or Get-Service TokenRelay (Windows)" -ForegroundColor White
Write-Host "- View logs: journalctl -u tokenrelay -f (Linux) or check Event Viewer (Windows)" -ForegroundColor White
Write-Host "- Test API: curl $BaseUrl/health" -ForegroundColor White
Write-Host "- View configuration: cat $ConfigPath" -ForegroundColor White

# Exit with appropriate code
if ($failedTests -gt 0) {
    exit 1
} else {
    exit 0
}
