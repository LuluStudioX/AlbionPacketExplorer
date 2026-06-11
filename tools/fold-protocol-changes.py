#!/usr/bin/env python3
"""Fold anonymous protocol-change submissions (from the apx-digest worker's /v1/protocols) into
the C# enums.

Reads /tmp/protocols.json (the worker's admin list), archives new submissions under
tools/digest-worker/protocols/, aggregates every archived submission, then:

  - auto-appends ADDED codes that are clean tail appends (next ordinal) to EventCodes.cs /
    OperationCodes.cs - the safe, common case (a patch adds events at the end);
  - flags everything else (shifted, removed, or non-tail additions) in
    tools/protocol-proposals.md for a maintainer to apply by hand, since reordering an enum is
    protocol-critical and must not be automated blindly.

The PR this feeds is reviewed before merge, so auto-applied edits always get a human gate.
Writes `new` (newly archived submissions) and `applied` (enum members added) to GITHUB_OUTPUT.
"""
import json
import os
import re
import pathlib

PROTO_DIR = pathlib.Path("tools/digest-worker/protocols")
EVENT_CS = pathlib.Path("src/AlbionPacketExplorer/Network/EventCodes.cs")
OP_CS = pathlib.Path("src/AlbionPacketExplorer/Network/OperationCodes.cs")
REPORT = pathlib.Path("tools/protocol-proposals.md")


def archive_new():
    PROTO_DIR.mkdir(parents=True, exist_ok=True)
    docs = json.load(open("/tmp/protocols.json"))
    new = 0
    for d in docs:
        parts = d["key"].split(":")  # p:<clientVersion>:<sha256>
        if len(parts) != 3:
            continue
        f = PROTO_DIR / f"{parts[1]}-{parts[2][:12]}.json"
        if not f.exists():
            f.write_text(json.dumps(d["body"], separators=(",", ":")) + "\n", encoding="utf-8")
            new += 1
    return new


def aggregate():
    # (enum, type, name, clientCode, appCode) -> {count, versions}
    agg = {}
    for f in sorted(PROTO_DIR.glob("*.json")):
        try:
            body = json.loads(f.read_text(encoding="utf-8"))
        except Exception:
            continue
        ver = str(body.get("clientVersion", "?"))
        for c in body.get("changes", []):
            key = (c.get("enum"), c.get("type"), c.get("name"), c.get("clientCode"), c.get("appCode"))
            e = agg.setdefault(key, {"count": 0, "versions": set()})
            e["count"] += 1
            e["versions"].add(ver)
    return agg


def parse_event(text):
    names, maxval = {}, 0
    for m in re.finditer(r"^\s*([A-Za-z_]\w*)\s*=\s*(\d+)\s*,", text, re.M):
        v = int(m.group(2))
        names[m.group(1)] = v
        maxval = max(maxval, v)
    return names, maxval


def parse_op(text):
    names, count = {}, 0
    body = text[text.index("{") + 1:]
    for line in body.splitlines():
        s = line.split("//")[0].strip().rstrip(",").strip()
        if not s or s in ("{", "}"):
            continue
        m = re.match(r"^([A-Za-z_]\w*)(?:\s*=\s*(\d+))?$", s)
        if not m:
            continue
        v = int(m.group(2)) if m.group(2) else count
        names[m.group(1)] = v
        count = v + 1
    return names, count


def append_before_close(text, lines):
    return re.sub(r"\n\}\s*$", "\n" + lines + "}\n", text)


def main():
    new = archive_new()
    agg = aggregate()

    event_text = EVENT_CS.read_text(encoding="utf-8")
    op_text = OP_CS.read_text(encoding="utf-8")
    ev_names, ev_max = parse_event(event_text)
    op_names, op_count = parse_op(op_text)

    applied, flagged = [], []
    ev_new, op_new = [], []

    # ADDED first, lowest client code first, so consecutive tail appends chain cleanly.
    added = sorted(
        [(k, v) for k, v in agg.items() if k[1] == "added" and k[3] is not None],
        key=lambda kv: kv[0][3],
    )
    for (enum, _t, name, cc, ac), info in added:
        if enum == "EventCodes":
            if name in ev_names:
                continue
            if cc == ev_max + 1:
                ev_new.append((name, cc))
                ev_names[name] = cc
                ev_max = cc
                applied.append((enum, name, cc, info))
            else:
                flagged.append((enum, "added (non-tail)", name, ac, cc, info))
        elif enum == "OperationCodes":
            if name in op_names:
                continue
            if cc == op_count:
                op_new.append(name)
                op_names[name] = cc
                op_count += 1
                applied.append((enum, name, cc, info))
            else:
                flagged.append((enum, "added (non-tail)", name, ac, cc, info))

    for (enum, typ, name, cc, ac), info in agg.items():
        if typ in ("shifted", "removed"):
            flagged.append((enum, typ, name, ac, cc, info))

    if ev_new:
        EVENT_CS.write_text(
            append_before_close(event_text, "".join(f"    {n} = {c},\n" for n, c in ev_new)),
            encoding="utf-8")
    if op_new:
        OP_CS.write_text(
            append_before_close(op_text, "".join(f"    {n},\n" for n in op_new)),
            encoding="utf-8")

    write_report(agg, applied, flagged)

    with open(os.environ.get("GITHUB_OUTPUT", os.devnull), "a") as out:
        out.write(f"new={new}\n")
        out.write(f"applied={len(applied)}\n")
    print(f"archived {new} new submission(s); applied {len(applied)} enum add(s), "
          f"flagged {len(flagged)} for review")


def write_report(agg, applied, flagged):
    versions = sorted({v for e in agg.values() for v in e["versions"] if v != "?"})
    lines = ["# Protocol change proposals", "",
             "Aggregated from anonymous client submissions to the apx-digest worker. Tail-append "
             "additions are applied in this PR's enum diff; everything else needs manual review "
             "(reordering an enum after a shift is protocol-critical).", "",
             f"Client versions seen: {', '.join(versions) if versions else 'unknown'}", ""]

    lines.append("## Applied (appended to enums)")
    if applied:
        for enum, name, cc, info in sorted(applied, key=lambda a: (a[0], a[2])):
            lines.append(f"- `{enum}.{name}` = {cc}  ({info['count']} submission(s))")
    else:
        lines.append("- none")
    lines.append("")

    lines.append("## Needs manual review")
    if flagged:
        for enum, typ, name, ac, cc, info in sorted(flagged, key=lambda a: (a[0], a[1], str(a[2]))):
            detail = f"code {ac} -> {cc}" if typ == "shifted" else (f"was {ac}" if typ == "removed" else f"= {cc}")
            lines.append(f"- {typ} `{enum}.{name}` ({detail}) - {info['count']} submission(s)")
    else:
        lines.append("- none")
    lines.append("")

    REPORT.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
