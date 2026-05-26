# Jellyfin Wrestling Plugin

Adds spoiler-safe wrestling match cards to Jellyfin pay-per-view movies.

The plugin targets Jellyfin 10.11.x on .NET 9 and uses CageMatch event metadata carefully:
it honors the published crawl delay, caches results in plugin configuration, and does
not attempt to bypass JavaScript or login gates.

## Build

```powershell
dotnet restore
dotnet build
```

## Install

Add the repository URL in Jellyfin:

```text
https://raw.githubusercontent.com/snwbt/jellyfin-plugin-wrestling/main/manifest.json
```

Install or upgrade the Wrestling plugin, then restart Jellyfin.

## Automatic CageMatch Scan

Open the Wrestling plugin settings, select one or more Jellyfin libraries from
**Libraries to scan**, then click **Scan now**.

The scanner searches CageMatch by movie title, production year, and premiere date,
chooses the best-scoring event candidate, fetches the event page, caches the match
card, and writes a `Match Card` section into the normal Jellyfin movie overview.
Results and winners are not shown in the overview.

CageMatch currently publishes a `527` second crawl delay, so large libraries process
slowly by design. The settings page shows queue progress, recent item outcomes, and
the latest CageMatch request status.

## Scheduled Scan

Enable **scheduled background scan** in plugin settings to let Jellyfin's scheduled
task process selected libraries automatically. The same cache, throttle, and overview
replacement logic are used for manual and scheduled scans.
