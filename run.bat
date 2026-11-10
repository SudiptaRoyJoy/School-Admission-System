@echo off
setlocal enabledelayedexpansion
title School Admission System Launcher
cd /d "%~dp0"

echo ===================================================
echo     Starting School Admission System
echo ===================================================
echo.

set "DOTNET=H:\Android_Work\dotnet\dotnet.exe"
set "PG_CTL=pgsql\pgsql\bin\pg_ctl.exe"
set "PG_DATA=pgsql\data"
set "PG_LOG=pgsql\logfile.log"

set "ASPNETCORE_URLS=http://localhost:8080"
set "ConnectionStrings__Default=Host=localhost;Port=5432;Database=admission;Username=admission_app;Password=local-dev-only-change-me;Maximum Pool Size=30;Timeout=15;Command Timeout=30"

:: 1. Clean up stale postmaster.pid if postgres is not running
if exist "%PG_DATA%\postmaster.pid" (
    tasklist /fi "imagename eq postgres.exe" 2>NUL | find /i /n "postgres.exe" >NUL
    if "%ERRORLEVEL%"=="1" (
        echo Cleaning up stale database lock...
        del /f /q "%PG_DATA%\postmaster.pid" >NUL 2>&1
    )
)

:: 2. Start PostgreSQL 16
echo [1/3] Starting PostgreSQL database...
"%PG_CTL%" -D "%PG_DATA%" -l "%PG_LOG%" start >NUL 2>&1
ping -n 2 127.0.0.1 >NUL 2>&1

:: 3. Start Backend API
echo [2/3] Starting Backend API on http://localhost:8080...
start "Admission Backend API [Port 8080]" cmd /k "title Admission Backend API (Port 8080) && set ASPNETCORE_URLS=http://localhost:8080 && echo Starting ASP.NET Core API on port 8080... && "%DOTNET%" backend\Admission.Api\publish\Admission.Api.dll"

:: 4. Start Frontend
echo [3/3] Starting Frontend UI on http://localhost:3000...
start "Admission Frontend [Port 3000]" cmd /k "title Admission Frontend (Port 3000) && cd frontend && echo Starting Vite Dev Server on port 3000... && npm run dev"

echo.
echo ===================================================
echo   All services launched!
echo.
echo   Parent/Admin UI: http://localhost:3000
echo   Backend API:     http://localhost:8080
echo   Health Check:    http://localhost:8080/health
echo ===================================================
echo.
echo Opening browser in 3 seconds...
ping -n 4 127.0.0.1 >NUL 2>&1
start http://localhost:3000
