#!/usr/bin/env python3
"""render_obj.py — 软件光栅化 OBJ 验证渲染 (零依赖纯 Python 版)
接口不变: load_obj(path) -> (verts, faces); render(v, f, size, view, out)"""
import sys, os

def load_obj(p):
    v, f = [], []
    for ln in open(p):
        if ln.startswith('v '):
            s = ln.split()
            v.append((float(s[1]), float(s[2]), float(s[3])))
        elif ln.startswith('f '):
            f.append([int(x.split('/')[0]) - 1 for x in ln.split()[1:]])
    return v, f


def _cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def render(v, f, size=760, view='front', out='render.png'):
    n = len(v)
    f = [t for t in f if all(0 <= i < n for i in t)]
    if not f:
        print(out + ': 无有效面'); return
    xs = [p[0] for p in v]; ys = [p[1] for p in v]; zs = [p[2] for p in v]
    ctr = ((min(xs)+max(xs))/2, (min(ys)+max(ys))/2, (min(zs)+max(zs))/2)
    rad = max(max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs)) / 2 * 1.06
    if rad <= 0: rad = 1.0
    # 正交投影: front=(x,y,z) side=(z,y,x) top=(x,z,y)
    if view == 'front':   P = [(p[0]-ctr[0], p[1]-ctr[1], p[2]-ctr[2]) for p in v]
    elif view == 'side':  P = [(p[2]-ctr[2], p[1]-ctr[1], p[0]-ctr[0]) for p in v]
    else:                 P = [(p[0]-ctr[0], p[2]-ctr[2], p[1]-ctr[1]) for p in v]
    S = size / 2 / rad
    X = [int(p[0]*S + size/2) for p in P]
    Y = [int(p[1]*S + size/2) for p in P]
    Z = [p[2] for p in P]
    img = bytearray(size*size*3)
    for i in range(0, len(img), 3):
        img[i] = img[i+1] = 24; img[i+2] = 30
    zbuf = [[-1e30]*size for _ in range(size)]
    # 面法线 + 光照 (L 归一化后 = (0.51, 0.73, 0.68) 近似)
    Lx, Ly, Lz = 0.45, 0.65, 0.61
    Ll = (Lx*Lx + Ly*Ly + Lz*Lz) ** 0.5
    Lx, Ly, Lz = Lx/Ll, Ly/Ll, Lz/Ll
    tris = []
    for t in f:
        a, b, c = v[t[0]], v[t[1]], v[t[2]]
        nx = (b[1]-a[1])*(c[2]-a[2]) - (b[2]-a[2])*(c[1]-a[1])
        ny = (b[2]-a[2])*(c[0]-a[0]) - (b[0]-a[0])*(c[2]-a[2])
        nz = (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0])
        nl = (nx*nx + ny*ny + nz*nz) ** 0.5 + 1e-12
        shade = abs(nx*Lx + ny*Ly + nz*Lz) / nl * 0.75 + 0.25
        zc = (Z[t[0]] + Z[t[1]] + Z[t[2]]) / 3
        tris.append((zc, t, shade))
    tris.sort(key=lambda x: x[0])        # 远的先画
    R = int(190*0.75+30); G = int(150*0.75+25); B = int(110*0.75+18)  # 占位
    for zc, t, shade in tris:
        cr = int(190*shade+30); cg = int(150*shade+25); cb = int(110*shade+18)
        x0, x1, x2 = X[t[0]], X[t[1]], X[t[2]]
        y0, y1, y2 = Y[t[0]], Y[t[1]], Y[t[2]]
        z0, z1, z2 = Z[t[0]], Z[t[1]], Z[t[2]]
        minx = max(min(x0, x1, x2), 0); maxx = min(max(x0, x1, x2), size-1)
        miny = max(min(y0, y1, y2), 0); maxy = min(max(y0, y1, y2), size-1)
        if maxx < minx or maxy < miny: continue
        d = float((x1-x0)*(y2-y0) - (x2-x0)*(y1-y0))
        if abs(d) < 1e-9: continue
        for yy in range(miny, maxy+1):
            row = zbuf[yy]; ro = (yy*size + minx)*3
            for xx in range(minx, maxx+1):
                w0 = ((x1-xx)*(y2-yy) - (x2-xx)*(y1-yy)) / d
                w1 = ((x2-xx)*(y0-yy) - (x0-xx)*(y2-yy)) / d
                w2 = 1 - w0 - w1
                if w0 < -0.001 or w1 < -0.001 or w2 < -0.001: continue
                zz = w0*z0 + w1*z1 + w2*z2
                if zz > row[xx]:
                    row[xx] = zz
                    i3 = ro + (xx-minx)*3
                    img[i3] = cr; img[i3+1] = cg; img[i3+2] = cb
    from PIL import Image
    Image.frombytes('RGB', (size, size), bytes(img)).save(out)
    print("%s: %d tris" % (out, len(f)))

if __name__ == '__main__':
    objdir = sys.argv[1] if len(sys.argv) > 1 else '.'
    outdir = sys.argv[2] if len(sys.argv) > 2 else '.'
    os.makedirs(outdir, exist_ok=True)
    for p in sorted(os.listdir(objdir))[:4]:
        if not p.endswith('.obj'): continue
        v, f = load_obj(os.path.join(objdir, p))
        render(v, f, out=os.path.join(outdir, p[:-4] + '_front.png'), view='front')
