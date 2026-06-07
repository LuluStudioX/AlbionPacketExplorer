#!/bin/sh
# Mirror the latest GitHub release's assets (the Velopack feed) into the served folder.
# The token is read from the environment and stays on the server; it is never shipped to clients.
set -eu

: "${GITHUB_REPO:?set GITHUB_REPO=owner/repo}"
: "${GITHUB_TOKEN:?set GITHUB_TOKEN to a read-only Contents PAT}"
FEED_DIR="${FEED_DIR:-/srv/feed}"

api="https://api.github.com/repos/${GITHUB_REPO}"
auth="Authorization: Bearer ${GITHUB_TOKEN}"
mkdir -p "$FEED_DIR"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Latest non-prerelease release and its assets.
curl -fsSL -H "$auth" -H "Accept: application/vnd.github+json" "$api/releases/latest" > "$tmp/rel.json"

jq -r '.assets[] | "\(.id)\t\(.name)"' "$tmp/rel.json" | while IFS="$(printf '\t')" read -r id name; do
    # Download to a .part then atomically rename so clients never read a half-written file.
    curl -fsSL -H "$auth" -H "Accept: application/octet-stream" \
        "$api/releases/assets/${id}" -o "$FEED_DIR/${name}.part"
    mv -f "$FEED_DIR/${name}.part" "$FEED_DIR/${name}"
done

echo "synced $(date -u '+%Y-%m-%dT%H:%M:%SZ') -> $FEED_DIR"
