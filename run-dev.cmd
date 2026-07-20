@echo off
rem Starts the DotFlow API and Blazor UI together for local development.
rem Delegates to run-dev.ps1 so a single Ctrl-C cleanly stops both.
rem
rem Usage:
rem   run-dev.cmd            (http  - API :5213, UI :5277)
rem   run-dev.cmd https      (https - API :7018, UI :7188)

setlocal
set "PROFILE=%~1"
if "%PROFILE%"=="" set "PROFILE=http"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-dev.ps1" -Profile %PROFILE%
exit /b %ERRORLEVEL%
