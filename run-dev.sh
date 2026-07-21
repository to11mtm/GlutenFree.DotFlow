#!/usr/bin/env bash
#
# Starts the DotFlow API and Blazor UI together for local development.
# Streams both projects' output; press Ctrl-C once to stop both, just like a single `dotnet run`.
#
# Both projects are started on the same scheme so the browser does not block cross-origin
# (or mixed-content) API calls.
#
# Usage:
#   ./run-dev.sh            # http  - API :5213, UI :5277
#   ./run-dev.sh https      # https - API :7018, UI :7188
#
# Note: for https you must also point the UI at the https API via
# Workflow.UI/Workflow.UI.Client/wwwroot/appsettings.json (Api:BaseUrl).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

PROFILE="${1:-http}"
if [[ "$PROFILE" != "http" && "$PROFILE" != "https" ]]; then
    echo "Unknown profile '$PROFILE' (expected 'http' or 'https')." >&2
    exit 1
fi

# Strip inherited hot-reload / browser-refresh env vars so this behaves like a plain `dotnet run`.
# If launched from an IDE terminal or a `dotnet watch` shell, these would otherwise make the app
# inject a reference to /_framework/blazor-hotreload.js that a plain run doesn't serve.
unset DOTNET_MODIFIABLE_ASSEMBLIES __ASPNETCORE_BROWSER_TOOLS ASPNETCORE_AUTO_RELOAD_WS_ENDPOINT \
      ASPNETCORE_AUTO_RELOAD_WS_KEY DOTNET_HOTRELOAD_NAMEDPIPE_NAME DOTNET_WATCH 2>/dev/null || true

cleanup() {
    # Disarm the trap, then signal the whole process group so both dotnet children stop.
    trap - INT TERM EXIT
    echo ""
    echo "Stopping DotFlow..."
    kill 0 2>/dev/null || true
}
trap cleanup INT TERM EXIT

echo "→ starting Workflow.Api ($PROFILE)..."
dotnet run --project Workflow.Api --launch-profile "$PROFILE" &

echo "→ starting Workflow.UI ($PROFILE)..."
dotnet run --project Workflow.UI/Workflow.UI --launch-profile "$PROFILE" &

echo ""
if [[ "$PROFILE" == "http" ]]; then
    echo "DotFlow is starting up:"
    echo "  API : http://localhost:5213  (Swagger at /swagger)"
    echo "  UI  : http://localhost:5277"
else
    echo "DotFlow is starting up (https):"
    echo "  API : https://localhost:7018  (Swagger at /swagger)"
    echo "  UI  : https://localhost:7188"
fi
echo ""
echo "Press Ctrl-C to stop both."

# Wait for either child to exit; the trap handles cleanup of the survivor.
wait -n
