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
