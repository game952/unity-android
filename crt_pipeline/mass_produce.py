#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""mass_produce.py — CRT 装备 ski 批量烘焙 → OBJ 模型库
★ 零依赖纯 Python 版（不需要 numpy；Pillow 可选用于贴图转 PNG）

用法 (手机 Termux 或电脑通用):
  python3 mass_produce.py \
      --actor /storage/emulated/0/Game/pak_out/creature/actor \
      --texture /storage/emulated/0/Game/pak_out/creature/texture \
      --out ./model_pack

变体:
  --limit 20        先试 20 个
  --act <role.act>  指定骨架（默认按装备名职业/性别自动匹配 role_*.act）
  --frame 477       T-pose (bind) 姿势; 默认 3736 自然站立
  --prefix a_wt_    只跑前缀匹配的装备
"""
import os, sys, glob, re, argparse, struct

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from act_parse import parse_act, quat_to_mat, mat_mul, mat_vec

# ski_core 里的 ski 解析 (struct/re, 零依赖) + dds2png(可选 PIL)
import ski_core


def load_skeleton(path, frame):
    """返回骨骼列表 [(R 3x3 list, t tuple)] — 与 numpy 版同布局"""
    a = parse_act(path, verbose=False)
    W = []
    for b in a["bones"]:
        if b["nf"] > 0:
            W.append((quat_to_mat(b["quats"][frame]), b["trans"][frame]))
        else:
            W.append(([[1.0, 0, 0], [0, 1.0, 0], [0, 0, 1.0]], (0.0, 0.0, 0.0)))
    return W


def bake_vertex(W, bones):
    """p = Σ w·(R·L + t) — 纯 Python 展开; W=None 时恒等(静态导出)"""
    acc = [0.0, 0.0, 0.0]
    if W is None:
        for bid, w, pos, nrm in bones:
            for k in range(3):
                acc[k] += w * pos[k]
        return acc
    for bid, w, pos, nrm in bones:
        if bid >= len(W):            # 超界骨骼(服装物理骨等) → 恒等
            for k in range(3):
                acc[k] += w * pos[k]
            continue
        R, t = W[bid]
        for k in range(3):
            acc[k] += w * (R[k][0]*pos[0] + R[k][1]*pos[1] + R[k][2]*pos[2] + t[k])
    return acc


def convert_one(ski_path, W, texdir, out_obj, out_texdir, seen_tex):
    nm = os.path.basename(ski_path)[:-4]
    d = open(ski_path, "rb").read()
    fmt = "old"
    mtl, chain, vc, rate, verts = ski_core.find_chain(d)
    tris = None
    if verts is not None:
        ih, icnt, idx0 = ski_core.find_indices(d, mtl)
        if ih is not None and icnt >= max(12, vc // 2):   # 索引过少 = 误命中小数据块
            raw = [ski_core.u16(d, idx0 + 2*i) for i in range(icnt)]
            tris = [(raw[i], raw[i+1], raw[i+2]) for i in range(0, len(raw) - 2, 3)
                    if max(raw[i], raw[i+1], raw[i+2]) < vc]
    if tris is None or not tris:
        # v3: 新版式 (索引表前置 + MaterialLib 后接顶点块, 支持多 part/部分解析)
        mtl, verts, tris, raw_n, parts = ski_core.parse_ski_v3(d)
        fmt = "new(%d段)" % len(parts)
        lost = 1.0 - (len(tris) * 3.0 / raw_n) if raw_n else 0.0
        if lost > 0.05:
            fmt += " PARTIAL%.0f%%" % (lost * 100)
    if not tris:
        raise ValueError("0 三角 (面块未解析)")
    if not verts:
        raise ValueError("0 顶点")
    # 贴图锚 (v5 定案): CRT_Mtl(Standard|Mu) 后 160B 内第一个非 CRT/0_ 字符串
    anchor = nm
    m = re.search(rb"CRT_Mtl(?:Standard|Mu)(.{0,160})", d, re.S)
    if m:
        for mm in re.finditer(rb"[A-Za-z0-9_]{4,32}", m.group(1)):
            s2 = mm.group().decode()
            if not s2.startswith(("CRT", "0_")):
                anchor = s2; break
    tex_path, stem = ski_core.resolve_tex(anchor, texdir)
    texname = (stem or nm) + ".png"
    ref = "MISSING.png"
    if tex_path and texname not in seen_tex:
        try:
            ski_core.dds2png(tex_path, os.path.join(out_texdir, texname))
            seen_tex.add(texname)
        except Exception:
            texname = None
    if texname and (texname in seen_tex or os.path.exists(os.path.join(out_texdir, texname))):
        seen_tex.add(texname)
        ref = texname
    with open(out_obj, "w") as f:
        f.write("mtllib %s.mtl\nusemtl %s\n" % (nm, nm))
        for bones, flag, tang, uv in verts:
            f.write("v %.4f %.4f %.4f\n" % tuple(bake_vertex(W, bones)))
        for bones, flag, tang, uv in verts:
            f.write("vt %.6f %.6f\n" % (uv[0], uv[1]))
        for x, y, z in tris:
            f.write("f %d/%d %d/%d %d/%d\n" % (x+1, x+1, y+1, y+1, z+1, z+1))
    with open(out_obj[:-4] + ".mtl", "w") as f:
        f.write("newmtl %s\nKd 1 1 1\nmap_Kd ../texture/%s\n" % (nm, ref))
    return len(verts), len(tris), fmt


def find_skels(actor_dir):
    return sorted(glob.glob(os.path.join(actor_dir, "role_*.act")))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--actor", required=True, help="actor 目录 (含 role_*.act 和 *.ski)")
    ap.add_argument("--texture", required=True, help="texture 目录 (含 *.dds)")
    ap.add_argument("--out", required=True, help="输出目录")
    ap.add_argument("--act", default=None, help="指定单一骨架 act（跳过自动匹配）")
    ap.add_argument("--frame", type=int, default=3736, help="姿势帧 (默认 3736 站立)")
    ap.add_argument("--limit", type=int, default=0, help="只处理前 N 个 (0=全部)")
    ap.add_argument("--prefix", default="", help="只跑文件名前缀匹配的 ski")
    args = ap.parse_args()

    os.makedirs(os.path.join(args.out, "obj"), exist_ok=True)
    os.makedirs(os.path.join(args.out, "texture"), exist_ok=True)

    skis = sorted(glob.glob(os.path.join(args.actor, "*.ski")))
    if args.prefix:
        skis = [s for s in skis if os.path.basename(s).startswith(args.prefix)]
    if args.limit:
        skis = skis[:args.limit]
    print("待量产: %d" % len(skis))

    W = None
    if args.act:
        W = load_skeleton(args.act, args.frame)
        print("骨架(手动指定):", os.path.basename(args.act))
    skels = None if args.act else {p: None for p in find_skels(args.actor)}
    if skels is not None:
        print("自动匹配骨架池: %d 个 role act" % len(skels))
        if not skels:
            print("!! actor 目录没找到 role_*.act — 用 --act 指定骨架")
            sys.exit(1)

    ok = fail = fallback = static = 0
    report = []
    skel_cache = {}      # path -> W (同一骨架只解析一次)
    seen_tex = set()     # 已转换贴图
    default_path = (sorted(find_skels(args.actor)) or [None])[0] if not args.act else None
    for i, sp in enumerate(skis):
        nm = os.path.basename(sp)[:-4]
        try:
            W = None
            path = None
            m = re.match(r"a_([a-z]+)_([fm])_", nm)
            if m is None:
                # 非 role 装备 (宠物/坐骑/怪物/特效/武器) → 静态导出, 不套骨架
                static += 1
                tag = "STATIC"
            else:
                if args.act:
                    path = args.act
                    tag = "OK"
                else:
                    key = m.group(1), m.group(2)
                    for p in find_skels(args.actor):
                        if re.match(r"role_%s_%s_.*\.act" % key, os.path.basename(p)):
                            path = p; break
                    if path is None:
                        fallback += 1
                        path = default_path
                        tag = "FALLBACK->%s" % (os.path.basename(path) if path else "?")
                    else:
                        tag = "OK"
                if path is None:
                    raise ValueError("无可用骨架 (用 --act 指定)")
                if path not in skel_cache:
                    skel_cache[path] = load_skeleton(path, args.frame)
                W = skel_cache[path]
            vc, tc, fmt = convert_one(sp, W, args.texture,
                                      os.path.join(args.out, "obj", nm + ".obj"),
                                      os.path.join(args.out, "texture"), seen_tex)
            ok += 1
            if tag != "OK":
                report.append("%s %s : vc=%d tri=%d %s" % (tag, nm, vc, tc, fmt))
            elif fmt != "old":
                report.append("OK %s : vc=%d tri=%d %s" % (nm, vc, tc, fmt))
            if ok % 25 == 0:
                print("  进度 %d/%d (成功 %d)" % (i+1, len(skis), ok), flush=True)
        except Exception as e:
            fail += 1
            report.append("FAIL %s : %s" % (nm, str(e)[:80]))
    with open(os.path.join(args.out, "report.txt"), "w") as f:
        f.write("成功 %d 失败 %d (静态导出 %d, 回退骨架 %d)\n\n" % (ok, fail, static, fallback))
        f.write("\n".join(report))
    print("量产完成: 成功 %d 失败 %d (静态导出 %d, 回退骨架 %d)" % (ok, fail, static, fallback))
    print("输出:", os.path.abspath(args.out))


if __name__ == "__main__":
    main()
