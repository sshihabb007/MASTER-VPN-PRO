import math
import os
import struct
import zlib


def make_png(width, height, pixel_fn):
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter type 0
        for x in range(width):
            r, g, b, a = pixel_fn(x, y, width, height)
            raw.extend((r, g, b, a))

    def chunk(tag, data):
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )


def inside_shield(nx, ny):
    # Normalized coords in [-1, 1]
    if ny < -0.78 or ny > 0.84:
        return False
    if ny <= 0.05:
        top_curve = -0.78 + 0.10 * (abs(nx) / 0.72) ** 1.6
        return abs(nx) <= 0.72 and ny >= top_curve
    t = (ny - 0.05) / 0.79
    half_w = 0.72 * (1.0 - t**1.35)
    return abs(nx) <= max(0.0, half_w)


def shihab_shield_pixel(x, y, w, h):
    nx = ((x + 0.5) / w) * 2.0 - 1.0
    ny = ((y + 0.5) / h) * 2.0 - 1.0

    # Rounded dark badge background
    rx = max(0.0, abs(nx) - 0.56)
    ry = max(0.0, abs(ny) - 0.56)
    dist_corner = math.hypot(rx, ry)
    if dist_corner > 0.40:
        return (0, 0, 0, 0)

    # Anti-aliased outer edge
    alpha = 255
    if dist_corner > 0.36:
        alpha = int(max(0.0, min(1.0, (0.40 - dist_corner) / 0.04)) * 255)

    # Subtle dark gradient background (#141821 -> #1E2636)
    t_bg = (ny + 1.0) * 0.5
    br = int(20 + 10 * (1.0 - t_bg))
    bg = int(24 + 14 * (1.0 - t_bg))
    bb = int(34 + 22 * (1.0 - t_bg))

    if inside_shield(nx * 1.08, ny * 1.08):
        # Outer cyan-blue shield gradient (#38BDF8 -> #0284C7)
        gt = (ny + 0.8) / 1.65
        sr = int(56 * (1.0 - gt) + 2 * gt)
        sg = int(189 * (1.0 - gt) + 132 * gt)
        sb = int(248 * (1.0 - gt) + 199 * gt)

        # Inner dark core cutout with glowing center dot
        if inside_shield(nx * 1.52, (ny - 0.02) * 1.52):
            d_center = math.hypot(nx, ny - 0.02)
            if d_center <= 0.17:
                return (125, 211, 252, alpha)
            return (20, 30, 46, alpha)
        return (sr, sg, sb, alpha)

    return (br, bg, bb, alpha)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ext_icons = os.path.join(root, "browser-extension", "icons")
    os.makedirs(ext_icons, exist_ok=True)
    for sz in (16, 32, 48, 128):
        out_path = os.path.join(ext_icons, "icon{0}.png".format(sz))
        with open(out_path, "wb") as f:
            f.write(make_png(sz, sz, shihab_shield_pixel))

    # Also generate Android launcher icons so android-app has matching crisp mipmap PNGs
    android_res = os.path.join(root, "android-app", "app", "src", "main", "res")
    mipmaps = {
        "mipmap-mdpi": 48,
        "mipmap-hdpi": 72,
        "mipmap-xhdpi": 96,
        "mipmap-xxhdpi": 144,
        "mipmap-xxxhdpi": 192,
    }
    for folder, sz in mipmaps.items():
        d = os.path.join(android_res, folder)
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "ic_launcher.png"), "wb") as f:
            f.write(make_png(sz, sz, shihab_shield_pixel))


if __name__ == "__main__":
    main()

