[CmdletBinding()]
param(
    [string[]] $ApiBaseUrls = @('http://localhost:8080'),
    [int] $Seats = 20,
    [int] $Applicants = 250,
    [int] $ThrottleLimit = 100
)

$ErrorActionPreference = 'Stop'

if ($ApiBaseUrls.Count -lt 1) { throw 'Supply at least one API URL.' }
if ($Seats -lt 1 -or $Applicants -le $Seats) { throw 'Applicants must be greater than a positive seat count.' }

$baseUrl = $ApiBaseUrls[0].TrimEnd('/')
$classPayload = @{ name = "Concurrency verification $(Get-Date -Format 'yyyyMMdd-HHmmss')"; totalSeats = $Seats } | ConvertTo-Json
$class = Invoke-RestMethod -Method Post -Uri "$baseUrl/classes" -ContentType 'application/json' -Body $classPayload
$classId = $class.id
Write-Host "Created $($class.name) ($classId) with $Seats seats." -ForegroundColor Cyan
Write-Host "Sending $Applicants concurrent applications to: $($ApiBaseUrls -join ', ')" -ForegroundColor Cyan

$sharedUrls = $ApiBaseUrls
$targetClassId = $classId
$results = 1..$Applicants | ForEach-Object -Parallel {
    $index = $_
    $studentId = ([long]20152691230000000 + $index).ToString()
    $urls = $using:sharedUrls
    $class = $using:targetClassId
    $base = $urls[($index - 1) % $urls.Count].TrimEnd('/')
    $uri = "$base/classes/$class/holds"
    try {
        Invoke-RestMethod -Method Post -Uri $uri -Headers @{ 'X-Student-Id' = $studentId } -TimeoutSec 90 | Out-Null
        [pscustomobject]@{ StudentId = $studentId; Status = 201; Error = '' }
    }
    catch {
        $status = 0
        try { $status = [int]$_.Exception.Response.StatusCode } catch { }
        [pscustomobject]@{ StudentId = $studentId; Status = $status; Error = $_.Exception.Message }
    }
} -ThrottleLimit $ThrottleLimit

$created = @($results | Where-Object Status -eq 201).Count
$conflicts = @($results | Where-Object Status -eq 409).Count
$unexpected = @($results | Where-Object { $_.Status -ne 201 -and $_.Status -ne 409 })
$summary = Invoke-RestMethod -Method Get -Uri "$baseUrl/classes/$classId"
$occupied = [int]$summary.heldSeats + [int]$summary.confirmedSeats

Write-Host "Created holds: $created" -ForegroundColor Green
Write-Host "Capacity conflicts: $conflicts" -ForegroundColor Yellow
Write-Host "Unexpected responses: $($unexpected.Count)"
Write-Host "Server seat summary: held=$($summary.heldSeats), confirmed=$($summary.confirmedSeats), available=$($summary.availableSeats), total=$($summary.totalSeats)"

if ($created -gt $Seats -or $occupied -gt $Seats -or $created -ne $Seats -or $unexpected.Count -gt 0) {
    if ($unexpected.Count -gt 0) { $unexpected | Format-Table -AutoSize }
    throw 'Concurrency verification failed. Inspect API logs and the result list.'
}

Write-Host 'PASS: capacity was not exceeded.' -ForegroundColor Green
