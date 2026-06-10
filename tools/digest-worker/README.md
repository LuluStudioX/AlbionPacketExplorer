# apx-digest worker

Cloudflare Worker that receives anonymous schema digests from AlbionPacketExplorer's
"Share field data" dialog and stores them in Workers KV for offline folding into the
packet schema.

- Endpoint: `https://apx-digest.workpressfail2ban.workers.dev/v1/digest` (POST only)
- The client constant lives in `src/AlbionPacketExplorer/Services/DigestUploadService.cs`.

## Design

- **No secrets anywhere.** The endpoint is public but accepts nothing except digest-shaped
  JSON (strict shape validation, 2 MB cap). There is no read endpoint; retrieval goes
  through wrangler auth only.
- **Dedupe by content hash.** KV key = `d:<schemaCommit>:<mode>:<sha256(body)>`, so
  identical re-submissions cost one read, not a write.
- **Rate limit** 10 submissions/min per IP via the Workers rate-limiting binding.
- **Free tier**: 100k requests/day, 1k KV writes/day - orders of magnitude above expected
  volume.
- **Privacy**: the client only ever sends field statistics (types, presence, ranges,
  whitelisted game identifiers). The worker stores no IP or requester identity; metadata
  is receivedAt + app version + code count.

## Automation (machine-independent)

`.github/workflows/digest-sync.yml` runs daily at 09:00 UTC (and on manual dispatch):
fetches digests via `GET /v1/digests` (Bearer `ADMIN_KEY`), archives new ones under
`digests/`, rebuilds `tools/digest-overlay.json`, regenerates the schema, build-gates,
and opens a PR. Needs no local machine. KV entries expire after 60 days on their own.

The admin key exists only as a Worker secret + the repo secret `APX_DIGEST_ADMIN_KEY`.
To rotate: generate a random string, then
`npx wrangler secret put ADMIN_KEY` and `gh secret set APX_DIGEST_ADMIN_KEY --body <key>`.

## Operations

```powershell
# deploy after changes
npx wrangler deploy

# inspect stored digests
npx wrangler kv key list --binding DIGESTS --remote

# pull everything into ./digests/ for schema folding
.\pull-digests.ps1            # keep in KV
.\pull-digests.ps1 -Delete    # drain KV after fetching

# full pipeline: drain KV -> rebuild overlay (captures + digests) -> regen schema -> build
.\sync-digests.ps1            # leaves git changes for review
.\sync-digests.ps1 -Commit    # commits schema files when build passes (never pushes)
# scheduled task "APX Digest Sync" runs sync-digests.ps1 -Commit -Log daily at 10:00
# (commits are local only, push stays manual)
#
# schema source resolution: clean SAT clone preferred (freshest enums + decoder param-sets);
# when the SAT clone is missing or its Network project is dirty, the sync falls back to this
# repo's own enum copies (src/AlbionPacketExplorer/Network/) and decoder names carry forward
# by name - the automation never hard-depends on the SAT checkout.

# live request log
npx wrangler tail
```

Digest JSON format: see `SchemaDigest` /
`SchemaDigestBuilder` in `src/AlbionPacketExplorer/Services/SchemaDigest.cs`.
