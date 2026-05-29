"""
Injects a .ico file as Win32 RT_ICON + RT_GROUP_ICON resources into a PE EXE.
Usage: python inject-icon.py <exe> <ico>
"""
import sys
import struct
import ctypes
from ctypes import wintypes

RT_ICON = ctypes.cast(3, ctypes.c_wchar_p)
RT_GROUP_ICON = ctypes.cast(14, ctypes.c_wchar_p)
LANG_NEUTRAL = 0

k32 = ctypes.WinDLL("kernel32", use_last_error=True)

def inject(exe_path: str, ico_path: str) -> None:
    with open(ico_path, "rb") as f:
        ico = f.read()

    # Parse ICO header
    reserved, img_type, count = struct.unpack_from("<HHH", ico, 0)
    assert img_type == 1, "Not an ICO file"

    # Read directory entries (16 bytes each, starting at offset 6)
    entries = []
    for i in range(count):
        base = 6 + i * 16
        w, h, color_count, reserved2, planes, bit_count, size, offset = \
            struct.unpack_from("<BBBBHHII", ico, base)
        entries.append((w, h, color_count, reserved2, planes, bit_count, size, offset))

    # Build GRPICONDIR in memory (RT_GROUP_ICON format)
    # GRPICONDIR header: reserved(2) + type(2) + count(2)
    grp = struct.pack("<HHH", 0, 1, count)
    # GRPICONDIRENTRY: width(1) + height(1) + colorCount(1) + reserved(1) +
    #                  planes(2) + bitCount(2) + bytesInRes(4) + id(2)
    for idx, (w, h, cc, res2, planes, bc, size, _offset) in enumerate(entries):
        grp += struct.pack("<BBBBHHI", w, h, cc, res2, planes, bc, size)
        grp += struct.pack("<H", idx + 1)  # icon resource id (1-based)

    hUpdate = k32.BeginUpdateResourceW(exe_path, False)
    if not hUpdate:
        raise ctypes.WinError(ctypes.get_last_error())

    try:
        # Add each individual icon image as RT_ICON with id = idx+1
        for idx, (_, _, _, _, _, _, size, offset) in enumerate(entries):
            icon_data = ico[offset:offset + size]
            buf = ctypes.create_string_buffer(icon_data)
            ok = k32.UpdateResourceW(hUpdate, RT_ICON, idx + 1, LANG_NEUTRAL,
                                     buf, len(icon_data))
            if not ok:
                raise ctypes.WinError(ctypes.get_last_error())

        # Add RT_GROUP_ICON with id = 1
        grp_buf = ctypes.create_string_buffer(grp)
        ok = k32.UpdateResourceW(hUpdate, RT_GROUP_ICON, 1, LANG_NEUTRAL,
                                 grp_buf, len(grp))
        if not ok:
            raise ctypes.WinError(ctypes.get_last_error())

        if not k32.EndUpdateResourceW(hUpdate, False):
            raise ctypes.WinError(ctypes.get_last_error())

    except Exception:
        k32.EndUpdateResourceW(hUpdate, True)  # discard on error
        raise

    print(f"Injected {count} icon(s) into {exe_path}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: inject-icon.py <exe> <ico>")
        sys.exit(1)
    inject(sys.argv[1], sys.argv[2])
