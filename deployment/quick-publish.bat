@echo off
echo TokenRelay Quick Publisher
echo =========================
echo.

REM Check if dotnet is available
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Error: .NET SDK not found. Please install .NET 10.0 SDK or later.
    echo Download from: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

echo Select deployment type:
echo 1. Windows Self-Contained (Optimized - Recommended for production)
echo 2. Windows Framework-Dependent (Optimized - Requires .NET runtime)
echo 3. Linux Self-Contained (Optimized)
echo 4. Linux Framework-Dependent (Optimized - Requires .NET runtime)
echo 5. Portable (Any platform with .NET runtime)
echo 6. Advanced (Use PowerShell script directly)
echo.

set /p choice="Enter your choice (1-6): "

set "runtime="
set "selfcontained=false"
set "useprofile=true"

if "%choice%"=="1" (
    set "runtime=win-x64"
    set "selfcontained=true"
) else if "%choice%"=="2" (
    set "runtime=win-x64"
    set "selfcontained=false"
) else if "%choice%"=="3" (
    set "runtime=linux-x64"
    set "selfcontained=true"
) else if "%choice%"=="4" (
    set "runtime=linux-x64"
    set "selfcontained=false"
) else if "%choice%"=="5" (
    set "runtime=portable"
    set "selfcontained=false"
    set "useprofile=false"
) else if "%choice%"=="6" (
    echo.
    echo Run: .\publish-standalone.ps1 -Runtime [runtime] -SelfContained $[true/false] -UsePublishProfile
    pause
    exit /b 0
) else (
    echo Invalid choice. Exiting.
    pause
    exit /b 1
)

echo.
echo Publishing with:
echo - Runtime: %runtime%
echo - Self-Contained: %selfcontained%
echo - Optimized: %useprofile%
echo.

REM Call PowerShell script
if "%useprofile%"=="true" (
    powershell.exe -ExecutionPolicy Bypass -File "publish-standalone.ps1" -Runtime "%runtime%" -SelfContained $%selfcontained% -UsePublishProfile
) else (
    powershell.exe -ExecutionPolicy Bypass -File "publish-standalone.ps1" -Runtime "%runtime%" -SelfContained $%selfcontained%
)

if %errorlevel% neq 0 (
    echo.
    echo Publication failed. Check the output above for errors.
    pause
    exit /b 1
)

echo.
echo Publication completed successfully!
echo Check the 'publish' folder for the output.
pause
