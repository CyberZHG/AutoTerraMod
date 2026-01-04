#!/bin/bash
# Command-line build. tModLoader cannot convert PNG assets outside the game on macOS
# (FNA3D is unavailable), so icons are moved aside for the duration of the build.
# Build from the game (Workshop > Develop Mods > Build + Reload) to package the icons.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STASH="$(mktemp -d)"
restore() { for icon in "$STASH"/*.png; do [ -e "$icon" ] && mv "$icon" "$ROOT/"; done; rmdir "$STASH"; }
trap restore EXIT
for icon in "$ROOT"/icon.png "$ROOT"/icon_small.png; do [ -e "$icon" ] && mv "$icon" "$STASH/"; done
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd "$ROOT" && dotnet build "$@"
