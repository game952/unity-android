#!/data/data/com.termux/files/usr/bin/bash
# deploy_v4.sh — v4.0 整装拼合预览台 一键部署 (自动禁用旧预览台v3.9/ModelShow)
# 前提: PreviewV4.cs 已存入 crt_pipeline/ 目录
set -e

PIPE=/storage/emulated/0/Game/游戏端/crt_pipeline
PROJ=/storage/emulated/0/Game/游戏端
ASSETS=$PROJ/Assets

echo "== 1/4 前提自查 =="
[ -f "$PIPE/PreviewV4.cs" ] || { echo "X 缺 $PIPE/PreviewV4.cs，先把 PreviewV4.cs 存进去"; exit 1; }
[ -d "$ASSETS" ] || { echo "X 找不到 $ASSETS"; exit 1; }
echo "OK"

echo "== 2/4 禁用旧件 (v3.9 预览台 / ModelShow / 旧验收台) =="
mkdir -p "$PROJ/DisabledBackup"
N=0
while IFS= read -r f; do
  B="$PROJ/DisabledBackup/$(basename "$f").bak"
  mv "$f" "$B"; N=$((N+1)); echo "  已禁用: $(basename "$f")"
done < <(grep -rl -e "Kabsch" -e "残差驱动" -e "ModelShow" "$ASSETS" --include="*.cs" 2>/dev/null || true)
echo "  共禁用 $N 个 (0 个 = 本来就干净)"
[ "$N" -eq 0 ] && echo "  ! 注意: 若手机上仍有旧预览台界面, 把它的 cs 文件名发我"

echo "== 3/4 v4 就位 =="
mkdir -p "$ASSETS/Scripts"
cp "$PIPE/PreviewV4.cs" "$ASSETS/Scripts/PreviewV4.cs"
DUP=$(grep -rl "class PreviewV4" "$ASSETS" --include="*.cs" 2>/dev/null | wc -l)
[ "$DUP" -eq 1 ] || echo "  ! PreviewV4 定义出现 $DUP 次 (应为 1), 检查重复文件"
echo "  PreviewV4.cs -> Assets/Scripts/"

echo "== 4/4 git 推送 =="
cd "$PROJ"
git add -A
git commit -m "v4.0 整装拼合预览台: act骨架池+LBS烘焙 (移植crt_pipeline v3.2.1, 禁用v3.9)" || echo "无改动"
git push && echo "== 已推送: 等 Actions 绿勾 → 装 APK → 打开游戏看 v4.0 预览台 ==" || echo "X push 失败: 检查网络/令牌后重跑"
