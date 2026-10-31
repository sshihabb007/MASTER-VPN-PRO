#!/usr/bin/env python3
"""
Generates a crisp multi-resolution Windows .ico (16, 32, 48, 64, 256 px)
for Shihab Shield VPN using pure Python standard library (zlib + struct).
"""
import math
import os
import struct
import zlib


def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else (hi if v > hi else v)


def smoothstep(edge0, edge1, x):
    t = clamp((x - edge0) / (edge1 - edge0))
    return t * t * (3.0 - 2.0 * t)


def sd_rounded_box(px, py, cx, cy, hw, hh, r):
    qx = abs(px - cx) - hw + r
    qy = abs(py - cy) - hh + r
    out_d = math.hypot(max(qx, 0.0), max(qy, 0.0))
    in_d = min(max(qx, qy), 0.0)
    return out_d + in_d - r


def sd_shield(nx, ny):
    # Classic symmetric security shield centered around (0.5, 0.49)
    dx = abs(nx - 0.5)
    top = ny - 0.20
    bot = 0.82 - ny
    if top < 0.0:
        # Above top edge
        w = 0.27
        return math.hypot(max(dx - w, 0.0), -top)
    if ny <= 0.48:
        w = 0.27 + (ny - 0.20) * 0.04
        return dx - w
    # Taper smoothly down to shield tip at (0.5, 0.82)
    t = clamp((ny - 0.48) / (0.82 - 0.48))
    w = 0.281 * (1.0 - t * t * 0.96)
    if bot < 0.0:
        return math.hypot(dx, -bot)
    return (dx - w) * 0.86


def blend(dst, src_r, src_g, src_b, src_a):
    if src_a <= 0.0:
        return dst
    dr, dg, db, da = dst
    out_a = src_a + da * (1.0 - src_a)
    if out_a <= 1e-6:
        return (0.0, 0.0, 0.0, 0.0)
    out_r = (src_r * src_a + dr * da * (1.0 - src_a)) / out_a
    out_g = (src_g * src_a + dg * da * (1.0 - src_a)) / out_a
    out_b = (src_b * src_a + db * da * (1.0 - src_a)) / out_a
    return (out_r, out_g, out_b, out_a)


def render_shihab_icon_rgba(size):
    buf = bytearray(size * size * 4)
    aa = 1.35 / size

    for y in range(size):
        ny = (y + 0.5) / size
        for x in range(size):
            nx = (x + 0.5) / size
            col = (0.0, 0.0, 0.0, 0.0)

            # 1. Dark squircle background card
            d_bg = sd_rounded_box(nx, ny, 0.5, 0.5, 0.45, 0.45, 0.22)
            bg_alpha = 1.0 - smoothstep(-aa, aa, d_bg)
            if bg_alpha > 0.0:
                t = clamp(nx * 0.4 + ny * 0.75)
                bg_r = (14.0 * (1.0 - t) + 21.0 * t) / 255.0
                bg_g = (19.0 * (1.0 - t) + 33.0 * t) / 255.0
                bg_b = (28.0 * (1.0 - t) + 54.0 * t) / 255.0

                glow = math.exp(-((nx - 0.5) ** 2 + (ny - 0.45) ** 2) / 0.16)
                bg_r = clamp(bg_r + glow * 0.04)
                bg_g = clamp(bg_g + glow * 0.22)
                bg_b = clamp(bg_b + glow * 0.40)
                col = blend(col, bg_r, bg_g, bg_b, bg_alpha)

                border_w = max(0.018, 1.1 / size)
                border_a = (1.0 - smoothstep(0.0, aa, abs(d_bg + border_w * 0.5) - border_w * 0.5)) * bg_alpha
                if border_a > 0.0:
                    col = blend(col, 29.0 / 255.0, 161.0 / 255.0, 242.0 / 255.0, border_a * 0.72)

            # 2. Glowing gradient shield silhouette
            d_sh = sd_shield(nx, ny)
            sh_fill = 1.0 - smoothstep(-aa, aa, d_sh)
            if sh_fill > 0.0:
                gt = clamp((ny - 0.20) / 0.62)
                sr = (40.0 * (1.0 - gt) + 14.0 * gt) / 255.0
                sg = (184.0 * (1.0 - gt) + 116.0 * gt) / 255.0
                sb = (255.0 * (1.0 - gt) + 232.0 * gt) / 255.0
                # Left-half subtle specular highlight
                if nx < 0.5:
                    sr = clamp(sr + 0.07)
                    sg = clamp(sg + 0.08)
                    sb = clamp(sb + 0.06)
                col = blend(col, sr, sg, sb, sh_fill * 0.96)

            # 3. Inner orbital vortex / tunnel ring inside shield
            cx, cy = 0.5, 0.47
            dist_c = math.hypot(nx - cx, ny - cy)
            ring_d = abs(dist_c - 0.125) - max(0.028, 1.2 / size)
            ring_a = (1.0 - smoothstep(-aa, aa, ring_d)) * sh_fill
            if ring_a > 0.0:
                ang = math.atan2(ny - cy, nx - cx)
                # Leave a sleek dynamic gap on top-right for orbital motion look
                if not (-0.75 < ang < -0.15):
                    col = blend(col, 255.0 / 255.0, 255.0 / 255.0, 255.0 / 255.0, ring_a * 0.96)

            # Center core lock dot
            dot_a = (1.0 - smoothstep(-aa, aa, dist_c - 0.042)) * sh_fill
            if dot_a > 0.0:
                col = blend(col, 14.0 / 255.0, 24.0 / 255.0, 42.0 / 255.0, dot_a * 0.92)

            idx = (y * size + x) * 4
            buf[idx + 0] = int(clamp(col[0]) * 255.0 + 0.5)
            buf[idx + 1] = int(clamp(col[1]) * 255.0 + 0.5)
            buf[idx + 2] = int(clamp(col[2]) * 255.0 + 0.5)
            buf[idx + 3] = int(clamp(col[3]) * 255.0 + 0.5)

    return bytes(buf)


def encode_png(size, rgba_bytes):
    raw_rows = bytearray()
    stride = size * 4
    for y in range(size):
        raw_rows.append(0)
        raw_rows.extend(rgba_bytes[y * stride : (y + 1) * stride])

    def chunk(tag, data):
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    idat = zlib.compress(bytes(raw_rows), 9)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", idat)
        + chunk(b"IEND", b"")
    )


def build_ico(out_path):
    sizes = [16, 32, 48, 64, 256]
    png_blobs = []
    for s in sizes:
        rgba = render_shihab_icon_rgba(s)
        png_blobs.append((s, encode_png(s, rgba)))

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    header = struct.pack("<HHH", 0, 1, len(png_blobs))
    offset = 6 + 16 * len(png_blobs)
    entries = bytearray()
    payload = bytearray()
    for s, blob in png_blobs:
        dim = 0 if s >= 256 else s
        entries.extend(struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset))
        payload.extend(blob)
        offset += len(blob)

    with open(out_path, "wb") as f:
        f.write(header + entries + payload)


if __name__ == "__main__":
    target = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "app.ico")
    build_ico(target)
    print("Generated Shihab Shield VPN icon:", target, os.path.getsize(target), "bytes")

