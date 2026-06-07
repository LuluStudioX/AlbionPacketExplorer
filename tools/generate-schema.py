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

# Authoritative island-domain entries keyed by REAL WIRE CODE. The positional EventCodes enum has
# drifted, so several handlers hardcode the wire code; these maps come from the island packet field
# reference (field-by-field decode cross-checked against captured traffic) and are the ground truth.
# Injected after the enum-based pass to override whatever ordinal landed on these codes.
def _ev(name, params):
    return {"name": name, "params": params}


WIRE_OVERRIDE = {
    "EVENT:27": _ev("NewSimpleItem", {
        "0": p("objectId"),
        "1": p("despawnRef", "Small int; despawn/destroy marker (not a real item create)"),
        "4": p("estimatedMarketValue", "FixPoint internal (when full form)"),
    }),
    "EVENT:32": _ev("NewLaborerItem", {
        "0": p("objectId"),
        "1": p("itemIndex", "Resource item id (e.g. T5 plank/metalbar/leather/cloth)", "itemIndex"),
        "2": p("quantity"),
        "3": p("newlyCreated", "bool (sometimes)"),
        "4": p("estimatedMarketValue", "FixPoint internal"),
        "6": p("qualityDenominator", "10000 = 100.00% (when present)"),
        "7": p("durability", "FixPoint internal (when present)"),
    }),
    "EVENT:35": _ev("NewJournalItem", {
        "0": p("objectId"),
        "1": p("journalItemIndex", "Journal item id (11877-11906)", "itemIndex"),
        "2": p("quantity"),
        "4": p("estimatedMarketValue", "FixPoint internal"),
        "5": p("crafterName", "Crafter / filler name"),
        "6": p("qualityDenominator", "10000 = 100.00%"),
        "7": p("durability", "FixPoint internal"),
    }),
    "EVENT:45": _ev("NewBuilding", {
        "0": p("objectId"),
        "1": p("buildingGuid", "16-byte GUID (Byte[])"),
        "2": p("parentHouseObjectId", "Object sits inside this house"),
        "3": p("uniqueName", "Type discriminator, e.g. T7_LABOURER_WOOD"),
        "4": p("position", "Single[2] tile coords {x, y}"),
        "5": p("rotation", "Degrees (0/90/180/270/custom)"),
        "6": p("placedFlag", "bool placed/visible"),
        "7": p("nutrition", "Current food units (FixPoint internal)"),
        "8": p("lastActionTicks", ".NET ticks (UTC); PlantedAt / last interaction"),
        "9": p("housePlotGuid", "16-byte plot GUID (Byte[])"),
        "10": p("ownerAccountGuid", "16-byte account GUID (Byte[])"),
        "11": p("islandOwnerName"),
        "12": p("builderName", "Placer (usually == owner)"),
        "13": p("laborerFirstName", "Laborer houses only"),
        "14": p("laborerLastName"),
        "15": p("occupied", "bool occupied/hireable (stations)"),
        "16": p("hasPremium", "bool"),
        "18": p("nutritionCapacity", "Max food"),
        "19": p("buildingItemTypeId", "-1 when n/a"),
        "20": p("placedAtTicks", ".NET ticks (UTC)"),
        "21": p("nutritionValue", "~capacity"),
        "22": p("lastUpdateTicks", ".NET ticks (UTC)"),
        "23": p("silverValue", "Station usage"),
        "24": p("usageFee", "Upkeep silver"),
        "29": p("stateTier", "State/tier byte (0/2/4)"),
    }),
    "EVENT:54": _ev("FarmBuildingInfo", {
        "0": p("objectId", "NOTE: wire code 54 collides with HarvestFinished (LIFO handler wins)"),
        "4": p("elapsedGrowTime", "Elapsed grow time (100us units); active form only"),
        "5": p("serverNow", ".NET ticks (UTC); PlantedAt = serverNow - elapsed; active form only"),
    }),
    "EVENT:56": _ev("LaborerObjectInfo", {
        "0": p("objectId"),
        "1": p("firstName"),
        "2": p("lastName"),
        "3": p("fameFill", "FixPoint internal"),
        "4": p("happiness", "FixPoint internal"),
        "5": p("happinessDup", "Duplicate of key 4"),
        "6": p("contractTimestamp1", ".NET ticks (UTC); static, ignored by app"),
        "7": p("contractTimestamp2", ".NET ticks (UTC); static, ignored by app"),
        "8": p("returnAtTicks", ".NET ticks (UTC); present only while on a job"),
        "9": p("activeJobId", "16-byte job GUID (Byte[]); present while on job"),
        "10": p("sentByCharacter", "Dispatching character / zone (on-job only)"),
    }),
    "EVENT:57": _ev("LaborerObjectJobInfo", {
        "0": p("objectId"),
        "1": p("hasActiveJob", "bool; always true when present (NOT loot-ready)"),
        "2": p("journalItemId", "Journal item id of the active job", "itemIndex"),
        "3": p("currentFameFill", "FixPoint internal"),
        "5": p("jobStartTime", ".NET ticks (UTC)"),
    }),
    "EVENT:60": _ev("ActionOnBuildingFinished", {
        "0": p("userObjectId"),
        "1": p("finishAtTicks", ".NET ticks (UTC)"),
        "2": p("buildingObjectId", "Often absent in captures (suspected model-index bug)"),
        "4": p("actionType", "ActionOnBuildingType enum; often absent in captures"),
    }),
    "EVENT:201": _ev("FarmableObjectInfo", {
        "0": p("objectId"),
        "1": p("pastureElapsedGrowTime", "Pasture layout: elapsed grow time (100us units)"),
        "2": p("pastureServerNow", "Pasture layout: server now (.NET ticks UTC)"),
        "3": p("activeFlag", "bool growing (pasture form)"),
        "4": p("cropElapsedGrowTime", "Crop/herb layout: elapsed grow time (100us units)"),
        "5": p("cropServerNow", "Crop/herb layout: server now (.NET ticks UTC)"),
        "6": p("stateFlagBytes", "Byte[]; usually empty"),
        "7": p("stateFlagBytes2", "Byte[]; usually empty"),
        "8": p("arrayLeadingFlag", "Byte[]; array-form leading flag"),
        "9": p("arrayServerNow", "Int64[]; array-form server-now ticks (single element)"),
        "10": p("arrayElapsedGrowTime", "Array-form elapsed grow time (100us units)"),
        "11": p("arrayTicks2", "Int64[]; array-form secondary ticks"),
        "12": p("feedFraction", "Single; 1.0 crops/full, 0.5/0.25 animals by feed level"),
        "13": p("nextReadyTicks", ".NET ticks (UTC); min-long = none"),
        "14": p("slotCount", "Item/slot count (optional)"),
    }),
}

# Harvest responses share one shape across the four op codes; only route/plot type differ.
def _harvest(label):
    return _ev(label, {
        "0": p("itemUniqueNames", "String[]; harvested item unique names"),
        "1": p("quantities", "Quantities parallel to key 0"),
        "255": p("correlationId", "Request correlation id"),
    })


WIRE_OVERRIDE.update({
    "RESPONSE:73": _harvest("FarmableHarvestResponse"),   # HerbGarden: crop/herb/fiber
    "RESPONSE:74": _harvest("PastureHarvestResponse"),    # grown animals
    "RESPONSE:76": _harvest("PastureProductHarvestResponse"),  # products (milk, eggs)
    "RESPONSE:77": _harvest("PastureFeedConsumedResponse"),    # feed consumed (pumpkin, foxglove)
    "REQUEST:45": _ev("OpenLaborerOrBuilding", {
        "0": p("targetObjectId", "0 = own laborer; spawns preview items"),
    }),
    "REQUEST:257": _ev("CollectLaborer", {
        "0": p("laborerObjectId"),
        "1": p("storageObjectId", "Real collect -> storage stacks grow"),
    }),
    "REQUEST:258": _ev("ViewLaborer", {
        "0": p("laborerObjectId", "Open/inspect only, no collect"),
    }),
})

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

    # Island-domain ground truth by REAL WIRE CODE (overrides the drifted enum ordinals above).
    for key, entry in WIRE_OVERRIDE.items():
        out[key] = entry

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
