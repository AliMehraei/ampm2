#!/usr/bin/env python3
"""Builds ampm2.app from a `dotnet publish -r osx-*` folder, and zips it keeping Unix permissions.

    python mac-bundle.py bundle <publish_dir> <icons_dir> <version> <out_dir>   -> <out_dir>/ampm2.app
    python mac-bundle.py zip <app_dir> <zip_path>

Works on Windows: file modes are set in the zip itself, so the executable bit survives.
"""
import os, shutil, struct, sys, zipfile

BUNDLE_ID = "com.alimehraei.ampm2"


def icns(icons_dir, out_path):
    # PNG-payload icns entries (macOS 10.7+)
    entries = [("icp4", 16), ("icp5", 32), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
               ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512)]
    chunks = b""
    for kind, size in entries:
        png = open(os.path.join(icons_dir, f"icon-{size}.png"), "rb").read()
        chunks += kind.encode() + struct.pack(">I", len(png) + 8) + png
    open(out_path, "wb").write(b"icns" + struct.pack(">I", len(chunks) + 8) + chunks)


def plist(version):
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>ampm2</string>
    <key>CFBundleDisplayName</key><string>ampm2</string>
    <key>CFBundleIdentifier</key><string>{BUNDLE_ID}</string>
    <key>CFBundleVersion</key><string>{version}</string>
    <key>CFBundleShortVersionString</key><string>{version}</string>
    <key>CFBundleExecutable</key><string>ampm2</string>
    <key>CFBundleIconFile</key><string>ampm2.icns</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>LSMinimumSystemVersion</key><string>14.0</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSPrincipalClass</key><string>NSApplication</string>
    <key>NSHumanReadableCopyright</key><string>© Ali Mehraei · PolyForm Noncommercial 1.0.0 + education</string>
</dict>
</plist>
"""


def bundle(publish_dir, icons_dir, version, out_dir):
    app = os.path.join(out_dir, "ampm2.app")
    if os.path.exists(app):
        shutil.rmtree(app)
    macos = os.path.join(app, "Contents", "MacOS")
    res = os.path.join(app, "Contents", "Resources")
    shutil.copytree(publish_dir, macos)
    os.makedirs(res, exist_ok=True)
    for junk in ("ampm2.pdb", "Ampm2.Core.pdb"):
        p = os.path.join(macos, junk)
        if os.path.exists(p):
            os.remove(p)
    icns(icons_dir, os.path.join(res, "ampm2.icns"))
    lic = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "LICENSE.md")
    if os.path.exists(lic):
        shutil.copy(lic, os.path.join(res, "LICENSE.md"))
    open(os.path.join(app, "Contents", "Info.plist"), "w", encoding="utf-8", newline="\n").write(plist(version))
    open(os.path.join(app, "Contents", "PkgInfo"), "w").write("APPL????")
    print(app)


def is_executable(rel, data_head):
    name = os.path.basename(rel)
    if rel.replace("\\", "/").endswith("Contents/MacOS/ampm2"):
        return True
    if name.endswith(".dylib") or name == "createdump":
        return True
    return data_head[:4] in (b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe")   # Mach-O 64 / fat


def zip_app(app_dir, zip_path):
    base = os.path.dirname(os.path.abspath(app_dir))
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for root, dirs, files in os.walk(app_dir):
            for d in sorted(dirs):
                full = os.path.join(root, d)
                zi = zipfile.ZipInfo(os.path.relpath(full, base).replace("\\", "/") + "/")
                zi.create_system = 3
                zi.external_attr = (0o40755 << 16) | 0x10
                z.writestr(zi, b"")
            for f in sorted(files):
                full = os.path.join(root, f)
                rel = os.path.relpath(full, base).replace("\\", "/")
                data = open(full, "rb").read()
                zi = zipfile.ZipInfo(rel)
                zi.create_system = 3            # Unix, so Archive Utility honours the modes
                zi.compress_type = zipfile.ZIP_DEFLATED
                zi.external_attr = ((0o100755 if is_executable(rel, data) else 0o100644) << 16)
                z.writestr(zi, data)
    print(zip_path)


if __name__ == "__main__":
    if sys.argv[1] == "bundle":
        bundle(*sys.argv[2:6])
    elif sys.argv[1] == "zip":
        zip_app(sys.argv[2], sys.argv[3])
