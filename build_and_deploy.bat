@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   Oxygen Not Included Mod Build ^& Deploy Automation
echo ===================================================
echo.

:: 1. Navigate and Build C# Mod
echo [1/3] Building C# Extractor Mod...
cd /d "%~dp0extractor"
dotnet build
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] dotnet build failed! Please check the compilation errors above.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo [SUCCESS] Mod built successfully!
echo.

:: 2. Determine target directories (Checks standard Documents and OneDrive Documents)
echo [2/3] Locating Oxygen Not Included mods folder...
set "TARGET_DIR=%USERPROFILE%\Documents\Klei\OxygenNotIncluded\mods\Local\DataNotIncluded"

if not exist "%USERPROFILE%\Documents\Klei\OxygenNotIncluded" (
    if exist "%USERPROFILE%\OneDrive\Documents\Klei\OxygenNotIncluded" (
        set "TARGET_DIR=%USERPROFILE%\OneDrive\Documents\Klei\OxygenNotIncluded\mods\Local\DataNotIncluded"
        echo [INFO] Found ONI directory in OneDrive folder.
    )
)

echo [INFO] Target Directory: !TARGET_DIR!
echo.

:: 3. Create target directory if it doesn't exist
if not exist "!TARGET_DIR!" (
    echo [INFO] Creating directory: !TARGET_DIR!
    mkdir "!TARGET_DIR!"
)

:: 4. Copy build artifacts
echo [3/3] Copying dll and mod_info.yaml...
copy /Y "%~dp0extractor\bin\Debug\net48\DataNotIncluded.dll" "!TARGET_DIR!\" >nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to copy DataNotIncluded.dll to local mods!
    pause
    exit /b %ERRORLEVEL%
)

copy /Y "%~dp0extractor\mod_info.yaml" "!TARGET_DIR!\" >nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Failed to copy mod_info.yaml to local mods!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo   [COMPLETE] Mod successfully built and deployed!
echo   Launch Oxygen Not Included to update your data.
echo ===================================================
echo.
pause
