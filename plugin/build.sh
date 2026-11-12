#!/bin/sh
# Baut das Plugin in einem .NET-SDK-Container. Vorher die Bibliotheken des eigenen Servers nach plugin/lib kopieren
# (MediaBrowser.Common.dll, MediaBrowser.Controller.dll, MediaBrowser.Model.dll), siehe docs/PLUGIN.md.
set -e
cd "$(dirname "$0")/.."
docker run --rm -v "$PWD":/w -w /w/plugin/src -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 sh -c "dotnet build -c Release -o /w/plugin/out ; chown -R $(id -u):$(id -g) /w/plugin/out /w/plugin/src/obj"
