#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""act_parse (纯 Python 零依赖版, 与 numpy 定案版逐行等价)
role 类 (标记 02): 节点 = name + parent + u32帧数 + {pos 3f}×N + u32 + {quat 4f}×N
输出: bones[i] = {name,parent,nf,trans[(x,y,z)]*,quats[(x,y,z,w)]*}
"""
import struct, sys


def u32(d, o): return struct.unpack_from("<I", d, o)[0]


def find_next_anchor(d, o):
    """扫描下一个节点名双锚点: u32 len(1..20)+printable + u32 len+printable"""
    F = len(d)
    p = o
    while p < F - 12:
        nl = u32(d, p)
        if 1 <= nl <= 20 and p + 4 + nl + 4 <= F:
            s = d[p+4:p+4+nl]
            if all(32 <= c < 127 for c in s):
                pl = u32(d, p + 4 + nl)
                if 1 <= pl <= 20 and p + 4 + nl + 4 + pl <= F:
                    s2 = d[p+8+nl:p+8+nl+pl]
                    if all(32 <= c < 127 for c in s2):
                        return p
        p += 1
    return F


def parse_act(path, verbose=True):
    d = open(path, "rb").read()
    F = len(d)
    assert d[:2] == b"\xff\xff", "not CRT stream"
    tl = struct.unpack_from("<H", d, 2)[0]
    tag = d[4:4+tl].decode()
    o = d.find(b"\x00\x00\x80\x3f", tl + 4)      # 1.0f 结束骨架名
    if o < 0: raise ValueError("no 1.0f header")
    o += 4
    ver_flag = d[o]; o += 1
    flag2 = d[o]; o += 1
    cnt = u32(d, o); o += 4
    if verbose: print("%s: %dB tag=%s ver=%d flag=%d 节点=%d" %
                      (path.split('/')[-1], F, tag, ver_flag, flag2, cnt))
    bones = []
    for i in range(cnt):
        nl = u32(d, o); o += 4
        name = d[o:o+nl].decode("utf-8", "replace"); o += nl
        pl = u32(d, o); o += 4
        parent = d[o:o+pl].decode("utf-8", "replace"); o += pl
        nf = u32(d, o); o += 4
        if nf == 0:
            nxt = find_next_anchor(d, o)
            if verbose: print("  [%3d] %-22s 父%-18s 静态(跳过%dB填充)" %
                              (i, name, parent, nxt - o))
            bones.append(dict(name=name, parent=parent, nf=0,
                              trans=None, quats=None))
            o = nxt
            continue
        if not 1 <= nf <= 60000: raise ValueError("帧数异常 %d @%s" % (nf, name))
        # f32 直接解为 Python float(双精度) — 与 numpy astype(float64) 等价
        pos_t = struct.unpack_from("<%df" % (nf * 3), d, o); o += nf * 12
        rq = u32(d, o); o += 4                       # rotatVec 计数(=nf)
        q_t = struct.unpack_from("<%df" % (nf * 4), d, o); o += nf * 16
        if rq != nf: raise ValueError("帧数不匹配 %d≠%d @%s" % (rq, nf, name))
        trans = [(pos_t[k*3], pos_t[k*3+1], pos_t[k*3+2]) for k in range(nf)]
        quats = [(q_t[k*4], q_t[k*4+1], q_t[k*4+2], q_t[k*4+3]) for k in range(nf)]
        nxt = find_next_anchor(d, o)
        bones.append(dict(name=name, parent=parent, nf=nf, trans=trans, quats=quats,
                          tail=nxt - o))
        if verbose and i < 14:
            print("  [%3d] %-24s 父%-18s 帧%d pos=(%.2f,%.2f,%.2f) 尾%dB" %
                  (i, name, parent, nf, *trans[0], nxt - o))
        o = nxt
    if verbose: print("  ... 共 %d 节点, 解析终点 %d/%d" % (cnt, o, F))
    return dict(tag=tag, ver=ver_flag, bones=bones, end=o, size=F)


def quat_to_mat(q):
    """四元数 (x,y,z,w) → 3x3 旋转矩阵 (list of 3 rows)"""
    x, y, z, w = q
    n = x*x + y*y + z*z + w*w
    if n < 1e-12: return [[1.0, 0, 0], [0, 1.0, 0], [0, 0, 1.0]]
    s = 2.0 / n
    return [
        [1 - s*(y*y+z*z), s*(x*y-z*w),     s*(x*z+y*w)],
        [s*(x*y+z*w),     1 - s*(x*x+z*z), s*(y*z-x*w)],
        [s*(x*z-y*w),     s*(y*z+x*w),     1 - s*(x*x+y*y)]]


def mat_mul(A, B):
    """3x3 @ 3x3"""
    return [[A[i][0]*B[0][j] + A[i][1]*B[1][j] + A[i][2]*B[2][j]
             for j in range(3)] for i in range(3)]


def mat_vec(A, v):
    """3x3 @ 3"""
    return [A[i][0]*v[0] + A[i][1]*v[1] + A[i][2]*v[2] for i in range(3)]


if __name__ == "__main__":
    for p in sys.argv[1:]:
        parse_act(p)
        print()
