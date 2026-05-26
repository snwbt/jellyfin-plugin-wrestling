# Jellyfin Wrestling Plugin

Adds spoiler-safe wrestling match cards to Jellyfin pay-per-view movies.

The plugin targets Jellyfin 10.11.x on .NET 9 and uses CageMatch event metadata carefully:
it honors a large default throttle, caches results in plugin configuration, and
does not attempt to bypass JavaScript or login gates.

## Build

```powershell
dotnet restore
dotnet build
```

## Install

Copy the built plugin output from:

```text
Jellyfin.Plugin.Wrestling\bin\Debug\net9.0\
```

to a Jellyfin plugin folder such as:

```text
%LOCALAPPDATA%\jellyfin\plugins\Wrestling
```

Restart Jellyfin, enable the metadata provider for your movie library, then
refresh metadata for wrestling PPVs.

## Manual Match Cards

Open the Wrestling plugin settings and add one mapping block per PPV:

```text
Title: ECW Heat Wave 1998
Year: 1998
Date:
CageMatch:
Matches:
1. Tommy Dreamer, Sandman & Spike Dudley vs. The Dudley Boyz | Dudleyville Street Fight | |
2. Taz vs. Bam Bam Bigelow | FTW Championship Match | |
```

After saving, refresh metadata for the movie or library. The plugin writes the
match card into the movie overview.

You can also click **Apply Match Cards** on the plugin settings page. That scans
the configured library name, defaults to `Wrestling PPVs`, and writes matching
manual cards directly into movie overviews.

## CageMatch IDs

For reliable matching, add a provider id named `CageMatch` to PPV metadata when
you know the CageMatch event URL. For an event URL like:

```text
https://www.cagematch.net/?id=1&nr=118234
```

the provider id value is:

```text
118234
```
