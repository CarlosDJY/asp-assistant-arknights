# ASP Assistant · Arknights v1.5.0｜阵容规划与转职追踪

本项目是基于 [Moonfair/ASP_Assistant](https://github.com/Moonfair/ASP_Assistant) 修改的独立衍生版本，保留上游来源、Apache License 2.0 与第三方归属声明。

## 更新内容

- 新增阵容规划，支持盟约Ⅰ／Ⅱ档目标、固定干员及 8／9 人、0／1／2 转职组合。
- 规划结果支持保存阵容和加入追踪，并与本局禁用信息联动。
- 优化干员卡片及追踪界面，显示目标盟约分布。
- 阵容编辑器和追踪页支持查看、取消计划转职，并保存转职信息。
- 修复魔王“调和”计数遗漏和搜索受限时的提示。
- 改进盟约录入，使其不再依赖敌人信息识别成功。

感谢 CarlosDJY（Jiayu Ding）通过 [PR #1](https://github.com/bingyunhe/asp-assistant-arknights/pull/1) 贡献阵容规划等功能。

## 下载与使用

- 下载 **ASPAssistant-v1.5.0-win-x64.zip**；`Source code` 为源码，不能直接运行。
- 适用于 Windows 10 / 11 x64。完整解压后运行 **ASPAssistant.App.exe**。
- 包内包含 .NET 8、OCR 模型、游戏数据、图标及所需 VC++ 运行库，无需另外安装 .NET。
- 如果旧版仍提示目录被占用，请完全退出旧辅助，将本包完整解压到新目录后启动；个人配置和阵容保留在本机用户数据目录。
- 同时提供 **SHA256SUMS.txt**，用于核对下载文件完整性。

包内保留 `LICENSE`、`LICENSE.upstream`、`NOTICE`；第三方依赖和游戏资源保留各自的许可与权利。
