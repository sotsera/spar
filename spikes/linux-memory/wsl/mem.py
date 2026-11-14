"""Memory snapshot of a process tree from /proc/<pid>/smaps (run with python3 -I).

Usage: python3 -I wsl/mem.py ROOT_PID OUT_JSON LABEL POINT

For the root and all its descendants: smaps_rollup totals, and per mapping Rss, Pss, USS (Private_Clean +
Private_Dirty) and Swap, classified as anonymous, file-backed or shared-memory/GPU, grouped by mapping name and by
library family. Writes one JSON object (appends a line to OUT_JSON) and prints a one-line summary.
"""

import json
import os
import re
import sys

FIELDS = ("Rss", "Pss", "Private_Clean", "Private_Dirty", "Shared_Clean", "Shared_Dirty", "Swap")

FAMILIES = [
    ("llvm", re.compile(r"^libLLVM")),
    ("mesa", re.compile(r"^(libgallium|libEGL_mesa|libGLX_mesa|libglapi|libgbm|libdrm|libvulkan|libxatracker|.*_dri\.so|libGLESv2|libEGL\.so|libGL\.so|libGLX\.so|libGLdispatch|libOpenGL|libLLVMSPIRV|libSPIRV|libz3|libelf|libsensors|libxcb-(dri|present|sync|xfixes|randr|shm|glx)|libwayland-egl|libepoxy|mesa_shader)")),
    ("webkit", re.compile(r"^libwebkit")),
    ("javascriptcore", re.compile(r"^libjavascriptcore")),
    ("gtk-gdk-gsk", re.compile(r"^(libgtk|libgdk|libgsk|libadwaita)")),
    ("glib-gio", re.compile(r"^(libglib|libgobject|libgio|libgmodule|libgthread)")),
    ("icu", re.compile(r"^libicu")),
    ("gstreamer", re.compile(r"^libgst")),
    ("cairo-pango-harfbuzz-freetype", re.compile(r"^(libcairo|libpango|libharfbuzz|libfreetype|libfontconfig|libpixman|libgraphite|libthai|libfribidi)")),
    ("fonts-and-caches", re.compile(r"(\.(ttf|otf|ttc|pcf|cache-\d+|cache-9)$|fontconfig)")),
    ("locale-and-icons", re.compile(r"(locale-archive|gconv|\.mo$|icon-theme\.cache|mime\.cache|\.gresource$|loaders\.cache)")),
    ("dotnet-app", re.compile(r"^Mem\.Linux$")),
    ("c-floor", re.compile(r"^floor-gtk")),
    ("electron", re.compile(r"^(electron|libffmpeg|libvk_swiftshader|libEGL\.so|libGLESv2\.so|chrome_.*\.pak|resources\.pak|icudtl\.dat|v8_context_snapshot\.bin|snapshot_blob\.bin|.*\.pak)$")),
    ("libc-and-system", re.compile(r"^(libc\.|libm\.|ld-linux|libstdc\+\+|libgcc|libpthread|libdl|librt|libsystemd|libselinux|libz\.|libzstd|liblzma|libcrypto|libssl|libgnutls|libnettle|libhogweed|libgmp|libp11|libtasn1|libidn|libunistring|libffi|libmount|libblkid|libpcre|libsqlite|libxml2|libxslt|libsoup|libpsl|libnghttp|libbrotli|libsecret|libgcrypt|libgpg|libcap|libseccomp|libatomic|libuuid|libbz2|libexpat|libdbus|libatk|libatspi|libX|libxcb|libxkb|libwayland|libjpeg|libpng|libwebp|libtiff|libavif|libjxl|libdav1d|libaom|libgav1|libyuv|liblcms|libwoff|libhyphen|libenchant|libmanette|libgudev|libudev|libevdev|libinput|libdw|libunwind|libsharpyuv|libjbig|libLerc|libdeflate|libdatrie|libstartup|libcloudproviders|libtracker|libgraphene|libcolord|libcups|libavahi|libkrb|libk5|libgssapi|libcom_err|libkeyutils|libresolv|liborc|libopus|libvorbis|libogg|libflac|libmp3|libpulse|libsndfile|libasound|libnss|libnspr|libsmime|libplc|libplds|libgbm|libxshmfence|libxss|libdrm|libnotify|libgirepository|libsysprof|libhwy|libbsd|libmd|libqrencode|libsoxr|libspeex|libwavpack|libmpg|libapparmor|libbrotlicommon|libcairo-gobject|libgdk_pixbuf|librsvg|libcroco|libepoxy|libgcr|libsoup)")),
]


def mapping_name(path):
    if not path:
        return "[anon]", "anon"
    if path.startswith("/memfd:"):
        return "memfd:" + path[len("/memfd:"):].replace(" (deleted)", ""), "shm"
    if path.startswith("/dev/shm/"):
        return "shm:" + re.sub(r"[0-9A-Za-z]{6,}$", "*", os.path.basename(path.replace(" (deleted)", ""))), "shm"
    if path.startswith("/SYSV"):
        return "sysv-shm", "shm"
    if path.startswith(("/dev/dri", "/dev/dxg", "/dmabuf", "anon_inode:", "/anon_hugepage", "/drm")) or "dmabuf" in path:
        return path.split()[0], "shm"
    if path.startswith("["):
        return path, "anon"
    if path.startswith("/"):
        return os.path.basename(path.replace(" (deleted)", "")), "file"
    return path, "anon"


def family(name, cls):
    if cls == "shm":
        return "shared-memory-gpu"
    if cls == "anon":
        return "anon"
    for label, pattern in FAMILIES:
        if pattern.search(name):
            return label
    return "other-files"


def read_smaps(pid):
    maps = []
    current = None
    with open(f"/proc/{pid}/smaps") as handle:
        for line in handle:
            parts = line.split()
            if not parts:
                continue
            if re.match(r"^[0-9a-f]+-[0-9a-f]+$", parts[0]):
                path = " ".join(parts[5:]) if len(parts) > 5 else ""
                start, end = (int(x, 16) for x in parts[0].split("-"))
                current = {"path": path, "perms": parts[1], "size": (end - start) // 1024}
                for field in FIELDS:
                    current[field] = 0
                maps.append(current)
            elif current is not None and parts[0].endswith(":") and parts[0][:-1] in FIELDS:
                current[parts[0][:-1]] = int(parts[1])
    return maps


def rollup(pid):
    values = {}
    with open(f"/proc/{pid}/smaps_rollup") as handle:
        for line in handle:
            parts = line.split()
            if len(parts) >= 2 and parts[0].endswith(":") and parts[1].isdigit():
                values[parts[0][:-1]] = int(parts[1])
    return values


def tree(root):
    parents = {}
    for entry in os.listdir("/proc"):
        if entry.isdigit():
            try:
                with open(f"/proc/{entry}/stat") as handle:
                    stat = handle.read()
                rest = stat[stat.rindex(")") + 2:].split()
                if rest[0] != "Z":
                    parents[int(entry)] = int(rest[1])
            except OSError:
                pass
    found, frontier = [root], [root]
    while frontier:
        nxt = [pid for pid, parent in parents.items() if parent in frontier]
        found += nxt
        frontier = nxt
    return found


def role(pid):
    try:
        with open(f"/proc/{pid}/comm") as handle:
            comm = handle.read().strip()
        with open(f"/proc/{pid}/cmdline", "rb") as handle:
            cmdline = handle.read().split(b"\0")
    except OSError:
        return "gone", ""
    # Chromium rewrites its children's argv into one space-separated string, so search the joined text.
    text = " ".join(a.decode(errors="replace") for a in cmdline if a)
    kind = re.search(r"--type=(\S+)", text)
    if kind:  # Electron / Chromium child
        sub = re.search(r"--utility-sub-type=(\S+)", text)
        return ("electron-" + kind.group(1) + (":" + sub.group(1).split(".")[-1] if sub else "")), comm
    if comm.startswith("WebKitWebProces"):
        return "web", comm
    if comm.startswith("WebKitNetworkPr"):
        return "network", comm
    if comm.startswith("WebKitGPUProces"):
        return "gpu", comm
    if comm in ("bwrap", "xdg-dbus-proxy"):
        return "sandbox-helper:" + comm, comm
    if comm.startswith("chrome_crashpad"):
        return "electron-crashpad", comm
    if comm == "electron":
        return "electron-main", comm
    return "ui", comm


def snapshot(root):
    processes = []
    for pid in tree(root):
        try:
            maps = read_smaps(pid)
            totals = rollup(pid)
        except OSError:
            continue
        kind, comm = role(pid)
        classes = {c: {"pss": 0, "uss": 0, "rss": 0} for c in ("anon", "file", "shm")}
        groups, families = {}, {}
        for m in maps:
            name, cls = mapping_name(m["path"])
            uss = m["Private_Clean"] + m["Private_Dirty"]
            classes[cls]["pss"] += m["Pss"]
            classes[cls]["uss"] += uss
            classes[cls]["rss"] += m["Rss"]
            g = groups.setdefault(name, {"pss": 0, "uss": 0, "rss": 0, "class": cls, "maps": 0, "dirty": 0})
            g["pss"] += m["Pss"]
            g["uss"] += uss
            g["rss"] += m["Rss"]
            g["maps"] += 1
            g["dirty"] += m["Private_Dirty"] + m["Shared_Dirty"]
            f = families.setdefault(family(name, cls), {"pss": 0, "uss": 0})
            f["pss"] += m["Pss"]
            f["uss"] += uss
        anon_maps = sorted((m for m in maps if mapping_name(m["path"])[1] == "anon"), key=lambda m: -m["Pss"])[:6]
        processes.append({
            "pid": pid, "role": kind, "comm": comm,
            "rollup": {k: totals.get(k, 0) for k in ("Rss", "Pss", "Pss_Anon", "Pss_File", "Pss_Shmem", "Private_Clean", "Private_Dirty", "Swap")},
            "uss": sum(m["Private_Clean"] + m["Private_Dirty"] for m in maps),
            "classes": classes,
            "families": families,
            "top": sorted(({"name": k, **v} for k, v in groups.items()), key=lambda g: -g["pss"])[:25],
            "top_anon": [{"size": m["size"], "pss": m["Pss"], "perms": m["perms"], "path": m["path"]} for m in anon_maps],
        })
    return processes


SHARED_LIBS = re.compile(r"/(libwebkit|libjavascriptcore|libgtk-|libgdk-|libLLVM|libgallium)[^/]*$")


def foreign(mine):
    """Processes outside the measured tree that map WebKit, GTK or Mesa libraries (they would share pages with it)."""
    found = []
    for entry in os.listdir("/proc"):
        if not entry.isdigit() or int(entry) in mine:
            continue
        try:
            with open(f"/proc/{entry}/maps") as handle:
                hit = any(SHARED_LIBS.search(line.rstrip()) for line in handle)
            if hit:
                with open(f"/proc/{entry}/comm") as handle:
                    found.append(f"{handle.read().strip()}:{entry}")
        except OSError:
            pass
    return found


def main():
    root, out, label, point = int(sys.argv[1]), sys.argv[2], sys.argv[3], sys.argv[4]
    processes = snapshot(root)
    others = foreign({p["pid"] for p in processes})
    record = {"label": label, "point": point, "processes": processes, "foreign": others}
    with open(out, "a") as handle:
        handle.write(json.dumps(record) + "\n")
    total_pss = sum(p["rollup"]["Pss"] for p in processes) / 1024
    total_uss = sum(p["uss"] for p in processes) / 1024
    parts = " ".join(f"{p['role']}={p['rollup']['Pss'] / 1024:.1f}/{p['uss'] / 1024:.1f}" for p in processes)
    print(f"MEM {label} {point} PSS {total_pss:.1f} MB USS {total_uss:.1f} MB; PSS/USS by process: {parts}"
          + (f"; FOREIGN {' '.join(others)}" if others else ""), flush=True)


main()
