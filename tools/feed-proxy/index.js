// Legacy update-feed proxy.
//
// Already-installed clients (<= 0.18.6) read their Velopack feed from
//   https://projects.lulustudio.dk/apx/feed/<file>
// which used to be static files published by hand and went stale. The repo is public now and new
// clients read GitHub releases directly (Velopack GithubSource), so this Worker makes the legacy
// path a thin passthrough to the latest GitHub release assets: the hub always serves the right
// packages with zero upkeep, and old installs update themselves onto the GitHub-based client.
//
// Route it at:  projects.lulustudio.dk/apx/feed/*
// Everything else on that host still falls through to GitHub Pages (the project page).

const REPO = "LuluStudioX/AlbionPacketExplorer";
const PREFIX = "/apx/feed/";

export default {
  async fetch(request) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith(PREFIX)) {
      return new Response("Not found", { status: 404 });
    }

    // Only ever serve a single flat asset name (releases.<channel>.json or *.nupkg). Reject anything
    // with path traversal so this can never be pointed at an arbitrary URL.
    const file = url.pathname.slice(PREFIX.length);
    if (!file || file.includes("/") || file.includes("..")) {
      return new Response("Bad request", { status: 400 });
    }

    const target = `https://github.com/${REPO}/releases/latest/download/${file}`;
    const upstream = await fetch(target, {
      redirect: "follow",
      headers: { "user-agent": "apx-feed-proxy" },
    });

    // Pass the body through; keep the feed cacheable but short so a new release shows up quickly.
    const headers = new Headers();
    headers.set("content-type", upstream.headers.get("content-type") || "application/octet-stream");
    const len = upstream.headers.get("content-length");
    if (len) headers.set("content-length", len);
    headers.set("cache-control", "public, max-age=300");
    return new Response(upstream.body, { status: upstream.status, headers });
  },
};
