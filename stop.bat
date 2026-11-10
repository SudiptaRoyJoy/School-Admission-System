@echo off
setlocal
cd /d "%~dp0"

echo ===================================================
echo     Stopping School Admission System
echo ===================================================
echo.

set "PG_CTL=pgsql\pgsql\bin\pg_ctl.exe"
set "PG_DATA=pgsql\data"

echo Stopping PostgreSQL...
"%PG_CTL%" -D "%PG_DATA%" stop >NUL 2>&1

echo Stopping Backend API and Frontend processes...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr :8080 ^| findstr LISTENING') do (
    taskkill /f /pid %%a >NUL 2>&1
)
for /f "tokens=5" %%a in ('netstat -aon ^| findstr :3000 ^| findstr LISTENING') do (
    taskkill /f /pid %%a >NUL 2>&1
)

echo.
echo All services have been stopped.
echo ===================================================
ping -n 3 127.0.0.1 >NUL 2>&1
