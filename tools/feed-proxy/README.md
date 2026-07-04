# apx-feed-proxy

Thin Cloudflare Worker that proxies the **legacy** Velopack update feed to the public GitHub
releases, so already-installed clients keep updating without a hand-maintained feed.

- New clients (0.18.7+) read GitHub directly via Velopack `GithubSource`, so they never hit this.
- Old clients read `https://projects.lulustudio.dk/apx/feed/<file>`; this Worker rewrites that to
  `https://github.com/LuluStudioX/AlbionPacketExplorer/releases/latest/download/<file>`.
- The digest ingest Worker (`apx-digest`, `/v1/digest` + `/v1/protocol`) is unrelated and untouched.

## Deploy (one time)

Requires an account-scoped `wrangler` login (the MCP token used during development is read-only).

1. `projects.lulustudio.dk` DNS record: set **Proxied** (orange cloud). It is a `CNAME` to
   `lulu195.github.io`; GitHub Pages behind Cloudflare needs zone SSL mode **Full** (not Flexible)
   so the origin fetch stays HTTPS. The apex is already proxied, so the zone mode is likely fine,
   just confirm it is Full or Full(strict).
2. `cd tools/feed-proxy && npx wrangler deploy` (creates the `apx-feed-proxy` Worker and the
   `projects.lulustudio.dk/apx/feed/*` route).
3. Verify: `curl -sSL https://projects.lulustudio.dk/apx/feed/releases.win-x64.json | head` should
   list the latest release (e.g. 0.18.6), not the stale 0.18.5.

Only `/apx/feed/*` is intercepted; every other path on the host still serves the GitHub Pages
project page. To roll back, delete the route (or set the DNS record back to DNS-only).
