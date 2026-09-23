$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Split-Path $PSScriptRoot -Parent)
$files = @(git ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Unable to list tracked files.' }
foreach ($required in @('README.md', 'doc/workflow.md', '.github/workflows/ci.yml')) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing required file: $required" }
}
# Validate relative Markdown file links, excluding external URLs and anchors.
foreach ($file in @($files | Where-Object { $_ -like '*.md' })) {
    $content = Get-Content -LiteralPath $file -Raw
    foreach ($match in [regex]::Matches($content, '\[[^\]]*\]\(([^\s)]+)\)')) {
        $target = $match.Groups[1].Value
        if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#|/)') { continue }
        $target = [Uri]::UnescapeDataString(($target -split '#', 2)[0])
        $parent = Split-Path $file -Parent
        if (-not $parent) { $parent = '.' }
        if ($target -and -not (Test-Path -LiteralPath (Join-Path $parent $target))) {
            throw "Broken link in ${file}: $target"
        }
    }
}
$solutions = @($files | Where-Object { $_ -match '^[^/]+\.slnx?$' })
$code = @($files | Where-Object { $_ -match '\.(cs|csproj|fsproj|vbproj)$' })
if ($solutions.Count -gt 1) { throw 'Expected one root solution.' }
if ($code.Count -gt 0 -and $solutions.Count -eq 0) { throw 'Code exists without a root solution; CI must not skip it.' }
$hasSolution = $solutions.Count -eq 1
if ($hasSolution -and -not (Test-Path global.json)) { throw 'Add the agreed SDK pin in global.json with the solution.' }
if ($env:GITHUB_OUTPUT) {
    "has_solution=$($hasSolution.ToString().ToLowerInvariant())" >> $env:GITHUB_OUTPUT
}
if ($hasSolution) {
    Write-Host "Repository checks passed. Solution: $($solutions[0])"
} else {
    $message = 'Documentation-only phase: repository checks passed; no application build or tests exist yet.'
    Write-Host $message
    if ($env:GITHUB_STEP_SUMMARY) { $message >> $env:GITHUB_STEP_SUMMARY }
}
