# Customization Summary: GeFeSLE Server Windows Installer & CI/CD

This document summarizes the changes made to adapt the Windows installer and GitHub Actions workflow from the systray client to the GeFeSLE server application.

## Files Modified

### 1. `win.installer/install.nsi`
**Changes Made:**
- Changed application name from "GeFeSLE-systray" to "GeFeSLE Server"
- Updated executable name from `GeFeSLE-systray.exe` to `GeFeSLE.exe`
- Updated installer output filename to `GeFeSLE-Server-${VERSION}-setup.exe`
- Updated version to match project version (0.0.9)
- Added installation of web assets (wwwroot folder, appsettings.json, config.SAMPLE.json)
- Added PowerShell service management scripts to installation
- Modified Start Menu shortcuts to include service management options
- Removed automatic startup registry entry (inappropriate for a server)
- Added service cleanup to uninstaller
- Updated all registry keys and display names

### 2. `.github/workflows/release.yml`
**Changes Made:**
- Updated workflow name to "GeFeSLE Server Release Build"
- Changed all artifact names from `GeFeSLE-systray_` to `GeFeSLE-Server_`
- Updated executable references from `GeFeSLE-systray.exe` to `GeFeSLE.exe`
- Modified archive commands to include all necessary server files (wwwroot, config files)
- Updated Linux binary archive to include server executable
- Modified package names for DEB and RPM packages
- Updated all file patterns in release asset uploads

## Files Created

### 3. `win.installer/install-service.ps1`
**Purpose:** PowerShell script to install GeFeSLE Server as a Windows Service
**Features:**
- Administrator privilege checking
- Service conflict detection and resolution
- Configurable service parameters
- Service failure recovery configuration
- User-friendly status reporting

### 4. `win.installer/uninstall-service.ps1`
**Purpose:** PowerShell script to remove the Windows Service
**Features:**
- Administrator privilege checking
- Graceful service stopping
- Complete service removal
- Error handling and reporting

### 5. `win.installer/README.md`
**Purpose:** Comprehensive documentation for the Windows installer
**Content:**
- File descriptions
- Build instructions (manual and automated)
- Installation options
- Service configuration details
- Troubleshooting guide

### 6. `win.installer/build-installer.bat`
**Purpose:** Local development script for building the installer
**Features:**
- Automated build process
- Dependency checking (NSIS installation)
- Error handling and user feedback
- File copying automation

## Key Differences from Systray Version

### Application Type
- **Systray:** Desktop tray application with automatic startup
- **Server:** Web server application that can run as console app or Windows Service

### Installation Approach
- **Systray:** Simple executable with automatic startup
- **Server:** Choice between console application or Windows Service with management tools

### File Structure
- **Systray:** Single executable with minimal dependencies
- **Server:** Web application with static assets, configuration files, and database components

### User Experience
- **Systray:** Background application, minimal user interaction
- **Server:** Web-based interface, requires configuration, can be accessed remotely

## Usage Instructions

### For Developers
1. Use `build-installer.bat` for local testing
2. Push tags (e.g., `v0.0.9`) to trigger automated builds
3. Review GitHub Actions output for any build issues

### For End Users
1. Run the installer (`GeFeSLE-Server-X.X.X-setup.exe`)
2. Choose installation location
3. Use Start Menu shortcuts to:
   - Run server directly
   - Install as Windows Service (requires admin)
   - Access configuration folder
4. Configure `config.json` before first use

## Future Considerations

1. **Service Configuration UI:** Consider adding a GUI tool for service configuration
2. **Automatic Updates:** Implement update checking and notification system
3. **Configuration Wizard:** Create a setup wizard for initial configuration
4. **SSL Certificate Management:** Add tools for HTTPS certificate installation
5. **Backup/Restore:** Include database backup and restore utilities

## Testing Checklist

- [ ] Installer builds successfully via GitHub Actions
- [ ] Manual installer build works with `build-installer.bat`
- [ ] Service installation/uninstallation works correctly
- [ ] Start Menu shortcuts function properly
- [ ] Uninstaller removes all components cleanly
- [ ] Version updates work correctly
- [ ] Multiple architecture builds (win-x64, linux-x64) complete successfully
