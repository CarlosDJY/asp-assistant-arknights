$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$saved = @{}
foreach ($name in @('GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_OUTPUT', 'RUNNER_TEMP', 'GH_TOKEN')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$root = Join-Path $temporaryRoot ('reuse-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null

$mockResponses = @{}
# Mock only the network boundary; execute the production resolver unchanged.
function Invoke-RestMethod {
    param($Uri, $Headers)
    if ($mockResponses.networkFailure) { throw 'Simulated API failure' }
    if ($Uri -match '/workflows/pr-tests.yml/runs\?') { return @{ workflow_runs = $mockResponses.runs } }
    if ($Uri -match '/attempts/2/jobs\?') { return @{ jobs = $mockResponses.jobs } }
    if ($Uri -match '/runs/100/artifacts\?') { return @{ artifacts = $mockResponses.artifacts } }
    throw "Unexpected API request: $Uri"
}
function Invoke-WebRequest {
    param($Uri, $Headers, $OutFile)
    if ($Uri -notmatch '/artifacts/200/zip$') { throw "Unexpected artifact request: $Uri" }
    Copy-Item -LiteralPath (Join-Path $root 'proof.zip') -Destination $OutFile
}

try {
    $env:GITHUB_REPOSITORY = 'example/project'
    $env:GITHUB_SHA = 'a' * 40
    $env:RUNNER_TEMP = $root
    $env:GH_TOKEN = 'offline-test'
    $cases = @('valid', 'no-run', 'latest-failed', 'running', 'wrong-head', 'wrong-workflow',
        'wrong-event', 'job-failed', 'no-artifact', 'expired', 'old-attempt-artifact',
        'wrong-sha', 'wrong-repository', 'wrong-run', 'wrong-attempt', 'wrong-suite',
        'wrong-os', 'invalid-json', 'invalid-zip', 'network-error')
    foreach ($case in $cases) {
        $mockResponses.networkFailure = $false
        $run = @{ id = 100; run_attempt = 2; head_sha = $env:GITHUB_SHA;
            path = '.github/workflows/pr-tests.yml'; event = 'pull_request'; status = 'completed'; conclusion = 'success' }
        $mockResponses.runs = @($run)
        $mockResponses.jobs = @(@{ name = 'Windows .NET tests'; conclusion = 'success' })
        $mockResponses.artifacts = @(@{ id = 200; name = 'verified-tests-2'; expired = $false; size_in_bytes = 500 })
        $proof = @{ schema = 1; repository = $env:GITHUB_REPOSITORY; sha = $env:GITHUB_SHA;
            runId = '100'; runAttempt = 2; suite = 'public-windows-dotnet-v1'; runnerOS = 'Windows' }
        switch ($case) {
            'no-run' { $mockResponses.runs = @() }
            'latest-failed' { $run.conclusion = 'failure'; $older = $run.Clone(); $older.id = 99; $older.conclusion = 'success'; $mockResponses.runs += $older }
            'running' { $run.status = 'in_progress' }
            'wrong-head' { $run.head_sha = 'b' * 40 }
            'wrong-workflow' { $run.path = '.github/workflows/other.yml' }
            'wrong-event' { $run.event = 'push' }
            'job-failed' { $mockResponses.jobs[0].conclusion = 'failure' }
            'no-artifact' { $mockResponses.artifacts = @() }
            'expired' { $mockResponses.artifacts[0].expired = $true }
            'old-attempt-artifact' { $mockResponses.artifacts[0].name = 'verified-tests-1' }
            'wrong-sha' { $proof.sha = 'b' * 40 }
            'wrong-repository' { $proof.repository = 'example/other' }
            'wrong-run' { $proof.runId = '99' }
            'wrong-attempt' { $proof.runAttempt = 1 }
            'wrong-suite' { $proof.suite = 'other-suite' }
            'wrong-os' { $proof.runnerOS = 'Linux' }
            'network-error' { $mockResponses.networkFailure = $true }
        }
        $zipPath = Join-Path $root 'proof.zip'
        $zip = [IO.Compression.ZipArchive]::new([IO.File]::Open($zipPath, [IO.FileMode]::Create), [IO.Compression.ZipArchiveMode]::Create)
        try {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry('result.json').Open())
            try {
                if ($case -eq 'invalid-json') { $writer.Write('{broken') }
                else { $writer.Write(($proof | ConvertTo-Json)) }
            }
            finally { $writer.Dispose() }
        }
        finally { $zip.Dispose() }
        if ($case -eq 'invalid-zip') { 'broken archive' | Set-Content -LiteralPath $zipPath }
        $env:GITHUB_OUTPUT = Join-Path $root "$case-output.txt"
        $failed = $false
        try { & "$PSScriptRoot/Resolve-ReleaseTestReuse.ps1" | Out-Null }
        catch {
            if ($case -notin @('invalid-json', 'invalid-zip', 'network-error')) { throw }
            $failed = $true
        }
        $output = @(Get-Content -LiteralPath $env:GITHUB_OUTPUT)
        $actualReuse = -not $failed -and $output[-1] -eq 'reuse=true'
        if ($actualReuse -ne ($case -eq 'valid')) { throw "Incorrect reuse decision: $case (failed=$failed)" }
        if ($output[0] -ne 'reuse=false') { throw "Missing fallback output: $case" }
        if ($failed -ne ($case -in @('invalid-json', 'invalid-zip', 'network-error'))) { throw "Unexpected resolver error state: $case" }
        Write-Output "PASS $case"
    }
    Write-Output "$($cases.Count) test-reuse cases passed."
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    $resolved = (Resolve-Path -LiteralPath $root).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot) { throw 'Test cleanup escaped its temporary root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
