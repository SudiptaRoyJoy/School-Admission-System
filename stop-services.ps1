# Stop all services for School Admission System
$ErrorActionPreference = "SilentlyContinue"

$repoRoot = $PSScriptRoot
$pgCtl = "$repoRoot\pgsql\pgsql\bin\pg_ctl.exe"
$pgData = "$repoRoot\pgsql\data"

Write-Host "Stopping Frontend, Backend API, and PostgreSQL..." -ForegroundColor Yellow

# Stop processes listening on 8080 and 3000
Get-NetTCPConnection -LocalPort 8080, 3000 -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue
}

# Stop PostgreSQL
& $pgCtl -D $pgData stop

Write-Host "All services stopped." -ForegroundColor Green
