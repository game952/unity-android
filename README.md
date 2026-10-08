# MyGame · 手机云端打包

只有手机也能打 Unity 的 Android 包：Termux 里改代码、git push，
GitHub Actions 云端编译，APK 从 Actions 页面下载。

- Unity 版本：2022.3.50f1（云端自动拉镜像，本地不需要装）
- 打包配置：IL2CPP + ARM64，适配 2015 年之后的绝大多数安卓机
- 许可证：Unity Personal（免费），见「① 获取 Unity 许可证」工作流
- 打包触发：push 到 main 分支，或在 Actions 页面手动 Run workflow
- 首包内容：自检画面（显示 BUILD OK 和设备信息），证明整条链路通了

以后改游戏内容：
编辑 Assets/Scripts/HelloWorld.cs → git push → 云端自动重新打包。

详细步骤见聊天里发的《手机云端打包操作手册》。

---

## v2 更新（2026-10-08）：591 个真资源进包

- `Assets/StreamingAssets/`：game_client.pak 解出的 591 个 a_wt/w_wt 资源
  （.ski 300 / .dds 201 / .act 90，约 26MB）按 pak 内相对路径入库，
  外加 manifest.txt 清单（591 行 = 相对路径 TAB 字节数）
- `Assets/Scripts/HelloWorld.cs`：BUILD OK 测试桩 → 升级为资源自检
  （读清单 → 等距抽样 16 个下载 → 校验大小与头部 → 屏幕绿=通过 / 红=失败+原因）
- 头部约定：.ski / .act 前 4 字节 FF FF 0C 00 + `CRT_SkelSkin` / `CRT_Actor`；.dds = 标准 `DDS ` 头
- 工作流、ProjectSettings、场景文件均未改动（脚本 GUID 不变，Main.unity 无需重挂）
- 注意：云端每次构建用临时 debug 签名，与旧 MyGame.apk 签名不同，
  安装新包前先卸载旧的，否则报「签名冲突」

---

## v3 更新（2026-10-08）：CRT 三格式直读 + 角色预览台

- `Assets/Scripts/CrtViewer.cs`（新增）：楼兰真封神 CRT 引擎资源解析器 + 3D 展台
  - .ski 逆向：对象头 `FFFF+类名` → 子块(材质/贴图名/面索引) → 材质库 →
    顶点区(法线/UV/骨骼引用{骨号,权重,骨空间位置,法线}，引用 30B+尾标)
  - .act 逆向：`tooth0708` 版本 → 骨骼树(名/父/位移键/旋转四元数) →
    内嵌 CRT_VaSkin 武器网格（剑/杵模型就藏在动画文件里，帧0为烘焙坐标）
  - .dds 软解码：DXT1/DXT3/DXT5 → Texture2D
  - 验证：Python 镜像算法 387/387 全通过（ski 297 + act 90），顶点 102,250 / 面 137,337
- 展台功能：武器·剑(10) / 武器·杵(9) / 服装 / 手套 / 鞋 / 外装 / 头 共 7 组
  部件实时切换（◀▶），触摸旋转，贴图自动按 texName 从 StreamingAssets 加载
- `HelloWorld.cs`：自检全绿 2 秒后自动挂载查看器（GUID 不变，场景零改动）
- v3 局限：部件均为骨骼局部坐标，只能单件展示；整装术士待 v4
  （需从游戏包提取 role_wt_m_01.act 身体骨骼 + bind 姿势）

---

## 资料来源边界（2026-10-08，长期有效）

- 所有游戏资料（模型/动画/贴图/配置等）只允许从以下两个目录搜索获取：
  1. /storage/emulated/0/Game/
  2. /sdcard/Download/
- 其他任何目录（如 exagear 下的私服端等）一律不作为资料来源，不做检索、不做提取
- 本条为本项目长期资料约束，v4 及以后所有开发均遵守
