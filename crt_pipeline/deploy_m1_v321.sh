#!/data/data/com.termux/files/usr/bin/bash
# deploy_m1_v321.sh v2 — M1 路线A一键部署（自动定位 obj/mtl + 抽样兜底 + 自诊断）
# 前提：本文件与 ModelShow.cs 都已放到 crt_pipeline/ 目录
set -e

PIPE=/storage/emulated/0/Game/游戏端/crt_pipeline
PROJ=/storage/emulated/0/Game/游戏端
SRC=$PIPE/model_pack
TEX=/storage/emulated/0/Game/pak_out/creature/texture
MODELS=$PROJ/Assets/Resources/models

echo "== 1/6 环境自查 =="
[ -d "$PROJ/.git" ] || { echo "X $PROJ 不是 git 工程"; exit 1; }
[ -d "$SRC" ] || { echo "X 找不到 $SRC，先跑量产"; exit 1; }
[ -f "$PIPE/ModelShow.cs" ] || { echo "X 缺 $PIPE/ModelShow.cs，先把 ModelShow.cs 存进去"; exit 1; }
[ -f "$SRC/report.txt" ] && echo "report 记录数: $(wc -l < "$SRC/report.txt")"

echo "== 2/6 自动定位 obj/mtl 目录 =="
OBJDIR=""; MTLDIR=""
for d in "$SRC" "$SRC/obj" "$SRC/models" "$SRC/mesh" "$SRC/model"; do
  if ls "$d"/*.obj >/dev/null 2>&1; then OBJDIR="$d"; break; fi
done
if [ -n "$OBJDIR" ]; then
  for d in "$OBJDIR" "$SRC/mtl" "$SRC/material"; do
    if ls "$d"/*.mtl >/dev/null 2>&1; then MTLDIR="$d"; break; fi
  done
fi
if [ -z "$OBJDIR" ]; then
  echo "X 在顶层和常见子目录都没找到 .obj，model_pack 实际内容："
  ls "$SRC" | head -15
  exit 1
fi
echo "OBJ 目录: $OBJDIR（$(ls "$OBJDIR"/*.obj | wc -l) 个）"
if [ -n "$MTLDIR" ]; then
  echo "MTL 目录: $MTLDIR（$(ls "$MTLDIR"/*.mtl | wc -l) 个）"
else
  echo "! 未找到 mtl → 全部白模验收（几何照验）"
fi

echo "== 3/6 清空旧样本 + 抽样 =="
mkdir -p "$MODELS"; rm -f "$MODELS"/*
( for kw in hea clo glo sho wai bpf mon emon ss; do
    ls "$OBJDIR" | grep "$kw" | grep '\.obj$' | head -3
  done ) | sort -u > "$PIPE/m1_samples.txt"
CNT=$(wc -l < "$PIPE/m1_samples.txt")
echo "关键词抽中 $CNT 个"
if [ "$CNT" -lt 10 ]; then
  echo "! 关键词覆盖不足 → 兜底取字母序前 24 个"
  ls "$OBJDIR" | grep '\.obj$' | sort | head -24 > "$PIPE/m1_samples.txt"
  CNT=$(wc -l < "$PIPE/m1_samples.txt")
fi
echo "最终样本 $CNT 个"
while read -r f; do
  cp "$OBJDIR/$f" "$MODELS/"
  if [ -n "$MTLDIR" ]; then
    m="${f%.obj}.mtl"; [ -f "$MTLDIR/$m" ] && cp "$MTLDIR/$m" "$MODELS/"
  fi
done < "$PIPE/m1_samples.txt"
echo "OBJ 已拷: $(ls "$MODELS"/*.obj | wc -l)  MTL 已拷: $(ls "$MODELS"/*.mtl 2>/dev/null | wc -l)"

echo "== 4/6 贴图 dds→png（失败自动白模降级，不阻塞）=="
python3 - <<'PYEOF'
import os, glob
DST = "/storage/emulated/0/Game/游戏端/Assets/Resources/models"
TEX = "/storage/emulated/0/Game/pak_out/creature/texture"
ok = fail = 0
try:
    from PIL import Image
except ImportError:
    print("! 无 Pillow → 全部白模（几何照验，贴图链下轮补）"); raise SystemExit
for mtl in sorted(glob.glob(DST + "/*.mtl")):
    base = os.path.basename(mtl)[:-4]
    anch = None
    for line in open(mtl, errors="ignore"):
        if line.strip().lower().startswith("map_kd"):
            anch = os.path.splitext(line.split()[-1])[0]
    if not anch:
        continue
    dds = os.path.join(TEX, anch + ".dds")
    if not os.path.exists(dds):
        d2 = os.path.join(TEX, anch.upper() + ".dds")
        dds = d2 if os.path.exists(d2) else None
    if not dds:
        fail += 1; continue
    try:
        Image.open(dds).convert("RGBA").save(os.path.join(DST, base + ".png"))
        ok += 1
    except Exception as e:
        fail += 1; print("  转png失败:", anch, e)
print(f"贴图 png: 成功 {ok} 失败 {fail}")
PYEOF

echo "== 5/6 显示脚本就位 =="
mkdir -p "$PROJ/Assets/Scripts"
cp "$PIPE/ModelShow.cs" "$PROJ/Assets/Scripts/ModelShow.cs"

echo "== 6/6 git 推送 =="
cd "$PROJ"
git add -A
git commit -m "M1 v3.2.1 修复版真机验收样本（$CNT 个 + ModelShow）" || echo "无改动"
git push && echo "== 已推送：等 GitHub Actions 绿勾 → 下载 APK → 安装 → 看首屏 ==" || echo "X push 失败：检查网络/令牌后重跑本脚本"
