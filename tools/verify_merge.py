"""Independent re-count: source packets present vs what the merge emitted.

For each source file count:
  present  = objects that ARE packets (ts/kind/code/params)  -> should be in merged
  dropped  = objects/lines that are NOT packets              -> intentionally skipped
Compares present-count to the merge's emitted-count so silent loss shows up.
"""
import os
import ijson

TEMP = os.path.join(
    os.environ["LOCALAPPDATA"],
    "StatisticsAnalysisTool", "Instances", "3168FFFA", "temp",
)

# name -> emitted count from the merge run
EMITTED = {
    "3 islands and half.json": 11715,
    "cass packets_20260530_203344.json": 18360,
    "h2.json": 4258,
    "inspect_stations.json": 138737,
    "islands_packets_20260602_210418.json": 42837,
    "j.json": 272,
    "j2.json": 4258,
    "new_packets.json": 5014,
    "packets_20260530_202123.json": 6031,
    "packets_20260607_130011.json": 59730,
    "packets_20260607_143115.json": 66039,
    "packets_20260607_153333.json": 49758,
    "packet_sniffer - Copy.json": 51881,
    "sat-06-06.json": 51039,
    "zenith.json": 464,
    "zenith2.json": 9060,
    "_collect_slice.json": 88991,
    "_win.json": 144,
    "T6_FARM_FOXGLOVE_SEED.json": 96,
    "packet_sniffer.json": 3377843,
}


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


def count_array(path):
    present = dropped = 0
    with open(path, "rb") as f:
        for item in ijson.items(f, "item"):
            if is_packet(item):
                present += 1
            else:
                dropped += 1
    return present, dropped


def count_ndjson(path):
    present = dropped = 0
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            s = line.strip().rstrip(",")
            if not s:
                continue
            if s[0] == "{" and '"ts"' in s and '"params"' in s:
                present += 1
            else:
                dropped += 1
    return present, dropped


def main():
    bad = 0
    print(f"{'present':>10} {'emitted':>10} {'drop':>7}  file")
    for name, emitted in EMITTED.items():
        path = os.path.join(TEMP, name)
        if not os.path.exists(path):
            print(f"{'?':>10} {emitted:>10} {'?':>7}  MISSING {name}")
            continue
        present, dropped = (
            count_array(path) if first_char(path) == "[" else count_ndjson(path)
        )
        flag = "" if present == emitted else "  <-- MISMATCH"
        if flag:
            bad += 1
        print(f"{present:>10,} {emitted:>10,} {dropped:>7,}  {name}{flag}")
    print("\nALL MATCH" if bad == 0 else f"\n{bad} MISMATCH(es)")


if __name__ == "__main__":
    main()
