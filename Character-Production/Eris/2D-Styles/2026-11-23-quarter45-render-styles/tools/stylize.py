"""Turn Iris quarter-view render passes into five 2D style concepts.

Usage:
  blender -b --python stylize.py -- PASS_DIR OUT_DIR [styles=cel,pixel,watercolor,manga,flat]

Only numpy (bundled with Blender) is used. Images are handled top-down
(row 0 = top) inside this script.
"""
import bpy
import math
import os
import sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
PASS_DIR, OUT_DIR = argv[0], argv[1]
STYLES = ["cel", "pixel", "watercolor", "manga", "flat"]
for a in argv[2:]:
    if a.startswith("styles="):
        STYLES = a.split("=", 1)[1].split(",")
os.makedirs(OUT_DIR, exist_ok=True)
RNG = np.random.default_rng(7)


# ----------------------------------------------------------------------------- io
def load(name):
    img = bpy.data.images.load(os.path.join(PASS_DIR, name + ".exr"))
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1].copy()


def save(path, rgba):
    h, w = rgba.shape[:2]
    img = bpy.data.images.new("out", w, h, alpha=True)
    img.pixels.foreach_set(np.clip(rgba[::-1], 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print("SAVED", path, w, h)


def to_srgb(c):
    c = np.clip(c, 0, None)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def to_lin(c):
    c = np.clip(c, 0, None)
    return np.where(c <= 0.04045, c / 12.92, np.power((c + 0.055) / 1.055, 2.4))


def hexc(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], np.float32)


# ------------------------------------------------------------------------ filters
def box1(a, r, axis):
    if r <= 0:
        return a
    pad = [(0, 0)] * a.ndim
    pad[axis] = (r + 1, r)
    p = np.pad(a, pad, mode="edge")
    c = np.cumsum(p, axis=axis, dtype=np.float64)
    n = a.shape[axis]
    hi = np.take(c, np.arange(2 * r + 1, 2 * r + 1 + n), axis=axis)
    lo = np.take(c, np.arange(0, n), axis=axis)
    return ((hi - lo) / (2 * r + 1)).astype(np.float32)


def blur(a, r, passes=3):
    a = a.astype(np.float32)
    for _ in range(passes):
        a = box1(box1(a, r, 0), r, 1)
    return a


def shift(a, dy, dx, fill=None):
    out = np.roll(a, (dy, dx), axis=(0, 1))
    if fill is not None:
        if dy > 0:
            out[:dy] = fill
        elif dy < 0:
            out[dy:] = fill
        if dx > 0:
            out[:, :dx] = fill
        elif dx < 0:
            out[:, dx:] = fill
    return out


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def line_cov(edges, width):
    """Anti-aliased line coverage of roughly `width` pixels around a 1px edge map."""
    r = max(1, int(round(width * 0.45)))
    b = blur(edges.astype(np.float32), r, passes=2)
    probe = np.zeros((9 * r + 9, 9 * r + 9), np.float32)
    probe[:, probe.shape[1] // 2] = 1
    peak = blur(probe, r, passes=2)[probe.shape[0] // 2, probe.shape[1] // 2]
    return np.clip(b / (peak * 0.5), 0, 1)


def value_noise(h, w, cell, seed):
    rng = np.random.default_rng(seed)
    gh, gw = h // cell + 3, w // cell + 3
    g = rng.random((gh, gw)).astype(np.float32)
    ys = np.arange(h) / cell
    xs = np.arange(w) / cell
    y0 = ys.astype(int)
    x0 = xs.astype(int)
    fy = smoothstep(0, 1, ys - y0)[:, None]
    fx = smoothstep(0, 1, xs - x0)[None, :]
    a = g[y0][:, x0]
    b = g[y0][:, x0 + 1]
    c = g[y0 + 1][:, x0]
    d = g[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(h, w, cell, octaves, seed):
    acc = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        acc += amp * value_noise(h, w, max(1, cell >> o), seed + o)
        tot += amp
        amp *= 0.5
    return acc / tot


def warp(a, dy, dx):
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    sy = np.clip(np.rint(yy + dy).astype(int), 0, h - 1)
    sx = np.clip(np.rint(xx + dx).astype(int), 0, w - 1)
    return a[sy, sx]


# -------------------------------------------------------------------- color space
def lin_to_oklab(c):
    m1 = np.array([[0.4122214708, 0.5363325363, 0.0514459929],
                   [0.2119034982, 0.6806995451, 0.1073969566],
                   [0.0883024619, 0.2817188376, 0.6299787005]], np.float32)
    m2 = np.array([[0.2104542553, 0.7936177850, -0.0040720468],
                   [1.9779984951, -2.4285922050, 0.4505937099],
                   [0.0259040371, 0.7827717662, -0.8086757660]], np.float32)
    lms = np.cbrt(np.clip(c @ m1.T, 0, None))
    return lms @ m2.T


def oklab_to_lin(l):
    m2i = np.array([[1.0, 0.3963377774, 0.2158037573],
                    [1.0, -0.1055613458, -0.0638541728],
                    [1.0, -0.0894841775, -1.2914855480]], np.float32)
    m1i = np.array([[4.0767416621, -3.3077115913, 0.2309699292],
                    [-1.2684380046, 2.6097574011, -0.3413193965],
                    [-0.0041960863, -0.7034186147, 1.7076147010]], np.float32)
    lms = (l @ m2i.T) ** 3
    return np.clip(lms @ m1i.T, 0, None)


def kmeans(samples, k, iters=18, seed=3):
    rng = np.random.default_rng(seed)
    # k-means++ style init
    cent = [samples[rng.integers(len(samples))]]
    for _ in range(1, k):
        d = np.min(((samples[:, None, :] - np.array(cent)[None]) ** 2).sum(-1), axis=1)
        cent.append(samples[rng.choice(len(samples), p=d / d.sum())])
    cent = np.array(cent, np.float32)
    for _ in range(iters):
        lab = np.argmin(((samples[:, None, :] - cent[None]) ** 2).sum(-1), axis=1)
        for i in range(k):
            sel = samples[lab == i]
            if len(sel):
                cent[i] = sel.mean(0)
    return cent


def assign(img_lab, cent, chunk=200000):
    flat = img_lab.reshape(-1, 3)
    out = np.empty(len(flat), np.int32)
    for s in range(0, len(flat), chunk):
        out[s:s + chunk] = np.argmin(((flat[s:s + chunk, None, :] - cent[None]) ** 2).sum(-1), axis=1)
    return out.reshape(img_lab.shape[:2])


def mode_filter(labels, k, r, mask):
    best = np.full(labels.shape, -1.0, np.float32)
    out = labels.copy()
    for i in range(k):
        c = blur(((labels == i) & mask).astype(np.float32), r, passes=1)
        upd = (c > best) & mask
        out[upd] = i
        best[upd] = c[upd]
    return out


# ------------------------------------------------------------------- load passes
A = load("albedo")
alpha = A[..., 3]
H, W = alpha.shape
safe = np.maximum(alpha, 1e-4)[..., None]
albedo = np.clip(A[..., :3] / safe, 0, 1)
Np = load("normal")
nrm = Np[..., :3] / safe
nrm /= np.maximum(np.linalg.norm(nrm, axis=-1, keepdims=True), 1e-5)
Dp = load("depth")
depth = Dp[..., 0] / safe[..., 0]
lam = load("lambert")[..., 0] / safe[..., 0] / 0.955
key = load("key")[..., 0] / safe[..., 0] / 0.955
ao = load("ao")[..., 0] / safe[..., 0]
M = alpha > 0.5
depth = np.where(M, depth, depth[M].max() + 1.0)
# Cycles camera space looks down +Z; flip so +Z points toward the viewer (+Y up, +X right)
nrm[..., 2] *= -1
# key light from the viewer's upper-left, computed per pixel from normals
_l = np.array([-0.35, 0.15, 1.0], np.float32)
_l /= np.linalg.norm(_l)
LIT = np.clip((nrm * _l).sum(-1), 0, 1)
# cast shadows (hood over face, skirt over legs) from the rendered key pass
CAST = blur(((key < lam * 0.45) & (lam > 0.15) & M).astype(np.float32), 2, 2) > 0.5

# crop box shared by every style (pixel art uses its own grid)
ys, xs = np.nonzero(alpha > 0.02)
PAD = 56
top, bot = max(0, ys.min() - PAD), min(H, ys.max() + PAD)
cx = (xs.min() + xs.max()) // 2
half = int(max(xs.max() - xs.min(), (bot - top) * 0.70) // 2) + PAD
left, right = max(0, cx - half), min(W, cx + half)


def crop(img):
    return img[top:bot, left:right]


# ------------------------------------------------------------------ shared edges
def edge_maps(depth_t=0.0035, normal_t=0.32):
    lap_edges = np.zeros((H, W), bool)
    nrm_edges = np.zeros((H, W), bool)
    sil = np.zeros((H, W), bool)
    for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
        dn = shift(depth, dy, dx, fill=depth.max())
        an = shift(M, dy, dx, fill=False)
        # silhouette: opaque pixel touching background
        sil |= M & ~an
        # depth discontinuity: mark the nearer pixel only
        closer = depth < dn - 0.004
        lap_edges |= M & an & closer & (np.abs(depth - dn) > depth_t * 4)
        nn = shift(nrm, dy, dx, fill=0)
        dot = (nrm * nn).sum(-1)
        nrm_edges |= M & an & (dot < 1 - normal_t) & (depth <= dn + 0.002)
    return sil, lap_edges, nrm_edges


SIL, DEPTH_E, NRM_E = edge_maps()

# flattened albedo palette shared by several styles
# grade: the baked texture is pale, push chroma and a little contrast toward the concept art
alb_lab = lin_to_oklab(albedo)
alb_lab[..., 1:] *= 1.35
alb_lab[..., 0] = 0.55 + (alb_lab[..., 0] - 0.55) * 1.08
albedo = np.clip(oklab_to_lin(alb_lab), 0, 1)
samp = alb_lab[M][RNG.choice(int(M.sum()), 60000, replace=False)]


def flatten(k, r, seed=3):
    cent = kmeans(samp, k, seed=seed)
    lab = assign(alb_lab, cent)
    lab = mode_filter(lab, k, r, M)
    return cent, lab


def label_edges(lab, cent, thresh):
    e = np.zeros((H, W), bool)
    for dy, dx in ((0, 1), (1, 0)):
        ln = shift(lab, dy, dx, fill=-1)
        mn = shift(M, dy, dx, fill=False)
        diff = (ln != lab) & M & mn
        idx = np.where(diff)
        de = np.linalg.norm(cent[lab[idx]] - cent[np.clip(ln[idx], 0, None)], axis=-1)
        m = np.zeros((H, W), bool)
        m[idx] = de > thresh
        e |= m
    return e


GAME_H = 180  # in-game preview canvas height (character is about 160 px)


def downscale(rgba, target_h):
    """Area-average downscale with premultiplied alpha."""
    h, w = rgba.shape[:2]
    nh = target_h
    nw = max(1, int(round(w * target_h / h)))
    pm = rgba.astype(np.float64).copy()
    pm[..., :3] *= pm[..., 3:4]
    c = np.pad(pm, ((1, 0), (1, 0), (0, 0))).cumsum(0).cumsum(1)
    ys = np.round(np.arange(nh + 1) * h / nh).astype(int)
    xs = np.round(np.arange(nw + 1) * w / nw).astype(int)
    S = c[ys[1:]][:, xs[1:]] - c[ys[:-1]][:, xs[1:]] - c[ys[1:]][:, xs[:-1]] + c[ys[:-1]][:, xs[:-1]]
    area = (ys[1:] - ys[:-1])[:, None] * (xs[1:] - xs[:-1])[None, :]
    avg = S / area[..., None]
    a = avg[..., 3:4]
    rgb = avg[..., :3] / np.maximum(a, 1e-6)
    return np.concatenate([rgb, a], -1).astype(np.float32)


def finish(rgb_srgb, a, name, extra=None):
    out = np.concatenate([np.clip(rgb_srgb, 0, 1), np.clip(a, 0, 1)[..., None]], -1)
    out = crop(out)
    save(os.path.join(OUT_DIR, name + ".png"), out)
    save(os.path.join(OUT_DIR, name + "-game.png"), downscale(out, GAME_H))
    return out


def light_terms():
    """(post-computed key light 0..1, cast-shadow mask)."""
    return LIT, CAST


def shadow_raw(lam_t=0.30, ao_t=0.25, cast_k=None):
    return ((LIT < lam_t) | CAST | (ao < ao_t)) & M


def debug_masks():
    for nm, arr in (("dbg-lam", LIT), ("dbg-key", CAST.astype(np.float32)), ("dbg-ao", np.clip(ao, 0, 1)),
                    ("dbg-shadow", shadow_raw().astype(np.float32))):
        save(os.path.join(OUT_DIR, nm + ".png"), np.concatenate([np.repeat(arr[..., None], 3, -1), alpha[..., None]], -1))
    print("LAM pct", np.percentile(LIT[M], [5, 25, 50, 75, 95]), "AO pct", np.percentile(ao[M], [5, 25, 50, 75, 95]))


# =================================================================== 1. anime cel
def style_cel():
    cent, lab = flatten(40, 1)
    flat_lin = oklab_to_lin(cent[lab])
    lit, cast = light_terms()
    raw = shadow_raw(0.25, 0.26)
    shadow = blur(raw.astype(np.float32), 2, passes=2) > 0.5
    sh = blur(shadow.astype(np.float32), 1, passes=1)
    tint = to_lin(hexc("#B8AEDD"))
    deep = to_lin(hexc("#8E84C2"))
    col = flat_lin * (1 - sh[..., None]) + flat_lin * tint * sh[..., None]
    # second, deeper tone in occluded crevices
    occl = blur(((ao < 0.22) & M).astype(np.float32), 1, passes=2)
    col = col * (1 - occl[..., None] * 0.55) + col * deep * occl[..., None] * 0.55
    # crisp rim light on the lit, screen-left silhouette
    rim = smoothstep(0.55, 0.75, (1 - np.abs(nrm[..., 2])) * np.clip(-nrm[..., 0] + 0.25, 0, 1.25)) * (~shadow)
    col = col + rim[..., None] * 0.22
    s = to_srgb(col)
    # lines
    cedge = label_edges(lab, cent, 0.16)
    inner = DEPTH_E | (NRM_E & (ao < 0.6)) | cedge
    sil_cov = line_cov(SIL | DEPTH_E & (blur(M.astype(np.float32), 3) < 0.92), 3.2)
    in_cov = line_cov(inner, 1.6) * 0.92
    ink = hexc("#2A2550")
    inner_col = s * 0.38 * 0.55 + ink * 0.45
    s = s * (1 - in_cov[..., None]) + inner_col * in_cov[..., None]
    s = s * (1 - sil_cov[..., None]) + ink * sil_cov[..., None]
    a = np.clip(alpha + line_cov(SIL, 3.2) * (blur(alpha, 2) > 0.02), 0, 1)
    return finish(s, a, "01-anime-cel")


# ============================================================= 2. 16-bit pixel art
def style_pixel():
    F = 6
    cent, lab = flatten(30, 1, seed=11)
    flat_lin = oklab_to_lin(cent[lab])
    lit, cast = light_terms()
    raw = shadow_raw(0.21, 0.24)
    shadow = blur(raw.astype(np.float32), 3, passes=2) > 0.5
    hil = (lit > 0.9) & ~shadow & (nrm[..., 1] > 0.35)
    col = np.where(shadow[..., None], flat_lin * to_lin(hexc("#B7ACDD")), flat_lin)
    col = np.where(hil[..., None], col + (1 - col) * 0.18, col)
    ok = lin_to_oklab(col)
    sm = ok[M][RNG.choice(int(M.sum()), 50000, replace=False)]
    pal = kmeans(sm, 22, seed=5)
    idx = assign(ok, pal)
    k = len(pal)
    # block mode down-sampling on the crop grid
    t, b_, l, r_ = top, bot, left, right
    hh = (b_ - t) // F
    ww = (r_ - l) // F
    sub_idx = idx[t:t + hh * F, l:l + ww * F].reshape(hh, F, ww, F).transpose(0, 2, 1, 3).reshape(hh, ww, F * F)
    sub_m = M[t:t + hh * F, l:l + ww * F].reshape(hh, F, ww, F).transpose(0, 2, 1, 3).reshape(hh, ww, F * F)
    sub_d = depth[t:t + hh * F, l:l + ww * F].reshape(hh, F, ww, F).transpose(0, 2, 1, 3).reshape(hh, ww, F * F)
    sub_e = DEPTH_E[t:t + hh * F, l:l + ww * F].reshape(hh, F, ww, F).transpose(0, 2, 1, 3).reshape(hh, ww, F * F)
    cover = sub_m.mean(-1)
    counts = np.zeros((hh, ww, k), np.int32)
    for i in range(k):
        counts[..., i] = ((sub_idx == i) & sub_m).sum(-1)
    # favour small, high-contrast features (eyes, buttons, flower) a little
    lum = pal[:, 0]
    contrast = np.abs(lum - np.median(lum))
    weights = 1.0 + contrast * 1.2 + (lum < 0.5) * 1.6
    px_idx = np.argmax(counts * weights[None, None], -1)
    pm = cover >= 0.45
    dmin = np.where(sub_m, sub_d, 99).min(-1)
    # remove lonely pixels whose 8 neighbours all disagree (keep high-contrast ones)
    for _ in range(1):
        same = np.zeros((hh, ww), np.int32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy or dx:
                    same += (shift(px_idx, dy, dx, fill=-1) == px_idx) & shift(pm, dy, dx, fill=False)
        lonely = pm & (same == 0) & (contrast[px_idx] < 0.12)
        if lonely.any():
            votes = np.zeros((hh, ww, k), np.int32)
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dy or dx:
                        nb = shift(px_idx, dy, dx, fill=0)
                        nm = shift(pm, dy, dx, fill=False)
                        for i in range(k):
                            votes[..., i] += (nb == i) & nm
            px_idx = np.where(lonely, np.argmax(votes, -1), px_idx)
    rgb = to_srgb(oklab_to_lin(pal[px_idx]))
    ink = hexc("#241C46")
    # interior depth lines: darker shade of the pixel colour
    dl = np.zeros((hh, ww), bool)
    for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
        dn = shift(dmin, dy, dx, fill=99)
        nm = shift(pm, dy, dx, fill=False)
        dl |= pm & nm & (dmin < dn - 0.045)
    dl |= pm & (sub_e.sum(-1) >= F)
    rgb = np.where(dl[..., None], rgb * 0.45 + ink * 0.35, rgb)
    # 1px selective outline outside the silhouette
    out_m = np.zeros((hh, ww), bool)
    nb_col = np.zeros((hh, ww, 3), np.float32)
    for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
        nm = shift(pm, dy, dx, fill=False)
        nc = shift(rgb, dy, dx, fill=0)
        add = nm & ~pm & ~out_m
        nb_col[add] = nc[add]
        out_m |= add
    outline = nb_col * 0.28 + ink * 0.62
    rgb = np.where(out_m[..., None], outline, rgb)
    a = (pm | out_m).astype(np.float32)
    native = np.concatenate([rgb, a[..., None]], -1)
    save(os.path.join(OUT_DIR, "02-pixel-16bit-game.png"), native)
    up = np.repeat(np.repeat(native, F, 0), F, 1)
    save(os.path.join(OUT_DIR, "02-pixel-16bit.png"), up)
    return up


# ============================================================ 3. watercolor book
def style_watercolor():
    cent, lab = flatten(32, 3, seed=21)
    wok = cent[lab].copy()
    wok[..., 1:] *= 1.15
    wash = oklab_to_lin(wok)
    # uneven pigment load inside each wash (low frequency blooms)
    bloom = fbm(H, W, 90, 3, 29) - 0.5
    wash = np.clip(wash * (1 + bloom[..., None] * 0.35), 0, 1)
    lit, cast = light_terms()
    sh = np.maximum(1 - smoothstep(0.05, 0.5, lit), cast * 0.8)
    sh = np.maximum(sh, (1 - smoothstep(0.25, 0.7, ao)) * 0.85)
    sh = blur(sh * M, 5, passes=2)
    glaze = to_lin(hexc("#7F86D6"))
    col = wash * (1 - sh[..., None] * 0.8) + wash * glaze * sh[..., None] * 0.8
    # paper reserve: the most lit light areas fall back to bare paper
    reserve = smoothstep(0.8, 0.95, lit) * smoothstep(0.75, 0.9, lin_to_oklab(wash)[..., 0])
    reserve = blur(reserve, 3, 2)
    col = col * (1 - reserve[..., None] * 0.7) + reserve[..., None] * 0.7
    # wet edges: pigment pools (same hue, deeper) along wash boundaries
    s = to_srgb(col)
    diff = np.abs(s - blur(s, 4, passes=2)).sum(-1)
    wet = np.clip(diff * 3.0, 0, 1)
    rim_in = 1 - blur(M.astype(np.float32), 4, passes=2)
    wet = np.clip(wet + rim_in * 2.0, 0, 1) * M
    pooled = s * (s * 0.55 + 0.35)
    s = s * (1 - wet[..., None] * 0.6) + pooled * wet[..., None] * 0.6
    # light paper granulation inside the pigment
    gran = fbm(H, W, 5, 2, 31) - 0.5
    dens = np.clip(1 - s.mean(-1, keepdims=True), 0, 1)
    s = s - gran[..., None] * 0.10 * (0.15 + dens)
    # bleed: wobble the paint against the drawing
    dy = (fbm(H, W, 48, 3, 41) - 0.5) * 9
    dx = (fbm(H, W, 48, 3, 43) - 0.5) * 9
    s = warp(s, dy, dx)
    a_paint = warp(alpha, dy, dx)
    edge_noise = fbm(H, W, 10, 3, 47)
    a_paint = np.clip(blur(a_paint, 1) * 1.15 - (1 - blur(a_paint, 3)) * edge_noise * 1.2, 0, 1)
    # graphite under-drawing, offset from the paint
    pencil_e = SIL | DEPTH_E | (NRM_E & (ao < 0.5))
    pc = line_cov(pencil_e, 1.3) * (0.45 + 0.4 * fbm(H, W, 3, 2, 53))
    pc = shift(pc, 1, -1, fill=0)
    graphite = hexc("#4A3F4C")
    s = s * (1 - pc[..., None] * 0.75) + graphite * pc[..., None] * 0.75
    a = np.clip(np.maximum(a_paint, pc * 0.8), 0, 1)
    return finish(s, a, "03-watercolor-storybook")


# =================================================== 4. manga ink + screentone
def halftone(tone, cell, angle_deg=45):
    """tone 0..1 (1 = full ink). Returns ink coverage of round dots."""
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    t = math.radians(angle_deg)
    u = (xx * math.cos(t) + yy * math.sin(t)) / cell
    v = (-xx * math.sin(t) + yy * math.cos(t)) / cell
    du = u - np.floor(u) - 0.5
    dv = v - np.floor(v) - 0.5
    dist = np.sqrt(du * du + dv * dv)
    rad = np.sqrt(np.clip(tone, 0, 1) / math.pi)
    aa = 0.8 / cell
    return 1 - smoothstep(rad - aa, rad + aa, dist)


def style_manga():
    cent, lab = flatten(24, 2, seed=61)
    ok = cent[lab]
    L, ca, cb = ok[..., 0], ok[..., 1], ok[..., 2]
    chroma = np.sqrt(ca * ca + cb * cb)
    hue = np.degrees(np.arctan2(cb, ca)) % 360
    lit, cast = light_terms()
    shadow = blur(shadow_raw(0.25, 0.26).astype(np.float32), 2, 2) > 0.5
    black = (L < 0.42) & M
    spot = (chroma > 0.06) & (hue > 220) & (hue < 285) & (L > 0.45) & M
    # base tone from value: whites stay paper, mids get light tone
    base_tone = np.clip((0.93 - L) * 0.9, 0, 0.45)
    tone = np.where(shadow, np.maximum(base_tone + 0.28, 0.32), base_tone)
    tone = np.where(tone < 0.07, 0, tone)
    ink_cov = halftone(tone, 6.5) * (tone > 0)
    ink_cov = np.maximum(ink_cov, black.astype(np.float32))
    # hatching on black areas in light: white highlight streaks along the coat
    hl = black & (lit > 0.9) & ~shadow
    hatch = (np.sin((np.mgrid[0:H, 0:W][1] - np.mgrid[0:H, 0:W][0]) * 0.55) > 0.55)
    ink_cov = np.where(hl & hatch, 0.0, ink_cov)
    paper = hexc("#FBF8F1")
    spot_col = hexc("#3F7FD8")
    s = np.ones((H, W, 3), np.float32) * paper
    spot_tone = np.where(shadow, 1.0, 0.75)
    s = np.where(spot[..., None], paper * (1 - spot_tone[..., None]) + spot_col * spot_tone[..., None], s)
    s = s * (1 - ink_cov[..., None] * (~spot)[..., None])
    # bold brush lines: weight grows with depth gap
    cedge = label_edges(lab, cent, 0.2)
    inner = line_cov(DEPTH_E, 2.6) * 1.0
    inner = np.maximum(inner, line_cov(cedge | (NRM_E & (ao < 0.55)), 1.4))
    sil = line_cov(SIL, 4.6)
    lines = np.maximum(inner, sil)
    ink = hexc("#111014")
    s = s * (1 - lines[..., None]) + ink * lines[..., None]
    a = np.clip(alpha + line_cov(SIL, 4.6) * (blur(alpha, 3) > 0.02), 0, 1)
    return finish(s, a, "04-manga-screentone")


# ============================================================ 5. flat vector
def style_flat():
    cent, lab = flatten(18, 3, seed=71)
    ok = cent[lab].copy()
    # poster-like flats: stronger chroma, whites pushed to a clean warm ivory
    ok[..., 1:] *= 1.3
    ok[..., 0] = np.where(ok[..., 0] > 0.86, 0.97, ok[..., 0])
    base = oklab_to_lin(ok)
    lit, cast = light_terms()
    raw = shadow_raw(0.28, 0.26)
    shadow = blur(raw.astype(np.float32), 6, passes=2) > 0.5
    sh = blur(shadow.astype(np.float32), 1, 1)
    hl_raw = (lit > 0.88) & (nrm[..., 1] > 0.35) & ~shadow
    hl = blur((blur(hl_raw.astype(np.float32), 5, 2) > 0.55).astype(np.float32), 1, 1)
    # hue-shifted shadow: darker, cooler, a little more saturated
    sok = lin_to_oklab(base)
    sok[..., 0] *= 0.74
    sok[..., 1] = sok[..., 1] * 1.1 + 0.004
    sok[..., 2] = sok[..., 2] * 1.1 - 0.06
    shade = oklab_to_lin(sok)
    hok = lin_to_oklab(base)
    hok[..., 0] = np.minimum(hok[..., 0] * 1.07 + 0.02, 1.0)
    hok[..., 2] += 0.015
    high = oklab_to_lin(hok)
    col = base * (1 - sh[..., None]) + shade * sh[..., None]
    col = col * (1 - hl[..., None]) + high * hl[..., None]
    # soft contact shadow where parts overlap (no drawn lines)
    contact = blur(DEPTH_E.astype(np.float32), 3, 2)
    contact = np.clip(contact * 6, 0, 1) * 0.35
    col = col * (1 - contact[..., None])
    s = to_srgb(col)
    # no ink: the silhouette gets a band of its own colour, deepened, so it reads on any floor
    band = line_cov(SIL, 3.0)
    deep = to_srgb(oklab_to_lin(np.stack([sok[..., 0] * 0.62, sok[..., 1] * 1.2, sok[..., 2] * 1.2 - 0.02], -1)))
    s = s * (1 - band[..., None]) + deep * band[..., None]
    a = np.clip(blur(alpha, 1, 1) + band * (blur(alpha, 2) > 0.02), 0, 1)
    return finish(s, a, "05-flat-vector")


FUNCS = dict(debug=debug_masks, cel=style_cel, pixel=style_pixel, watercolor=style_watercolor, manga=style_manga, flat=style_flat)
for st in STYLES:
    FUNCS[st]()
print("DONE")
