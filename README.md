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

If CageMatch returns `403` or a gate page, the scan stops immediately in a visible
`Blocked` state instead of quietly skipping the item. Use **Cancel scan** to stop a
running or throttled scan, and **Clear status** before starting a fresh blocked run.

## Imported Cache

When live CageMatch lookups are blocked, you can export workbook data to CSV and
import it from the plugin settings. The importer expects workbook-style columns such
as `Date`, `Match Fixture`, `Match Type`, `Event`, `Rating on CageMatch`, `Votes`,
and an optional CageMatch URL. Imported rows are grouped by event and date, then used
before live CageMatch requests during scans.

## Scheduled Scan

Enable **scheduled background scan** in plugin settings to let Jellyfin's scheduled
task process selected libraries automatically. The same cache, throttle, and overview
replacement logic are used for manual and scheduled scans.
