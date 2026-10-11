#!/data/data/com.termux/files/usr/bin/bash
# get_apk.sh — 自动从最新成功的云端构建下载 APK（不依赖浏览器）
# 用法: bash get_apk.sh   （在 crt_pipeline/ 目录或任意位置均可）

cd /storage/emulated/0/Game/游戏端 || exit 1

# 1. 自动识别仓库地址（不写死）
REPO=$(git remote get-url origin | sed 's#.*github.com[:/]##; s#\.git$##')
echo "仓库: $REPO"

# 2. 从 git 凭据里取 GitHub token（你平时 push 用的就是它）
TOKEN=$(printf 'protocol=https\nhost=github.com\n' | git credential fill 2>/dev/null | grep '^password=' | cut -d= -f2)
if [ -z "$TOKEN" ]; then
  echo "!! 未找到 GitHub token"
  echo "   如果你平时 push 用的是 SSH 而不是 https，请告诉我，换方案"
  exit 1
fi

# 3. 查最新一次"成功"的构建 run
RUN_ID=$(curl -s -H "Authorization: token $TOKEN" \
  "https://api.github.com/repos/$REPO/actions/runs?status=success&per_page=1" \
  | grep -o '"id": [0-9]*' | head -1 | grep -o '[0-9]*')
if [ -z "$RUN_ID" ]; then
  echo "!! 还没有成功的构建（绿勾）——先等 Actions 编译成功再跑本脚本"
  exit 1
fi
echo "最新成功构建: $RUN_ID"

# 4. 找该 run 的 APK 产物
ART_ID=$(curl -s -H "Authorization: token $TOKEN" \
  "https://api.github.com/repos/$REPO/actions/runs/$RUN_ID/artifacts" \
  | grep -o '"id": [0-9]*' | head -1 | grep -o '[0-9]*')
if [ -z "$ART_ID" ]; then
  echo "!! 该构建没有产物（可能编译失败没走到上传步骤）"
  exit 1
fi

# 5. 下载到手机 Download 文件夹
mkdir -p /storage/emulated/0/Download
echo "开始下载（几十 MB，请稍等）..."
curl -L -H "Authorization: token $TOKEN" \
  -o /storage/emulated/0/Download/game-apk.zip \
  "https://api.github.com/repos/$REPO/actions/artifacts/$ART_ID/zip"
echo ""
echo "完成: /storage/emulated/0/Download/game-apk.zip"
echo "用手机文件管理器解压它，得到 .apk 后点安装（覆盖安装，数据保留）"
