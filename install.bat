@echo off
REM Thin wrapper. The real logic is in install.ps1, which publishes into C:\TKHN.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
exit /b %ERRORLEVEL%
