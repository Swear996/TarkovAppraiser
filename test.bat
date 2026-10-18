@echo off
setlocal
cd /d "%~dp0"

rem ---------------------------------------------------------
rem  Tarkov Appraiser - tests
rem  1. Numbers: stack counts, armor durability and key uses are read
rem     from the pictures in Tests\images (Tests\digits.json).
rem  2. Item names: replays the OCR readings in Tests\cases.json against
rem     the saved price list and checks that the right item is found.
rem  Run it after every change to the recognition.
rem ---------------------------------------------------------

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET 8 SDK was not found.
    goto end
)

dotnet run --project Tests\MatcherTests.csproj -c Release
if errorlevel 2 goto end
if errorlevel 1 (
    echo.
    echo Some tests FAILED - please send me the output above.
)

:end
echo.
pause
