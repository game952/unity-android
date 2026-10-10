#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ski2obj v3: CRT_SkelSkin → OBJ + MTL + PNG (贴图链锚名制定案版)
在 v2 几何闭环基础上:
  1. 解析 MaterialLib 子块 → 锚名 + blendmode (贴图查找锚, 非文件自身名)
  2. resolve 链: 原名 → 剥尾缀 → 层后缀 → 无下划线变体
  3. OBJ 加 mtllib/usemtl/vn; MTL map_Kd; DDS→PNG (Pillow)
用法: python3 ski2obj_v3.py [ski目录] [输出目录] [贴图目录]"""
import struct, os, re, sys

TAIL_LOST, GRP, VTAIL = 19, 30, 21

def u32(d, o): return struct.unpack_from("<I", d, o)[0]
def u16(d, o): return struct.unpack_from("<H", d, o)[0]
def f32(d, o): return struct.unpack_from("<f", d, o)[0]


def walk(d, start, vc):
    p = start; ok = tot = 0; verts = []; trunc = 0; F = len(d)
    for i in range(vc):
        if p + 4 > F: return None, p, "EOF@v%d" % i
        nb = u32(d, p); p += 4
        if not 1 <= nb <= 8: return None, p, "nb=%d@v%d" % (nb, i)
        bones = []
        for j in range(nb):
            if p + 29 > F: return None, p, "组EOF@v%d" % i
            bone = d[p]; w = f32(d, p + 1)
            if bone > 128 or not 0 <= w <= 1.02: return None, p, "骨权@v%d" % i
            pos = struct.unpack_from("<3f", d, p + 5)
            if not all(-60 < c < 60 for c in pos): return None, p, "pos@v%d" % i
            nrm = struct.unpack_from("<3f", d, p + 17)
            n2 = nrm[0]**2 + nrm[1]**2 + nrm[2]**2
            tot += 1; ok += 0.8 < n2 < 1.2
            bones.append((bone, w, pos, nrm)); p += GRP
        if p + VTAIL <= F:
            flag = d[p]; p += 1
            tang = struct.unpack_from("<3f", d, p); p += 12
            uv = struct.unpack_from("<2f", d, p); p += 8
        else:
            trunc += 1; flag, tang, uv = 1, (0.0, 0.0, 1.0), (0.0, 0.0); p += VTAIL
        verts.append((bones, flag, tang, uv))
    return ok / max(tot, 1), p, verts


def find_chain(d):
    m = d.find(b"CRT_MaterialLib")
    if m < 0: m = d.find(b"CRT_Mtl")
    F = len(d)
    for p in range(m + 4, min(m + 0x800, F - 64)):
        vc = u32(d, p)
        if not 3 < vc < 4000: continue
        if not all(abs(f32(d, p + 4 + 4 * k)) < 100 for k in range(5)): continue
        if not 1 <= u32(d, p + 24) <= 4: continue
        r, end, verts = walk(d, p + 24, vc)
        if r is not None and r > 0.9:
            gap = end - F
            if -2 <= gap <= TAIL_LOST + 2: return m, p, vc, r, verts
    return m, None, 0, 0, None


# ---------- v3: 新版式 (索引表在头部/MaterialLib 后接顶点块) ----------
# 与老格式差异:
#   组: bone u8 + w f32 + pos 3f + nrm 3f = 29B, 无 pad (老 GRP=30 含 1B pad)
#   尾: flag u8 + pad u8 + tangent 3f + uv 2f = 22B (老 VTAIL=21 无 pad)
#   版式: [头: scene, icnt, 索引][partN: 01+名A+名B+icnt+索引]* [MaterialLib]
#         [Mtl 块][partM: 名, 元数据, vc, 顶点]*


def walk2(d, start, vc, grp=30, vtail=21, plim=500.0, allow_partial=False):
    """新版式顶点链 walk (与老格式同构: 组30B含尾pad, 尾21B=flag+tang+uv, pos校验放宽)。
    返回 (rate, end, verts), verts 元组 = (bones, flag, tang, uv)。
    allow_partial=True 时中途失败也返回已验证顶点 (rate 按已走部分计)"""
    p = start; ok = tot = 0; verts = []; F = len(d)
    for i in range(vc):
        if p + 4 > F:
            if allow_partial and len(verts) >= 3: return ok / max(tot, 1), p, verts
            return None, p, "EOF@v%d" % i
        nb = u32(d, p); p += 4
        if not 1 <= nb <= 8:
            if allow_partial and len(verts) >= 3: return ok / max(tot, 1), p, verts
            return None, p, "nb=%d@v%d" % (nb, i)
        bones = []
        for j in range(nb):
            if p + 29 > F:
                if allow_partial and len(verts) >= 3: return ok / max(tot, 1), p, verts
                return None, p, "组EOF@v%d" % i
            bone = d[p]; w = f32(d, p + 1)
            if bone > 200 or not 0 <= w <= 1.02:
                if allow_partial and len(verts) >= 3: return ok / max(tot, 1), p, verts
                return None, p, "骨权@v%d" % i
            pos = struct.unpack_from("<3f", d, p + 5)
            if not all(-plim < c < plim for c in pos):
                if allow_partial and len(verts) >= 3: return ok / max(tot, 1), p, verts
                return None, p, "pos@v%d" % i
            nrm = struct.unpack_from("<3f", d, p + 17)
            n2 = nrm[0]**2 + nrm[1]**2 + nrm[2]**2
            tot += 1; ok += 0.8 < n2 < 1.2
            bones.append((bone, w, pos, nrm)); p += grp
        if p + vtail <= F:
            flag = d[p]; p += 1
            p += vtail - 21          # 预留: 若未来变体尾长不同
            tang = struct.unpack_from("<3f", d, p); p += 12
            uv = struct.unpack_from("<2f", d, p); p += 8
        else:
            if allow_partial and len(verts) >= 3: return ok / max(tot, 1), p, verts
            return None, p, "尾EOF@v%d" % i
        verts.append((bones, flag, tang, uv))
    return ok / max(tot, 1), p, verts


def _u16_triplet_ok(d, off, n, lim=8192):
    """检查 off 起 n 个 u16 是否像索引序列"""
    for i in range(n):
        v = u16(d, off + 2 * i)
        if v >= lim and v != 0xffff: return False
    return True


def walk_free(d, start, minv=30, plim=500.0):
    """裸顶点流自适应 walk: 无 vc 声明, 逐顶点校验直到失败。
    返回 (rate, end, verts) 或 (None, p, 原因)。命中要求 ≥minv 顶点且 rate≥0.85"""
    p = start; ok = tot = 0; verts = []; F = len(d)
    while p + 55 <= F:
        nb = u32(d, p)
        if not 1 <= nb <= 8: break
        q = p + 4; good = True; bones = []
        for j in range(nb):
            if q + 30 > F: good = False; break
            bone = d[q]; w = f32(d, q + 1)
            if bone > 200 or not 0 <= w <= 1.02: good = False; break
            pos = struct.unpack_from("<3f", d, q + 5)
            if not all(-plim < c < plim for c in pos): good = False; break
            nrm = struct.unpack_from("<3f", d, q + 17)
            n2 = nrm[0]**2 + nrm[1]**2 + nrm[2]**2
            tot += 1; ok += 0.8 < n2 < 1.2
            bones.append((bone, w, pos, nrm))
            q += 30
        if not good: break
        if q + 21 > F: break
        flag = d[q]; q += 1
        tang = struct.unpack_from("<3f", d, q); q += 12
        uv = struct.unpack_from("<2f", d, q); q += 8
        verts.append((bones, flag, tang, uv))
        p = q
    rate = ok / max(tot, 1)
    if len(verts) >= minv and rate >= 0.85: return rate, p, verts
    return None, p, "free%d" % len(verts)


def parse_ski_v3(d):
    """新版式全解析。返回 (mtl, verts, tris, anchor_hint, parts_info)
    verts 布局与老格式一致: [(bones, flag, tang, uv)]"""
    F = len(d)
    mtl = d.find(b"CRT_MaterialLib")
    if mtl < 0: raise ValueError("新格式: 无 MaterialLib")
    # ---- A. 索引表区 [0x10, mtl) ----
    p1 = None
    for p in range(0x10, min(0x160, mtl - 16)):
        v = u32(d, p)
        if 3 <= v <= 30000 and p + 4 + 2 * v <= mtl + 8:
            if _u16_triplet_ok(d, p + 4, min(16, v)):
                end = p + 4 + 2 * v
                if end + 8 >= mtl or d[end] == 0x01 or u32(d, end) <= 1:
                    p1 = p; break
    if p1 is None: raise ValueError("新格式: 索引表头未定位")
    cur = p1 + 4 + 2 * u32(d, p1)
    nparts = 1
    while cur + 20 < mtl and nparts < 32:
        while cur < mtl and d[cur] == 0: cur += 1
        if cur >= mtl - 20 or d[cur] != 0x01: break
        q = cur + 1; names = []
        good = True
        for _ in range(2):
            ln = u32(d, q)
            if 2 <= ln <= 48 and q + 4 + ln <= F:
                try:
                    s = d[q+4:q+4+ln].decode()
                    if not all(33 <= ord(c) < 127 for c in s): good = False; break
                    names.append(s); q += 4 + ln
                except: good = False; break
            else: good = False; break
        if not good: break
        icnt = u32(d, q)
        if not 3 <= icnt <= 30000 or q + 4 + 2 * icnt > mtl + 8: break
        nparts += 1
        cur = q + 4 + 2 * icnt
    idx_end = cur
    # ---- B. 顶点区 [mtl, F) ----
    verts_all = []; parts = []; p = mtl
    while p < F - 64:
        # 先试名字块
        advanced = False
        ln = u32(d, p)
        name = None
        if 4 <= ln <= 40 and p + 4 + ln <= F:
            try:
                s = d[p+4:p+4+ln].decode()
                if all(33 <= ord(c) < 127 for c in s): name = s
            except: pass
        if name is not None:
            # vc 候选后滑动找 nb 起点 (part 头部有不定长 20B 附加数据)
            for q in range(p + 4 + ln, min(p + 4 + ln + 260, F - 64)):
                vc = u32(d, q)
                if 3 <= vc <= 30000:
                    for s0 in range(q + 4, min(q + 4 + 80, F - 64)):
                        r, end2, verts = walk2(d, s0, vc, allow_partial=True)
                        if r is not None and r >= 0.85:
                            verts_all.extend(verts)
                            parts.append((name, vc, round(r, 2)))
                            p = end2; advanced = True
                            break
                    if advanced: break
            if advanced: continue
            p += 4 + ln; continue
        # 无名字块: 三分支 — vc声明续排 / 裸顶点流兜底 / 前进1B
        vc = u32(d, p)
        hit = False
        if 3 <= vc <= 30000:
            for s0 in range(p + 4, min(p + 4 + 120, F - 64)):
                r, end2, verts = walk2(d, s0, vc, allow_partial=True)
                if r is not None and r >= 0.85:
                    verts_all.extend(verts)
                    parts.append(("(续排)", vc, round(r, 2)))
                    p = end2; hit = True; break
        if not hit:
            r, end2, verts = walk_free(d, p)
            if r is not None:
                verts_all.extend(verts)
                parts.append(("(裸流)", len(verts), round(r, 2)))
                p = end2; hit = True
        if hit: continue
        p += 1
    if not verts_all: raise ValueError("新格式: 顶点区未解析")
    # ---- C. 索引 → 三角 (全局池) ----
    vtot = len(verts_all)
    raw = []
    # part1
    raw += [u16(d, p1 + 4 + 2 * i) for i in range(u32(d, p1))]
    # 再扫一遍 partN 索引 (重新走链)
    cur = p1 + 4 + 2 * u32(d, p1)
    while cur + 20 < idx_end:
        while cur < idx_end and d[cur] == 0: cur += 1
        if cur >= idx_end - 20 or d[cur] != 0x01: break
        q = cur + 1
        for _ in range(2):
            ln = u32(d, q)
            if not 2 <= ln <= 48: break
            q += 4 + ln
        icnt = u32(d, q)
        if 3 <= icnt <= 30000 and q + 4 + 2 * icnt <= idx_end + 8:
            raw += [u16(d, q + 4 + 2 * i) for i in range(icnt)]
            cur = q + 4 + 2 * icnt
        else: break
    tris = [(raw[i], raw[i+1], raw[i+2]) for i in range(0, len(raw) - 2, 3)
            if max(raw[i], raw[i+1], raw[i+2]) < vtot]
    anchor_hint = ""
    return mtl, verts_all, tris, len(raw), parts


def find_indices(d, mtl):
    seg_end = mtl - 4; lo = max(0x20, mtl - 0x4000)
    for o in range(lo, seg_end - 16):
        c = u32(d, o); n = (seg_end - o - 4) // 2
        if 3 < c <= n + 4 and abs(c - n) <= 4:
            if all(u16(d, o + 4 + 2 * i) < 0x0fff or u16(d, o + 4 + 2 * i) == 0xffff
                   for i in range(min(8, n))):
                return o, c, o + 4
    return None, 0, 0


def parse_tris(idx):
    tris = []; strip = []
    for v in idx:
        if v == 0xffff: strip = []; continue
        strip.append(v)
        if len(strip) >= 3:
            i = len(strip) - 3
            a, b, c = strip[i], strip[i+1], strip[i+2]
            tris.append((a, c, b) if i % 2 else (a, b, c))
    return tris


# ---------- v3: 材质锚名 ----------
def parse_materials(d, ml):
    mats = []
    for m in re.finditer(rb"CRT_Mtl(Standard|Mu)(?![a-z])", d[ml:ml + 0x1000]):
        o = ml + m.end(); ln = u32(d, o)
        if not 2 <= ln <= 40: continue
        try: anchor = d[o+4:o+4+ln].decode()
        except: continue
        bm = ""
        for b in re.finditer(rb"[\x20-\x7e]{2,8}", d[o+4+ln:o+4+ln+40]):
            t = b.group().decode()
            if re.match(r"^(Add|Alpha|Blend|Sub|Mul|None)?\x00?$", t) and t.strip("\x00"):
                bm = t.strip("\x00"); break
        mats.append((m.group(1).decode(), anchor, bm))
    return mats


def resolve_tex(anchor, texdir):
    n = anchor.lower()
    cands = [n, re.sub(r"[a-zA-Z0-9]$", "", n)]
    m = re.match(r"^(.*?)[_\-]?[aA][lLtTgG]?$", n)
    if m and m.group(1): cands.append(m.group(1))
    cands.append(n.replace("_", ""))
    for c in cands:
        for e in (".dds", ".tga", ".bmp", ".png"):
            p = os.path.join(texdir, c + e)
            if os.path.exists(p): return p, c
    return None, None


def dds2png(src, dst):
    from PIL import Image
    with Image.open(src) as im:
        im.convert("RGBA").save(dst)
    return os.path.getsize(dst)


def convert(path, out_dir, texdir):
    d = open(path, "rb").read(); F = len(d)
    name = os.path.splitext(os.path.basename(path))[0]
    mtl, chain, vc, rate, verts = find_chain(d)
    if chain is None: return "%s 链定位失败" % name
    ih, icnt, idx0 = find_indices(d, mtl)
    if ih is None: return "%s 索引定位失败" % name
    idx = [u16(d, idx0 + 2 * i) for i in range(icnt)]
    tris = parse_tris(idx)

    # 材质: 主层 = 第一个 resolve 成功的子块; 记录全部子块
    mats = parse_materials(d, mtl)
    main_tex = main_stem = None; add_tex = None
    for kind, anchor, bm in mats:
        p, stem = resolve_tex(anchor, texdir)
        if not p: continue
        if bm == "Add" and add_tex is None and main_tex: add_tex = (p, stem)
        elif main_tex is None: main_tex, main_stem = p, stem
    if main_tex is None and mats:
        main_stem = mats[0][1]  # 未命中也用锚名占位

    obj_d = os.path.join(out_dir, "obj"); tex_d = os.path.join(out_dir, "texture")
    os.makedirs(obj_d, exist_ok=True); os.makedirs(tex_d, exist_ok=True)
    png_ref = ""
    if main_tex:
        png = os.path.join(tex_d, main_stem + ".png")
        try:
            dds2png(main_tex, png)
            png_ref = "../texture/%s.png" % main_stem
        except Exception as e:
            png_ref = "(转换失败 %s)" % e

    # MTL
    mtlp = os.path.join(obj_d, name + ".mtl")
    with open(mtlp, "w") as f:
        f.write("# ski2obj v3  %s\n" % name)
        f.write("newmtl %s\nKd 1.0 1.0 1.0\nd 1.0\n" % name)
        if png_ref and not png_ref.startswith("("):
            f.write("map_Kd %s\n" % png_ref)
        for kind, anchor, bm in mats:
            f.write("# pass: %s anchor=%s blend=%s\n" % (kind, anchor, bm or "diffuse"))

    # OBJ (v + vn + vt + f v//vn/vt)
    op = os.path.join(obj_d, name + ".obj")
    with open(op, "w") as f:
        f.write("# %s vc=%d icnt=%d\nmtllib %s.mtl\nusemtl %s\n"
                % (name, vc, icnt, name, name))
        for bones, flag, tang, uv in verts:
            x, y, z = bones[0][2]
            f.write("v %.6f %.6f %.6f\n" % (x, y, z))
        for bones, flag, tang, uv in verts:
            nx, ny, nz = bones[0][3]
            f.write("vn %.4f %.4f %.4f\n" % (nx, ny, nz))
        for bones, flag, tang, uv in verts:
            f.write("vt %.6f %.6f\n" % (uv[0], uv[1]))
        for a, b, c in tris:
            f.write("f %d/%d/%d %d/%d/%d %d/%d/%d\n"
                    % (a+1, a+1, a+1, b+1, b+1, b+1, c+1, c+1, c+1))
    return ("%-28s ✓ vc=%d 三角 %d | 材质子块 %d %s | 主贴图 %s | OBJ %dB"
            % (name, vc, len(tris), len(mats),
               "/".join(a[2] or "dif" for a in mats) or "-",
               main_stem + ("✓" if png_ref and not png_ref.startswith("(") else "?"),
               os.path.getsize(op)))


if __name__ == "__main__":
    BASE = "/home/z/my-project/share/agent-model/6ac1ae06d3e5cff5fbaa5478/tmp/awt/creature"
    ACT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(BASE, "actor")
    OUT = sys.argv[2] if len(sys.argv) > 2 else "tmp/demo_pack"
    TEX = sys.argv[3] if len(sys.argv) > 3 else os.path.join(BASE, "texture")
    files = ["a_wt_m_hea_00", "a_wt_m_clo_00", "a_wt_m_clo_01", "a_wt_m_sho_00", "a_wt_m_wai_01"]
    for n in files:
        p = os.path.join(ACT, n + ".ski")
        print(convert(p, OUT, TEX) if os.path.exists(p) else "(缺) " + n)
