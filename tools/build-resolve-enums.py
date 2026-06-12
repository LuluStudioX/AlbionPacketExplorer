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
OUT = APX / "src" / "AlbionPacketExplorer" / "Assets" / "resolve-enums.json"

# Curated, high-confidence packet-relevant enums (from ideas/decode-findings/review/enums-resolveAs.md).
PICK = [
    "ActionComponentType", "ActionOnBuildingErrorCode", "EquipmentSlot", "GuildRole",
    "Faction", "FlaggingStatus", "Rarity", "ChestState", "CastleGateState", "RcGeneric",
]

MEMBER = re.compile(r"^\s*(?:public\s+const\s+\w+\s+)?(\w+)\s*=\s*(-?\d+)\s*;", re.M)


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
    for name in PICK:
        path = find_enum(name)
        members = parse_members(path)
        if not members:
            raise SystemExit(f"no members parsed for {name} ({path})")
        result[name] = members
        print(f"{name}: {len(members)} members")
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(result, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
    print(f"wrote {OUT} ({len(result)} enums)")


if __name__ == "__main__":
    main()
