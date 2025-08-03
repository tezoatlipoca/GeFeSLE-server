# PowerShell script to uninstall GeFeSLE Server Windows Service
# Run this script as Administrator

param(
    [Parameter(Mandatory=$false)]
    [string]$ServiceName = "GeFeSLE-Server"
)

# Check if running as Administrator
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "This script requires Administrator privileges. Please run as Administrator." -ForegroundColor Red
    exit 1
}

# Check if service exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existingService) {
    Write-Host "Service '$ServiceName' does not exist." -ForegroundColor Yellow
    exit 0
}

Write-Host "Stopping and removing Windows Service '$ServiceName'..." -ForegroundColor Yellow

# Stop the service if it's running
if ($existingService.Status -eq "Running") {
    Write-Host "Stopping service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 3
}

# Delete the service
Write-Host "Removing service..." -ForegroundColor Yellow
$result = sc.exe delete $ServiceName

if ($LASTEXITCODE -eq 0) {
    Write-Host "Service '$ServiceName' has been successfully removed!" -ForegroundColor Green
} else {
    Write-Host "Failed to remove service. Error code: $LASTEXITCODE" -ForegroundColor Red
    exit 1
}
