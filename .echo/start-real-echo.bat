@echo off
cls
echo =====================================================
echo   ECHO - REAL HUMAN INTERFACE
echo =====================================================
echo.
echo Starting backend with natural voice...
echo.

start "ECHO Backend" python .echo\echo-real.py

echo Waiting for backend to start...
timeout /t 4 /nobreak > nul

echo.
echo Opening face interface...
echo.
echo =====================================================
echo   TALK TO ME - I'M ALWAYS LISTENING!
echo   Natural human voice powered by Edge TTS
echo =====================================================
echo.

start "" ".echo\echo-face.html"
