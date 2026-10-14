# -*- coding: utf-8 -*-
# ==================================================================
# verify_v482a.py — v4.8.2a 轴向指纹验真 (Termux python3 直跑, 零云端成本)
# 目的: 用顶点包围盒定案"模型横向躺倒"是全局轴向问题还是逐件解析问题
#   指纹: 顶点流 pos0 (第一绑定块原始坐标) 的 X/Y/Z 三轴延伸, 最长轴 = 源数据"竖直"轴
#   判据: 站立人形件源数据最长轴应稳定指向同一轴
#     → 多数件最长轴=Z : 源数据 Z-up 站立, ModelRot=(-90,0,0) 方向正确,
#       "横向"= 蒙皮/逐件解析问题 (下一步 dump 对比好坏件)
#     → 多数件最长轴=Y : 源数据已 Y-up, ModelRot 属双重旋转 → 改 ModelRot=(0,0,0) 一行修复
#     → 混合无主导     : 顶点流逐件错位, 与轴向无关 → 面表/链定位排障
# 复用 verify_v481g.chain_vtx 做流定位 (与 C# ParseSki 同判据, 定位零漂移),
# 走查条件逐行镜像 verify_v481g.parse_ski (nbk 1..4 / bid≤127 / w∈[-0.01,1.001] / uv∈[-8,8])。
# 用法:
#   python verify_v482a.py                  # 全部默认件 (数据目录自动探测)
#   python verify_v482a.py 名1 名2          # 指定件 (不带扩展名)
#   python verify_v482a.py --dir <ski目录>  # 显式指定数据目录
# 依赖: 本文件与 verify_v481g.py 同目录 (tools/)
# ==================================================================
import os, sys, struct, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
try:
    from verify_v481g import chain_vtx, LogTee
except ImportError:
    print("✗ 需要 verify_v481g.py 与本文件同目录 (tools/) — 先确认后再跑")
    sys.exit(1)

u32 = lambda d, o: struct.unpack_from("<I", d, o)[0]
f32 = lambda d, o: struct.unpack_from("<f", d, o)[0]

# 装机目检 6 件 (2 好件 + 4 异常件) + v481 基线 3 件 + 跨族对照 3 件
DEFAULT_PIECES = [
    "a_wt_m_hea_00", "a_wt_m_wai_04",                    # 目检正常件(对照)
    "a_wt_m_sho_01", "a_wt_m_clo_03",                    # 扭麻花
    "a_wt_m_hea_03", "a_wt_m_glo_00",                    # 面片大缺
    "a_wt_m_clo_00", "a_wt_m_sho_00", "a_wt_m_wai_01",   # v481 基线
    "a_hj_f_clo_00", "a_mj_m_clo_00", "a_ss_f_clo_00",   # 跨族
]

def data_dir(argv):
    if "--dir" in argv:
        i = argv.index("--dir")
        if i + 1 < len(argv): return argv[i + 1]
    for c in ("/storage/emulated/0/Game/pak_out/creature/actor",):
        if os.path.isdir(c): return c
    return "."

def walk_positions(d, best, F):
    """镜像 parse_ski 走查, 收集每顶点第一绑定块 pos0=(x,y,z)。
    校验顺序与 verify_v481g.parse_ski 完全一致, 仅额外在步进前读出 pos0。"""
    o = best; pts = []
    while o < F - 8:
        if o + 4 > F: break
        nbk = u32(d, o)
        if nbk < 1 or nbk > 4: break
        q = o + 4
        ok = True
        if q + 18 <= F:
            px, py, pz = f32(d, q + 5), f32(d, q + 9), f32(d, q + 13)
        else:
            ok = False
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
        pts.append((px, py, pz))
        if len(pts) > 60000: break
    return pts

def bbox(pts):
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; zs = [p[2] for p in pts]
    return (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))

def locate_stream(d):
    """与 parse_ski 同款: 全长扫描取最长自校验链起点"""
    F = len(d); best, bestN = -1, 0
    for p in range(16, F - 60):
        n = chain_vtx(d, p, F)
        if n > bestN: bestN, best = n, p
        if bestN > 4000: break
    return best, bestN

def mk_sample(axis):
    """合成 3 顶点直杆: axis='Z' 竖杆(沿+Z), 'X' 横杆(沿+X)。块长严格 30B + 块后 21B。"""
    import io
    P = {"Z": [(0,0,0),(0,0,0.5),(0,0,1.0)], "X": [(0,0,0),(0.5,0,0),(1.0,0,0)]}[axis]
    b = io.BytesIO()
    b.write(b"\xff\xff\x0c\x00" + b"CRT_SkelSkin")   # 16B 头
    b.write(struct.pack("<I", 0))                     # skelRef 空串
    for (x, y, z) in P:
        b.write(struct.pack("<I", 1))                          # nbk=1
        b.write(struct.pack("<B", 0) + struct.pack("<f", 1.0)) # bid+w (5B)
        b.write(struct.pack("<fff", x, y, z))                  # pos (12B)
        b.write(struct.pack("<fff", 0, 0, 1))                  # tangent (12B)
        b.write(struct.pack("<B", 0))                          # 补齐 30B
        b.write(b"\x00" * 13)                                  # 块后未知 13B
        b.write(struct.pack("<ff", 0.5, 0.5))                  # uv (8B) → 共 21B
    return b.getvalue()

def selftest():
    for axis, expect in (("Z", "Z"), ("X", "X")):
        d = mk_sample(axis); best, bestN = locate_stream(d)
        pts = walk_positions(d, best, len(d))
        assert len(pts) == 3, "合成%s杆走查仅%d顶点" % (axis, len(pts))
        ex, ey, ez = bbox(pts)
        longest = max({"X": ex, "Y": ey, "Z": ez}, key={"X": ex, "Y": ey, "Z": ez}.get)
        assert longest == expect, "合成%s杆最长轴=%s(应为%s) %r" % (axis, longest, expect, (ex, ey, ez))
    print("自检: 合成竖杆→Z / 横杆→X 方向映射全对 ✓ (走查无漂移)")

def main():
    argv = sys.argv
    names = [a for a in argv[1:] if not a.startswith("--") and a != (argv[argv.index("--dir") + 1] if "--dir" in argv else None)]
    pieces = names or DEFAULT_PIECES
    D = data_dir(argv)
    print("数据目录: %s" % D)
    selftest()
    rows, axes = [], {"X": 0, "Y": 0, "Z": 0}
    for nm in pieces:
        p = os.path.join(D, nm + ".ski")
        if not os.path.exists(p):
            rows.append((nm + ".ski", "—", 0, "文件不存在(跳过)")); continue
        d = open(p, "rb").read()
        best, bestN = locate_stream(d)
        if bestN < 8:
            rows.append((nm + ".ski", "✗", 0, "顶点流未定位(链%d)" % bestN)); continue
        pts = walk_positions(d, best, len(d))
        if len(pts) < 3:
            rows.append((nm + ".ski", "✗", len(pts), "走查顶点不足")); continue
        ex, ey, ez = bbox(pts)
        ext = {"X": ex, "Y": ey, "Z": ez}
        longest = max(ext, key=ext.get)
        axes[longest] += 1
        mn = min(ext.values())
        ratio = (max(ext.values()) / mn) if mn > 1e-6 else float("inf")
        rows.append((nm + ".ski", "✓", len(pts),
                     "X%7.2f Y%7.2f Z%7.2f → 最长=%s 竖/横比%.1f" % (ex, ey, ez, longest, ratio)))
    print("\n%-26s %-4s %8s  %s" % ("文件", "结果", "顶点", "三轴延伸与最长轴"))
    print("-" * 96)
    for r in rows: print("%-26s %-4s %8d  %s" % r)
    print("-" * 96)
    tot = sum(axes.values())
    if tot == 0:
        print("结论: 无可实检文件 — 用 --dir 指定 ski 目录后重跑。"); return
    print("最长轴分布: X=%d  Y=%d  Z=%d  (共%d件)" % (axes["X"], axes["Y"], axes["Z"], tot))
    if axes["Z"] >= tot * 0.7:
        print("结论A: 源数据 Z-up 站立 → ModelRot=(-90,0,0) 方向正确,【横向不是全局轴向问题】")
        print("       → 下一步: 蒙皮/逐件解析排障 (对最扁件跑 verify_v481g.py --dump 对比好件)")
    elif axes["Y"] >= tot * 0.7:
        print("结论B: 源数据已 Y-up → ModelRot 属双重旋转(合计180°=躺平)")
        print("       → 修复: PreviewV4.cs 一行  ModelRot = new Vector3(0f,0f,0f)")
    else:
        print("结论C: 最长轴逐件不一致 → 顶点流逐件错位/权重块污染, 与根节点轴向无关")
        print("       → 下一步: 把本页输出发清言, 对好坏件各跑一次 --dump 对比链定位")
    print("(指纹实据: 走查与 C#/verify_v481g 同判据; pos0=第一绑定块原始坐标)")

if __name__ == "__main__":
    logp = os.path.join(HERE, "verify_history.log")
    try:
        sys.stdout = LogTee(logp)
        print("\n==== verify_v482a 轴向指纹 %s ====" % time.strftime("%Y-%m-%d %H:%M:%S"))
    except Exception as e:
        print("(日志不可写, 仅终端输出: %s)" % e)
    try:
        main()
    finally:
        if isinstance(sys.stdout, LogTee):
            sys.stdout.f.close(); sys.stdout = sys.stdout.so
