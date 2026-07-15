<#
Build, version, publish and optionally create/update a GitHub Release for DefaultAppLocker.

All project paths in this script are resolved from the script directory using
relative path segments. The default output path resolves to D:\Files\DefaultAppLocker.exe
for the repository location D:\Files\Develop\Windows\DefaultAppLocker.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputExeRelativePath = "..\..\..\DefaultAppLocker.exe",
    [string]$ReleaseNotes = "Automated DefaultAppLocker release.",
    [switch]$SkipGitHubRelease,
    [switch]$SkipTests,
    [switch]$NoVersionIncrement
)

$ErrorActionPreference = "Stop"

function Resolve-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $RelativePath))
}

function Assert-Command {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Get-GitRemoteOwnerRepo {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $remote = (& git -C $RepositoryRoot remote get-url origin 2>$null).Trim()
    if ([string]::IsNullOrWhiteSpace($remote)) {
        return $null
    }

    if ($remote -match "github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(\.git)?$") {
        return @{
            Owner = $Matches["owner"]
            Repo = $Matches["repo"]
            Url = $remote
        }
    }

    return $null
}

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body = $null,
        [string]$ContentType = "application/json"
    )

    $token = $env:GH_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) {
        $token = $env:GITHUB_TOKEN
    }

    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "GitHub token missing. Set GH_TOKEN or GITHUB_TOKEN, or install and authenticate GitHub CLI."
    }

    $headers = @{
        Authorization = "Bearer $token"
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "DefaultAppLocker-build"
    }

    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers
    }

    $json = $Body | ConvertTo-Json -Depth 10
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -ContentType $ContentType -Body $json
}

function Update-GitHubRelease {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$AssetPath,
        [Parameter(Mandatory = $true)][string]$ReleaseNotes
    )

    $ownerRepo = Get-GitRemoteOwnerRepo -RepositoryRoot $RepositoryRoot
    if ($null -eq $ownerRepo) {
        Write-Warning "GitHub remote was not detected; skipping GitHub Release."
        return
    }

    $tagName = "v$Version"
    $assetName = [System.IO.Path]::GetFileName($AssetPath)

    if (-not (Get-Command gh -ErrorAction SilentlyContinue) -and [string]::IsNullOrWhiteSpace($env:GH_TOKEN) -and [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        throw "GitHub Release requires GitHub CLI authentication or GH_TOKEN/GITHUB_TOKEN."
    }

    $existingTag = (& git -C $RepositoryRoot tag --list $tagName).Trim()
    if ([string]::IsNullOrWhiteSpace($existingTag)) {
        & git -C $RepositoryRoot tag $tagName
        if ($LASTEXITCODE -ne 0) { throw "Failed to create git tag $tagName." }
    }

    & git -C $RepositoryRoot push origin $tagName
    if ($LASTEXITCODE -ne 0) { throw "Failed to push git tag $tagName." }

    if (Get-Command gh -ErrorAction SilentlyContinue) {
        & gh release view $tagName --repo "$($ownerRepo.Owner)/$($ownerRepo.Repo)" *> $null
        if ($LASTEXITCODE -ne 0) {
            & gh release create $tagName $AssetPath --repo "$($ownerRepo.Owner)/$($ownerRepo.Repo)" --title $tagName --notes $ReleaseNotes
            if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed to create release $tagName." }
        }
        else {
            & gh release upload $tagName $AssetPath --repo "$($ownerRepo.Owner)/$($ownerRepo.Repo)" --clobber
            if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed to upload release asset for $tagName." }
        }
        Write-Host "GitHub Release handled with gh: $tagName"
        return
    }

    $apiRoot = "https://api.github.com/repos/$($ownerRepo.Owner)/$($ownerRepo.Repo)"
    $release = $null
    try {
        $release = Invoke-GitHubApi -Method "GET" -Uri "$apiRoot/releases/tags/$tagName"
    }
    catch {
        $release = Invoke-GitHubApi -Method "POST" -Uri "$apiRoot/releases" -Body @{
            tag_name = $tagName
            name = $tagName
            body = $ReleaseNotes
            draft = $false
            prerelease = $false
        }
    }

    foreach ($asset in $release.assets) {
        if ($asset.name -eq $assetName) {
            Invoke-GitHubApi -Method "DELETE" -Uri "$apiRoot/releases/assets/$($asset.id)" | Out-Null
        }
    }

    $token = $env:GH_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) { $token = $env:GITHUB_TOKEN }
    $uploadHeaders = @{
        Authorization = "Bearer $token"
        Accept = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
        "Content-Type" = "application/octet-stream"
        "User-Agent" = "DefaultAppLocker-build"
    }
    $uploadUri = $release.upload_url -replace "\{\?name,label\}", "?name=$([System.Uri]::EscapeDataString($assetName))"
    Invoke-RestMethod -Method Post -Uri $uploadUri -Headers $uploadHeaders -InFile $AssetPath | Out-Null
    Write-Host "GitHub Release handled with REST API: $tagName"
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionPath = Resolve-RelativePath -BasePath $scriptRoot -RelativePath ".\DefaultAppLocker.slnx"
$appProjectPath = Resolve-RelativePath -BasePath $scriptRoot -RelativePath ".\DefaultAppLocker\DefaultAppLocker.csproj"
$publishDirectory = Resolve-RelativePath -BasePath $scriptRoot -RelativePath ".\publish\$RuntimeIdentifier"
$outputExePath = Resolve-RelativePath -BasePath $scriptRoot -RelativePath $OutputExeRelativePath
$outputDirectory = Split-Path -Parent $outputExePath

Assert-Command -Name "dotnet"
Assert-Command -Name "git"

if (-not (Test-Path $solutionPath)) { throw "Solution not found: $solutionPath" }
if (-not (Test-Path $appProjectPath)) { throw "App project not found: $appProjectPath" }

[xml]$projectXml = Get-Content -LiteralPath $appProjectPath -Encoding UTF8 -Raw
$propertyGroup = $projectXml.Project.PropertyGroup | Select-Object -First 1
if ($null -eq $propertyGroup) { throw "No PropertyGroup found in $appProjectPath" }

$currentVersionNode = $propertyGroup.Version
$currentVersion = "1.0.0"
if ($null -ne $currentVersionNode -and -not [string]::IsNullOrWhiteSpace($currentVersionNode)) {
    $currentVersion = [string]$currentVersionNode
}

if ($currentVersion -notmatch "^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<revision>\d+))?$") {
    throw "Version '$currentVersion' is not supported. Expected Major.Minor.Patch or Major.Minor.Patch.Revision."
}

if ($NoVersionIncrement) {
    $newVersion = $currentVersion
}
else {
    $newVersion = "$($Matches['major']).$($Matches['minor']).$([int]$Matches['patch'] + 1)"
}

$fileVersion = "$newVersion.0"

function Set-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)][xml]$Xml,
        [Parameter(Mandatory = $true)][object]$Group,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $node = $Group.SelectSingleNode($Name)
    if ($null -eq $node) {
        $node = $Xml.CreateElement($Name)
        [void]$Group.AppendChild($node)
    }
    $node.InnerText = $Value
}

Set-ProjectProperty -Xml $projectXml -Group $propertyGroup -Name "Version" -Value $newVersion
Set-ProjectProperty -Xml $projectXml -Group $propertyGroup -Name "AssemblyVersion" -Value $fileVersion
Set-ProjectProperty -Xml $projectXml -Group $propertyGroup -Name "FileVersion" -Value $fileVersion
Set-ProjectProperty -Xml $projectXml -Group $propertyGroup -Name "InformationalVersion" -Value $newVersion
Set-ProjectProperty -Xml $projectXml -Group $propertyGroup -Name "AssemblyTitle" -Value "Windows default application configuration manager"
Set-ProjectProperty -Xml $projectXml -Group $propertyGroup -Name "FileDescription" -Value "Windows default application configuration manager"
Set-ProjectProperty -Xml $projectXml -Group $propertyGroup -Name "Copyright" -Value "Copyright © FairyXH"
Set-ProjectProperty -Xml $projectXml -Group $propertyGroup -Name "IncludeSourceRevisionInInformationalVersion" -Value "false"
$projectXml.Save($appProjectPath)
Write-Host "Version: $currentVersion -> $newVersion"

if (-not $SkipTests) {
    & dotnet test $solutionPath -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }
}

if (Test-Path $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& dotnet publish $appProjectPath -c $Configuration -r $RuntimeIdentifier --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$publishedExe = Join-Path $publishDirectory "DefaultAppLocker.exe"
if (-not (Test-Path $publishedExe)) { throw "Published executable was not found: $publishedExe" }
Copy-Item -LiteralPath $publishedExe -Destination $outputExePath -Force

if (-not $SkipGitHubRelease) {
    try {
        Update-GitHubRelease -RepositoryRoot $scriptRoot -Version $newVersion -AssetPath $outputExePath -ReleaseNotes $ReleaseNotes
    }
    catch {
        Write-Warning "GitHub Release was not completed: $($_.Exception.Message)"
    }
}

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($outputExePath)
Write-Host "Published: $outputExePath"
Write-Host "ProductVersion: $($versionInfo.ProductVersion)"
Write-Host "FileVersion: $($versionInfo.FileVersion)"
Write-Host "CompanyName: $($versionInfo.CompanyName)"
Write-Host "FileDescription: $($versionInfo.FileDescription)"
