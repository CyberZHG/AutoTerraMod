#!/bin/bash
# Usage: tools/headless/run.sh [seconds] [--fresh]
# Runs the mod on a headless tModLoader server and stops it after the given time,
# or earlier once AUTOTERRA_STOP_PATTERN (default "nav bench done" for the nav scenario) appears in the log.
SECONDS_TO_RUN=${1:-300}
TML="$HOME/Library/Application Support/Steam/steamapps/common/tModLoader"
WORLDS="$HOME/Library/Application Support/Terraria/tModLoader/Worlds"
HERE="$(cd "$(dirname "$0")" && pwd)"
CONFIG="$(mktemp -t autoterra-serverconfig)"
{ echo "world=$WORLDS/AutoTest.wld"; cat "$HERE/serverconfig.txt"; } > "$CONFIG"
if [[ "$2" == "--fresh" ]]; then
	rm -f "$WORLDS"/AutoTest.wld "$WORLDS"/AutoTest.twld "$WORLDS"/AutoTest.wld.bak "$WORLDS"/AutoTest.twld.bak
fi
STOP_PATTERN=${AUTOTERRA_STOP_PATTERN:-}
if [[ -z "$STOP_PATTERN" && "$AUTOTERRA_SCENARIO" == "nav" ]]; then
	STOP_PATTERN="nav bench done"
fi
LOG="$TML/tModLoader-Logs/server.log"
cd "$TML" || exit 1
[[ -n "$STOP_PATTERN" && -f "$LOG" ]] && mv "$LOG" "$LOG.prev"
(tail -f /dev/null | AUTOTERRA_HEADLESS=1 "$TML/dotnet/dotnet" tModLoader.dll -server -config "$CONFIG" -nosteam > "$HERE/server-console.txt" 2>&1) &
SERVER_SHELL=$!
WAITED=0
while (( WAITED < SECONDS_TO_RUN )); do
	sleep 2
	WAITED=$((WAITED + 2))
	if [[ -n "$STOP_PATTERN" && -f "$LOG" ]] && grep -aq "$STOP_PATTERN" "$LOG"; then
		echo "stop pattern seen after $WAITED s"
		break
	fi
done
pkill -f "$CONFIG"
sleep 3
pkill -P "$SERVER_SHELL" 2>/dev/null
kill "$SERVER_SHELL" 2>/dev/null
rm -f "$CONFIG"
echo "server stopped after ${WAITED} s; see $TML/tModLoader-Logs/server.log"
