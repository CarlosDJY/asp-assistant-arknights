$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This output remains false on missing evidence or any failed API/archive operation.
'reuse=false' | Add-Content -LiteralPath $env:GITHUB_OUTPUT
$repository = $env:GITHUB_REPOSITORY
$sha = $env:GITHUB_SHA
if ($repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $sha -notmatch '^[0-9a-f]{40}$') {
    throw 'Invalid repository or commit SHA.'
}
$api = "https://api.github.com/repos/$repository/actions"
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$runs = Invoke-RestMethod -Uri "$api/workflows/pr-tests.yml/runs?head_sha=$sha&event=pull_request&per_page=100" -Headers $headers
$run = $runs.workflow_runs | Sort-Object id -Descending | Select-Object -First 1
if ($null -eq $run) { Write-Output 'No matching PR run; run the full test suite.'; return }
if ($run.head_sha -ne $sha -or $run.path -ne '.github/workflows/pr-tests.yml' -or $run.event -ne 'pull_request' -or
    $run.status -ne 'completed' -or $run.conclusion -ne 'success') {
    Write-Output 'Latest PR run is not a successful match; run the full test suite.'
    return
}
$jobs = Invoke-RestMethod -Uri "$api/runs/$($run.id)/attempts/$($run.run_attempt)/jobs?per_page=100" -Headers $headers
$testJobs = @($jobs.jobs | Where-Object { $_.name -eq 'Windows .NET tests' -and $_.conclusion -eq 'success' })
if ($testJobs.Count -ne 1) { Write-Output 'Required Windows test job did not pass; run the full test suite.'; return }
$artifacts = Invoke-RestMethod -Uri "$api/runs/$($run.id)/artifacts?per_page=100" -Headers $headers
$proofs = @($artifacts.artifacts | Where-Object { $_.name -eq "verified-tests-$($run.run_attempt)" -and -not $_.expired })
if ($proofs.Count -ne 1 -or $proofs[0].size_in_bytes -gt 65536) {
    Write-Output 'Current-attempt test evidence is missing, expired or invalid; run the full test suite.'
    return
}
$temporaryRoot = (Resolve-Path -LiteralPath $env:RUNNER_TEMP).Path
$scratch = Join-Path $temporaryRoot ("verified-tests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $zipPath = Join-Path $scratch 'evidence.zip'
    Invoke-WebRequest -Uri "$api/artifacts/$($proofs[0].id)/zip" -Headers $headers -OutFile $zipPath
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if ($archive.Entries.Count -ne 1 -or $archive.Entries[0].FullName -ne 'result.json' -or $archive.Entries[0].Length -gt 16384) {
            Write-Output 'Unexpected evidence archive; run the full test suite.'
            return
        }
        $reader = [IO.StreamReader]::new($archive.Entries[0].Open())
        try { $proof = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
    if ($proof.schema -ne 1 -or $proof.repository -ne $repository -or $proof.sha -ne $sha -or
        [string]$proof.runId -ne [string]$run.id -or [int]$proof.runAttempt -ne [int]$run.run_attempt -or
        $proof.suite -ne 'public-windows-dotnet-v1' -or $proof.runnerOS -ne 'Windows') {
        Write-Output 'Evidence does not describe this exact commit and run attempt; run the full test suite.'
        return
    }
    "run_id=$($run.id)" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    'reuse=true' | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    Write-Output "Reusing successful Windows tests from run $($run.id), attempt $($run.run_attempt), commit $sha."
}
finally {
    $resolved = (Resolve-Path -LiteralPath $scratch).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot) { throw 'Evidence cleanup escaped its temporary root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
