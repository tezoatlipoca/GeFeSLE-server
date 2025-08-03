# PowerShell script to install GeFeSLE Server as a Windows Service
# Run this script as Administrator
# 
# Note: As of this version, GeFeSLE Server now includes built-in Windows Service support
# using Microsoft.Extensions.Hosting.WindowsServices

param(
    [Parameter(Mandatory=$false)]
    [string]$InstallPath = "$env:ProgramFiles\GeFeSLE-Server",
    
    [Parameter(Mandatory=$false)]
    [string]$ServiceName = "GeFeSLE-Server",
    
    [Parameter(Mandatory=$false)]
    [string]$DisplayName = "GeFeSLE Server",
    
    [Parameter(Mandatory=$false)]
    [string]$Description = "GeFeSLE bookmark and list management web server",
    
    [Parameter(Mandatory=$false)]
    [string]$ListenUrl = "http://localhost:5000"
)

# Check if running as Administrator
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "This script requires Administrator privileges. Please run as Administrator." -ForegroundColor Red
    exit 1
}

$ExePath = Join-Path $InstallPath "GeFeSLE.exe"

# Check if the executable exists
if (-not (Test-Path $ExePath)) {
    Write-Host "GeFeSLE.exe not found at $ExePath" -ForegroundColor Red
    Write-Host "Please ensure GeFeSLE Server is properly installed." -ForegroundColor Red
    exit 1
}

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service '$ServiceName' already exists. Stopping and removing..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# Create the service
# Note: GeFeSLE now has built-in Windows Service support, so we can pass --urls directly
Write-Host "Creating Windows Service '$DisplayName'..." -ForegroundColor Green
$binPath = "$ExePath --urls=$ListenUrl"
$result = sc.exe create $ServiceName binPath= $binPath DisplayName= "$DisplayName" start= auto

if ($LASTEXITCODE -eq 0) {
    Write-Host "Service created successfully!" -ForegroundColor Green
    
    # Set service description
    sc.exe description $ServiceName "$Description"
    
    # Configure service to restart on failure
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000
    
    Write-Host "Starting service..." -ForegroundColor Green
    Start-Service -Name $ServiceName
    
    $service = Get-Service -Name $ServiceName
    Write-Host "Service Status: $($service.Status)" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "GeFeSLE Server has been installed as a Windows Service!" -ForegroundColor Green
    Write-Host "Service Name: $ServiceName" -ForegroundColor Cyan
    Write-Host "Display Name: $DisplayName" -ForegroundColor Cyan
    Write-Host "Executable: $ExePath" -ForegroundColor Cyan
    Write-Host "Listen URL: $ListenUrl" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "You can manage the service using:" -ForegroundColor Yellow
    Write-Host "  - Services.msc (Windows Services Manager)" -ForegroundColor Yellow
    Write-Host "  - net start $ServiceName" -ForegroundColor Yellow
    Write-Host "  - net stop $ServiceName" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To uninstall the service, run: sc delete $ServiceName" -ForegroundColor Yellow
    Write-Host "Or use the 'Uninstall Service' shortcut from the Start Menu" -ForegroundColor Yellow
} else {
    Write-Host "Failed to create service. Error code: $LASTEXITCODE" -ForegroundColor Red
    exit 1
}
