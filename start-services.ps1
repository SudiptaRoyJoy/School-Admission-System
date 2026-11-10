# Start all services for School Admission System (PostgreSQL, .NET 10 API, React Frontend)
$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$dotnet = "H:\Android_Work\dotnet\dotnet.exe"
$pgCtl = "$repoRoot\pgsql\pgsql\bin\pg_ctl.exe"
$pgData = "$repoRoot\pgsql\data"
$pgLog = "$repoRoot\pgsql\logfile.log"

Write-Host "===================================================" -ForegroundColor Green
Write-Host "   Starting School Admission System Services" -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green

# 1. Clean up stale postmaster.pid if postgres is not running
if (Test-Path "$pgData\postmaster.pid") {
    $pidLine = (Get-Content "$pgData\postmaster.pid" -TotalCount 1).Trim()
    $existing = Get-Process -Id $pidLine -ErrorAction SilentlyContinue
    if (-not $existing) {
        Write-Host "Cleaning up stale database lock..." -ForegroundColor Yellow
        Remove-Item "$pgData\postmaster.pid" -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "1. Starting PostgreSQL 16..." -ForegroundColor Cyan
& $pgCtl -D $pgData -l $pgLog start
Start-Sleep -Seconds 1

Write-Host "2. Starting Backend API on http://localhost:8080..." -ForegroundColor Cyan
Start-Process -FilePath $dotnet -ArgumentList "$repoRoot\backend\Admission.Api\publish\Admission.Api.dll" -WorkingDirectory "$repoRoot\backend\Admission.Api\publish"

Write-Host "3. Starting Frontend UI on http://localhost:3000..." -ForegroundColor Cyan
Start-Process -FilePath "npm.cmd" -ArgumentList "run dev" -WorkingDirectory "$repoRoot\frontend"

Start-Sleep -Seconds 2

Write-Host "`nAll services started successfully!" -ForegroundColor Green
Write-Host "  Parent & Admin UI: http://localhost:3000" -ForegroundColor Yellow
Write-Host "  Backend API:       http://localhost:8080" -ForegroundColor Yellow
Write-Host "  API Health Check:  http://localhost:8080/health" -ForegroundColor Yellow

Start-Process "http://localhost:3000"
