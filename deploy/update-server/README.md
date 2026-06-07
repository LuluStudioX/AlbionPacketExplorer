# Self-hosted update feed

Serves the Velopack update feed from your own domain so the app can auto-update while the source
GitHub repo stays **private**. The GitHub token lives only on this server and is never shipped in
the client.

```
private GitHub repo  ──(CI builds Velopack feed, attaches to the release)
        │
        ▼  this container pulls the latest release assets (server-side token)
   nginx serves /srv/feed at  http://127.0.0.1:8080/apx/feed/
        │
        ▼  Cloudflare Tunnel
   https://projects.lulustudio.dk/apx/feed/   ◄── the app's SimpleWebSource base URL
```

The app points at `https://projects.lulustudio.dk/apx/feed` (constant `DefaultFeedUrl` in
`UpdateService`, overridable per machine with the `APX_UPDATE_URL` env var). Change the constant
if you use a different host.

> This `deploy/update-server/` is the standalone prototype. It is superseded by the
> `projects.lulustudio.dk` hub (see the hub plan), which serves the site and this feed together.

## How it works

- `sync.sh` calls the GitHub API for the latest release and downloads **all** its assets
  (`releases.<channel>.json`, `RELEASES-<channel>`, `*-full.nupkg`, …) into `/srv/feed`.
- `entrypoint.sh` syncs on boot, then every `SYNC_INTERVAL` seconds, while nginx serves the feed.
- The app's `SimpleWebSource` requests `…/apx/feed/releases.<rid>.json` then the listed `.nupkg`.
  Per-RID channels (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`) all live in the same folder.

## Setup

1. Create a **fine-grained PAT**, read-only **Contents** on the private repo only.
2. `cp .env.example .env` and set `GITHUB_TOKEN`.
3. `docker compose up -d --build`
4. Verify locally: `curl -s http://127.0.0.1:8080/apx/feed/releases.win-x64.json` → JSON with the
   current `Version`.

## Cloudflare Tunnel

Map a public hostname to the container with no open inbound ports:

```bash
cloudflared tunnel login
cloudflared tunnel create apx-updates
# Route DNS:
cloudflared tunnel route dns apx-updates projects.lulustudio.dk
# config.yml ingress -> service: http://127.0.0.1:8080
cloudflared tunnel run apx-updates
```

(Or uncomment the `cloudflared` service in `docker-compose.yml` and set `TUNNEL_TOKEN`.)

## Notes

- The feed is then publicly reachable (anyone with the URL can download the installers). It is not
  linked anywhere. To restrict it, put **Cloudflare Access** in front — but note the app updates
  anonymously, so an Access policy that blocks anonymous requests would also block auto-update.
- Because installers are served publicly, the usual GPL obligation applies: make the corresponding
  source available to recipients.
- The token is read-only and scoped to one repo; keep `.env` off version control.
