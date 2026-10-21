#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""C# ParseActContainer 的 Python 同构移植 — 对全部样本回归验证"""
import struct, math, sys

def u16(d, o): return struct.unpack_from("<H", d, o)[0]
def u32(d, o): return struct.unpack_from("<I", d, o)[0]
def f32(d, o): return struct.unpack_from("<f", d, o)[0]
def printable(d, o, n):
    return o + n <= len(d) and all(32 <= c < 127 for c in d[o:o+n])

def index_of(d, pat, fr):
    return d.find(pat, fr)

def chain_vtx(d, p, F):
    o = p; n = 0
    while o < F - 8:
        if o + 4 > F: break
        nbk = u32(d, o)
        if nbk < 1 or nbk > 8: break
        q = o + 4; tw = 0.0
        for i in range(nbk):
            if q + 30 > F: return n
            if d[q] > 127: return n
            w = f32(d, q + 1)
            if w < -0.01 or w > 1.001: return n
            tw += w; q += 30
        if nbk > 1 and (tw < 0.5 or tw > 2.5): return n
        if q + 21 <= F:
            u = f32(d, q + 13); v = f32(d, q + 17)
            if u < -8 or u > 8 or v < -8 or v > 8: return n
            o = q + 21
        elif q <= F: o = q
        else: return n
        n += 1
        if n > 60000: break
    return n

def walk_verts(d, p, F, limit):
    out = []; o = p
    while o < F - 8 and len(out) < limit:
        if o + 4 > F: break
        nbk = u32(d, o)
        if nbk < 1 or nbk > 8: break
        q = o + 4; tw = 0.0; ok = True
        for i in range(nbk):
            if q + 30 > F: ok = False; break
            if d[q] > 127: ok = False; break
            w = f32(d, q + 1)
            if w < -0.01 or w > 1.001: ok = False; break
            tw += w; q += 30
        if not ok: break
        if nbk > 1 and (tw < 0.5 or tw > 2.5): break
        if q + 21 <= F:
            u = f32(d, q + 13); v = f32(d, q + 17)
            if u < -8 or u > 8 or v < -8 or v > 8: break
            o = q + 21
        elif q <= F: o = q
        else: break
        out.append((struct.unpack_from("<3f", d, p + len(out) * 0 + 0),))  # placeholder
        # 正确提取: 本记录起点 = 回推 — 记录位置追踪
        out[-1] = pos_of_last
        break
    return out

# WalkVerts 正确移植: 追踪记录起点
def walk_verts2(d, p, F, limit):
    verts = []; uvs = []; o = p
    while o < F - 8 and len(verts) < limit:
        if o + 4 > F: break
        nbk = u32(d, o)
        if nbk < 1 or nbk > 8: break
        q = o + 4; tw = 0.0; ok = True
        for i in range(nbk):
            if q + 30 > F: ok = False; break
            if d[q] > 127: ok = False; break
            w = f32(d, q + 1)
            if w < -0.01 or w > 1.001: ok = False; break
            tw += w; q += 30
        if not ok: break
        if nbk > 1 and (tw < 0.5 or tw > 2.5): break
        rec = []
        for i2 in range(nbk):
            qq = o + 4 + i2 * 30
            rec.append((d[qq], f32(d, qq + 1),
                        (f32(d, qq + 5), f32(d, qq + 9), f32(d, qq + 13))))
        if q + 21 <= F:
            u = f32(d, q + 13); v = f32(d, q + 17)
            if u < -8 or u > 8 or v < -8 or v > 8: break
            uvs.append((u, v)); o = q + 21
        elif q <= F:
            uvs.append((0.0, 0.0)); o = q
        else: break
        verts.append(rec)
    return verts, uvs, o

def act_name_at(d, q, maxn):
    if q + 4 > len(d): return False
    n = u32(d, q)
    if n < 1 or n > maxn or q + 4 + n > len(d): return False
    return printable(d, q + 4, n)

def skip_name(d, p): return p + 4 + u32(d, p)

def parse_skel_chunk(d, o, F):
    try:
        p = o + 4 + u16(d, o + 2)
        if not act_name_at(d, p, 64): return None
        p = skip_name(d, p)
        if p + 6 > F: return None
        p += 2
        mc = u32(d, p); p += 4
        if mc < 1 or mc > 8: return None
        s = dict(idx=[], faceEnd=0, mesh=None, mat=None)
        for m in range(mc):
            if not act_name_at(d, p, 48): return None
            mesh = d[p+4:p+4+u32(d,p)].decode(); p = skip_name(d, p)
            if not act_name_at(d, p, 48): return None
            mat = d[p+4:p+4+u32(d,p)].decode(); p = skip_name(d, p)
            fc = u32(d, p); p += 4
            if fc < 3 or fc > 262144 or fc % 3 != 0 or p + 2*fc + 1 > F: return None
            for j in range(fc):
                a = u16(d, p + 2*j)
                if a > 60000: return None
                s["idx"].append(a)
            p += 2*fc + 1
            if m == 0: s["mesh"], s["mat"] = mesh, mat
        s["faceEnd"] = p
        return s
    except Exception:
        return None

def parse_va_chunk(d, o, F):
    try:
        p = o + 4 + u16(d, o + 2)
        names = 0
        while names < 3:
            got = False
            for skip in range(9):
                if p + skip + 4 <= F and act_name_at(d, p + skip, 64):
                    p = skip_name(d, p + skip); names += 1; got = True; break
            if not got: break
        if names < 2: return None
        cnt = u32(d, p); p += 4
        if cnt < 3 or cnt > 300000 or p + 2*cnt > F: return None
        s = dict(idx=[], faceEnd=0, mesh=None, mat=None)
        mx = 0
        for k in range(cnt):
            a = u16(d, p + 2*k)
            if a > 60000: return None
            s["idx"].append(a); mx = max(mx, a)
        p += 2*cnt
        ok_end = p + 4 >= F
        for e in range(5):
            if p + 1 + e < F and d[p+e] == 0xFF and d[p+e+1] == 0xFF: ok_end = True; break
        if not ok_end: return None
        s["faceEnd"] = p
        return s
    except Exception:
        return None

# ============ v4.8.9 同步段 (C# PreviewV4.v489.cs 逐行同构) ============
def parse_self_nodes(d):
    F = len(d)
    if F < 64 or d[0] != 0xFF or d[1] != 0xFF: return None
    tl = u16(d, 2)
    o = index_of(d, b"\x00\x00\x80\x3f", tl + 4)
    if o < 0: return None
    o += 6
    if o + 4 > F: return None
    cnt = u32(d, o); o += 4
    if not 1 <= cnt <= 512: return None
    bones = []
    for i in range(cnt):
        if not act_name_at(d, o, 24): return None
        name = d[o+4:o+4+u32(d,o)].decode(); o = skip_name(d, o)
        if not act_name_at(d, o, 24): return None
        par = d[o+4:o+4+u32(d,o)].decode(); o = skip_name(d, o)
        if o + 4 > F: return None
        nf = u32(d, o); o += 4
        b = dict(name=name, parent=par, nf=0, trans=None, quats=None)
        if 1 <= nf <= 60000 and o + nf*12 + 4 <= F:
            b["nf"] = nf
            b["trans"] = [f32(d, o + k*4) for k in range(nf*3)]; o += nf*12
            if o + 4 > F: return None
            nf2 = u32(d, o); o += 4
            if nf2 != nf or o + nf*16 > F: return None
            b["quats"] = [f32(d, o + k*4) for k in range(nf*4)]; o += nf*16
            o += 10
        else:
            nxt = find_node_anchor(d, o, F)
            if nxt >= F:
                bones.append(b); break
            o = nxt
        bones.append(b)
    return bones or None

def find_node_anchor(d, o, F):
    p = o
    while p + 12 < F:
        if act_name_at(d, p, 24) and act_name_at(d, skip_name(d, p), 24):
            return p
        p += 1
    return F

def bone_local_w(b, frame):
    if b["nf"] > 0 and b["quats"] and b["trans"]:
        f = min(frame, b["nf"] - 1)
        w, x, y, z = b["quats"][f*4:f*4+4]
        n = x*x + y*y + z*z + w*w
        if n < 1e-12:
            return ([1,0,0, 0,1,0, 0,0,1], 0.0, 0.0, 0.0)
        s = 2.0 / n
        R = (1-s*(y*y+z*z), s*(x*y-z*w),   s*(x*z+y*w),
             s*(x*y+z*w),   1-s*(x*x+z*z), s*(y*z-x*w),
             s*(x*z-y*w),   s*(y*z+x*w),   1-s*(y*y+x*x))
        return (R, b["trans"][f*3], b["trans"][f*3+1], b["trans"][f*3+2])
    return ([1,0,0, 0,1,0, 0,0,1], 0.0, 0.0, 0.0)

def world_mats(bones, frame):
    n = len(bones)
    name_idx = {}
    for i, b in enumerate(bones):
        if b["name"] not in name_idx: name_idx[b["name"]] = i
    W = [None] * n
    done = [False] * n
    def resolve(i):
        if done[i]: return
        done[i] = True
        b = bones[i]
        R, tx, ty, tz = bone_local_w(b, frame)
        pid = name_idx.get(b["parent"])
        if pid is not None and pid != i:
            resolve(pid)
            pR, ptx, pty, ptz = W[pid]
            NR = (pR[0]*R[0]+pR[1]*R[3]+pR[2]*R[6], pR[0]*R[1]+pR[1]*R[4]+pR[2]*R[7], pR[0]*R[2]+pR[1]*R[5]+pR[2]*R[8],
                  pR[3]*R[0]+pR[4]*R[3]+pR[5]*R[6], pR[3]*R[1]+pR[4]*R[4]+pR[5]*R[7], pR[3]*R[2]+pR[4]*R[5]+pR[5]*R[8],
                  pR[6]*R[0]+pR[7]*R[3]+pR[8]*R[6], pR[6]*R[1]+pR[7]*R[4]+pR[8]*R[7], pR[6]*R[2]+pR[7]*R[5]+pR[8]*R[8])
            tx, ty, tz = (pR[0]*tx+pR[1]*ty+pR[2]*tz+ptx,
                          pR[3]*tx+pR[4]*ty+pR[5]*tz+pty,
                          pR[6]*tx+pR[7]*ty+pR[8]*tz+ptz)
            R = NR
        W[i] = (R, tx, ty, tz)
    for i in range(n):
        resolve(i)
    return W

def bake_vertex(W, rec):
    ax = ay = az = 0.0
    for bid, w, (px, py, pz) in rec:
        if W is None or bid >= len(W) or W[bid] is None:
            ax += w*px; ay += w*py; az += w*pz; continue
        R, tx, ty, tz = W[bid]
        ax += w*(R[0]*px + R[1]*py + R[2]*pz + tx)
        ay += w*(R[3]*px + R[4]*py + R[5]*pz + ty)
        az += w*(R[6]*px + R[7]*py + R[8]*pz + tz)
    return (ax, ay, az)

def bounds_diag(vs):
    if not vs: return 1.0
    xs = [v[0] for v in vs]; ys = [v[1] for v in vs]; zs = [v[2] for v in vs]
    return math.sqrt((max(xs)-min(xs))**2 + (max(ys)-min(ys))**2 + (max(zs)-min(zs))**2)

def scan_whole_pool(d, F, vNeed):
    bestP, bestN = -1, 0
    p = 0
    while p < F - 60:
        nb = u32(d, p)
        if 1 <= nb <= 8 and p + 4 < F and d[p+4] <= 127:
            w0 = f32(d, p + 5)
            if -0.01 <= w0 <= 1.001:
                n = chain_vtx(d, p, F)
                if n >= vNeed: return p
                if n > bestN: bestN, bestP = n, p
        p += 1
    return bestP

def dist(a, b): return math.sqrt((a[0]-b[0])**2 + (a[1]-b[1])**2 + (a[2]-b[2])**2)

def act_edge_check(vs, idx):
    nt = len(idx) // 3
    if nt == 0: return 0.0
    n = min(24, nt); tot = 0.0
    for k in range(n):
        t = k * nt // n
        a, b, c = idx[t*3], idx[t*3+1], idx[t*3+2]
        if a >= len(vs) or b >= len(vs) or c >= len(vs): return 1e9
        tot += dist(vs[a], vs[b]) + dist(vs[b], vs[c]) + dist(vs[c], vs[a])
    return tot / (n * 3)

def extract_verts(d, s, F, bones=None):
    vNeed = max(s["idx"]) + 1
    if vNeed < 3 or vNeed > 60000: return None
    winLo, winHi = s["faceEnd"], min(F, s["faceEnd"] + 65536)
    # A. 链 (64K窗口 → 全文件兜底 v4.8.9)
    bestP, bestN = -1, 0
    p = winLo
    while p < winHi - 60:
        n = chain_vtx(d, p, F)
        if n > bestN: bestN, bestP = n, p
        if bestN >= vNeed: break
        p += 1
    if bestN < vNeed:
        gP = scan_whole_pool(d, F, vNeed)
        if gP >= 0: bestP, bestN = gP, vNeed
    if bestP >= 0 and bestN >= min(8, vNeed):
        recs, uvs, sp = [], [], bestP
        scanHi = min(F, bestP + vNeed*8*30 + 4096)
        guard = 0
        while len(recs) < vNeed and sp < scanHi and guard < 4096:
            v2, u2, p2 = walk_verts2(d, sp, F, vNeed - len(recs))
            if p2 <= sp: sp += 1; guard += 1; continue
            recs.extend(v2); uvs.extend(u2); sp = p2
        if len(recs) >= vNeed:
            recs = recs[:vNeed]; uvs = uvs[:vNeed]
            if bones:
                W = world_mats(bones, 0)
                bv = [bake_vertex(W, r) for r in recs]
                ed = act_edge_check(bv, s["idx"])
                dg = bounds_diag(bv)
                if ed < max(4.0, dg * 0.45) and dg > 1e-4:
                    return bv, uvs, "烘"
            sv = [r[0][2] for r in recs]
            if act_edge_check(sv, s["idx"]) < 4.0:
                return sv, uvs, "静"
    # B. 32B池
    for pre in (2, 4):
        if winLo + pre + vNeed*32 > F: continue
        s0 = winLo
        while s0 + pre + vNeed*32 <= min(winHi, F):
            c = u16(d, s0) if pre == 2 else u32(d, s0)
            if c == vNeed:
                ps = s0 + pre
                nok = uok = tot0 = 0
                brk = False
                for v in range(vNeed):
                    o = ps + v*32
                    x, y, z = f32(d,o), f32(d,o+4), f32(d,o+8)
                    nx, ny, nz = f32(d,o+12), f32(d,o+16), f32(d,o+20)
                    u, w = f32(d,o+24), f32(d,o+28)
                    if x != x or abs(x) > 1e5 or abs(y) > 1e5 or abs(z) > 1e5: brk = True; break
                    if x == 0 and y == 0 and z == 0 and nx == 0 and ny == 0 and nz == 0: brk = True; break
                    nl = math.sqrt(nx*nx+ny*ny+nz*nz)
                    if abs(nl-1) < 0.2: nok += 1
                    if -0.05 < u < 1.05 and -0.05 < w < 1.05: uok += 1
                    tot0 += 1
                if not brk and tot0 == vNeed and nok*100//vNeed >= 55 and uok*100//vNeed >= 55:
                    verts = [struct.unpack_from("<3f", d, ps+v*32) for v in range(vNeed)]
                    uvs = [struct.unpack_from("<2f", d, ps+v*32+24) for v in range(vNeed)]
                    if act_edge_check(verts, s["idx"]) < 4.0:
                        return verts, uvs, "池"
            s0 += 1
    return None

def parse_container(path):
    d = open(path, "rb").read()
    F = len(d)
    if F < 64 or d[0] != 0xFF or d[1] != 0xFF: return None
    if index_of(d, b"CRT_Actor", 0) < 0 and index_of(d, b"CRT_SkelSkin", 0) < 0 and index_of(d, b"CRT_VaSkin", 0) < 0:
        return None
    subs = []; o = 4
    while o < F - 8:
        if d[o] == 0xFF and d[o+1] == 0xFF:
            nl = u16(d, o+2)
            if 4 <= nl <= 32 and o+4+nl <= F and printable(d, o+4, nl):
                nm = d[o+4:o+4+nl].decode()
                if nm == "CRT_SkelSkin":
                    s = parse_skel_chunk(d, o, F)
                    if s: subs.append(s); o = s["faceEnd"]; continue
                elif nm == "CRT_VaSkin":
                    s = parse_va_chunk(d, o, F)
                    if s: subs.append(s); o = s["faceEnd"]; continue
                o += 4 + nl
                continue
        o += 1
    if not subs: return None
    bones = parse_self_nodes(d)
    rverts = []; ruvs = []; rtris = []; meshed = 0; mats = []
    tagc = {}
    for s in subs:
        r = extract_verts(d, s, F, bones)
        if r:
            verts, uvs, tag = r
            tagc[tag] = tagc.get(tag, 0) + 1
            vb = len(rverts)
            rverts.extend(verts); ruvs.extend(uvs)
            idx = s["idx"]
            for k in range(0, len(idx)-2, 3):
                a, b, c = idx[k]+vb, idx[k+1]+vb, idx[k+2]+vb
                if a == b or b == c or a == c: continue
                rtris.extend((a, b, c))
            meshed += 1
            if s["mat"] and s["mat"] not in mats: mats.append(s["mat"])
    rig = f" 自架{len(bones)}骨" if bones else ""
    tg = "".join(f" {k}{v}" for k, v in tagc.items())
    print(f"{path.split('/')[-1]}: 子网格{meshed}/{len(subs)} 顶点{len(rverts)} 三角{len(rtris)//3}{rig}{tg} 材质{mats[:3]}")
    return len(rverts), len(rtris)//3, meshed

if __name__ == "__main__":
    # v4.8.9: 用法 python verify_container.py [样本目录] — 缺省跑基线5件; 目录内无基线名则扫前8个 .act
    import glob, os
    base = sys.argv[1] if len(sys.argv) > 1 else "/home/z/my-project/share/agent-model/6ac1ae06d3e5cff5fbaa5478/tmp/samples/"
    if not base.endswith("/"): base += "/"
    names = ["iwgong01.act", "iw0401huan_04.act", "1dao01.act", "6dao01.act", "0_chibang01_01.act"]
    files = [base + n for n in names if os.path.exists(base + n)]
    if not files:
        files = sorted(glob.glob(base + "*.act"))[:8]
    if not files:
        print("未找到样本 .act — 用法: python verify_container.py <样本目录>")
    for f in files:
        try:
            parse_container(f)
        except Exception as e:
            print(f, "异常:", e)
