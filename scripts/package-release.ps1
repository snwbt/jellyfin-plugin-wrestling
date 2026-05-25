param(
    [Parameter(Mandatory = $true)]
    [string]$Owner,

    [Parameter(Mandatory = $true)]
    [string]$Repo,

    [string]$Version = "1.0.0.1",
    [string]$TargetAbi = "10.11.0.0",
    [string]$Configuration = "Release",

    [switch]$UseGitHubReleaseUrl,

    [switch]$ForcePackage
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dotnet = Join-Path $root ".dotnet9\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$publishDir = Join-Path $root "bin\plugin"
$packageStagingDir = Join-Path $root "bin\package"
$distDir = Join-Path $root "dist"
$releaseAssetsDir = Join-Path $root "release-assets"
$zipName = "Jellyfin.Plugin.Wrestling_$Version.zip"
$zipPath = Join-Path $distDir $zipName

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseAssetsDir | Out-Null

if ((Test-Path $zipPath) -and -not $ForcePackage) {
    Write-Host "Using existing $zipPath"
}
else {
    & $dotnet publish (Join-Path $root "Jellyfin.Plugin.Wrestling\Jellyfin.Plugin.Wrestling.csproj") `
        --configuration $Configuration `
        --output $publishDir

    if (Test-Path $packageStagingDir) {
        Remove-Item -LiteralPath $packageStagingDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $packageStagingDir | Out-Null

    $allowedArtifacts = @(
        "Jellyfin.Plugin.Wrestling.dll",
        "Jellyfin.Plugin.Wrestling.pdb",
        "Jellyfin.Plugin.Wrestling.xml"
    )

    foreach ($artifact in $allowedArtifacts) {
        $source = Join-Path $publishDir $artifact
        if (Test-Path $source) {
            Copy-Item -LiteralPath $source -Destination $packageStagingDir -Force
        }
    }

    if (Test-Path (Join-Path $packageStagingDir "Jellyfin.Plugin.Wrestling.deps.json")) {
        throw "Package staging unexpectedly contains a deps.json file."
    }

    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $packageStagingDir "*") -DestinationPath $zipPath -Force
}

$checksum = (Get-FileHash -Algorithm MD5 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$releaseAssetPath = Join-Path $releaseAssetsDir $zipName
Copy-Item -LiteralPath $zipPath -Destination $releaseAssetPath -Force

if ($UseGitHubReleaseUrl) {
    $sourceUrl = "https://github.com/$Owner/$Repo/releases/download/v$Version/$zipName"
}
else {
    $sourceUrl = "https://raw.githubusercontent.com/$Owner/$Repo/main/release-assets/$zipName"
}

$newVersion = [ordered]@{
    version = $Version
    changelog = "Rebuild against Jellyfin 10.11.0 and package only plugin-owned artifacts."
    targetAbi = $TargetAbi
    sourceUrl = $sourceUrl
    checksum = $checksum
    timestamp = $timestamp
}

$manifestPath = Join-Path $root "manifest.json"
$existingVersions = @()
if (Test-Path $manifestPath) {
    $existingManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $existingPlugin = @($existingManifest) | Where-Object { $_.guid -eq "89e3cfaf-ef94-4dd5-9134-bab43eaf993d" } | Select-Object -First 1
    if ($existingPlugin -and $existingPlugin.versions) {
        $existingVersions = @($existingPlugin.versions) | Where-Object { $_.version -ne $Version }
    }
}

$versions = @($newVersion) + @($existingVersions)
$manifest = @(
    [ordered]@{
        guid = "89e3cfaf-ef94-4dd5-9134-bab43eaf993d"
        name = "Wrestling"
        description = "Adds spoiler-safe wrestling match cards to Jellyfin pay-per-view movie pages."
        overview = "Wrestling match cards for PPVs"
        owner = $Owner
        category = "Metadata"
        versions = $versions
    }
)
$json = ConvertTo-Json -InputObject $manifest -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Created $zipPath"
Write-Host "Updated $releaseAssetPath"
Write-Host "MD5: $checksum"
Write-Host "Updated $manifestPath"
Write-Host "Tag should use v$Version"
Write-Host "Jellyfin repo URL will be:"
Write-Host "https://raw.githubusercontent.com/$Owner/$Repo/main/manifest.json"
