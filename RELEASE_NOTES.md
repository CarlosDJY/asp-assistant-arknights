# ASP Assistant · Arknights v1.7.1｜阵容窗口与追踪体验优化

## 更新内容

- 阵容编辑器和选择干员窗口新增最小化按钮；子窗口最小化后，可移动上一级窗口，并保留当前编辑、搜索和筛选状态。
- 干员和装备追踪栏新增“已获取”按钮：点击后停止追踪并保留记录，再次点击可恢复追踪与商店高亮。
- 已获取状态随配置保存，追踪数量及关联装备需求同步更新。
- 优化普通游戏窗口的追踪标记刷新，减少窗口轮询时的重复处理。

感谢 hesperine（Eosbound）通过 [PR #13](https://github.com/bingyunhe/asp-assistant-arknights/pull/13) 贡献窗口轮询优化。

## 下载与使用

- 下载 **ASPAssistant-v1.7.1-win-x64.zip**；`Source code` 为源码，不能直接运行。
- 适用于 Windows 10 / 11 x64。完整解压后运行 **ASPAssistant.App.exe**。
- 包内包含 .NET 8、OCR 模型、游戏数据、图标及所需 VC++ 运行库，无需另外安装 .NET。
- 更新前请完全退出旧辅助；个人配置和阵容保留在本机用户数据目录。
- 同时提供 **SHA256SUMS.txt** 和 **PACKAGE-MANIFEST.txt**，用于核对下载文件及包内容。

包内保留 `LICENSE`、`LICENSE.upstream`、`NOTICE`；第三方依赖和游戏资源保留各自的许可与权利。
