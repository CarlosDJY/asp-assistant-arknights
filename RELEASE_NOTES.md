# ASP Assistant · Arknights v1.7.0｜模拟器接入与单人信息识别

本项目是基于 [Moonfair/ASP_Assistant](https://github.com/Moonfair/ASP_Assistant) 修改的独立衍生版本，保留上游来源、Apache License 2.0 与第三方归属声明。

## 更新内容

- 初步支持 MuMu 模拟器，新增“游戏连接”设置，可切换 PC／ADB 截图，自动发现设备或手动填写 ADB 路径、连接地址。
- 支持 **Android 12、Android 15**，支持 **1920×1080** 和 **1280×720（240 DPI，平板模式）**。
- 支持 MuMu 中的商店追踪与本局信息录入，追踪标记跟随模拟器内部游戏显示区域。
- 支持单人模式两种开局信息页布局及高难 10/16 盟约识别，处理左右分组和滚动后核心标题离开画面的情况。
- 信息未完整读出时保留已有结果，并提示滚动到末行名称完整可见后重新扫描。

感谢 hesperine（Eosbound）通过 [PR #5](https://github.com/bingyunhe/asp-assistant-arknights/pull/5) 贡献模拟器接入，lemon-n-orange 通过 [PR #7](https://github.com/bingyunhe/asp-assistant-arknights/pull/7) 贡献单人信息页识别。

## 下载与使用

- 下载 **ASPAssistant-v1.7.0-win-x64.zip**；`Source code` 为源码，不能直接运行。
- 适用于 Windows 10 / 11 x64。完整解压后运行 **ASPAssistant.App.exe**。
- 包内包含 .NET 8、OCR 模型、游戏数据、图标及所需 VC++ 运行库，无需另外安装 .NET。
- 更新前请完全退出旧辅助；个人配置和阵容保留在本机用户数据目录。
- 同时提供 **SHA256SUMS.txt** 和 **PACKAGE-MANIFEST.txt**，用于核对下载文件及包内容。

包内保留 `LICENSE`、`LICENSE.upstream`、`NOTICE`；第三方依赖和游戏资源保留各自的许可与权利。
