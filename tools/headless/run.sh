#!/bin/bash
# Usage: tools/headless/run.sh [seconds] [--fresh]
# Runs the mod on a headless tModLoader server and stops it after the given time.
SECONDS_TO_RUN=${1:-300}
TML="$HOME/Library/Application Support/Steam/steamapps/common/tModLoader"
WORLDS="$HOME/Library/Application Support/Terraria/tModLoader/Worlds"
HERE="$(cd "$(dirname "$0")" && pwd)"
CONFIG="$(mktemp -t autoterra-serverconfig)"
{ echo "world=$WORLDS/AutoTest.wld"; cat "$HERE/serverconfig.txt"; } > "$CONFIG"
if [[ "$2" == "--fresh" ]]; then
	rm -f "$WORLDS"/AutoTest.wld "$WORLDS"/AutoTest.twld "$WORLDS"/AutoTest.wld.bak "$WORLDS"/AutoTest.twld.bak
fi
cd "$TML" || exit 1
(tail -f /dev/null | AUTOTERRA_HEADLESS=1 "$TML/dotnet/dotnet" tModLoader.dll -server -config "$CONFIG" -nosteam > "$HERE/server-console.txt" 2>&1) &
sleep "$SECONDS_TO_RUN"
pkill -f "$CONFIG"
sleep 3
rm -f "$CONFIG"
echo "server stopped after $SECONDS_TO_RUN s; see $TML/tModLoader-Logs/server.log"
