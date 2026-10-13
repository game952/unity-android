# -*- coding: utf-8 -*-
# ==================================================================
# verify_v481d.py — v4.8.1d 解析逻辑本地验真 + 失败自动诊断 (Termux python3 直跑)
# v4.8.1d 格式定案 (真机指纹):
#   ski: 面表在顶点流【之后】(流前最长u16段仅骨架碎段; 计数器vcp与链长互证)
#        → 双区间收集面表 + maxval≈vcp 过滤骨架父表垃圾段
#   act: 节点间有变换数据, 逐节点走查失效 → 全域自校验签名扫描 [u32 V][V*3个合理浮点]
# 用法: python verify_v481d.py                # 自检 + 全部默认文件 + 失败自动dump
#       python verify_v481d.py --dump 名1 名2  # 只对指定文件输出格式指纹
# 逐行对应 PreviewV4_v48d.cs (v4.8.1d)
# ==================================================================
import struct, os, sys, time, re, inspect

ACT_DIR = "/storage/emulated/0/Game/pak_out/creature/actor"
OBJ_DIRS = [
    "/storage/emulated/0/Game/ski_model_pack/obj",
    "/storage/emulated/0/Game/游戏端/ski_model_pack/obj",
    "/storage/emulated/0/Game/ski_model_pack_297/obj",
]
DEFAULT_SKIS = ["a_wt_m_glo_01", "a_wt_m_clo_01_02", "a_wt_m_clo_03",
                "a_wt_m_sho_00", "a_wt_m_wai_01", "a_wt_m_clo_00",
                "a_hj_f_clo_00", "a_hj_f_hea_00", "a_mj_m_clo_00",
                "a_mj_f_clo_00", "a_ss_m_clo_00", "a_ss_f_clo_00"]
DEFAULT_ACTS = ["0_chibang02", "0_chibang02_02", "0_1_chibang01", "1dao01", "iwgong01"]

u32 = lambda d, o: struct.unpack_from("<I", d, o)[0]
u16 = lambda d, o: struct.unpack_from("<H", d, o)[0]
f32 = lambda d, o: struct.unpack_from("<f", d, o)[0]

def printable(d, off, n):
    return off + n <= len(d) and all(32 <= d[off + i] < 127 for i in range(n))

# ---------------- ski: 自校验链 (ChainVtx 等价) ----------------
def chain_vtx(d, p, F, nbk_max=4, bone_max=127):
    o, n = p, 0
    while o < F - 8:
        if o + 4 > F: break
        nbk = u32(d, o)
        if nbk < 1 or nbk > nbk_max: break
        q = o + 4; tw = 0.0
        for _ in range(nbk):
            if q + 30 > F: return n
            if d[q] > bone_max: return n
            w = f32(d, q + 1)
            if w < -0.01 or w > 1.001: return n
            tw += w; q += 30
        if nbk > 1 and (tw < 0.5 or tw > 1.6): return n
        if q + 21 <= F:
            u = f32(d, q + 13); v = f32(d, q + 17)
            if u < -8 or u > 8 or v < -8 or v > 8: return n
            o = q + 21
        elif q <= F:
            o = q
        else:
            return n
        n += 1
        if n > 60000: break
    return n

def i16(d, p, be):
    """面索引解码: 大端 u16 (v4.8.1e 定案: 面表=头部后 BE u16, v4.7 曾被顶点流问题连坐冤枉)"""
    return ((d[p] << 8) | d[p + 1]) if be else u16(d, p)

def flush_run(d, rs, rl, vcp, be=False):
    """FlushRun 等价 + 对齐搜索: 段头零填充长度非3倍数会整表错位, 0/1/2 取有效三角形最多者"""
    if rl < 9: return 0
    best_shift, best_cnt = 0, -1
    for sh in range(3):
        cnt = 0
        i = sh
        while i + 2 < rl:
            a = i16(d, rs + 2 * i, be); b = i16(d, rs + 2 * i + 2, be); c = i16(d, rs + 2 * i + 4, be)
            if a < vcp and b < vcp and c < vcp and a != b and b != c and a != c: cnt += 1
            i += 3
        if cnt > best_cnt: best_cnt, best_shift = cnt, sh
    i, n = best_shift, 0
    while i + 2 < rl:
        a = i16(d, rs + 2 * i, be); b = i16(d, rs + 2 * i + 2, be); c = i16(d, rs + 2 * i + 4, be)
        if a < vcp and b < vcp and c < vcp and a != b and b != c and a != c: n += 1
        i += 3
    return n

def collect_runs(d, lo, hi, vcp, be=False):
    """CollectRuns 等价: [lo,hi) 内 u16<vb 的极大段(可大端) → [(start, len, maxval, be)]"""
    vb = max(vcp, 1024)
    res = []
    for par in (0, 1):                  # 面表可能落在奇数偏移 → 两种字节对齐都扫
        rs, rl, mx, hot = -1, 0, 0, False
        p3 = lo + par
        while p3 + 2 <= hi:
            v0 = i16(d, p3, be)
            if v0 < vb:
                if rl == 0: rs, hot, mx = p3, False, 0
                rl += 1
                if v0 >= 4: hot = True
                if v0 > mx: mx = v0
            else:
                if rl >= 9 and hot: res.append((rs, rl, mx, 1 if be else 0))
                rl, hot = 0, False
            p3 += 2
        if rl >= 9 and hot: res.append((rs, rl, mx, 1 if be else 0))
    return res

def parse_ski(d):
    """返回 (vcp, tris, best, err)。err=None 即成功, 与 C# ParseSki 同判据"""
    F = len(d)
    if F < 64 or d[0] != 0xFF or d[1] != 0xFF:
        return 0, 0, 0, "非CRT对象流"
    if F < 16 or d[4:16] != b"CRT_SkelSkin":
        return 0, 0, 0, "缺CRT_SkelSkin头"
    best, bestN = -1, 0
    for p in range(16, F - 60):
        n = chain_vtx(d, p, F)
        if n > bestN: bestN, best = n, p
        if bestN > 4000: break
    if bestN < 8:
        return 0, 0, best, "顶点流未定位(最长链%d)" % bestN
    # 走查建顶点 (与 C# 同: 走到流尾, 得 walkEnd)
    o = best; vcp = 0
    while o < F - 8:
        if o + 4 > F: break
        nbk = u32(d, o)
        if nbk < 1 or nbk > 4: break
        q = o + 4; ok = True
        for _ in range(nbk):
            if q + 30 > F or d[q] > 127: ok = False; break
            w = f32(d, q + 1)
            if w < -0.01 or w > 1.001: ok = False; break
            q += 30
        if not ok: break
        if q + 21 <= F:
            u = f32(d, q + 13); v = f32(d, q + 17)
            if u < -8 or u > 8 or v < -8 or v > 8: break
            o = q + 21
        elif q <= F:
            o = q
        else:
            break
        vcp += 1
        if vcp > 60000: break
    if vcp < 3:
        return vcp, 0, best, "0 顶点"
    walk_end = o
    # 面表: 双区间收集 (v4.8.1d: 真面表在流后)
    # v4.8.1f: 双对齐×双端序候选段 → 按长度降序贪心; 重叠过半跳过(幽灵段), 产量(有效面)为纲
    # (LE 表的 BE 奇对齐幽灵会复刻同值序列致双倍计数; 幽灵产量恒0 → 产出为零不占位)
    cands = (collect_runs(d, 16, best, vcp) + collect_runs(d, 16, best, vcp, True)
             + collect_runs(d, walk_end, F, vcp) + collect_runs(d, walk_end, F, vcp, True))
    cands.sort(key=lambda r: -r[1])
    max_len = cands[0][1] if cands else 0
    tris, taken = 0, []          # taken: 已产出段的 [start, end)
    for rs, rl, _, be in cands:
        if rl < max_len // 2: continue
        s0, e0 = rs, rs + 2 * rl
        if any(min(e0, t1) - max(s0, t0) > 0 and (min(e0, t1) - max(s0, t0)) * 2 > (e0 - s0) for t0, t1 in taken):
            continue
        n = flush_run(d, rs, rl, vcp, be != 0)
        if n > 0:
            tris += n
            taken.append((s0, e0))
    if tris == 0:
        return vcp, 0, best, "0 面表(候选%d段)" % len(cands)
    return vcp, tris, best, None

# ---------------- act: 全域网格签名扫描 (v4.8.1d) ----------------
def act_str(d, off, minn=2, maxn=64):
    if off < 0 or off + 4 > len(d): return None
    n = u32(d, off)
    if n < minn or n > maxn or off + 4 + n > len(d): return None
    if not printable(d, off + 4, n): return None
    return off + 4 + n

def str_at(d, off):
    n = u32(d, off)
    return d[off + 4: off + 4 + n].decode("ascii", "replace")

def nice_ratio(d, off, nfloats):
    n = min(nfloats, 300)
    if n <= 0: return 0.0
    step = max(1, nfloats // n)
    ok = tot = 0
    i = 0
    while i < nfloats:
        if off + i * 4 + 4 > len(d): break
        v = f32(d, off + i * 4)
        if v == v and -1e7 < v < 1e7: ok += 1
        tot += 1
        i += step
    return ok / tot if tot else 0.0

def act_candidates(d):
    F = len(d)
    lim = min(F - 12, 26 + 65536)
    fc = find_chunk_after(d, 13)
    if fc < lim: lim = fc
    cands = []
    p = 26
    while p < lim and len(cands) < 10:
        x = u32(d, p)
        if 1 <= x <= 4096:
            e1 = act_str(d, p + 4)
            if e1:
                e2 = act_str(d, e1)
                if e2:
                    nm1 = str_at(d, p + 4).lower()
                    if nm1 in ("root", "bip01") or str_at(d, e1).lower() == "null":
                        cands.insert(0, p)
                    else:
                        cands.append(p)
        p += 1
    return cands

def act_scan_meshes(d, frm):
    """全域签名扫描: [u32 V][V*3个合理浮点] → [(off, V, pts)]"""
    F = len(d); out = []
    p = max(13, frm)
    while p + 16 <= F:
        V = u32(d, p)
        if 24 <= V <= 300000 and p + 4 + V * 12 <= F and nice_ratio(d, p + 4, V * 3) >= 0.85:
            out.append((p, V, min(V, 50000)))
            p += 4 + V * 12
            if p + V * 16 <= F and nice_ratio(d, p, V * 4) >= 0.6: p += V * 16
        else:
            p += 1
    return out

def find_chunk_after(d, frm):
    F = len(d)
    i = max(13, frm)
    while i + 68 <= F:
        if d[i] == 0xFF and d[i + 1] == 0xFF:
            nl = u16(d, i + 2)
            if 4 <= nl <= 64 and printable(d, i + 4, nl):
                return i
        i += 1
    return F

def parse_act(d):
    """返回 (nodes, meshes, pts, err)。与 C# ParseActMesh v4.8.1d 同判据"""
    F = len(d)
    if F < 64 or d[0] != 0xFF or d[1] != 0xFF or d[2] != 0x09 or d[3] != 0x00:
        return 0, 0, 0, "非CRT_Actor流"
    cands = act_candidates(d)
    if not cands:
        return 0, 0, 0, "ACT节点表未定位"
    node_count0 = u32(d, cands[0])
    hits = act_scan_meshes(d, cands[0])
    if not hits:
        return node_count0, 0, 0, "无网格节点(纯动画/特效)"
    pts = sum(h[2] for h in hits)
    return node_count0, len(hits), pts, None

# ---------------- 自检: 合成样张 ----------------
def selftest():
    import io
    buf = io.BytesIO()
    buf.write(b"\xff\xff\x0c\x00" + b"CRT_SkelSkin")
    name = b"test"
    buf.write(struct.pack("<I", len(name)) + name)
    buf.write(struct.pack("<H", 0) + struct.pack("<I", 1))
    sk = b"skel06"
    buf.write(struct.pack("<I", len(sk)) + sk)
    buf.write(struct.pack("<I", len(sk)) + sk)
    buf.write(struct.pack("<I", 3) + struct.pack("<HHH", 0, 0, 1))
    buf.write(b"\x00" * 16)
    VCP, NF = 30, 20
    for i in range(NF):
        a, b, c = (i * 3) % VCP, (i * 3 + 1) % VCP, (i * 3 + 2) % VCP
        if a == b or b == c: c = (c + 1) % VCP
        buf.write(struct.pack("<HHH", a, b, c))
    buf.write(struct.pack("<I", VCP))
    buf.write(b"\x00" * 8)
    for i in range(VCP):
        buf.write(struct.pack("<I", 1))
        buf.write(struct.pack("<B", 2) + struct.pack("<f", 1.0))
        buf.write(struct.pack("<fff", i * 0.1, 0.5, -0.5))
        buf.write(struct.pack("<fff", 0.0, 1.0, 0.0))
        buf.write(b"\x00")
        buf.write(struct.pack("<B", 5))
        buf.write(struct.pack("<fff", 0.0, 0.0, 1.0))
        buf.write(struct.pack("<ff", 0.25, 0.75))
    d = buf.getvalue()
    vcp, tris, best, err = parse_ski(d)
    assert err is None, "自检失败: %s" % err
    assert vcp == VCP, "自检失败: vcp=%d != %d" % (vcp, VCP)
    assert tris == NF, "自检失败: tris=%d != %d" % (tris, NF)
    # 自检2: 面表在流后的变体
    buf2 = io.BytesIO()
    buf2.write(b"\xff\xff\x0c\x00" + b"CRT_SkelSkin")
    buf2.write(struct.pack("<I", 4) + b"test" + struct.pack("<H", 0) + struct.pack("<I", 1))
    buf2.write(struct.pack("<I", 3) + struct.pack("<HHH", 0, 0, 1) + b"\x00" * 8)
    for i in range(VCP):
        buf2.write(struct.pack("<I", 1))
        buf2.write(struct.pack("<B", 2) + struct.pack("<f", 1.0))
        buf2.write(struct.pack("<fff", i * 0.1, 0.5, -0.5))
        buf2.write(struct.pack("<fff", 0.0, 1.0, 0.0))
        buf2.write(b"\x00")
        buf2.write(struct.pack("<B", 5))
        buf2.write(struct.pack("<fff", 0.0, 0.0, 1.0))
        buf2.write(struct.pack("<ff", 0.25, 0.75))
    for i in range(NF):
        a, b, c = (i * 3) % VCP, (i * 3 + 1) % VCP, (i * 3 + 2) % VCP
        buf2.write(struct.pack("<HHH", a, b, c))
    vcp2, tris2, _, err2 = parse_ski(buf2.getvalue())
    assert err2 is None and vcp2 == VCP and tris2 == NF, "自检2失败(面在流后): %s %d/%d" % (err2, vcp2, tris2)
    # 自检3: ACT 全域签名
    buf3 = io.BytesIO()
    buf3.write(b"\xff\xff\x09\x00" + b"CRT_Actor" + b"tootooth0708" + struct.pack("<f", 1.0) + b"\x02\x01")
    buf3.write(struct.pack("<I", 2) + struct.pack("<I", 4) + b"root" + struct.pack("<I", 4) + b"NULL")
    buf3.write(b"\x00" * 64)
    buf3.write(struct.pack("<I", 100))
    for i in range(100):
        buf3.write(struct.pack("<fff", i * 0.3, 8.0 - i * 0.1, -3.0 + i * 0.2))
    buf3.write(b"\x00" * 32)
    n3, m3, p3, e3 = parse_act(buf3.getvalue())
    assert e3 is None and m3 == 1 and p3 == 100, "自检3失败(ACT): %s m=%d p=%d" % (e3, m3, p3)
    print("自检 ✓ 合成样张: ski顶点%d 面%d | ski面在流后 ✓ | ACT网格%d点%d ✓" % (vcp, tris, m3, p3))
    return True

# ---------------- 诊断 dump ----------------
def chain_top(d, nbk_max, bone_max, topk=5):
    F = len(d); res = []
    for p in range(16, F - 60):
        n = chain_vtx(d, p, F, nbk_max, bone_max)
        if n >= 8:
            res.append((n, p)); res.sort(reverse=True); del res[topk:]
    return [(hex(o), n) for n, o in res]

def longest_u16_run(d, lo, hi, bound, big_endian=False):
    """[lo,hi) 内 u16 ≤ bound 的最长连续段 → (off, len, max)"""
    best, rs, rl, mx = (0, 0, 0), -1, 0, 0
    p = lo
    while p + 2 <= hi:
        v0 = ((d[p] << 8) | d[p + 1]) if big_endian else u16(d, p)
        if v0 <= bound:
            if rl == 0: rs, mx = p, 0
            rl += 1
            if v0 > mx: mx = v0
        else:
            if rl > best[1]: best = (rs, rl, mx)
            rl = 0
        p += 2
    if rl > best[1]: best = (rs, rl, mx)
    return best

def longest_u32_run(d, lo, hi, bound):
    """[lo,hi) 内 u32 ≤ bound 的最长连续段 → (off, len)"""
    best, rs, rl = (0, 0), -1, 0
    p = lo
    while p + 4 <= hi:
        if u32(d, p) <= bound:
            if rl == 0: rs = p
            rl += 1
        else:
            if rl > best[1]: best = (rs, rl)
            rl = 0
        p += 4
    if rl > best[1]: best = (rs, rl)
    return best

def find_u32(d, val, limit=6):
    hits, i = [], 0
    while True:
        i = d.find(struct.pack("<I", val), i)
        if i < 0 or len(hits) >= limit: break
        hits.append(i); i += 1
    return hits

def dump_ski(path):
    d = open(path, "rb").read(); F = len(d)
    base = os.path.basename(path).rsplit(".", 1)[0]
    print("== ski 诊断 %s (%dB) ==" % (base, F))
    variants = [("严格 nbk≤4 骨≤64", 4, 64), ("骨≤127", 4, 127),
                ("nbk≤6 骨≤127", 6, 127), ("nbk≤8 骨≤127", 8, 127)]
    gbest = None
    for nm, nk, bm in variants:
        t0 = time.time()
        tops = chain_top(d, nk, bm)
        print("  [%s] 最长链: %s (%.0fms)" % (nm, tops, (time.time() - t0) * 1000))
        if tops and (gbest is None or tops[0][1] > gbest[2]):
            gbest = (nm, int(tops[0][0], 16), tops[0][1], nk)
    if not gbest:
        print("  四种变体均无 ≥8 链 — 格式假设需重审"); return
    nm, off, n, nk = gbest
    print("  全局最优链 [%s] @%s n=%d 首2记录:" % (nm, hex(off), n))
    o = off
    for r in range(2):
        nbk = u32(d, o)
        ids = [d[o + 4 + 30 * i] for i in range(min(nbk, 8))]
        ws = [round(f32(d, o + 5 + 30 * i), 3) for i in range(min(nbk, 8))]
        print("    rec%d nbk=%d 骨=%s 权=%s 头24B=%s" % (r, nbk, ids, ws, d[o:o + 24].hex(" ")))
        o = o + 4 + 30 * nbk + 21
    # 走一遍流拿 walkEnd
    o = off; cnt = 0
    while o < F - 8 and cnt < n:
        nbk = u32(d, o)
        if nbk < 1 or nbk > nk: break
        q = o + 4
        for _ in range(nbk): q += 30
        if q + 21 <= F: o = q + 21
        else: o = q
        cnt += 1
    print("  计数器搜索 u32==%d: %s" % (n, [hex(x) for x in find_u32(d, n)]))
    cps = [x for x in find_u32(d, n) if x < off]
    if cps and off - cps[-1] <= 512:
        print("  计数器→流间 %dB: %s" % (off - cps[-1], d[cps[-1]:off].hex(" ")))
    for lo_, hi_, tag in ((16, off, "流前"), (o, F, "流后")):
        r1 = [longest_u16_run(d, lo_, hi_, b) for b in (255, 1023, 4095, 16383)]
        r2 = [longest_u16_run(d, lo_, hi_, b, True) for b in (1023, 16383, 65535)]
        r3 = longest_u32_run(d, lo_, hi_, 65535)
        print("  %s u16小端(off/len/max): %s" % (tag, [(hex(a), b, c) for a, b, c in r1]))
        print("  %s u16大端: %s" % (tag, [(hex(a), b, c) for a, b, c in r2]))
        print("  %s u32<65536(off/len): (%s, %d)" % (tag, hex(r3[0]), r3[1]))
    tail = d[o:F]
    print("  流后尾巴(%dB) hex: %s" % (len(tail), (tail if len(tail) <= 256 else tail[:96]).hex(" ")))
    print("  文件尾32B:", d[-32:].hex(" "))
    mtl, i = [], 0
    while True:
        i = d.find(b"CRT_Mtl", i)
        if i < 0 or len(mtl) >= 5: break
        mtl.append(hex(i)); i += 7
    print("  CRT_Mtl位置:", mtl)
    ref = obj_ref(base)
    if ref: print("  297包对照: obj 顶点%d 面%d → vcp应=%d" % (ref[0], ref[1], ref[0]))
    else: print("  297包对照: 未找到同名 obj")

def dump_act(path):
    d = open(path, "rb").read(); F = len(d)
    print("== act 诊断 %s (%dB) ==" % (os.path.basename(path), F))
    print("  头64B:", d[:64].hex(" "))
    cands = act_candidates(d)
    print("  候选锚(前10):", [(hex(c), str_at(d, c + 4), str_at(d, act_str(d, c + 4))) for c in cands])
    hits = act_scan_meshes(d, cands[0] if cands else 13)
    print("  全域网格签名命中 %d 处:" % len(hits))
    for h in hits[:8]:
        print("    V=%d @%s (点数%d)" % (h[1], hex(h[0]), h[2]))

def run_dump(names):
    for nm in names:
        p = os.path.join(ACT_DIR, nm + ".ski")
        if os.path.exists(p): dump_ski(p); continue
        p = os.path.join(ACT_DIR, nm + ".act")
        if os.path.exists(p): dump_act(p); continue
        print("== 找不到 %s (.ski/.act 都没有)" % nm)
    sys.exit(0)

def obj_ref(name):
    for od in OBJ_DIRS:
        p = os.path.join(od, name + ".obj")
        if os.path.exists(p):
            v = f = 0
            for line in open(p, "r", errors="ignore"):
                if line.startswith("v "): v += 1
                elif line.startswith("f "): f += 1
            return v, f
    return None

def main():
    bm = inspect.signature(chain_vtx).parameters["bone_max"].default
    print("== verify_v481f | bone_max=%d | %s ==" % (bm, time.strftime("%m-%d %H:%M")))
    if sys.argv[1:2] == ["--dump"]:
        run_dump(sys.argv[2:]); return
    selftest()
    extra = sys.argv[1:]
    rows, bad = [], 0
    fail_ski, fail_act = [], []
    def check_ski(base):
        nonlocal bad
        p = os.path.join(ACT_DIR, base + ".ski")
        if not os.path.exists(p):
            rows.append((base + ".ski", "—", 0, 0, "文件不在 pak_out(跳过)")); return
        t0 = time.time()
        vcp, tris, best, err = parse_ski(open(p, "rb").read())
        ms = (time.time() - t0) * 1000
        if err:
            rows.append((base + ".ski", "✗FAIL", 0, 0, "%s (耗时%.0fms)" % (err, ms))); bad += 1
            fail_ski.append(base)
        elif tris < max(3, vcp // 10):
            rows.append((base + ".ski", "✗FAIL", vcp, tris, "面表过少(假通过拦截) (耗时%.0fms)" % ms)); bad += 1
            fail_ski.append(base)
        else:
            ref = obj_ref(base)
            note = "流@0x%x 耗时%.0fms" % (best, ms)
            if ref: note += " | 297包 v%d/f%d %s" % (ref[0], ref[1],
                     ("✓" if abs(ref[0] - vcp) <= max(8, vcp // 50) else "顶点数不一致!"))
            rows.append((base + ".ski", "✓PASS", vcp, tris, note))
    def check_act(base):
        nonlocal bad
        p = os.path.join(ACT_DIR, base + ".act")
        if not os.path.exists(p):
            rows.append((base + ".act", "—", 0, 0, "文件不在 pak_out(跳过)")); return
        t0 = time.time()
        nodes, meshes, pts, err = parse_act(open(p, "rb").read())
        ms = (time.time() - t0) * 1000
        expect = not re.search(r"_\d{2}$", base)
        if err:
            tag = "动画变体(正常)" if not expect else "✗FAIL"
            if expect: bad += 1; fail_act.append(base)
            rows.append((base + ".act", tag, 0, 0, "%s (耗时%.0fms)" % (err, ms)))
        else:
            rows.append((base + ".act", "✓PASS", pts, meshes, "节点表%d 耗时%.0fms" % (nodes, ms)))
    for b in DEFAULT_SKIS: check_ski(b)
    for b in DEFAULT_ACTS: check_act(b)
    for e in extra:
        bp = os.path.basename(e)
        nm = bp.rsplit(".", 1)[0]
        if bp.endswith(".ski"): check_ski(nm)
        else: check_act(nm)
    print("\n%-28s %-10s %8s %8s  %s" % ("文件", "结果", "顶点/点", "面/网格", "备注"))
    print("-" * 100)
    for r in rows:
        print("%-28s %-10s %8s %8s  %s" % r)
    print("-" * 100)
    if fail_ski or fail_act:
        print("—— 失败文件自动诊断 (前3 ski / 前2 act) ——")
        for b in fail_ski[:3]: dump_ski(os.path.join(ACT_DIR, b + ".ski"))
        for b in fail_act[:2]: dump_act(os.path.join(ACT_DIR, b + ".act"))
    checked = sum(1 for r in rows if r[1] != "—")
    if bad == 0 and checked > 0:
        print("结论: %d 项实检全部通过 ✓ — 放心提交等绿勾。动画变体报无网格属正常。" % checked)
    elif bad == 0:
        print("结论: 没有实检任何文件(都缺) — 确认 pak_out 路径后再跑。")
    else:
        print("结论: %d 项失败 ✗ — 上方已自动附诊断, 整页发给清言, 不要提交装包。" % bad)
    sys.exit(0 if bad == 0 else 1)

if __name__ == "__main__":
    main()
