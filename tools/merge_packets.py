"""Merge all Albion packet capture files into one NDJSON (one packet per line).

Handles both shapes found in the SAT temp dir:
  - pretty/compact JSON arrays  ( [ {pkt}, {pkt} ] )  -> streamed via ijson
  - NDJSON                      ( {pkt}\n{pkt}\n ... ) -> passed through per line

Only objects carrying ts/kind/code/params are emitted; anything else is skipped.
"""
import os
import sys
import json
from decimal import Decimal

import ijson

TEMP = os.path.join(
    os.environ["LOCALAPPDATA"],
    "StatisticsAnalysisTool", "Instances", "3168FFFA", "temp",
)
OUT = os.path.join(TEMP, "packets-merged.json")

# Every capture file. festivity_raw.json excluded (not packet schema).
FILES = [
    "3 islands and half.json",
    "cass packets_20260530_203344.json",
    "h2.json",
    "inspect_stations.json",
    "islands_packets_20260602_210418.json",
    "j.json",
    "j2.json",
    "new_packets.json",
    "packets_20260530_202123.json",
    "packets_20260607_130011.json",
    "packets_20260607_143115.json",
    "packets_20260607_153333.json",
    "packet_sniffer - Copy.json",
    "sat-06-06.json",
    "zenith.json",
    "zenith2.json",
    "_collect_slice.json",
    "_win.json",
    "T6_FARM_FOXGLOVE_SEED.json",
    "packet_sniffer.json",  # 1.5G NDJSON, last
]


def _num(o):
    if isinstance(o, Decimal):
        return int(o) if o == o.to_integral_value() else float(o)
    raise TypeError(repr(o))


def dump(obj):
    return json.dumps(obj, separators=(",", ":"), ensure_ascii=False, default=_num)


def is_packet(d):
    return isinstance(d, dict) and "ts" in d and "kind" in d and "code" in d and "params" in d


def first_char(path):
    with open(path, "rb") as f:
        while True:
            b = f.read(1)
            if not b:
                return ""
            if not b.isspace():
                return b.decode("latin1")


def emit_array(path, out):
    n = 0
    with open(path, "rb") as f:
        for item in ijson.items(f, "item"):
            if is_packet(item):
                out.write(dump(item))
                out.write("\n")
                n += 1
    return n


def emit_ndjson(path, out):
    n = 0
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            s = line.strip().rstrip(",")
            if not s or s[0] != "{":
                continue
            if '"ts"' not in s or '"params"' not in s:
                continue
            out.write(s)
            out.write("\n")
            n += 1
    return n


def main():
    total = 0
    with open(OUT, "w", encoding="utf-8", newline="\n") as out:
        for name in FILES:
            path = os.path.join(TEMP, name)
            if not os.path.exists(path):
                print(f"SKIP (missing): {name}", flush=True)
                continue
            kind = first_char(path)
            try:
                n = emit_array(path, out) if kind == "[" else emit_ndjson(path, out)
            except Exception as e:  # noqa: BLE001
                print(f"ERROR {name}: {e}", flush=True)
                continue
            total += n
            print(f"{n:>9,}  {name}", flush=True)
    size = os.path.getsize(OUT)
    print(f"\nTOTAL {total:,} packets -> {OUT} ({size/1024/1024:.1f} MB)", flush=True)


if __name__ == "__main__":
    sys.exit(main())
