#!/usr/bin/env python3
"""
Extracts packet param schemas from SAT Network/Events/*.cs and Network/Handler/*.cs.
Outputs packet-schema.base.json to src/AlbionPacketExplorer/Assets/.

Usage:
    python tools/generate-schema.py [--sat-path PATH]

Default SAT path: D:/Users/bimas/Documents/github/AlbionOnline-StatisticsAnalysis
"""

import re
import json
import argparse
from pathlib import Path

SAT_DEFAULT = Path("D:/Users/bimas/Documents/github/AlbionOnline-StatisticsAnalysis")
OUT_PATH = Path(__file__).parent.parent / "src/AlbionPacketExplorer/Assets/packet-schema.base.json"

# Maps SAT EventCodes enum value -> (name, numeric_code)
# We parse EventCodes.cs to build this dynamically.

def parse_event_codes(sat_root: Path) -> dict[str, int]:
    """Returns dict name->code from EventCodes.cs"""
    cs = sat_root / "src/StatisticsAnalysisTool/Network/EventCodes.cs"
    if not cs.exists():
        print(f"  WARN: {cs} not found")
        return {}
    text = cs.read_text(encoding="utf-8", errors="replace")
    result = {}
    counter = 0
    for line in text.splitlines():
        line = line.strip().rstrip(",")
        # match: Name = VALUE or just Name
        m = re.match(r"^(\w+)\s*=\s*(\d+)", line)
        if m:
            result[m.group(1)] = int(m.group(2))
            counter = int(m.group(2))
        elif re.match(r"^([A-Z][A-Za-z0-9]+)$", line):
            counter += 1
            result[line] = counter
    return result


def parse_op_codes(sat_root: Path) -> dict[str, int]:
    """Returns dict name->code from OperationCodes.cs"""
    cs = sat_root / "src/StatisticsAnalysisTool/Network/OperationCodes.cs"
    if not cs.exists():
        print(f"  WARN: {cs} not found")
        return {}
    text = cs.read_text(encoding="utf-8", errors="replace")
    result = {}
    counter = 0
    for line in text.splitlines():
        line = line.strip().rstrip(",")
        m = re.match(r"^(\w+)\s*=\s*(\d+)", line)
        if m:
            result[m.group(1)] = int(m.group(2))
            counter = int(m.group(2))
        elif re.match(r"^([A-Z][A-Za-z0-9]+)$", line):
            counter += 1
            result[line] = counter
    return result


# Patterns for extracting param accesses from C# code
PARAM_PATTERNS = [
    # parameters[0], parameters["0"]
    re.compile(r'parameters\[(?:"(\d+)"|(\d+))\]'),
    # dict[0], dict["0"]
    re.compile(r'\bdict\[(?:"(\d+)"|(\d+))\]'),
]

# Patterns to find the variable name assigned from param
ASSIGN_PATTERN = re.compile(
    r'(?:var\s+(\w+)|(\w+)\s*=)\s*.*?parameters\[(?:"?(\d+)"?)\]'
)

# Property pattern in DTO classes: public Type PropName { get; set; }
PROP_PATTERN = re.compile(
    r'public\s+\S+\s+(\w+)\s*\{[^}]*get[^}]*\}'
)

# [GameParameter(ActionType.Something)] attribute before property
GAME_PARAM_ATTR = re.compile(r'\[GameParameter[^\]]*\]')


def extract_params_from_file(path: Path) -> dict[str, dict]:
    """
    Extract {key: {name, note, resolveAs}} from a C# event/handler/DTO file.
    Best-effort — returns empty dict if nothing found.
    """
    text = path.read_text(encoding="utf-8", errors="replace")
    params: dict[str, dict] = {}

    # Strategy 1: look for assignment patterns like:
    #   var objectId = parameters[0]
    #   ObjectId = parameters["0"]
    for m in ASSIGN_PATTERN.finditer(text):
        var_name = m.group(1) or m.group(2)
        key = m.group(3)
        if var_name and key and var_name not in ("null", "true", "false", "value"):
            name = camel_to_snake_hint(var_name)
            if key not in params:
                params[key] = {"name": name, "note": "", "resolveAs": resolve_hint(name)}

    # Strategy 2: look for [GameParameter(X)] followed by property
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if "[GameParameter" in line:
            # extract key from attribute if present
            key_m = re.search(r'ActionType\.(\w+)|(\d+)', line)
            # look ahead for property name
            for j in range(i+1, min(i+4, len(lines))):
                prop_m = re.search(r'public\s+\S+\s+(\w+)\s*[{;]', lines[j])
                if prop_m:
                    prop_name = prop_m.group(1)
                    # try to get numeric key from attribute
                    attr_key_m = re.search(r'\b(\d+)\b', line)
                    if attr_key_m:
                        key = attr_key_m.group(1)
                        name = camel_to_snake_hint(prop_name)
                        if key not in params:
                            params[key] = {"name": name, "note": "", "resolveAs": resolve_hint(name)}
                    break

    return params


def camel_to_snake_hint(name: str) -> str:
    """Convert CamelCase to camelCase for display (keep first char lower)."""
    if not name:
        return name
    return name[0].lower() + name[1:]


ITEM_HINT_WORDS = {"itemid", "itemindex", "itemidx", "item"}
ITEM_RESOLVE_NAMES = {"itemId", "itemIndex", "itemIdx"}

def resolve_hint(name: str) -> str:
    """Return 'itemIndex' if the name suggests an item ID, else empty string."""
    lower = name.lower()
    if any(w in lower for w in ITEM_HINT_WORDS):
        return "itemIndex"
    return ""


def find_event_code_for_handler(text: str, event_codes: dict[str, int]) -> tuple[str, int] | None:
    """Try to find which event code a handler file handles."""
    # Look for: EventCodes.SomeName or OperationCodes.SomeName
    for pattern, kind in [
        (re.compile(r'EventCodes\.(\w+)'), "EVENT"),
        (re.compile(r'OperationCodes\.(\w+)'), None),
    ]:
        for m in pattern.finditer(text):
            name = m.group(1)
            if kind == "EVENT" and name in event_codes:
                return ("EVENT", event_codes[name])
    return None


def find_request_response_code(text: str, op_codes: dict[str, int]) -> list[tuple[str, int]]:
    results = []
    for m in re.finditer(r'OperationCodes\.(\w+)', text):
        name = m.group(1)
        if name in op_codes:
            # determine if REQUEST or RESPONSE from class name / comments
            if "Request" in text[:500]:
                results.append(("REQUEST", op_codes[name]))
            if "Response" in text[:500]:
                results.append(("RESPONSE", op_codes[name]))
            if not results:
                results.append(("REQUEST", op_codes[name]))
                results.append(("RESPONSE", op_codes[name]))
    return results


# Manually curated high-confidence entries from research (fills gaps regex misses)
MANUAL_ENTRIES: dict[str, dict] = {
    "EVENT:6": {
        "name": "HealthUpdate",
        "params": {
            "0": {"name": "affectedObjectId", "note": "", "resolveAs": ""},
            "1": {"name": "timestamp",         "note": ".NET ticks", "resolveAs": ""},
            "2": {"name": "healthChange",      "note": "", "resolveAs": ""},
            "3": {"name": "newHealthValue",    "note": "", "resolveAs": ""},
            "4": {"name": "effectType",        "note": "", "resolveAs": ""},
            "5": {"name": "effectOrigin",      "note": "", "resolveAs": ""},
            "6": {"name": "causerId",          "note": "", "resolveAs": ""},
            "7": {"name": "causingSpellIndex", "note": "", "resolveAs": ""},
        }
    },
    "EVENT:25": {
        "name": "NewCharacter",
        "params": {
            "0":  {"name": "objectId",   "note": "", "resolveAs": ""},
            "1":  {"name": "name",       "note": "Character name", "resolveAs": ""},
            "7":  {"name": "guid",       "note": "16-byte player GUID", "resolveAs": ""},
            "8":  {"name": "guildName",  "note": "", "resolveAs": ""},
            "40": {"name": "equipment",  "note": "Array of equipped item indices", "resolveAs": ""},
        }
    },
    "EVENT:27": {
        "name": "NewEquipmentItem",
        "params": {
            "0": {"name": "objectId",    "note": "", "resolveAs": ""},
            "1": {"name": "itemId",      "note": "Item index — maps to IndexedItems", "resolveAs": "itemIndex"},
            "2": {"name": "quantity",    "note": "", "resolveAs": ""},
            "4": {"name": "emv",         "note": "Estimated market value in silver", "resolveAs": ""},
            "6": {"name": "quality",     "note": "0=Normal 1=Good 2=Outstanding 3=Excellent 4=Masterpiece", "resolveAs": ""},
            "7": {"name": "durability",  "note": "", "resolveAs": ""},
        }
    },
    "EVENT:32": {
        "name": "NewSimpleItem",
        "params": {
            "0": {"name": "objectId",   "note": "", "resolveAs": ""},
            "1": {"name": "itemIndex",  "note": "Item ID — maps to IndexedItems", "resolveAs": "itemIndex"},
            "2": {"name": "quantity",   "note": "", "resolveAs": ""},
            "4": {"name": "emv",        "note": "Estimated market value in silver", "resolveAs": ""},
            "7": {"name": "durability", "note": "", "resolveAs": ""},
        }
    },
    "EVENT:35": {
        "name": "LaborerObjectJobInfo",
        "params": {
            "0": {"name": "objectId",        "note": "", "resolveAs": ""},
            "1": {"name": "journalItemId",   "note": "Item ID of the laborer's journal", "resolveAs": "itemIndex"},
            "2": {"name": "isLootReady",     "note": "", "resolveAs": ""},
            "4": {"name": "returnTime",      "note": ".NET ticks", "resolveAs": ""},
            "5": {"name": "owner",           "note": "Owner character name", "resolveAs": ""},
            "6": {"name": "capacity",        "note": "", "resolveAs": ""},
        }
    },
    "EVENT:30": {
        "name": "LaborerObjectInfo",
        "params": {
            "0":  {"name": "objectId",     "note": "", "resolveAs": ""},
            "1":  {"name": "itemId",       "note": "Laborer item ID", "resolveAs": "itemIndex"},
            "2":  {"name": "tier",         "note": "", "resolveAs": ""},
            "4":  {"name": "returnTime",   "note": ".NET ticks", "resolveAs": ""},
            "5":  {"name": "owner",        "note": "Owner character name", "resolveAs": ""},
            "6":  {"name": "lootState",    "note": "0=empty 1=ready", "resolveAs": ""},
            "7":  {"name": "capacity",     "note": "", "resolveAs": ""},
            "8":  {"name": "itemIds",      "note": "Array of item IDs in loot", "resolveAs": ""},
            "9":  {"name": "quantities",   "note": "Array of quantities matching itemIds", "resolveAs": ""},
            "10": {"name": "isAwayOnJob",  "note": "", "resolveAs": ""},
        }
    },
    "EVENT:56": {
        "name": "NewBuilding",
        "params": {
            "0": {"name": "objectId",    "note": "", "resolveAs": ""},
            "1": {"name": "firstName",   "note": "Laborer first name", "resolveAs": ""},
            "2": {"name": "lastName",    "note": "Laborer last name", "resolveAs": ""},
            "3": {"name": "uniqueName",  "note": "Building UniqueName (optional)", "resolveAs": ""},
            "6": {"name": "lastHarvest", "note": ".NET ticks", "resolveAs": ""},
            "7": {"name": "plantedAt",   "note": ".NET ticks", "resolveAs": ""},
        }
    },
    "EVENT:57": {
        "name": "FarmableObjectInfo",
        "params": {
            "0": {"name": "objectId",  "note": "", "resolveAs": ""},
            "1": {"name": "isReady",   "note": "", "resolveAs": ""},
            "2": {"name": "itemId",    "note": "Farmable item ID", "resolveAs": "itemIndex"},
            "3": {"name": "growTime",  "note": "Total grow time in seconds", "resolveAs": ""},
            "5": {"name": "plantedAt", "note": ".NET ticks", "resolveAs": ""},
        }
    },
    "EVENT:55": {
        "name": "TakeSilver",
        "params": {
            "0": {"name": "objectId",      "note": "", "resolveAs": ""},
            "1": {"name": "timestamp",     "note": ".NET ticks", "resolveAs": ""},
            "2": {"name": "targetEntityId","note": "", "resolveAs": ""},
            "3": {"name": "yieldPreTax",   "note": "Silver before tax", "resolveAs": ""},
            "5": {"name": "guildTax",      "note": "Guild tax amount", "resolveAs": ""},
            "6": {"name": "clusterTax",    "note": "Zone tax amount", "resolveAs": ""},
            "7": {"name": "isPremiumBonus","note": "", "resolveAs": ""},
            "8": {"name": "multiplier",    "note": "", "resolveAs": ""},
        }
    },
    "EVENT:71": {
        "name": "UpdateMoney",
        "params": {
            "1": {"name": "currentSilver", "note": "Total silver of player", "resolveAs": ""},
        }
    },
    "EVENT:72": {
        "name": "UpdateFame",
        "params": {
            "1":  {"name": "totalFame",          "note": "", "resolveAs": ""},
            "2":  {"name": "fameWithMultiplier", "note": "", "resolveAs": ""},
            "3":  {"name": "zoneFame",           "note": "", "resolveAs": ""},
            "4":  {"name": "multiplier",         "note": "", "resolveAs": ""},
            "5":  {"name": "isPremiumBonus",     "note": "", "resolveAs": ""},
            "8":  {"name": "bagInsightItemIndex","note": "", "resolveAs": "itemIndex"},
            "10": {"name": "satchelFame",        "note": "", "resolveAs": ""},
            "17": {"name": "bonusFactor",        "note": "", "resolveAs": ""},
        }
    },
    "EVENT:129": {
        "name": "NewMob",
        "params": {
            "0":  {"name": "objectId",           "note": "", "resolveAs": ""},
            "1":  {"name": "mobIndex",           "note": "", "resolveAs": ""},
            "11": {"name": "moveSpeed",          "note": "", "resolveAs": ""},
            "13": {"name": "hitPoints",          "note": "", "resolveAs": ""},
            "14": {"name": "hitPointsMax",       "note": "", "resolveAs": ""},
            "17": {"name": "energy",             "note": "", "resolveAs": ""},
            "18": {"name": "energyMax",          "note": "", "resolveAs": ""},
            "19": {"name": "energyRegeneration", "note": "", "resolveAs": ""},
        }
    },
    "EVENT:171": {
        "name": "Died",
        "params": {
            "2":  {"name": "diedName",         "note": "Name of player who died", "resolveAs": ""},
            "3":  {"name": "diedGuild",        "note": "", "resolveAs": ""},
            "10": {"name": "killedBy",         "note": "Killer name", "resolveAs": ""},
            "11": {"name": "killedByGuild",    "note": "", "resolveAs": ""},
        }
    },
    "EVENT:212": {
        "name": "PartyJoined",
        "params": {
            "4": {"name": "partyLead",       "note": "", "resolveAs": ""},
            "5": {"name": "partyUserGuids",  "note": "Array of party member GUIDs", "resolveAs": ""},
            "6": {"name": "partyUserNames",  "note": "Array of party member names", "resolveAs": ""},
        }
    },
    "EVENT:214": {
        "name": "PartyPlayerJoined",
        "params": {
            "1": {"name": "userGuid",  "note": "16-byte GUID", "resolveAs": ""},
            "2": {"name": "username",  "note": "", "resolveAs": ""},
        }
    },
    "REQUEST:45": {
        "name": "ActionOnBuildingStart",
        "params": {
            "0": {"name": "ticks",            "note": ".NET ticks", "resolveAs": ""},
            "1": {"name": "buildingObjectId", "note": "", "resolveAs": ""},
            "2": {"name": "actionType",       "note": "Enum value — TBD", "resolveAs": ""},
            "4": {"name": "costs",            "note": "Silver cost", "resolveAs": ""},
            "7": {"name": "itemIndex",        "note": "Item being used", "resolveAs": "itemIndex"},
            "9": {"name": "quantity",         "note": "", "resolveAs": ""},
        }
    },
    "REQUEST:46": {
        "name": "ActionOnBuildingEnd",
        "params": {
            "0": {"name": "buildingObjectId", "note": "", "resolveAs": ""},
        }
    },
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sat-path", default=str(SAT_DEFAULT))
    args = parser.parse_args()

    sat_root = Path(args.sat_path)
    print(f"SAT root: {sat_root}")
    print(f"Output:   {OUT_PATH}")

    event_codes = parse_event_codes(sat_root)
    op_codes = parse_op_codes(sat_root)
    print(f"Loaded {len(event_codes)} event codes, {len(op_codes)} op codes")

    schema: dict[str, dict] = {}

    # Start with manual curated entries
    schema.update(MANUAL_ENTRIES)
    print(f"Seeded {len(MANUAL_ENTRIES)} manual entries")

    # Auto-extract from SAT handler files
    handler_dirs = [
        sat_root / "src/StatisticsAnalysisTool/Network/Handler",
        sat_root / "src/StatisticsAnalysisTool/Network/Events",
        sat_root / "src/StatisticsAnalysisTool/Network/Operations",
    ]

    auto_count = 0
    for d in handler_dirs:
        if not d.exists():
            print(f"  SKIP (missing): {d}")
            continue
        for cs_file in sorted(d.glob("*.cs")):
            text = cs_file.read_text(encoding="utf-8", errors="replace")
            params = extract_params_from_file(cs_file)
            if not params:
                continue

            # Try to find event code
            entry = find_event_code_for_handler(text, event_codes)
            if entry:
                kind, code = entry
                key = f"{kind}:{code}"
                if key not in schema:
                    schema[key] = {"name": entry_name_from_file(cs_file), "params": {}}
                for pk, pv in params.items():
                    if pk not in schema[key].get("params", {}):
                        schema[key].setdefault("params", {})[pk] = pv
                        auto_count += 1

    print(f"Auto-extracted {auto_count} additional param entries")
    print(f"Total schema entries: {len(schema)}")

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(OUT_PATH, "w", encoding="utf-8") as f:
        json.dump(schema, f, indent=2, ensure_ascii=False)
    print(f"Written: {OUT_PATH}")


def entry_name_from_file(path: Path) -> str:
    name = path.stem
    for suffix in ["EventHandler", "Handler", "Event", "Operation", "Request", "Response"]:
        if name.endswith(suffix):
            name = name[:-len(suffix)]
    return name[0].lower() + name[1:] if name else ""


if __name__ == "__main__":
    main()
