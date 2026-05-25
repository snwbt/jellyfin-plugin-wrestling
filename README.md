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
