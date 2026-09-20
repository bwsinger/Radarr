#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet_bin="${DOTNET_BIN:-$HOME/.local/share/arr-dev-dotnet/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_PROCESSOR_COUNT=2
"$dotnet_bin" build src/NzbDrone.Core/Radarr.Core.csproj \
  -c Release -f net8.0 -r linux-musl-x64 \
  -p:UseSharedCompilation=false -p:SolutionDir="$PWD/src/" -p:RuntimeIdentifiers=linux-musl-x64 -p:AssemblyVersion=6.4.4.10685 \
  -p:AssemblyConfiguration=media-server-dev -o .dev-build --nologo
docker build -f dev/Dockerfile \
  --build-arg DEV_REVISION="$(git rev-parse HEAD)" \
  -t radarr-dev:local .
