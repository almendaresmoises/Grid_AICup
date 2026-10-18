@echo off
rem Values come from the repository .env; policy applies only to this process.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-cloudrun.ps1" %*
exit /b %errorlevel%
