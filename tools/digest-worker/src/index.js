/**
 * apx-digest: receiver for AlbionPacketExplorer schema digests.
 *
 * POST /v1/digest   - validates a digest payload (shape + size), dedupes by content hash,
 *                     stores it in the DIGESTS KV namespace with a 60-day TTL. No auth:
 *                     the endpoint accepts nothing but digest-shaped JSON and stores no
 *                     requester identity.
 * GET  /v1/digests  - admin only (Authorization: Bearer <ADMIN_KEY>): returns every stored
 *                     digest with its metadata, for the repo's scheduled sync workflow.
 * DELETE /v1/digest/<key> - admin only: removes one entry. The sync workflow uses this to
 *                     clear entries it has already archived in the repo, keeping storage lean.
 *
 * POST /v1/protocol - validates a protocol-change payload (new/shifted enum codes detected in a
 *                     patched game client), dedupes by hash, stores under a "p:" key. No auth.
 * GET  /v1/protocols - admin only: lists every stored protocol submission.
 * DELETE /v1/protocol/<key> - admin only: removes one "p:" entry.
 *
 * Entries also expire on their own (60-day TTL) as a backstop, and a full KV (storage or
 * write budget) degrades to a clean 503 instead of an unhandled error.
 */

const MAX_BODY_BYTES = 2 * 1024 * 1024; // a full "all codes" digest stays well under this
const MAX_CODES = 3000;
const MAX_KEYS_PER_CODE = 80;
const MAX_TOP_PER_KEY = 8;
const MAX_VALUE_LEN = 64;
const TTL_SECONDS = 60 * 60 * 24 * 60; // 60 days: ample for any sync cadence
const KINDS = new Set(["EVENT", "REQUEST", "RESPONSE"]);

// Protocol-change submissions (new/shifted enum codes from a patched game client).
const MAX_CHANGES = 4000;
const CHANGE_TYPES = new Set(["added", "removed", "shifted"]);
const ENUM_NAMES = new Set(["EventCodes", "OperationCodes"]);

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/v1/digests") {
      return listDigests(request, env);
    }

    if (request.method === "DELETE" && url.pathname.startsWith("/v1/digest/")) {
      return deleteDigest(request, env, url);
    }

    if (request.method === "GET" && url.pathname === "/v1/protocols") {
      return listByPrefix(request, env, "p:");
    }

    if (request.method === "DELETE" && url.pathname.startsWith("/v1/protocol/")) {
      return deleteByPrefix(request, env, url, "/v1/protocol/", "p:");
    }

    if (request.method === "POST" && url.pathname === "/v1/protocol") {
      return handleProtocol(request, env);
    }

    if (request.method !== "POST" || url.pathname !== "/v1/digest") {
      return json({ ok: false, error: "not found" }, 404);
    }

    const ip = request.headers.get("CF-Connecting-IP") ?? "unknown";
    const { success } = await env.RATE_LIMITER.limit({ key: ip });
    if (!success) {
      return json({ ok: false, error: "rate limited" }, 429);
    }

    const length = Number(request.headers.get("Content-Length") ?? "0");
    if (length > MAX_BODY_BYTES) {
      return json({ ok: false, error: "too large" }, 413);
    }

    let body;
    try {
      body = await request.text();
    } catch {
      return json({ ok: false, error: "unreadable body" }, 400);
    }
    if (body.length > MAX_BODY_BYTES) {
      return json({ ok: false, error: "too large" }, 413);
    }

    let digest;
    try {
      digest = JSON.parse(body);
    } catch {
      return json({ ok: false, error: "invalid json" }, 400);
    }

    const error = validate(digest);
    if (error) {
      return json({ ok: false, error }, 422);
    }

    const hash = await sha256Hex(body);
    const key = `d:${digest.schemaCommit || "nocommit"}:${digest.mode}:${hash}`;

    // Same content = same key, so re-submissions are idempotent (no KV write wasted on
    // an existing entry; reads are 100x cheaper than writes on the free tier).
    const existing = await env.DIGESTS.get(key, { type: "stream" });
    if (existing === null) {
      try {
        await env.DIGESTS.put(key, body, {
          expirationTtl: TTL_SECONDS,
          metadata: {
            receivedAt: new Date().toISOString(),
            app: String(digest.app).slice(0, 64),
            codes: digest.codes.length,
          },
        });
      } catch {
        // KV write budget or storage exhausted: tell the client to retry later instead of
        // surfacing an opaque 1101. The sync workflow's archive+delete keeps this rare.
        return json({ ok: false, error: "storage busy, try again later" }, 503);
      }
    }

    return json({ ok: true, id: hash.slice(0, 12) });
  },
};

async function deleteDigest(request, env, url) {
  const auth = request.headers.get("Authorization") ?? "";
  if (!env.ADMIN_KEY || auth !== `Bearer ${env.ADMIN_KEY}`) {
    return json({ ok: false, error: "unauthorized" }, 401);
  }
  const key = decodeURIComponent(url.pathname.slice("/v1/digest/".length));
  if (!key.startsWith("d:")) {
    return json({ ok: false, error: "bad key" }, 400);
  }
  await env.DIGESTS.delete(key);
  return json({ ok: true });
}

async function listDigests(request, env) {
  const auth = request.headers.get("Authorization") ?? "";
  if (!env.ADMIN_KEY || auth !== `Bearer ${env.ADMIN_KEY}`) {
    return json({ ok: false, error: "unauthorized" }, 401);
  }

  const out = [];
  let cursor;
  do {
    const page = await env.DIGESTS.list({ cursor });
    for (const k of page.keys) {
      const body = await env.DIGESTS.get(k.name, { type: "json" });
      if (body !== null) {
        out.push({ key: k.name, metadata: k.metadata ?? {}, body });
      }
    }
    cursor = page.list_complete ? undefined : page.cursor;
  } while (cursor);

  return json(out);
}

async function handleProtocol(request, env) {
  const ip = request.headers.get("CF-Connecting-IP") ?? "unknown";
  const { success } = await env.RATE_LIMITER.limit({ key: ip });
  if (!success) return json({ ok: false, error: "rate limited" }, 429);

  const length = Number(request.headers.get("Content-Length") ?? "0");
  if (length > MAX_BODY_BYTES) return json({ ok: false, error: "too large" }, 413);

  let body;
  try {
    body = await request.text();
  } catch {
    return json({ ok: false, error: "unreadable body" }, 400);
  }
  if (body.length > MAX_BODY_BYTES) return json({ ok: false, error: "too large" }, 413);

  let payload;
  try {
    payload = JSON.parse(body);
  } catch {
    return json({ ok: false, error: "invalid json" }, 400);
  }

  const error = validateProtocol(payload);
  if (error) return json({ ok: false, error }, 422);

  const hash = await sha256Hex(body);
  const key = `p:${String(payload.clientVersion).slice(0, 32)}:${hash}`;

  const existing = await env.DIGESTS.get(key, { type: "stream" });
  if (existing === null) {
    try {
      await env.DIGESTS.put(key, body, {
        expirationTtl: TTL_SECONDS,
        metadata: {
          receivedAt: new Date().toISOString(),
          app: String(payload.app).slice(0, 64),
          clientVersion: String(payload.clientVersion).slice(0, 32),
          changes: payload.changes.length,
        },
      });
    } catch {
      return json({ ok: false, error: "storage busy, try again later" }, 503);
    }
  }

  return json({ ok: true, id: hash.slice(0, 12) });
}

/** Returns an error string, or null when the payload is a well-formed protocol submission. */
function validateProtocol(d) {
  if (typeof d !== "object" || d === null || Array.isArray(d)) return "not an object";
  if (d.v !== 1) return "unsupported version";
  if (typeof d.app !== "string" || d.app.length > 64) return "bad app";
  if (typeof d.clientVersion !== "string" || d.clientVersion.length > 32) return "bad clientVersion";
  if (!Array.isArray(d.changes) || d.changes.length === 0) return "no changes";
  if (d.changes.length > MAX_CHANGES) return "too many changes";

  for (const c of d.changes) {
    if (typeof c !== "object" || c === null) return "bad change entry";
    if (!ENUM_NAMES.has(c.enum)) return "bad enum";
    if (!CHANGE_TYPES.has(c.type)) return "bad type";
    if (typeof c.name !== "string" || c.name.length === 0 || c.name.length > 96) return "bad name";
    if (c.appCode !== null && (!Number.isInteger(c.appCode) || c.appCode < 0 || c.appCode > 65535))
      return "bad appCode";
    if (c.clientCode !== null && (!Number.isInteger(c.clientCode) || c.clientCode < 0 || c.clientCode > 65535))
      return "bad clientCode";
  }
  return null;
}

/** Admin-only paginated list of KV entries whose key starts with the given prefix. */
async function listByPrefix(request, env, prefix) {
  const auth = request.headers.get("Authorization") ?? "";
  if (!env.ADMIN_KEY || auth !== `Bearer ${env.ADMIN_KEY}`) {
    return json({ ok: false, error: "unauthorized" }, 401);
  }

  const out = [];
  let cursor;
  do {
    const page = await env.DIGESTS.list({ prefix, cursor });
    for (const k of page.keys) {
      const entry = await env.DIGESTS.get(k.name, { type: "json" });
      if (entry !== null) out.push({ key: k.name, metadata: k.metadata ?? {}, body: entry });
    }
    cursor = page.list_complete ? undefined : page.cursor;
  } while (cursor);

  return json(out);
}

/** Admin-only delete of one KV entry, guarded so only the intended prefix can be removed. */
async function deleteByPrefix(request, env, url, base, prefix) {
  const auth = request.headers.get("Authorization") ?? "";
  if (!env.ADMIN_KEY || auth !== `Bearer ${env.ADMIN_KEY}`) {
    return json({ ok: false, error: "unauthorized" }, 401);
  }
  const key = decodeURIComponent(url.pathname.slice(base.length));
  if (!key.startsWith(prefix)) return json({ ok: false, error: "bad key" }, 400);
  await env.DIGESTS.delete(key);
  return json({ ok: true });
}

/** Returns an error string, or null when the payload is a well-formed digest. */
function validate(d) {
  if (typeof d !== "object" || d === null || Array.isArray(d)) return "not an object";
  if (d.v !== 1) return "unsupported version";
  if (typeof d.app !== "string" || d.app.length > 64) return "bad app";
  if (typeof d.schemaCommit !== "string" || d.schemaCommit.length > 64) return "bad schemaCommit";
  if (d.mode !== "unknown" && d.mode !== "all") return "bad mode";
  if (!Array.isArray(d.codes) || d.codes.length === 0) return "no codes";
  if (d.codes.length > MAX_CODES) return "too many codes";

  for (const c of d.codes) {
    if (typeof c !== "object" || c === null) return "bad code entry";
    if (!KINDS.has(c.kind)) return "bad kind";
    if (!Number.isInteger(c.code) || c.code < 0 || c.code > 65535) return "bad code";
    if (!Number.isInteger(c.count) || c.count < 0) return "bad count";
    if (typeof c.keys !== "object" || c.keys === null) return "bad keys";

    const keys = Object.entries(c.keys);
    if (keys.length > MAX_KEYS_PER_CODE) return "too many keys";

    for (const [idx, k] of keys) {
      const i = Number(idx);
      if (!Number.isInteger(i) || i < 0 || i > 255) return "bad key index";
      if (typeof k !== "object" || k === null) return "bad key entry";
      if (!Array.isArray(k.types) || k.types.length > 8) return "bad types";
      if (!k.types.every((t) => typeof t === "string" && t.length <= 32)) return "bad type name";
      if (!Number.isInteger(k.present) || k.present < 0) return "bad present";
      if (k.top !== undefined && k.top !== null) {
        if (!Array.isArray(k.top) || k.top.length > MAX_TOP_PER_KEY) return "bad top";
        for (const t of k.top) {
          if (typeof t !== "object" || t === null) return "bad top entry";
          if (typeof t.v !== "string" || t.v.length > MAX_VALUE_LEN) return "bad top value";
          if (!Number.isInteger(t.n) || t.n < 0) return "bad top count";
        }
      }
    }
  }
  return null;
}

async function sha256Hex(text) {
  const data = new TextEncoder().encode(text);
  const buf = await crypto.subtle.digest("SHA-256", data);
  return [...new Uint8Array(buf)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function json(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
