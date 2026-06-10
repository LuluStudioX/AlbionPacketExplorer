#!/usr/bin/env python3
"""Build tools/inferred-overlay.json from decoded captures and/or schema digests.

Inputs (mixed freely on the command line):
  - decoded capture: Save-as-JSON array or NDJSON of {ts, kind, code, params}
  - schema digest:   {"v":1, "codes":[...]} produced by the app's Share Field Data dialog
                     (pulled from KV via tools/digest-worker/pull-digests.ps1)

For every (kind, code) seen, emit a per-byte-key descriptor inferred from the observed wire
shape: dominant type, presence %, cardinality, numeric range, and a conservative name guess
for unambiguous shapes (timestamp / objectId / position / vector / flags). Every entry is
marked "(inferred)" in its note. generate-schema.py merges this overlay at LOWEST precedence:
it only fills key slots that the enum/decoder/hand-curated passes left empty, so authoritative
field names always win. Re-run on the full file set to extend coverage:

    python tools/build-inference-overlay.py <capture.json> <digest.json> [more ...]
"""
import argparse, json, sys, collections
from pathlib import Path

APX = Path(__file__).resolve().parents[1]
OUT = APX / "tools/inferred-overlay.json"
ECHO = {"EVENT": "252", "REQUEST": "253", "RESPONSE": "253"}
TICKS_FLOOR = 6.0e17  # DateTime.Ticks for recent years ~ 6.3e17


def load(path):
    text = Path(path).read_text(encoding="utf-8-sig")
    s = text.lstrip()
    if s.startswith("["):
        yield from json.loads(text)
        return
    for line in text.splitlines():
        line = line.strip()
        if line:
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                pass


def read_digest(path):
    """Returns the parsed digest dict when the file is a schema digest, else None."""
    try:
        doc = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    except (json.JSONDecodeError, UnicodeDecodeError):
        return None
    if isinstance(doc, dict) and doc.get("v") == 1 and isinstance(doc.get("codes"), list):
        return doc
    return None


def key_stats(e, pk):
    return e["keys"].setdefault(pk, {"pres": 0, "t": collections.Counter(),
                                     "v": collections.Counter(), "nmin": None, "nmax": None,
                                     "dmin": 0})


def fold_digest(agg, digest):
    """Folds a digest's pre-aggregated stats into the same accumulators the capture path
    fills. Digests carry no per-type counts, so each seen type is weighted by presence;
    the distinct count survives as a floor (dmin) since only the top values travel."""
    for c in digest.get("codes", []):
        kind, code = c.get("kind"), c.get("code")
        if kind is None or code is None:
            continue
        e = agg.setdefault((kind, code), {"count": 0, "keys": {}})
        e["count"] += c.get("count", 0)
        echo = ECHO.get(kind)
        for pk, k in (c.get("keys") or {}).items():
            if pk == echo or not isinstance(k, dict):
                continue
            ks = key_stats(e, pk)
            pres = k.get("present", 0)
            ks["pres"] += pres
            for t in (k.get("types") or []):
                ks["t"][t] += max(pres, 1)
            for tv in (k.get("top") or []):
                if isinstance(tv, dict) and "v" in tv:
                    ks["v"][str(tv["v"])] += tv.get("n", 1)
            dmin = k.get("distinct", 0)
            if k.get("capped"):
                dmin = max(dmin, 500)
            ks["dmin"] = max(ks["dmin"], dmin)
            if isinstance(k.get("min"), (int, float)):
                ks["nmin"] = k["min"] if ks["nmin"] is None else min(ks["nmin"], k["min"])
            if isinstance(k.get("max"), (int, float)):
                ks["nmax"] = k["max"] if ks["nmax"] is None else max(ks["nmax"], k["max"])


def arity(sample):
    sample = sample.strip()
    if not (sample.startswith("[") and sample.endswith("]")):
        return None
    inner = sample[1:-1].strip()
    return 0 if not inner else inner.count(",") + 1


def guess(key, dom, pres_pct, distinct, nmin, nmax, top):
    arr = dom.endswith("[]")
    if dom == "Int64" and nmin is not None and nmin >= TICKS_FLOOR:
        return "timestamp", "likely .NET ticks (UTC)"
    if dom == "String":
        return "", "string value"
    if arr and dom.startswith("Single"):
        a = arity(top[0][0]) if top else None
        if a == 2:
            return "position", "Single[2] world coords {x, y}?"
        return "vector", f"{dom}"
    if arr:
        return "", f"{dom} array"
    if key == "0" and dom in ("Int64", "Int32") and distinct >= 50 and (nmax or 0) < 5_000_000:
        return "objectId", "high-cardinality id at key 0"
    if dom == "Byte" and distinct <= 4:
        return "flags", "small enum/flag byte"
    if dom == "Guid":
        return "guid", "16-byte GUID"
    return "", ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+", help="captures and/or digests, mixed freely")
    ap.add_argument("--out", default=str(OUT),
                    help="overlay to write (default tools/inferred-overlay.json; the CI digest "
                         "sync writes tools/digest-overlay.json from the digest archive alone)")
    args = ap.parse_args()
    caps = args.files
    out_path = Path(args.out)

    agg = {}
    digests = 0
    for cap in caps:
        digest = read_digest(cap)
        if digest is not None:
            fold_digest(agg, digest)
            digests += 1
            continue
        for pkt in load(cap):
            if not isinstance(pkt, dict):
                continue
            kind, code = pkt.get("kind"), pkt.get("code")
            if kind is None or code is None:
                continue
            e = agg.setdefault((kind, code), {"count": 0, "keys": {}})
            e["count"] += 1
            echo = ECHO.get(kind)
            for pk, pv in (pkt.get("params") or {}).items():
                if pk == echo:
                    continue
                ks = key_stats(e, pk)
                ks["pres"] += 1
                t = pv.get("type") if isinstance(pv, dict) else None
                v = pv.get("value") if isinstance(pv, dict) else pv
                ks["t"][t] += 1
                sv = str(v)
                if len(ks["v"]) < 80 or sv in ks["v"]:
                    ks["v"][sv] += 1
                if isinstance(v, (int, float)) and not isinstance(v, bool):
                    ks["nmin"] = v if ks["nmin"] is None else min(ks["nmin"], v)
                    ks["nmax"] = v if ks["nmax"] is None else max(ks["nmax"], v)

    overlay = {"$meta": {"source": "AlbionPacketExplorer capture inference",
                         "captures": [Path(c).name for c in caps],
                         "note": "Lowest-precedence fill. Every param is (inferred) from wire shape."}}
    for (kind, code), e in sorted(agg.items()):
        cnt = e["count"]
        params = {}
        for pk, ks in sorted(e["keys"].items(), key=lambda x: int(x[0])):
            dom = ks["t"].most_common(1)[0][0] or "?"
            types = "/".join(t for t, _ in ks["t"].most_common())
            distinct = max(len(ks["v"]), ks.get("dmin", 0))
            pct = round(ks["pres"] * 100 / cnt)
            name, hint = guess(pk, dom, pct, distinct, ks["nmin"], ks["nmax"], ks["v"].most_common(3))
            bits = [types if "/" in types else dom]
            if pct < 100:
                bits.append(f"{pct}% present")
            bits.append("high-card" if distinct >= 80 else f"{distinct} distinct")
            if ks["nmin"] is not None and dom not in ("Single",) and not dom.endswith("[]"):
                bits.append(f"range [{ks['nmin']}..{ks['nmax']}]")
            elif dom == "Single" and ks["nmin"] is not None:
                bits.append(f"range [{ks['nmin']:.3g}..{ks['nmax']:.3g}]")
            note = "(inferred) " + "; ".join(bits)
            if hint:
                note += f"; {hint}"
            params[pk] = {"name": name, "note": note, "resolveAs": ""}
        overlay[f"{kind}:{code}"] = {"count": cnt, "params": params}

    out_path.write_text(json.dumps(overlay, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    rel = out_path.relative_to(APX) if out_path.is_relative_to(APX) else out_path
    print(f"inputs: {len(caps)} ({digests} digest)  codes: {len(agg)}  -> {rel}")


if __name__ == "__main__":
    main()
