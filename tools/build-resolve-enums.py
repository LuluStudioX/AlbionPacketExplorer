#!/usr/bin/env python3
"""Build src/AlbionPacketExplorer/Assets/resolve-enums.json from the private decode findings.

The decode (ideas/decode-findings/, gitignored) holds every client enum verbatim. This script
copies a CURATED short-list of small, stable, packet-relevant helper enums into a tracked asset
the app embeds, so a param tagged `resolveAs: "enum:<Name>"` resolves an int to its member name.

Only the enums in PICK are exported (the full dump stays private). Re-run after a client re-dump;
member values can renumber across patches, so this asset is regenerated, never hand-edited.

    python tools/build-resolve-enums.py
"""
import json
import re
from pathlib import Path

APX = Path(__file__).resolve().parents[1]
ENUMS = APX / "ideas" / "decode-findings" / "enums"
CROSSCHECK = APX / "ideas" / "decode-findings" / "review" / "enum-crosscheck.json"
OUT = APX / "src" / "AlbionPacketExplorer" / "Assets" / "resolve-enums.json"

# The shipped set = every STRONG (string-literal-verified) enum in a game-domain namespace from the
# cross-check sweep (tools/.../cross_check.py). Game-domain = Albion.* or empty namespace, excluding
# engine/framework namespaces. Falls back to a small hand list if the cross-check has not been run.
GAME_NS_OK = ("Albion", "AlbionGui")
GAME_NS_SKIP = ("Unity", "System", "TMPro", "Cinemachine", "Wwise", "Newtonsoft")
HAND_FALLBACK = [
    "ActionComponentType", "ActionOnBuildingErrorCode", "EquipmentSlot", "GuildRole",
    "Faction", "FlaggingStatus", "Rarity", "ChestState", "CastleGateState", "RcGeneric",
]

MEMBER = re.compile(r"^\s*(?:public\s+const\s+\w+\s+)?(\w+)\s*=\s*(-?\d+)\s*;", re.M)


def is_game_domain(ns):
    ns = ns or ""
    if any(s in ns for s in GAME_NS_SKIP):
        return False
    return ns == "" or any(ns.startswith(p) for p in GAME_NS_OK)


def pick_enums():
    if not CROSSCHECK.exists():
        print("cross-check not found; using hand fallback. Run cross_check.py first for the full set.")
        return [(n, None) for n in HAND_FALLBACK]
    strong = json.loads(CROSSCHECK.read_text(encoding="utf-8"))["strong"]
    picked = [(e["enum"], e.get("members")) for e in strong if is_game_domain(e.get("ns"))]
    # de-dup by enum name (first wins), keep deterministic order
    seen, out = set(), []
    for name, members in sorted(picked, key=lambda t: t[0]):
        if name not in seen:
            seen.add(name)
            out.append((name, members))
    return out


def find_enum(name):
    hits = list(ENUMS.rglob(f"{name}.tdi*.md"))
    if not hits:
        raise SystemExit(f"enum not found in decode findings: {name}")
    return hits[0]


def parse_members(path):
    text = path.read_text(encoding="utf-8")
    out = {}
    for m in MEMBER.finditer(text):
        member, value = m.group(1), m.group(2)
        if member == "value__":          # IL2CPP backing field, not a member
            continue
        out[value] = member               # keyed by string value for direct JSON lookup
    return out


def main():
    result = {}
    for name, members_from_crosscheck in pick_enums():
        # Prefer members already in the cross-check json; else re-parse the finding file.
        members = members_from_crosscheck or parse_members(find_enum(name))
        if not members:
            print(f"  skip {name}: no members")
            continue
        result[name] = members
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(result, indent=2, ensure_ascii=True, sort_keys=True) + "\n",
                   encoding="utf-8")
    print(f"wrote {OUT} ({len(result)} enums, "
          f"{sum(len(v) for v in result.values())} members)")


if __name__ == "__main__":
    main()
