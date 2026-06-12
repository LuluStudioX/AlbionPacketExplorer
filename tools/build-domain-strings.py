#!/usr/bin/env python3
"""Build src/AlbionPacketExplorer/Assets/domain-strings.json: value-sets for STRING-typed packet
params whose values are domain identifiers (not numbers, not @LOC keys). The first set is
"accessrights": the access-role names (owner, coowner, builder, ...) that appear in packets like
EVENT 210 AccessStatus, mapped to readable English via ao-bin-dumps.

A string param tagged resolveAs "str:<set>" then resolves its raw value to the English meaning.
Regenerate after a client/ao-bin update.

    python tools/build-domain-strings.py
"""
import json, re
from pathlib import Path

APX = Path(__file__).resolve().parents[1]
AOBIN = APX / "ideas" / "ao-bin-dumps"
OUT = APX / "src" / "AlbionPacketExplorer" / "Assets" / "domain-strings.json"


def load_loc():
    tus = json.loads((AOBIN / "localization.json").read_text(encoding="utf-8"))["tmx"]["body"]["tu"]
    out = {}
    for tu in tus:
        key = (tu.get("@tuid") or "")
        tuv = tu.get("tuv")
        if isinstance(tuv, dict):
            tuv = [tuv]
        for seg in tuv or []:
            if seg.get("@xml:lang", "").upper().startswith("EN"):
                if isinstance(seg.get("seg"), str):
                    out[key] = seg["seg"]
                break
    return out


def accessrights(loc):
    d = json.loads((AOBIN / "accessrights.json").read_text(encoding="utf-8"))
    classes = d["accessrights"]["accessclass"]
    if isinstance(classes, dict):
        classes = [classes]
    roles = {}
    for c in classes:
        rs = c.get("role", [])
        if isinstance(rs, dict):
            rs = [rs]
        for r in rs:
            name = r["@name"]
            disp = loc.get(r.get("@displayname", ""), "")
            roles[name] = disp or name.capitalize()
    # common sentinel seen in capture (no role granted)
    roles.setdefault("noaccess", "No access")
    return roles


def main():
    loc = load_loc()
    result = {
        "accessrights": accessrights(loc),
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(result, indent=2, ensure_ascii=True, sort_keys=True) + "\n",
                   encoding="utf-8")
    for k, v in result.items():
        print(f"{k}: {len(v)} values -> {v}")
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
