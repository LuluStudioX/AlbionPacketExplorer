#!/bin/sh
# Sync once on boot, then periodically in the background, while nginx serves the feed.
set -eu

/usr/local/bin/sync.sh || echo "initial sync failed (will retry on interval)"

(
    while true; do
        sleep "${SYNC_INTERVAL:-300}"
        /usr/local/bin/sync.sh || echo "sync failed (keeping previous feed)"
    done
) &

exec nginx -g 'daemon off;'
