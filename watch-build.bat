@echo off
REM ----------------------------------------------------------------------
REM  dotnet watch build
REM
REM  Starts a file watcher on Source\*.cs that triggers an incremental
REM  Release build whenever a C# file is saved. Build output drops into
REM  1.6\Assemblies\CreepjoinerTimeTraveler.dll, which is what RimWorld
REM  loads on startup.
REM
REM  Stop with Ctrl+C. RimWorld must be restarted manually to pick up
REM  changes (Harmony patches and Defs are loaded once at game start).
REM
REM  Note: while RimWorld is running, the .dll is locked. Builds during
REM  a live game will fail with "file in use" - close RimWorld first or
REM  ignore those failed builds and let the next one succeed.
REM ----------------------------------------------------------------------

title dotnet watch build

cd /d "%~dp0Source"
if errorlevel 1 (
    echo [watch-build] Source\ folder not found relative to this .bat file.
    pause
    exit /b 1
)

echo [watch-build] Watching Source\*.cs - press Ctrl+C to stop.
echo.

dotnet watch build -c Release

echo.
echo [watch-build] watcher exited.
@REM pause
