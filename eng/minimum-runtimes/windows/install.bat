@echo off
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0qualify.ps1"
exit /b %ERRORLEVEL%
