param(
    [Parameter(Mandatory = $true)]
    [string]$Owner,

    [Parameter(Mandatory = $true)]
    [string]$Repo,

    [string]$Version = "1.0.0.0",
    [string]$TargetAbi = "10.11.0.0",
    [string]$Configuration = "Release",

    [switch]$ForcePackage
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dotnet = Join-Path $root ".dotnet9\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$publishDir = Join-Path $root "bin\plugin"
$distDir = Join-Path $root "dist"
$zipName = "Jellyfin.Plugin.Wrestling_$Version.zip"
$zipPath = Join-Path $distDir $zipName

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

if ((Test-Path $zipPath) -and -not $ForcePackage) {
    Write-Host "Using existing $zipPath"
}
else {
    & $dotnet publish (Join-Path $root "Jellyfin.Plugin.Wrestling\Jellyfin.Plugin.Wrestling.csproj") `
        --configuration $Configuration `
        --output $publishDir

    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
}

$checksum = (Get-FileHash -Algorithm MD5 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$sourceUrl = "https://github.com/$Owner/$Repo/releases/download/v$Version/$zipName"

$manifest = @(
    [ordered]@{
        guid = "89e3cfaf-ef94-4dd5-9134-bab43eaf993d"
        name = "Wrestling"
        description = "Adds spoiler-safe wrestling match cards to Jellyfin pay-per-view movie pages."
        overview = "Wrestling match cards for PPVs"
        owner = $Owner
        category = "Metadata"
        versions = @(
            [ordered]@{
                version = $Version
                changelog = "Initial wrestling match card metadata provider."
                targetAbi = $TargetAbi
                sourceUrl = $sourceUrl
                checksum = $checksum
                timestamp = $timestamp
            }
        )
    }
)

$manifestPath = Join-Path $root "manifest.json"
$json = ConvertTo-Json -InputObject $manifest -Depth 10
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Created $zipPath"
Write-Host "MD5: $checksum"
Write-Host "Updated $manifestPath"
Write-Host "Release upload URL should use tag v$Version"
Write-Host "Jellyfin repo URL will be:"
Write-Host "https://raw.githubusercontent.com/$Owner/$Repo/main/manifest.json"
