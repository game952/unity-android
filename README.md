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
