$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Split-Path $PSScriptRoot -Parent)
$solutions = @(Get-ChildItem -File | Where-Object { $_.Extension -in '.sln', '.slnx' })
if ($solutions.Count -ne 1) { throw 'Expected one root solution.' }
$solution = $solutions[0].FullName

dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet format $solution --verify-no-changes --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Formatting check failed.' }
dotnet build $solution --configuration Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
# Fresh results prevent stale reports from hiding a zero-test run.
$results = Join-Path 'TestResults' ([Guid]::NewGuid().ToString('N'))
dotnet test $solution --configuration Release --no-build --no-restore --logger trx --results-directory $results
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
$reports = @(Get-ChildItem $results -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
if ($reports.Count -eq 0) { throw 'No TRX reports produced. Add NUnit tests and a compatible test adapter.' }
$executed = 0
foreach ($report in $reports) {
    [xml]$xml = Get-Content $report.FullName -Raw
    $executed += [int]$xml.TestRun.ResultSummary.Counters.executed
}
if ($executed -eq 0) { throw 'No tests executed.' }
Write-Host "Build and tests passed ($executed tests executed)."
