@echo off
echo Building GeFeSLE Server for Windows...

REM Build the application
echo.
echo Step 1: Building application...
dotnet publish -c Release -r win-x64 --self-contained true

if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)

REM Copy files to installer directory
echo.
echo Step 2: Copying files to installer directory...
copy "bin\Release\net8.0\win-x64\publish\GeFeSLE.exe" "win.installer\" /Y
copy "bin\Release\net8.0\win-x64\publish\*.dll" "win.installer\" /Y

REM Check if NSIS is installed
echo.
echo Step 3: Checking for NSIS...
if not exist "C:\Program Files (x86)\NSIS\makensis.exe" (
    echo NSIS not found at C:\Program Files ^(x86^)\NSIS\makensis.exe
    echo Please install NSIS from https://nsis.sourceforge.io/
    pause
    exit /b 1
)

REM Build installer
echo.
echo Step 4: Building installer...
pushd win.installer
"C:\Program Files (x86)\NSIS\makensis.exe" install.nsi
popd

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ===============================================
    echo Build completed successfully!
    echo Installer created: win.installer\GeFeSLE-Server-0.0.9-setup.exe
    echo ===============================================
) else (
    echo.
    echo Installer build failed!
)

pause
