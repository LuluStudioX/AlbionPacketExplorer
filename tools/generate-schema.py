#!/usr/bin/env python3
"""Resync packet-schema.base.json with the current EventCodes / OperationCodes enums found in a
reference C# source repo.

Model: RE-KEYS the existing schema to the current enum ordinals and applies the island field-map
overrides below. Curated param annotations carry forward BY NAME (a param byte-index map is tied
to the packet structure i.e. the event/op name, not the numeric code, so it survives code
renumbering across game patches).

The parser strips inline `// comments` before reading each member and honors explicit `= N`
resets, so the ordinal counter stays in sync. Enum ordinal == wire code == the `code` field in a
captured packet.

Usage:
    python tools/generate-schema.py [--source-path PATH]
    (PATH defaults to env APX_SOURCE_REPO, else a sibling reference-source clone.)
"""
import argparse
import json
import os
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

APX = Path(__file__).resolve().parents[1]
OUT = APX / "src/AlbionPacketExplorer/Assets/packet-schema.base.json"

# Bump when the schema's shape or curation method changes (not on routine resyncs).
SCHEMA_VERSION = "1"


def source_commit(repo: Path) -> str:
    """Short commit of the reference source the schema was built from; 'unknown' if git fails."""
    try:
        out = subprocess.run(
            ["git", "-C", str(repo), "rev-parse", "--short", "HEAD"],
            capture_output=True, text=True, timeout=10,
        )
        return out.stdout.strip() or "unknown"
    except Exception:
        return "unknown"


def find_one(root: Path, name: str):
    """First file named `name` anywhere under `root` (so the repo layout is not hardcoded)."""
    return next(iter(sorted(root.rglob(name))), None)


def parse_enum(path: Path):
    """Return ordered list of (ordinal, name) honoring explicit `= N` resets."""
    text = path.read_text(encoding="utf-8-sig")
    body = text[text.index("{") + 1: text.rindex("}")]
    members, counter = [], 0
    for raw in body.splitlines():
        line = re.sub(r"//.*$", "", raw).strip()  # strip trailing/whole-line comments first
        if not line:
            continue
        line = line.rstrip(",").strip()
        if not line:
            continue
        if "=" in line:
            name, val = line.split("=", 1)
            name = name.strip()
            counter = int(val.strip())
        else:
            name = line
        if not re.fullmatch(r"[A-Za-z_]\w*", name):
            print(f"  ! skip unparsable enum line: {raw!r}", file=sys.stderr)
            continue
        members.append((counter, name))
        counter += 1
    return members


def p(name, note="", resolve=""):
    return {"name": name, "note": note, "resolveAs": resolve}


# ---- island / corrected overrides (from current reference event constructors) ----
EVENT_OVERRIDE = {
    "NewBuilding": {
        "0": p("objectId"),
        "1": p("buildingGuid", "16-byte building GUID (Byte[])"),
        "2": p("houseObjectId"),
        "3": p("uniqueName", "Building UniqueName"),
        "4": p("position", "World position Single[] {X, Y}"),
        "7": p("nutrition", "Current nutrition"),
        "8": p("plantedAt", ".NET ticks (UTC); lastActionAt"),
        "9": p("housePlotGuid", "16-byte plot GUID (Byte[])"),
        "11": p("islandOwnerName"),
        "13": p("laborerFirstName"),
        "14": p("laborerLastName"),
        "16": p("hasPremium", "bool"),
    },
    "FarmBuildingInfo": {
        "0": p("objectId"),
        "4": p("elapsedGrowTime", "Elapsed grow time in 100us units"),
        "5": p("serverNow", ".NET ticks (UTC) server now; PlantedAt = serverNow - elapsed"),
    },
    "LaborerObjectInfo": {
        "0": p("objectId"),
        "1": p("firstName", "Laborer first name"),
        "2": p("lastName", "Laborer last name"),
        "3": p("fameFill", "FixPoint internal value (FixPoint.FromInternalValue)"),
        "4": p("happiness", "FixPoint internal value, truncated to int"),
        "6": p("nextReturnAt", ".NET ticks (UTC)"),
        "7": p("lastJobStartedAt", ".NET ticks (UTC)"),
        "8": p("jobDispatchTime", ".NET ticks (UTC); present only while away on job"),
        "9": p("activeJobId", "16-byte job GUID (Byte[]); present while on job"),
        "10": p("sentByCharacter", "Dispatching character / zone name"),
    },
    "LaborerObjectJobInfo": {
        "0": p("objectId"),
        "1": p("isLootReady", "bool; true = returned home, loot ready"),
        "2": p("journalItemId", "Journal item index", "itemIndex"),
        "3": p("currentFameFill", "FixPoint internal value"),
        "5": p("jobStartTime", ".NET ticks (UTC)"),
    },
    "FarmableObjectInfo": {
        "0": p("objectId"),
        "1": p("elapsedGrowTime", "Pasture/herb layout: elapsed grow time (100us units)"),
        "2": p("serverNow", "Pasture/herb layout: server now (.NET ticks UTC)"),
        "4": p("elapsedGrowTimeFarm", "Farm layout: elapsed grow time (100us units)"),
        "5": p("serverNowFarm", "Farm layout: server now (.NET ticks UTC)"),
        "9": p("activityTimestamp", ".NET ticks (UTC); last activity. A string param holds the FARMABLE uniqueName"),
    },
    "FestivitiesUpdate": {
        "0": p("typeFlags", "Byte[]; per-entry flag (2 = daily)"),
        "1": p("categories", "String[]; per-entry category"),
        "2": p("uniqueNames", "String[]; festivity unique names"),
        "3": p("startTicks", "Int64[]; per-entry start (.NET ticks UTC)"),
        "4": p("endTicks", "Int64[]; per-entry end (.NET ticks UTC)"),
    },
    # enum renamed MightAndFavorReceived -> MightAndFavorReceivedEvent; remap + correct from source.
    "MightAndFavorReceivedEvent": {
        "0": p("might", "Might"),
        "2": p("premiumOfMight", "Premium of might"),
        "3": p("favor", "Favor"),
        "5": p("premiumOfFavor", "Premium of favor"),
        "6": p("totalFavor", "Total favor"),
        "8": p("unknown8"),
    },
}

REQUEST_OVERRIDE = {
    "ActionOnBuildingStart": {
        "0": p("ticks", ".NET ticks"),
        "1": p("buildingObjectId"),
        "2": p("actionType", "Enum value"),
        "4": p("costs", "Silver cost"),
        "5": p("itemIndices", "Int[] of item indices being used", "itemIndex"),
    },
}

# Old dump carried a mislabeled buildingObjectId on these generic ops; drop the stale guess.
REQUEST_SKIP_CARRY = {"RegisterToObject", "UnRegisterFromObject"}

_DASHES = {"‒": "-", "–": "-", "—": "-", "―": "-"}


def sanitize(params):
    """Carried-forward notes: normalize en/em dashes to plain hyphen (standing no-dash rule)."""
    for entry in params.values():
        note = entry.get("note", "")
        for bad, good in _DASHES.items():
            note = note.replace(bad, good)
        entry["note"] = note
    return params


def best_params_by_name(old, kind):
    """name -> richest param dict among existing `kind:code` entries."""
    best = {}
    for key, val in old.items():
        k, _, _code = key.partition(":")
        if k != kind:
            continue
        name = val.get("name", "")
        params = val.get("params", {}) or {}
        if not name:
            continue
        if name not in best or len(params) > len(best[name]):
            best[name] = sanitize(params)
    return best


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--source-path",
        default=os.environ.get("APX_SOURCE_REPO", str(APX.parent / "reference-source")),
    )
    args = parser.parse_args()

    repo = Path(args.source_path)
    eventcodes = find_one(repo, "EventCodes.cs")
    opcodes = find_one(repo, "OperationCodes.cs")
    for label, f in (("EventCodes.cs", eventcodes), ("OperationCodes.cs", opcodes)):
        if f is None or not f.exists():
            sys.exit(f"ERROR: {label} not found under {repo} (pass --source-path or set APX_SOURCE_REPO)")

    old = json.loads(OUT.read_text(encoding="utf-8-sig"))
    ev_best = best_params_by_name(old, "EVENT")
    req_best = best_params_by_name(old, "REQUEST")
    resp_best = best_params_by_name(old, "RESPONSE")

    events = parse_enum(eventcodes)
    ops = parse_enum(opcodes)

    # Provenance stamp (first key). Inert to PacketSchemaService: it only looks up by "KIND:code".
    out = {
        "$schemaMeta": {
            "schemaVersion": SCHEMA_VERSION,
            "sourceCommit": source_commit(repo),
            "generatedAt": datetime.now(timezone.utc).date().isoformat(),
        }
    }
    carried_ev = set()
    for ordinal, name in events:
        if name in EVENT_OVERRIDE:
            params = EVENT_OVERRIDE[name]
        else:
            params = ev_best.get(name, {})
            if params:
                carried_ev.add(name)
        out[f"EVENT:{ordinal}"] = {"name": name, "params": params}

    for ordinal, name in ops:
        if name in REQUEST_OVERRIDE:
            rparams = REQUEST_OVERRIDE[name]
        elif name in REQUEST_SKIP_CARRY:
            rparams = {}
        else:
            rparams = req_best.get(name, {})
        out[f"REQUEST:{ordinal}"] = {"name": name, "params": rparams}
    for ordinal, name in ops:
        out[f"RESPONSE:{ordinal}"] = {"name": name, "params": resp_best.get(name, {})}

    OUT.write_text(json.dumps(out, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    ev_names = {n for _, n in events}
    op_names = {n for _, n in ops}
    dropped_ev = sorted(n for n in ev_best if n not in ev_names and ev_best[n])
    dropped_req = sorted(n for n in req_best if n not in op_names and req_best[n])
    print(f"events: {len(events)}  ops: {len(ops)}  total keys: {len(out)}")
    print(f"carried EVENT param-sets: {len(carried_ev)}")
    if dropped_ev:
        print(f"DROPPED EVENT param-sets (name not in current enum): {dropped_ev}")
    if dropped_req:
        print(f"DROPPED REQUEST param-sets: {dropped_req}")


if __name__ == "__main__":
    main()
