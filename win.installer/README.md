# GeFeSLE Server Windows Installer

This directory contains the Windows installer and related files for the GeFeSLE Server.

## Files

- `install.nsi` - NSIS installer script
- `install-service.ps1` - PowerShell script to install GeFeSLE as a Windows Service
- `uninstall-service.ps1` - PowerShell script to uninstall the Windows Service
- `path.ps1` - Utility script for PATH environment variable management
- `VersionCompare.nsh` - NSIS helper for version comparison
- `gefesleff.ico` - Application icon
- `gefesleff.png` - Application icon (PNG format)

## Building the Installer

The installer is automatically built by the GitHub Actions workflow when you create a release tag. However, you can build it manually:

### Prerequisites

1. **NSIS** (Nullsoft Scriptable Install System)
   - Download from: https://nsis.sourceforge.io/
   - Install to default location: `C:\Program Files (x86)\NSIS\`

2. **Build the application first:**
   ```cmd
   dotnet publish -c Release -r win-x64 --self-contained true
   ```

### Manual Build Steps

1. Copy the published files to the installer directory:
   ```cmd
   copy bin\Release\net8.0\win-x64\publish\GeFeSLE.exe win.installer\
   copy bin\Release\net8.0\win-x64\publish\*.dll win.installer\
   ```

2. Navigate to the installer directory:
   ```cmd
   cd win.installer
   ```

3. Build the installer:
   ```cmd
   "C:\Program Files (x86)\NSIS\makensis.exe" install.nsi
   ```

## Installation Options

After running the installer, users have several options:

### 1. Run as Regular Application
- Use the "GeFeSLE Server" shortcut in the Start Menu
- Server will run in console mode
- Default URL: http://localhost:5000

### 2. Install as Windows Service ⭐ **NEW: Built-in Service Support**
- Use the "Install as Windows Service" shortcut (requires Administrator privileges)
- The application now includes native Windows Service support via `Microsoft.Extensions.Hosting.WindowsServices`
- Service will start automatically on system boot
- Can be managed through Windows Services Manager

### 3. Manual Service Management
Users can also install the service manually using the provided PowerShell scripts:

```powershell
# Install as service (run as Administrator)
.\install-service.ps1

# Optional: Install with custom URL
.\install-service.ps1 -ListenUrl "http://localhost:8080"

# Uninstall service (run as Administrator)
.\uninstall-service.ps1
```

## ✅ Windows Service Implementation

**Important Update**: As of this version, GeFeSLE Server includes proper Windows Service support:

- ✅ **Native Service Support**: Uses `Microsoft.Extensions.Hosting.WindowsServices`
- ✅ **Proper Lifecycle Management**: Handles service start, stop, and shutdown events
- ✅ **Service Control Manager Integration**: Works correctly with Windows SCM
- ✅ **Logging Integration**: Service events are logged to Windows Event Log
- ✅ **Configuration Support**: Service can be configured with different URLs and options

This means the application can run both as:
1. **Console Application**: For development and testing
2. **Windows Service**: For production deployment

## Service Configuration

The Windows Service is configured with:
- **Service Name:** GeFeSLE-Server
- **Display Name:** GeFeSLE Server
- **Start Type:** Automatic
- **Recovery:** Restart on failure
- **Default URL:** http://localhost:5000

## Configuration Files

After installation, users need to:
1. Copy `config.SAMPLE.json` to `config.json`
2. Edit `config.json` with appropriate settings
3. Restart the service or application

## Uninstallation

The installer creates an uninstaller that:
- Stops and removes the Windows Service (if installed)
- Removes all application files
- Removes Start Menu shortcuts
- Removes registry entries

## Troubleshooting

### Service Won't Start
1. Check if port 5000 is already in use
2. Verify configuration files are properly set up
3. Check Windows Event Viewer for error details
4. Ensure the service account has proper permissions

### Permission Issues
- Service installation requires Administrator privileges
- Use "Run as Administrator" when installing the service

### Port Conflicts
- Modify the service command line in Services Manager to use a different port:
  `--urls=http://localhost:XXXX`
