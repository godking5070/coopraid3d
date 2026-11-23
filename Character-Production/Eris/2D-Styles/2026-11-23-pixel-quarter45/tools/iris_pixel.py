"""Iris quarter-view pixel sprites in five pixel-art styles.

The character is built from shapes in a 56x80 "standard" design space
(Iris about 72 px tall, facing screen down-left at 45 degrees), rasterised
per style with 8x supersampling and majority vote, then shaded, outlined
and detailed per pixel.  Pure python + numpy.

usage: python iris_pixel.py OUT_DIR [style ...]
"""
import math
import os
import struct
import sys
import zlib

import numpy as np

# --------------------------------------------------------------------- png io
def write_png(path, rgba):
    rgba = np.ascontiguousarray(rgba.astype(np.uint8))
    h, w = rgba.shape[:2]
    raw = b"".join(b"\x00" + rgba[y].tobytes() for y in range(h))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(png)


def hx(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.int32)


# ------------------------------------------------------------------ materials
EMPTY, HAIR, HOOD, BAND, SKIN, FLOWER, GOLD, RIBBON, COAT, NAVY, FUR, BOOT, SOLE, COLLAR, BOW, BACKHAIR = range(16)
NAMES = {HAIR: "hair", HOOD: "hood", BAND: "band", SKIN: "skin", FLOWER: "flower", GOLD: "gold", RIBBON: "ribbon",
         COAT: "coat", NAVY: "navy", FUR: "fur", BOOT: "boot", SOLE: "sole", COLLAR: "collar", BOW: "bow",
         BACKHAIR: "hair"}

# ----------------------------------------------------------------- the shapes
# Each shape: (kind, params, material, transform-group).  Draw order = list order.
def circle_chain(points, r):
    return [("ell", (x, y, r, r), None) for x, y in points]


def build_shapes():
    S = []

    def add(kind, params, mat, grp):
        S.append((kind, params, mat, grp))

    # 1. long back hair (behind everything)
    add("poly", [(13.5, 22), (11, 31), (9.8, 39), (10.2, 46), (12.0, 50.5), (15, 52.5), (21, 54), (34, 54),
                 (40.0, 53.6), (43.8, 51.6), (46.4, 47), (47.2, 40), (46.2, 32), (43.5, 26), (38, 22)], BACKHAIR, "hairback")
    for x, y, r in ((11.2, 48.6, 1.9), (14.4, 51.4, 1.7), (41.6, 52.8, 1.8), (44.8, 50.0, 1.8), (46.8, 44.6, 1.6)):
        add("ell", (x, y, r, r), BACKHAIR, "hairback")
    # pink curl tips that stick out beyond the sleeves
    add("poly", [(11.0, 38.5), (8.2, 42.5), (9.4, 45.0), (7.4, 48.0), (9.0, 50.6), (8.0, 53.0), (11.6, 54.2),
                 (13.4, 50.0), (13.6, 44.0)], BACKHAIR, "hairback")
    add("poly", [(44.6, 36.5), (47.6, 40.5), (46.8, 43.2), (49.2, 46.4), (47.6, 49.4), (48.6, 52.4), (45.0, 53.6),
                 (43.4, 49.0), (43.2, 42.0)], BACKHAIR, "hairback")
    # 2. hood: fitted dome + back drape
    add("ell", (27.6, 17.4, 13.2, 11.2), HOOD, "head")
    add("poly", [(34.5, 9.5), (39.4, 12.8), (42.8, 20), (44.4, 28.5), (44.0, 35.5), (41.2, 38.8), (37.8, 37.8),
                 (35.4, 32.5), (34.2, 25.5)], HOOD, "head")
    # 3. legs (behind the coat), boots
    add("poly", [(20.2, 54), (23.6, 54), (23.2, 64), (20.4, 64)], SKIN, "legs")
    add("poly", [(27.2, 54), (30.6, 54), (30.4, 66.5), (27.6, 66.5)], SKIN, "legs")
    add("poly", [(18.0, 64.0), (25.0, 64.0), (25.6, 69.0), (24.0, 70.6), (16.4, 70.6), (15.0, 69.2), (16.2, 67.0)], BOOT, "legs")
    add("poly", [(14.8, 69.4), (25.6, 69.4), (25.4, 71.8), (15.0, 71.8)], SOLE, "legs")
    add("ell", (21.6, 64.0, 3.9, 2.1), FUR, "legs")
    add("poly", [(25.2, 66.6), (32.6, 66.6), (33.4, 71.8), (31.8, 73.4), (23.4, 73.4), (22.0, 72.0), (23.4, 69.6)], BOOT, "legs")
    add("poly", [(21.8, 72.2), (33.4, 72.2), (33.2, 74.8), (22.0, 74.8)], SOLE, "legs")
    add("ell", (29.0, 66.6, 4.1, 2.2), FUR, "legs")
    add("poly", [(31.8, 68.0), (33.6, 68.0), (34.0, 71.6), (32.2, 71.6)], NAVY, "legs")
    # 4. far sleeve (screen left, her right arm)
    add("ell", (15.8, 41.0, 5.0, 8.6), COAT, "body")
    add("ell", (14.6, 48.6, 4.6, 2.6), FUR, "body")
    add("ell", (14.2, 51.2, 1.6, 1.6), SKIN, "body")
    # 5. coat body and front panel
    add("poly", [(18.5, 30.5), (33, 30.5), (36.8, 40), (41.8, 54.6), (38, 56.8), (27, 57.8), (16, 56.8), (12.6, 54.6),
                 (15.2, 40)], COAT, "body")
    add("poly", [(20.2, 32.5), (27.2, 32.5), (29.2, 57.2), (17.4, 56.6)], NAVY, "body")
    # hem patches
    add("poly", [(32.6, 56.6), (32.8, 52.6), (34.4, 50.6), (36.4, 50.6), (38.0, 52.6), (38.2, 56.4)], NAVY, "body")
    add("poly", [(13.0, 54.8), (13.6, 51.8), (15.2, 50.6), (16.6, 52.0), (16.8, 56.4)], NAVY, "body")
    # 6. near sleeve (screen right, her left arm, toward the viewer)
    add("ell", (38.4, 40.2, 6.2, 9.2), COAT, "body")
    add("ell", (39.2, 48.8, 5.6, 3.0), FUR, "body")
    add("ell", (39.6, 52.0, 1.7, 1.7), SKIN, "body")
    # 7. neck, collar ruffle, bow
    add("poly", [(22.6, 27.5), (27.2, 27.5), (27.2, 31.5), (22.6, 31.5)], SKIN, "head")
    add("ell", (25.4, 31.0, 6.4, 2.3), COLLAR, "body")
    add("poly", [(21.6, 31.3), (25.2, 32.6), (21.6, 34.2)], BOW, "body")
    add("poly", [(29.0, 31.3), (25.4, 32.6), (29.0, 34.2)], BOW, "body")
    add("poly", [(24.2, 32.6), (25.4, 32.2), (24.0, 37.0), (22.6, 36.6)], BOW, "body")
    add("poly", [(25.6, 32.6), (26.6, 32.4), (28.4, 36.6), (27.0, 37.0)], BOW, "body")
    # 8. hood opening: hair inside the hood, then face
    add("ell", (23.6, 21.8, 10.4, 9.8), HAIR, "head")
    add("ell", (22.6, 23.4, 7.8, 7.3), SKIN, "head")
    add("poly", [(16.8, 27), (20.6, 31.0), (23.6, 30.2), (19.4, 25)], SKIN, "head")
    # 9. headband along the top of the opening
    add("arc", (23.6, 21.8, 10.4, 9.8, 1.7, 195, 352), BAND, "head")
    # 10. bangs and side locks
    add("poly", [(13.6, 15.4), (33.6, 14.6), (33.8, 24.5), (31.4, 21.2), (29.6, 23.0), (27.6, 20.4), (25.0, 22.2),
                 (22.6, 19.8), (20.0, 21.8), (17.6, 19.6), (15.4, 22.6)], HAIR, "head")
    add("poly", [(14.6, 18.5), (17.6, 22.5), (17.4, 28.5), (16.4, 33.5), (14.2, 38.5), (10.8, 36.0), (11.8, 28.6),
                 (12.6, 22.4)], HAIR, "head")
    add("ell", (11.4, 36.8, 2.4, 2.4), HAIR, "head")
    add("poly", [(30.6, 20.2), (34.6, 21.0), (35.6, 28.2), (36.6, 34.2), (33.8, 38.2), (31.6, 32.0), (31.2, 25.2)], HAIR, "head")
    add("ell", (34.4, 37.4, 2.4, 2.4), HAIR, "head")
    # 11. flower and ribbons
    add("poly", [(32.8, 20.4), (33.8, 20.4), (32.8, 27.6), (31.6, 27.2)], RIBBON, "head")
    add("poly", [(34.6, 20.4), (35.6, 20.4), (37.4, 26.6), (36.2, 27.0)], RIBBON, "head")
    for dx, dy in ((0, -2.1), (2.1, 0), (0, 2.1), (-2.1, 0)):
        add("ell", (34.0 + dx, 18.0 + dy, 1.9, 1.9), FLOWER, "head")
    add("ell", (34.0, 18.0, 1.0, 1.0), GOLD, "head")
    return S


# ------------------------------------------------------------- rasterisation
def raster(shapes, style):
    sc = style["scale"]
    SS = 8
    tr = style["groups"]
    W = int(math.ceil(style["canvas"][0] * sc))
    H = int(math.ceil(style["canvas"][1] * sc))
    hy, hx_ = np.mgrid[0:H * SS, 0:W * SS].astype(np.float32)
    px = (hx_ + 0.5) / (SS * sc)
    py = (hy + 0.5) / (SS * sc)
    mat = np.zeros((H * SS, W * SS), np.int16)
    z = np.full((H * SS, W * SS), -1, np.int16)
    for zi, (kind, p, m, grp) in enumerate(shapes):
        s, (ax, ay), (ox, oy) = tr.get(grp, (1.0, (0, 0), (0, 0)))
        mx = (px - ox - ax) / s + ax
        my = (py - oy - ay) / s + ay
        if kind == "ell":
            cx, cy, rx, ry = p
            inside = ((mx - cx) / rx) ** 2 + ((my - cy) / ry) ** 2 <= 1
        elif kind == "poly":
            inside = point_in_poly(mx, my, p)
        elif kind == "arc":
            cx, cy, rx, ry, t, a0, a1 = p
            d = np.sqrt(((mx - cx) / rx) ** 2 + ((my - cy) / ry) ** 2)
            ang = (np.degrees(np.arctan2(my - cy, mx - cx)) + 360) % 360
            inside = (d <= 1.0) & (d >= 1.0 - t / min(rx, ry)) & (ang >= a0) & (ang <= a1)
        mat[inside] = m
        z[inside] = zi
    # majority vote per target pixel
    blocks = mat.reshape(H, SS, W, SS).transpose(0, 2, 1, 3).reshape(H, W, SS * SS)
    zb = z.reshape(H, SS, W, SS).transpose(0, 2, 1, 3).reshape(H, W, SS * SS)
    counts = np.stack([(blocks == i).sum(-1) for i in range(16)], -1)
    counts[..., 0] = counts[..., 0] * 1.05  # slight bias against fringe pixels
    out = np.argmax(counts, -1).astype(np.int16)
    # z of the chosen material = max z among its samples (front-most)
    zz = np.where(blocks == out[..., None], zb, -1).max(-1)
    return out, zz


def point_in_poly(x, y, pts):
    inside = np.zeros(x.shape, bool)
    n = len(pts)
    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        cond = ((y1 > y) != (y2 > y))
        with np.errstate(divide="ignore", invalid="ignore"):
            xi = (x2 - x1) * (y - y1) / (y2 - y1 + 1e-12) + x1
        inside ^= cond & (x < xi)
    return inside


# --------------------------------------------------------------- pixel tools
def nb(a, dy, dx, fill):
    out = np.full_like(a, fill)
    H, W = a.shape
    ys = slice(max(dy, 0), H + min(dy, 0))
    yd = slice(max(-dy, 0), H + min(-dy, 0))
    xs = slice(max(dx, 0), W + min(dx, 0))
    xd = slice(max(-dx, 0), W + min(-dx, 0))
    out[ys, xs] = a[yd, xd]
    return out


def run_length(mat, dy, dx, cap=8):
    """steps from each pixel in direction (dy,dx) until the material changes."""
    d = np.full(mat.shape, cap, np.int16)
    cur = mat.copy()
    for k in range(1, cap + 1):
        n = nb(mat, -dy * k, -dx * k, -1)
        stop = (n != mat) & (d == cap)
        d[stop] = k
    return d


def cleanup(mat, zz, protect=()):
    """remove 1-pixel spurs: pixels with <=1 same-material 4-neighbours take the neighbour majority."""
    for _ in range(2):
        same = sum((nb(mat, dy, dx, -1) == mat).astype(np.int16) for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)))
        bad = (same <= 1) & ~np.isin(mat, protect)
        if not bad.any():
            break
        votes = np.zeros(mat.shape + (16,), np.int16)
        for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            n = nb(mat, dy, dx, 0)
            for i in range(16):
                votes[..., i] += n == i
        mat = np.where(bad, np.argmax(votes, -1), mat).astype(np.int16)
    return mat, zz


# ------------------------------------------------------------------- styling
def render(style, out_dir):
    shapes = build_shapes()
    mat, zz = raster(shapes, style)
    mat, zz = cleanup(mat, zz, protect=(GOLD, FLOWER, RIBBON, NAVY))
    H, W = mat.shape
    sc = style["scale"]
    pal = style["palette"]
    nt = style["tones"]          # tones per ramp (ramps are dark -> light)
    base_i = style["base_tone"]   # index of the base tone in each ramp
    solid = mat > 0

    # ---- tone index per pixel (0 = darkest)
    tone = np.full((H, W), base_i, np.int16)
    grp = np.where(mat == BACKHAIR, HAIR, mat)
    d_r = run_length(grp, 0, 1)
    d_d = run_length(grp, 1, 0)
    d_l = run_length(grp, 0, -1)
    d_u = run_length(grp, -1, 0)
    ks = style["shadow_px"]
    shaded = np.isin(mat, [HAIR, BACKHAIR, HOOD, COAT, FUR, BOOT, FLOWER, SKIN, NAVY, BOW, SOLE, COLLAR])
    sh1 = shaded & ((d_r <= ks) | (d_d <= ks))
    tone[sh1] = base_i - 1
    if style.get("deep_shadow"):
        sh2 = shaded & ((np.minimum(d_r, d_d) <= max(1, ks // 2)) & (d_r + d_d <= ks + 1))
        tone[sh2] = base_i - 2
    hl = shaded & ((d_l <= style["light_px"]) | (d_u <= style["light_px"])) & ~sh1 & (mat != SKIN) & (mat != NAVY)
    tone[hl] = base_i + 1
    # cast shadows: bangs over the forehead, coat over the legs, hood over the back hair
    up = nb(mat, 1, 0, 0)
    up2 = nb(mat, 2, 0, 0)
    tone[(mat == SKIN) & ((up == HAIR) | (up == BAND))] = base_i - 1
    tone[(mat == SKIN) & ((up == COAT) | (up == NAVY) | (up2 == COAT) | (up2 == NAVY)) & (np.arange(H)[:, None] > 50 * sc)] = base_i - 1
    tone[(mat == BACKHAIR) & ((up == HOOD) | (up2 == HOOD))] = base_i - 1
    tone[mat == BACKHAIR] = np.minimum(tone[mat == BACKHAIR], base_i)  # back hair never takes highlights
    # hair strands: soft wavy darker lines inside large hair areas
    if style.get("strands"):
        per = style["strands"]
        yy0, xx0 = np.mgrid[0:H, 0:W]
        wave = (xx0 + 2.2 * np.sin(yy0 / (2.6 * sc))) % per
        inner = np.isin(grp, [HAIR]) & (d_l > 1) & (d_r > ks) & (d_u > 1) & (d_d > ks)
        tone[inner & (wave < 1.0) & (tone >= base_i)] = base_i - 1
    # retro: shadows become 50% checker dither, no highlights
    if style.get("dither_shadow"):
        chk0 = ((np.arange(H)[:, None] + np.arange(W)[None, :]) % 2) == 0
        soft = shaded & (tone == base_i - 1) & (np.minimum(d_r, d_d) > 1)
        tone[soft & chk0] = base_i
        tone[tone > base_i] = base_i
    tone = np.clip(tone, 0, nt - 1)

    # ---- dithering for the retro style
    if style.get("dither"):
        chk = ((np.arange(H)[:, None] + np.arange(W)[None, :]) % 2) == 0
        edge_band = shaded & ((d_r == ks + 1) | (d_d == ks + 1)) & (tone == base_i)
        tone[edge_band & chk] = base_i - 1

    # ---- colour lookup (hair ramps blend cream -> pink downwards)
    rgb = np.zeros((H, W, 3), np.int32)
    yy = np.arange(H)[:, None].repeat(W, 1) / sc
    for m in range(1, 16):
        sel = mat == m
        if not sel.any():
            continue
        name = NAMES[m]
        ramp = [hx(c) for c in pal[name]]
        rgb[sel] = np.stack(ramp)[tone[sel]]
        if name == "hair":
            pink = np.stack([hx(c) for c in pal["hair_pink"]])[tone[sel]]
            t0, t1 = style["pink_band"]
            t = np.clip((yy[sel] - t0) / (t1 - t0), 0, 1)
            if style.get("dither"):
                chk = (((np.arange(H)[:, None] + np.arange(W)[None, :]) % 2) == 0)[sel]
                use_pink = (t > 0.66) | ((t > 0.33) & chk)
            else:
                use_pink = t > 0.5
            rgb[sel] = np.where(use_pink[:, None], pink, rgb[sel])

    # ---- interior lines: front-most edge of a part against a different part behind it
    line_c = style["inner_line"]
    if line_c is not None:
        linegrp = np.where(np.isin(mat, [BACKHAIR]), HAIR, mat)
        lines = np.zeros((H, W), bool)
        for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
            n = nb(linegrp, dy, dx, 0)
            nz = nb(zz, dy, dx, -1)
            diff = solid & (n > 0) & (n != linegrp) & (zz > nz)
            # skip lines that would eat small accents
            diff &= ~np.isin(mat, [GOLD, FLOWER, RIBBON])
            diff &= ~((mat == HAIR) & (n == HOOD) & (nb(mat, 0, 0, 0) == HAIR) & False)
            lines |= diff
        # face keeps a soft edge only against hair
        lines &= ~((mat == SKIN) & np.isin(nb(linegrp, 0, 1, 0), [COLLAR]))
        if line_c == "dark":
            dark = np.zeros((H, W, 3), np.int32)
            for m in range(1, 16):
                sel = mat == m
                if sel.any():
                    dark[sel] = hx(pal[NAMES[m]][0])
            ink = hx(style["outline"]) if style["outline"] != "selout" else hx("#3a2b4a")
            col = (dark * 0.55 + ink * 0.45).astype(np.int32)
        else:
            col = np.broadcast_to(hx(line_c), (H, W, 3))
        rgb[lines] = col[lines]

    # ---- face, eyes, buttons and embroidery at pixel level
    draw_details(rgb, mat, style)

    # ---- outer outline (1 px outside the silhouette)
    outline = np.zeros((H, W), bool)
    nbcol = np.zeros((H, W, 3), np.int32)
    for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
        n = nb(solid, dy, dx, False)
        add = n & ~solid & ~outline
        c = nb_rgb(rgb, dy, dx)
        nbcol[add] = c[add]
        outline |= add
    if style["outline"] == "selout":
        oc = (nbcol * style.get("selout_mix", 0.45) + hx(style.get("selout_ink", "#4a3a63")) * (1 - style.get("selout_mix", 0.45)) * 0.8).astype(np.int32)
        rgb[outline] = oc[outline]
    elif style["outline"] is not None:
        rgb[outline] = hx(style["outline"])
    alpha = (solid | (outline if style["outline"] is not None else False)).astype(np.int32) * 255

    rgba = np.concatenate([np.clip(rgb, 0, 255), alpha[..., None]], -1)
    # crop to content + 2 px
    ys, xs = np.nonzero(alpha)
    rgba = rgba[max(0, ys.min() - 2):ys.max() + 3, max(0, xs.min() - 2):xs.max() + 3]
    name = style["id"]
    write_png(os.path.join(out_dir, f"{name}.png"), rgba)
    big = np.repeat(np.repeat(rgba, style["preview_scale"], 0), style["preview_scale"], 1)
    write_png(os.path.join(out_dir, f"{name}-x{style['preview_scale']}.png"), big)
    print("WROTE", name, rgba.shape[1], "x", rgba.shape[0])
    return rgba


def nb_rgb(rgb, dy, dx):
    out = np.zeros_like(rgb)
    for c in range(3):
        out[..., c] = nb(rgb[..., c], dy, dx, 0)
    return out


def to_px(style, grp, x, y):
    s, (ax, ay), (ox, oy) = style["groups"].get(grp, (1.0, (0, 0), (0, 0)))
    X = ((x - ax) * s + ax + ox) * style["scale"]
    Y = ((y - ay) * s + ay + oy) * style["scale"]
    return X, Y


def put(rgb, mat, x, y, color, only=None):
    H, W = mat.shape
    x, y = int(round(x)), int(round(y))
    if 0 <= x < W and 0 <= y < H and (only is None or mat[y, x] in only):
        rgb[y, x] = hx(color)


def draw_details(rgb, mat, style):
    pal = style["palette"]
    lvl = style["detail"]  # "tiny", "std", "hd"
    eye_dark, eye_mid, eye_light = pal["eye"]
    star = pal["star"]
    lash = pal["lash"]
    # eyes: near (screen right) and far (screen left)
    for which, (ex, ey) in (("near", (25.6, 24.2)), ("far", (19.0, 24.2))):
        cx, cy = to_px(style, "head", ex, ey)
        if lvl == "tiny":
            x0, y0 = int(math.floor(cx)), int(math.floor(cy)) - 1
            put(rgb, mat, x0, y0, lash)
            put(rgb, mat, x0, y0 + 1, eye_mid)
            if which == "near":
                put(rgb, mat, x0 + 1, y0, lash)
                put(rgb, mat, x0 + 1, y0 + 1, star)
        elif lvl == "std":
            w = 3 if which == "near" else 2
            x0 = int(round(cx - w / 2))
            y0 = int(round(cy - 2))
            for i in range(w):
                put(rgb, mat, x0 + i, y0, lash)
                put(rgb, mat, x0 + i, y0 + 1, eye_dark)
                put(rgb, mat, x0 + i, y0 + 2, eye_mid)
                put(rgb, mat, x0 + i, y0 + 3, eye_light)
            put(rgb, mat, x0 + (1 if w == 3 else 0), y0 + 2, star)
            put(rgb, mat, x0 + w - 1, y0 + 1, "#ffffff")
            put(rgb, mat, x0 - 1 if which == "far" else x0 + w, y0, lash)
        else:  # hd
            w = 5 if which == "near" else 3
            h = 7
            x0 = int(round(cx - w / 2))
            y0 = int(round(cy - h / 2))
            for i in range(-1, w + 1):
                put(rgb, mat, x0 + i, y0, lash)
            for j in range(1, h):
                for i in range(w):
                    c = eye_dark if j <= 2 else (eye_mid if j <= 4 else eye_light)
                    put(rgb, mat, x0 + i, y0 + j, c)
            sx = x0 + w // 2
            sy = y0 + 4
            for dx_, dy_ in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
                if which == "far" and dx_ != 0:
                    continue
                put(rgb, mat, sx + dx_, sy + dy_, star)
            put(rgb, mat, x0 + w - 1, y0 + 1, "#ffffff")
            put(rgb, mat, x0 + w - 2, y0 + 1, "#ffffff")
            put(rgb, mat, x0, y0 + h - 1, eye_light)
            if which == "near":
                put(rgb, mat, x0 + w + 1, y0 - 1, lash)
    # blush and mouth
    for bx, by in ((17.6, 27.0), (27.6, 27.0)):
        x, y = to_px(style, "head", bx, by)
        put(rgb, mat, x, y, pal["blush"], only=(SKIN,))
        if lvl != "tiny":
            put(rgb, mat, x + 1, y, pal["blush"], only=(SKIN,))
        if lvl == "hd":
            put(rgb, mat, x - 1, y, pal["blush"], only=(SKIN,))
            put(rgb, mat, x, y + 1, pal["blush"], only=(SKIN,))
    mx, my = to_px(style, "head", 21.4, 28.6)
    if lvl == "tiny":
        put(rgb, mat, mx, my, pal["mouth"][0], only=(SKIN,))
    elif lvl == "std":
        put(rgb, mat, mx, my, pal["mouth"][0], only=(SKIN,))
        put(rgb, mat, mx + 1, my, pal["mouth"][0], only=(SKIN,))
        put(rgb, mat, mx + 1, my + 1, pal["mouth"][1], only=(SKIN,))
    else:
        for i in range(-1, 3):
            put(rgb, mat, mx + i, my, pal["mouth"][0], only=(SKIN,))
        for i in range(0, 2):
            put(rgb, mat, mx + i, my + 1, pal["mouth"][1], only=(SKIN,))
            put(rgb, mat, mx + i, my + 2, pal["mouth"][1], only=(SKIN,))
    # gold buttons (2 x 3) on the navy panel, buckle on the near boot, bow clasp
    rows = (37.5, 43.0, 48.5) if lvl != "tiny" else (39.0, 47.0)
    for by in rows:
        for bx in (21.0 + (by - 37) * -0.08, 25.6 + (by - 37) * 0.04):
            x, y = to_px(style, "body", bx, by)
            put(rgb, mat, x, y, pal["gold"][-1], only=(NAVY,))
            if lvl == "hd":
                put(rgb, mat, x + 1, y, pal["gold"][1], only=(NAVY,))
                put(rgb, mat, x, y + 1, pal["gold"][1], only=(NAVY,))
                put(rgb, mat, x + 1, y + 1, pal["gold"][0], only=(NAVY,))
    x, y = to_px(style, "body", 25.4, 32.6)
    put(rgb, mat, x, y, pal["gold"][-1])
    x, y = to_px(style, "legs", 33.8, 69.5)
    put(rgb, mat, x, y, pal["gold"][-1])
    if lvl != "tiny":
        put(rgb, mat, x, y + 1, pal["gold"][1])
    # clover embroidery on the hood drape and near sleeve, clover on hem patches
    emb = pal["flower"][-1]
    for gx, gy, g in ((41.6, 30.0, "head"), (39.0, 21.0, "head"), (43.8, 37.0, "head"), (39.6, 41.0, "body"),
                      (35.3, 53.6, "body")):
        x, y = to_px(style, g, gx, gy)
        if lvl == "tiny" and g == "head" and gy > 25:
            continue
        col = "#ffffff" if gy > 50 else emb
        only = (NAVY,) if gy > 50 else (HOOD, COAT)
        put(rgb, mat, x, y, col, only=only)
        if lvl == "hd":
            for dx_, dy_ in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                put(rgb, mat, x + dx_, y + dy_, col, only=only)


# ------------------------------------------------------------------- palettes
BASE_PAL = {
    # ramps: dark -> light
    "hair": ["#c9a27e", "#e8c99c", "#f6e3bd", "#fff4dc"],
    "hair_pink": ["#d98f9f", "#efafb8", "#f8ccd0", "#fde3e3"],
    "hood": ["#aab4cf", "#d4dbea", "#f4f5fa", "#ffffff"],
    "band": ["#1c2147", "#2d3670", "#3f4a8c", "#56619f"],
    "skin": ["#e3a996", "#f6cdbd", "#fde6dc", "#fff2ea"],
    "flower": ["#4f78c0", "#7fa8e4", "#a9c8f2", "#cfe1fa"],
    "gold": ["#9b6f24", "#d4a443", "#f0cf72", "#fff0a8"],
    "ribbon": ["#161a3a", "#262d5e", "#38427e", "#4d589a"],
    "coat": ["#b9bdd3", "#dcdeea", "#f6f4f0", "#ffffff"],
    "navy": ["#1a1f44", "#2b3366", "#3b4583", "#525d9c"],
    "fur": ["#b8bfd4", "#dfe3ee", "#fbfbff", "#ffffff"],
    "boot": ["#aeb4c9", "#d8dcea", "#f5f6fa", "#ffffff"],
    "sole": ["#a89880", "#cdbfa6", "#e6dcc8", "#f4ede0"],
    "collar": ["#b9bdd3", "#e2e4ee", "#fbfbfd", "#ffffff"],
    "bow": ["#151a3c", "#262e62", "#39437f", "#4f5a99"],
    "eye": ["#1f4c94", "#3f8fd6", "#8fd0f2"],
    "star": "#ffe26a",
    "lash": "#2a1f3d",
    "blush": "#f3a3ac",
    "mouth": ["#9c3b4f", "#e07a86"],
}


def shifted(pal, **over):
    p = {k: (list(v) if isinstance(v, list) else v) for k, v in pal.items()}
    p.update(over)
    return p


def std_groups():
    return {"head": (1.0, (0, 0), (0, 0)), "hairback": (1.0, (0, 0), (0, 0)), "body": (1.0, (0, 0), (0, 0)), "legs": (1.0, (0, 0), (0, 0))}


STYLES = [
    dict(id="01-classic-mmo", scale=1.0, canvas=(56, 80), groups=std_groups(), palette=BASE_PAL, tones=4,
         base_tone=2, shadow_px=2, light_px=1, outline="#1c1626", inner_line="dark", detail="std",
         pink_band=(40, 47), strands=5, preview_scale=6),
    dict(id="02-hd-detail", scale=2.0, canvas=(56, 80), groups=std_groups(),
         palette=shifted(BASE_PAL,
                         hair=["#b98a78", "#dcb08e", "#f1d3aa", "#fbe9c9", "#fff7e6"],
                         hair_pink=["#c9798f", "#e39aa9", "#f3bcc3", "#fbd9dc", "#fff0ef"],
                         hood=["#8f98bf", "#bcc5df", "#e1e6f2", "#f8f9fc", "#ffffff"],
                         coat=["#9ea3c4", "#c8cbe0", "#e8e8f0", "#fbfaf6", "#ffffff"],
                         fur=["#a3abc9", "#cdd3e6", "#eef0f7", "#ffffff", "#ffffff"],
                         boot=["#9aa1c0", "#c6cbe0", "#e8ebf4", "#fbfcff", "#ffffff"],
                         skin=["#d6927f", "#eeb5a3", "#fbd4c6", "#ffe9e0", "#fff5f0"],
                         navy=["#141838", "#222a58", "#323d78", "#47529a", "#5d69b0"],
                         bow=["#121636", "#202856", "#313b76", "#46519a", "#5b67ae"],
                         band=["#141838", "#222a58", "#323d78", "#47529a", "#5d69b0"],
                         ribbon=["#121636", "#202856", "#313b76", "#46519a", "#5b67ae"],
                         flower=["#3e64ad", "#5f8ad4", "#87aeea", "#b0cdf6", "#d8e7fd"],
                         gold=["#8a5d1c", "#c08d35", "#e3b956", "#f6da86", "#fff3bf"],
                         sole=["#94836c", "#b9a78d", "#d6c8ae", "#ebe2d0", "#f7f1e6"],
                         collar=["#a3a8c8", "#cdd0e3", "#eef0f6", "#ffffff", "#ffffff"]),
         tones=5, base_tone=3, shadow_px=3, light_px=1, deep_shadow=True, outline="selout", selout_mix=0.3, selout_ink="#241a38", inner_line="dark",
         detail="hd", pink_band=(39, 47), strands=6, preview_scale=4),
    dict(id="03-tiny-sd", scale=0.7, canvas=(56, 80),
         groups={"head": (1.15, (27, 18), (0, 0)), "hairback": (0.92, (28, 24), (0, 2.2)), "body": (0.85, (27, 31), (0, 2.0)), "legs": (0.8, (25, 54), (0, -4.2))},
         palette=BASE_PAL, tones=4, base_tone=2, shadow_px=1, light_px=1, outline="#1c1626", inner_line=None,
         detail="tiny", pink_band=(41, 47), preview_scale=8),
    dict(id="04-pastel-lineless", scale=1.15, canvas=(56, 80), groups=std_groups(),
         palette=shifted(BASE_PAL,
                         hair=["#e2c3ae", "#f3dcc3", "#fcecd9", "#fff8ee"],
                         hair_pink=["#e8b3c2", "#f5cbd5", "#fde0e5", "#fff0f2"],
                         hood=["#c9cde6", "#e3e6f4", "#f8f8fd", "#ffffff"],
                         coat=["#cfd0e6", "#e7e7f2", "#faf9f7", "#ffffff"],
                         navy=["#5b5f9a", "#7176b2", "#8a90c6", "#a3a9d8"],
                         bow=["#5b5f9a", "#7176b2", "#8a90c6", "#a3a9d8"],
                         band=["#5b5f9a", "#7176b2", "#8a90c6", "#a3a9d8"],
                         ribbon=["#5b5f9a", "#7176b2", "#8a90c6", "#a3a9d8"],
                         flower=["#7c9fe0", "#9dbcf0", "#bcd3f8", "#dbe8fd"],
                         gold=["#c9a466", "#e6c785", "#f6dea6", "#fff2cf"],
                         skin=["#f0c1b6", "#fbd9cf", "#fff0ea", "#fff8f4"],
                         fur=["#d0d4ea", "#e8eaf5", "#fdfdff", "#ffffff"],
                         boot=["#cdd1e8", "#e6e8f4", "#fafbff", "#ffffff"],
                         sole=["#c9bba8", "#ded3c2", "#eee6d9", "#f8f3ea"],
                         eye=["#4e73c4", "#6fa2e6", "#b3daf7"], lash="#6a5a8c", mouth=["#d07a8c", "#f2a6b1"]),
         tones=4, base_tone=2, shadow_px=2, light_px=1, outline="selout", inner_line=None, detail="std",
         pink_band=(40, 48), selout_mix=0.4, selout_ink="#5a4a78", preview_scale=6),
    dict(id="05-retro-dither", scale=1.0, canvas=(56, 80), groups=std_groups(),
         palette=shifted(BASE_PAL,
                         hair=["#c49464", "#e8c48c", "#f8e8b8", "#f8e8b8"],
                         hair_pink=["#d8788c", "#f0a8b0", "#f8d0d0", "#f8d0d0"],
                         hood=["#8c9cc8", "#c8d0e8", "#f8f8f8", "#f8f8f8"],
                         coat=["#8c9cc8", "#c8d0e8", "#f8f8f8", "#f8f8f8"],
                         fur=["#8c9cc8", "#c8d0e8", "#f8f8f8", "#f8f8f8"],
                         boot=["#8c9cc8", "#c8d0e8", "#f8f8f8", "#f8f8f8"],
                         collar=["#8c9cc8", "#c8d0e8", "#f8f8f8", "#f8f8f8"],
                         skin=["#e8a088", "#f8d0b8", "#f8e8d8", "#f8e8d8"],
                         navy=["#182048", "#283878", "#283878", "#4c64b0"],
                         bow=["#182048", "#283878", "#283878", "#4c64b0"],
                         band=["#182048", "#283878", "#283878", "#4c64b0"],
                         ribbon=["#182048", "#283878", "#283878", "#4c64b0"],
                         flower=["#4c64b0", "#78a8e8", "#78a8e8", "#c8d0e8"],
                         gold=["#a87830", "#e8b848", "#e8b848", "#f8e8b8"],
                         sole=["#a87830", "#c49464", "#e8c48c", "#e8c48c"],
                         eye=["#283878", "#4c64b0", "#78a8e8"], star="#f8e8b8", lash="#182048", blush="#f0a8b0",
                         mouth=["#a83850", "#d8788c"]),
         tones=4, base_tone=2, shadow_px=2, light_px=1, outline="#182048", inner_line="#283878", detail="std",
         dither=True, dither_shadow=True, pink_band=(38, 48), preview_scale=6),
]


def sheet(sprites, path, floor="#ad9bc4", target_h=440, native=False):
    """Sprites side by side on a game-like floor with a soft ellipse shadow."""
    tiles = []
    for rgba in sprites:
        h, w = rgba.shape[:2]
        f = 1 if native else max(1, int(round(target_h / h)))
        tiles.append(np.repeat(np.repeat(rgba, f, 0), f, 1))
    pad = 24 if not native else 8
    th = max(t.shape[0] for t in tiles) + pad * 2
    tw = sum(t.shape[1] for t in tiles) + pad * (len(tiles) + 1)
    bg = np.zeros((th, tw, 3), np.float32) + hx(floor)
    x = pad
    for t in tiles:
        h, w = t.shape[:2]
        y = th - pad - h
        # shadow ellipse under the feet
        cx, cy = x + w * 0.47, y + h - (h * 0.03)
        yy, xx = np.mgrid[0:th, 0:tw]
        e = ((xx - cx) / (w * 0.42)) ** 2 + ((yy - cy) / (h * 0.05 + 2)) ** 2 <= 1
        bg[e] = bg[e] * 0.82
        a = t[..., 3:4] / 255.0
        bg[y:y + h, x:x + w] = t[..., :3] * a + bg[y:y + h, x:x + w] * (1 - a)
        x += w + pad
    out = np.concatenate([np.clip(bg, 0, 255), np.full((th, tw, 1), 255)], -1)
    write_png(path, out)
    print("SHEET", path, tw, th)


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    want = sys.argv[2:]
    done = []
    for st in STYLES:
        if not want or st["id"] in want or st["id"][:2] in want:
            done.append(render(st, out))
    sheet(done, os.path.join(out, "sheet.png"))
    sheet(done, os.path.join(out, "sheet-native.png"), native=True)
