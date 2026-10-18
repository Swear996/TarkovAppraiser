@echo off
setlocal
cd /d "%~dp0"

rem ---------------------------------------------------------
rem  Tarkov Appraiser - build script
rem  false = small .exe, needs the .NET 8 Desktop Runtime (standard)
rem  true  = big .exe with .NET included, runs without anything installed
rem ---------------------------------------------------------
set SELF_CONTAINED=false

where dotnet >nul 2>nul
if errorlevel 1 goto nosdk

dotnet --list-sdks | findstr /b "8." >nul
if errorlevel 1 goto nosdk

rem Close the running program so the .exe can be replaced
taskkill /f /im TarkovAppraiser.exe >nul 2>nul
taskkill /f /im LootLens.exe >nul 2>nul
taskkill /f /im EFTPriceTooltip.exe >nul 2>nul
taskkill /f /im TarkovPriceOverlay.exe >nul 2>nul
timeout /t 1 /nobreak >nul
rem the program was renamed (EFT Price Tooltip -> Loot Lens -> Tarkov Appraiser): remove the old .exe
if exist "%~dp0dist\EFTPriceTooltip.exe" del /q "%~dp0dist\EFTPriceTooltip.exe"
if exist "%~dp0dist\LootLens.exe" del /q "%~dp0dist\LootLens.exe"

echo Building TarkovAppraiser.exe ...
echo.

dotnet publish TarkovPriceOverlay.csproj -c Release -r win-x64 --self-contained %SELF_CONTAINED% -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "%~dp0dist"
if errorlevel 1 goto failed

echo.
echo ============================================
echo  Done: %~dp0dist\TarkovAppraiser.exe
echo  Starting the program ...
echo ============================================
start "" "%~dp0dist\TarkovAppraiser.exe"
goto :eof

:nosdk
echo .NET 8 SDK was not found.
echo Please install it: https://dotnet.microsoft.com/download/dotnet/8.0
start https://dotnet.microsoft.com/download/dotnet/8.0
goto end

:failed
echo.
echo Build failed. Please copy the error message above and send it to me.

:end
echo.
pause
