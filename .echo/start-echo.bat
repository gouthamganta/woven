@echo off
cls
echo.
echo =====================================================
echo    ECHO - PIXEL 3D CHARACTER (FAST VERSION)
echo =====================================================
echo.
echo [1] Testing microphone first...
echo.

python .echo\test-mic.py

echo.
echo =====================================================
echo    Did you see bars when you talked? (Y/N)
echo =====================================================
set /p response="> "

if /i "%response%"=="N" (
    echo.
    echo ERROR: Microphone not working!
    echo Check Windows sound settings.
    pause
    exit
)

echo.
echo [2] Starting ECHO backend...
echo.

start "ECHO Backend" python .echo\echo-fast.py

timeout /t 3 /nobreak > nul

echo [3] Opening pixel character interface...
echo.
echo =====================================================
echo   TALK NOW - WATCH THE MIC LEVEL BAR!
echo   Character changes color based on mood
echo =====================================================
echo.

start "" ".echo\echo-pixel.html"
