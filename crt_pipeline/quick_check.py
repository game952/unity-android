#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""quick_check.py — 量产产出自检: OBJ 有效性 + 贴图引用 + 抽样渲染拼图

用法:
  python3 quick_check.py --pack ./model_pack
  python3 quick_check.py --pack ./model_pack --samples 8   # 抽样渲染 8 个
"""
import os, sys, glob, re, argparse, random

ap = argparse.ArgumentParser()
ap.add_argument("--pack", required=True)
ap.add_argument("--samples", type=int, default=6)
ap.add_argument("--seed", type=int, default=42)
args = ap.parse_args()

objdir = os.path.join(args.pack, "obj")
texdir = os.path.join(args.pack, "texture")
objs = sorted(glob.glob(os.path.join(objdir, "*.obj")))
print("OBJ 总数:", len(objs))

bad, texok, texmiss = [], 0, []
for p in objs:
    nm = os.path.basename(p)[:-4]
    nv = nf = maxidx = 0
    mtlref = None
    for ln in open(p):
        if ln.startswith("v "): nv += 1
        elif ln.startswith("f "):
            nf += 1
            for t in ln.split()[1:]:
                i = int(t.split("/")[0]); maxidx = max(maxidx, i)
        elif ln.startswith("mtllib"): mtlref = ln.split()[1]
    if nv == 0 or nf == 0 or maxidx > nv:
        bad.append((nm, nv, nf, maxidx)); continue
    mp = os.path.join(objdir, mtlref) if mtlref else None
    texname = None
    if mp and os.path.exists(mp):
        m = re.search(r"map_Kd \.\./texture/(\S+)", open(mp).read())
        if m: texname = m.group(1)
    if texname and "MISSING" not in texname:
        if os.path.exists(os.path.join(texdir, texname)): texok += 1
        else: texmiss.append((nm, texname))

print("坏 OBJ:", len(bad), bad[:6] if bad else "无")
print("贴图齐全:", texok, "缺贴图:", len(texmiss), texmiss[:8])

if args.samples > 0:
    try:
        import importlib.util
        _ro = os.path.join(os.path.dirname(os.path.abspath(__file__)), "render_obj.py")
        spec = importlib.util.spec_from_file_location("render_obj", _ro)
        render_obj = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(render_obj)
        from PIL import Image
        random.seed(args.seed)
        picks = random.sample(objs, min(args.samples, len(objs)))
        cols = 2
        rows = (len(picks) + 1) // cols
        S = 340
        canvas = Image.new("RGB", (cols*S + 12, rows*S + 12), (24, 24, 30))
        for i, p in enumerate(picks):
            v, f = render_obj.load_obj(p)
            tmp = os.path.join(args.pack, "_qc_%d.png" % i)
            render_obj.render(v, f, size=S, out=tmp, view="front")
            im = Image.open(tmp).transpose(Image.FLIP_TOP_BOTTOM)
            canvas.paste(im, (6 + (i % cols)*S, 6 + (i // cols)*S))
            os.remove(tmp)
        out = os.path.join(args.pack, "sample_check.png")
        canvas.save(out)
        print("抽样渲染 →", out)
    except Exception as e:
        print("渲染抽样跳过:", e)

print("自检完成")
