<#
DefaultAppLocker automated regression runner.

Design goals:
- No GUI automation.
- Exercise the published command-line interface only for app-level regression checks.
- Use an isolated test configuration root so user configuration is not touched.
- Print a final PASS or FAIL line plus failure reasons.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$script:Failures = New-Object System.Collections.Generic.List[string]

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot "DefaultAppLocker.slnx"
$Project = Join-Path $RepoRoot "DefaultAppLocker\DefaultAppLocker.csproj"
$PublishDir = Join-Path $RepoRoot "publish\regression-$Runtime"
$Exe = Join-Path $PublishDir "DefaultAppLocker.exe"
$RegressionRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DefaultAppLocker-Regression-" + [Guid]::NewGuid().ToString("N"))
$OriginalConfigRoot = $env:DEFAULTAPPLOCKER_CONFIG_ROOT

function Add-Step([string]$Message) {
    Write-Host "[STEP] $Message"
}

function Add-Failure([string]$Message) {
    $script:Failures.Add($Message) | Out-Null
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

function Join-ProcessArguments([string[]]$Arguments) {
    $escaped = New-Object System.Collections.Generic.List[string]
    foreach ($arg in $Arguments) {
        if ($arg -match '[\s"]') {
            $escaped.Add(('"' + ($arg -replace '"', '\"') + '"')) | Out-Null
        } else {
            $escaped.Add($arg) | Out-Null
        }
    }
    return [string]::Join(' ', $escaped)
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$Name = $FilePath,
        [string]$WorkingDirectory = $RepoRoot,
        [int]$TimeoutSeconds = 120,
        [switch]$AllowNonZero
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = Join-ProcessArguments -Arguments $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($psi)
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        Add-Failure "$Name timed out after $TimeoutSeconds seconds."
        return @{ ExitCode = -999; Output = ""; Error = "timeout" }
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0 -and -not $AllowNonZero) {
        Add-Failure "$Name exited with code $($process.ExitCode). STDOUT: $stdout STDERR: $stderr"
    }

    return @{ ExitCode = $process.ExitCode; Output = $stdout; Error = $stderr }
}

function Assert-FileExists([string]$Path, [string]$Reason) {
    if (-not (Test-Path -LiteralPath $Path)) { Add-Failure "$Reason Missing path: $Path" }
}

function Assert-JsonContains([string]$Path, [string]$Needle, [string]$Reason) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Add-Failure "$Reason Missing JSON file: $Path"
        return
    }
    $text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ($text -notmatch [regex]::Escape($Needle)) {
        Add-Failure "$Reason Expected to find '$Needle' in $Path."
    }
}

try {
    Add-Step "Build solution ($Configuration)"
    Invoke-CheckedProcess -FilePath "dotnet" -Arguments @("build", $Solution, "-c", $Configuration, "-v:minimal") -Name "dotnet build" -TimeoutSeconds 300 | Out-Null

    Add-Step "Run unit tests ($Configuration)"
    Invoke-CheckedProcess -FilePath "dotnet" -Arguments @("test", $Solution, "-c", $Configuration, "-v:minimal") -Name "dotnet test" -TimeoutSeconds 300 | Out-Null

    if (-not $SkipPublish) {
        Add-Step "Publish command-line executable"
        Get-Process DefaultAppLocker -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
        Invoke-CheckedProcess -FilePath "dotnet" -Arguments @("publish", $Project, "-c", $Configuration, "-r", $Runtime, "--self-contained", "false", "-p:PublishSingleFile=false", "-o", $PublishDir) -Name "dotnet publish" -TimeoutSeconds 300 | Out-Null
    }

    Assert-FileExists $Exe "Published executable check failed."

    $SourceConfigRoot = Join-Path $RegressionRoot "SourceConfig"
    $TargetConfigRoot = Join-Path $RegressionRoot "TargetConfig"
    New-Item -ItemType Directory -Force -Path $SourceConfigRoot, $TargetConfigRoot | Out-Null

    Add-Step "CLI help exits successfully without opening GUI"
    $env:DEFAULTAPPLOCKER_CONFIG_ROOT = $SourceConfigRoot
    $help = Invoke-CheckedProcess -FilePath $Exe -Arguments @("--help") -Name "DefaultAppLocker --help" -TimeoutSeconds 30
    if ($help.ExitCode -ne 0) { Add-Failure "--help returned non-zero exit code $($help.ExitCode)." }

    Add-Step "CLI capture snapshot creates isolated profile"
    $capture = Invoke-CheckedProcess -FilePath $Exe -Arguments @("--capture-snapshot", "Regression Snapshot") -Name "DefaultAppLocker --capture-snapshot" -TimeoutSeconds 60
    if ($capture.ExitCode -ne 0) { Add-Failure "--capture-snapshot returned non-zero exit code $($capture.ExitCode)." }
    Assert-FileExists (Join-Path $SourceConfigRoot "SnapshotProfiles") "Capture snapshot did not create SnapshotProfiles directory."
    $createdProfiles = Get-ChildItem -LiteralPath (Join-Path $SourceConfigRoot "SnapshotProfiles") -Filter *.json -ErrorAction SilentlyContinue
    if (-not $createdProfiles -or $createdProfiles.Count -lt 1) { Add-Failure "Capture snapshot did not create any snapshot profile JSON file." }

    Add-Step "CLI export snapshots writes portable package"
    $SnapshotExport = Join-Path $RegressionRoot "snapshots-only.json"
    $exportSnapshots = Invoke-CheckedProcess -FilePath $Exe -Arguments @("--export-snapshots", $SnapshotExport) -Name "DefaultAppLocker --export-snapshots" -TimeoutSeconds 60
    if ($exportSnapshots.ExitCode -ne 0) { Add-Failure "--export-snapshots returned non-zero exit code $($exportSnapshots.ExitCode)." }
    Assert-FileExists $SnapshotExport "Snapshot export failed."
    Assert-JsonContains $SnapshotExport "Regression Snapshot" "Snapshot export content check failed."

    Add-Step "CLI import snapshot package into clean isolated config root"
    $env:DEFAULTAPPLOCKER_CONFIG_ROOT = $TargetConfigRoot
    $import = Invoke-CheckedProcess -FilePath $Exe -Arguments @("--import", $SnapshotExport) -Name "DefaultAppLocker --import snapshots" -TimeoutSeconds 60
    if ($import.ExitCode -ne 0) { Add-Failure "--import returned non-zero exit code $($import.ExitCode)." }
    $ImportedProfiles = Get-ChildItem -LiteralPath (Join-Path $TargetConfigRoot "SnapshotProfiles") -Filter *.json -ErrorAction SilentlyContinue
    if (-not $ImportedProfiles -or $ImportedProfiles.Count -lt 1) {
        Add-Failure "Import did not create snapshot profile files in clean isolated config root."
    }

    Add-Step "CLI export all from imported profile"
    $AllExport = Join-Path $RegressionRoot "all.json"
    $exportAll = Invoke-CheckedProcess -FilePath $Exe -Arguments @("--export-all", $AllExport) -Name "DefaultAppLocker --export-all" -TimeoutSeconds 60
    if ($exportAll.ExitCode -ne 0) { Add-Failure "--export-all returned non-zero exit code $($exportAll.ExitCode)." }
    Assert-JsonContains $AllExport "Regression Snapshot" "Export-all content check failed."

    Add-Step "CLI rejects unknown argument with non-zero exit"
    $unknown = Invoke-CheckedProcess -FilePath $Exe -Arguments @("--definitely-not-a-real-command") -Name "DefaultAppLocker unknown command" -TimeoutSeconds 30 -AllowNonZero
    if ($unknown.ExitCode -eq 0) {
        Add-Failure "Unknown command unexpectedly returned exit code 0."
    } else {
        Write-Host "[OK] Unknown command failed as expected with exit code $($unknown.ExitCode)."
    }
}
catch {
    Add-Failure ("Unhandled regression runner exception: " + $_.Exception.Message)
}
finally {
    $env:DEFAULTAPPLOCKER_CONFIG_ROOT = $OriginalConfigRoot
    if (Test-Path -LiteralPath $RegressionRoot) {
        Remove-Item $RegressionRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($script:Failures.Count -eq 0) {
    Write-Host "PASS" -ForegroundColor Green
    exit 0
}

Write-Host "FAIL" -ForegroundColor Red
Write-Host "Failure reasons:" -ForegroundColor Red
foreach ($failure in $script:Failures) {
    Write-Host "- $failure" -ForegroundColor Red
}
exit 1
