<div align="center">
  <img src="data/icons/aspa_logo.png" width="96" height="96" alt="鸿雪画框图标"/>
  <h1>ASP Assistant · Arknights</h1>
  <p>明日方舟·卫戍协议 辅助工具<br/>基于 Moonfair/ASP_Assistant 修改，由 MaaFramework 提供图像识别能力。</p>
  <p>
    <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Windows"/>
    <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square" alt=".NET 8"/>
    <img src="https://img.shields.io/badge/game-卫戍协议·规程-orange?style=flat-square" alt="卫戍协议·规程"/>
    <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-green?style=flat-square" alt="Apache 2.0"/></a>
  </p>
  <p><a href="https://github.com/Moonfair/ASP_Assistant">原项目</a> · <a href="https://github.com/bingyunhe/asp-assistant-arknights/issues">问题反馈</a></p>
</div>

---

> **项目来源**：本项目是在 [Moonfair/ASP_Assistant](https://github.com/Moonfair/ASP_Assistant) 基础上修改的独立衍生版本，保留原项目 Apache License 2.0 协议与归属声明。感谢 Moonfair 及上游贡献者。本仓库不是原作者发布的版本。
>
> 上游基线：[`93b2ba8`](https://github.com/Moonfair/ASP_Assistant/commit/93b2ba8514f6591a67e4cbf5a6169dee7d065bf6)。本次修改日期为 **2026-11-09**；改动范围见各文件头部声明。

## 截图

以下为测试数据生成的界面截图，不含个人游戏记录。

<div align="center">
  <img src="docs/images/operators-dark.png" width="300" alt="深色主题干员筛选"/>
  <img src="docs/images/operators-light.png" width="300" alt="浅色主题干员筛选"/>
</div>

| 干员悬停详情 | 装备数值对比 | 多队伍追踪 |
| :---: | :---: | :---: |
| ![普通与精锐详情](docs/images/operator-details.png) | ![装备界面](docs/images/equipment-light.png) | ![追踪界面](docs/images/tracking-light.png) |

## 功能简介

ASP Assistant · Arknights 是运行于《明日方舟》游戏窗口旁的侧边辅助工具，提供干员、装备查询，追踪列表、阵容管理、Ban 位录入与站位规划。

当前版本为 **v1.7.1**，新增阵容窗口最小化与追踪“已获取”；v1.7.0 起初步支持 MuMu / ADB 连接、模拟器商店追踪与本局信息录入，支持单人模式两种开局信息页布局及高难 10/16 盟约识别。旧版用户可从本仓库 Releases 更新。

### 阵容规划

- 选择核心或附加盟约的Ⅰ／Ⅱ档目标，按 8／9 人、0／1／2 次转职分别搜索候选，可固定必须加入的干员。
- 与本局禁用信息联动；支持“只看本局”，被禁的附加盟约仍可由原生干员凑人数，但不能作为转职目标。
- 候选显示转职目标、调和加成及自由位置，可直接保存为阵容或加入追踪。
- 需要叠层、精锐等额外条件的Ⅱ档会标注，规划只保证对应人数，其他条件需自行确认。
- 达到搜索上限时明确提示；未找到候选不代表已证明无解。
- 感谢 CarlosDJY（Jiayu Ding）通过 [PR #1](https://github.com/bingyunhe/asp-assistant-arknights/pull/1) 贡献阵容规划等功能。

### 站位规划（实验）

- 追踪页顶部切换「购买清单 / 站位」。站位页识别当前卫戍协议地图（10 张，商店收起 / 展开两种镜头），也可手动选图；场上站满干员时仍可识别，暂时看不到棋盘时沿用上次的地图。
- 从干员池里挑 8／9 人上场，给出格子和朝向：按两条敌人路线的拦截点、纵深（重装在前、远程在后）、奶的覆盖、阻挡数和地块效果（红地板、沼泽、蓝箭头、城区、宫殿）打分，并保证至少一个奶上场。
- 计入位置相关的叠层特质（身前 / 身后、周围、同一行、攻击范围内）、塞雷娅复制特质、涤火杰西卡（按满层）、整备区也生效的干员和盟约激活。
- 小棋盘显示头像，可拖动换位或「固定在这里」后围绕它重新规划；支持同名干员重复上场（盟约只按不同干员计数）。检查项区分硬要求与地块提示。
- 干员池：追踪的最终 / 过渡干员默认灰色，点亮即已拥有；扩展池列出与阵容共享盟约的未追踪干员，按阶数切换；可手动添加、标记精锐和同名数量。
- 游戏内显示默认关闭，在站位页底部开启后，在游戏画面上标出格框、朝向和名牌。
- 打分权重是估计值，不是游戏数值；阿戈尔吞噬、雪原隐匿格和飞行敌人尚未纳入，结果仅供参考。

### 干员浏览与筛选

- 按等阶、核心盟约、其他盟约、词条类型、触发时机与本局状态筛选，支持名称搜索与等阶多选。
- 核心盟约和其他盟约默认命中任一选项，勾选该行“同时选中”后要求全部满足；不同筛选行之间始终同时满足。
- 筛选开关显示已选数量，空结果显示提示；“重置”恢复筛选与搜索，保留本局可用盟约高亮。
- 干员以紧凑列表展示头像、名称与盟约；鼠标悬停即可同时查看普通和精锐信息。
- 可手动追踪或标记禁用；展开的筛选区在滚动时保持展开。
- 原有 Ban 界面分析返回本局可用盟约后，高亮对应选项；“清除高亮”独立于筛选和禁用标记。

### 装备浏览与数值对比

- 按等阶、功能类型及普通 / 转职装备类别筛选，支持名称搜索与追踪；移除当前赛季没有匹配项的“专属”分类。
- 等阶和功能类型支持多选，类型可切换为“同时选中”。功能类型分攻击、防御、生存、技力、经济、盟约、其他，一件装备可属于多类，卡片显示对应标签；分类由项目手工维护。
- 名称显示在图片上方，取消普通 / 精锐切换按钮。
- 有数值差异时直接合并，例如 `攻击力+15(25%)%`，高亮普通数值 `15`，括号内为精锐数值。
- 没有精锐版本时显示“无精锐装备”；转职装备显示对应盟约。
- 非数值差异保留两种完整说明，例如“博士投影”的不同生效时机。
- 修复选择等阶后无法重新选择“全部”的问题。

### 追踪与阵容

- 将干员、装备加入追踪列表，或从保存的阵容中导入。
- 在标题栏“设置”中勾选“同时追踪多个队伍”后，连续追踪阵容会合并目标并去重；未勾选时，下次追踪阵容会替换列表。该选项会保存，追踪页不再单独显示复选框。
- 追踪干员同样显示缩略头像、盟约与普通 / 精锐悬停详情。
- 阵容编辑器和追踪页显示计划转职，支持分别取消并同步计数；追踪页的目标盟约分布包含计划转职与调和加成。
- 计划转职随阵容和追踪记录保存；多队伍追踪时，同名干员采用最近追踪阵容的转职计划。
- 多队伍共用一份追踪列表，移除目标会从共用列表中移除。
- 追踪目标可右键「标记已获取」：保留在列表中但不再在商店标出，也不计入目标盟约分布；再次右键或重新追踪即恢复。
- 阵容编辑器、选择干员等对话框可最小化。
- 商店名称栏识别同时读取追踪干员和装备，在目标名称上方显示绿色标记；按实际名称位置定位，不固定为五格，已售空槽不挤占其他目标的位置。
- 灰色卡、冻结雪花与金色扫光均可参与名称识别。装备金光表示可升精锐；角色金光也可能仅表示已持有同名角色，本工具不据此判断三连或升级。

### 截屏录入 Ban 位

- 开局信息页自动录入；“截屏录入”按钮及快捷键读取核心盟约、附加盟约、Ban 位和敌人，支持开局后从游戏内重新打开的信息页，无需顶部“确认本局信息”标题。
- 缩放、最小化、失焦或标题漏读不会重新允许自动录入；看到明确对局画面后才准备识别下一次开局信息页。自动未录入时，可打开局内信息页手动补录。
- 游戏被其他应用覆盖且不在前台时暂停截屏；请先将游戏置前、打开信息页，再点击录入。截图会短暂隐藏辅助和覆盖层以避免遮挡。
- 重试过程中保留旧结果，失败只提示；完整有效的盟约结果更新 Ban 和高亮，敌人信息独立校验，敌人识别失败不阻止盟约录入。识别忙碌时禁用重复录入，不自动滚动游戏重试。

### 本局敌人识别

- 进入“确认本局信息”页面时自动读取右侧“敌方领袖”和特训敌人，也可在敌人页点击“识别本局敌人”重试。
- 截图时短暂隐藏辅助窗口及覆盖层，随后恢复显示，避免贴在游戏内部的辅助遮住敌人区域；手动识别不点击或切换游戏页面。
- 识别中禁用重复请求，显示成功、部分识别或失败状态；重试失败保留并标注上次录入结果。支持首领名称断行、相邻特训类型合并与去重，不固定为两类敌人。
- 已收录的首领和类型展示现有详情；未收录的“假想敌：鳍”“致命”“复苏”等保留文字并提示缺少资料，可展开核对识别文字。不会用相似名称强行套用其他敌人的资料。

### 外观与窗口

- 标题栏齿轮打开设置面板，集中选择辅助界面深色／浅色、游戏主题和多队伍追踪，立即生效并保存。
- EXE 和窗口标题栏统一采用鸿雪画框图标；点击标题栏关闭按钮时先确认是否退出。
- 游戏主题提供“卫戍协议终端”（默认）、“乌萨斯齐心团”和“‘全能的’可露希尔”。请选择游戏内正在使用的主题；不同主题使用各自的名称栏位置和图像处理方式。
- 支持窗口置顶、拖动和缩放；调整六个页面的内容宽度与筛选布局。
- 游戏最小化时同步最小化辅助并隐藏标记；恢复游戏后恢复由游戏联动收起的辅助。提前手动最小化的辅助保持最小化。
- 商店星号忽略小于 6 个界面像素的 OCR 位置变化，减少文字框抖动；明显移动继续跟随。
- 版本号为 0.0.0 的开发构建跳过更新检查；正式发行版仅检查本仓库 GitHub Releases。
- 自动更新校验 `SHA256SUMS.txt`，缺失或不匹配时停止；更新采用同盘目录替换，替换或启动失败时尝试回滚并显示失败原因。

## 当前数据与限制

| 项目 | 状态 |
| --- | --- |
| 数据赛季 | 卫戍协议·规程·序列 I，`act3autochess` / `V3_1` |
| 干员 / 装备 / 盟约 | 141 / 70 / 26 |
| 核心转职盟约 | 从游戏表 `giveBondId` 生成映射；其他转职盟约沿用名称匹配 |
| Ban 位识别 | 单张截图结合图标亮灰与 OCR 名称吸附，支持合并名称拆分；未命名图标会报告，仍受画面布局影响 |
| 商店追踪识别 | 收录三款主题共 14 张 1080p 实拍脱敏图，覆盖一本四格、二本五格、四本六格、灰色、冻结、扫光、重复干员和售空；默认终端漏读时补读精确名称行。720p 与超宽留边使用合成图验证，原生超宽屏、其他 DPI 及终端／乌萨斯六本待实测 |
| 站位地图识别 | 收录 10 张地图的可部署格、敌人路线与特殊地块，按格子颜色匹配地图与镜头；已用实拍截图验证，MuMu 下仅验证编译 |
| 场上与备战区干员识别 | 尚未实现，站位规划使用干员池中手动点亮的干员 |
| 首领 / 敌人 | 新版右侧区域识别流程已通过两张 1080p 实拍图验证；详情保留原有资料，尚未完整同步本赛季，未收录内容单独提示 |
| 自动更新 | 只读取本仓库发布，校验 SHA256 后替换目录；更新失败尝试回滚，仍需自行退出旧实例再手动安装时请保留个人配置 |

## 运行要求

| 条件 | 说明 |
| --- | --- |
| 操作系统 | Windows 10 / 11，x64 |
| .NET 运行时 | 源码构建的非独立版本需要 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 原生依赖 | 图像识别需要 Microsoft Visual C++ 2015–2022 x64 Runtime；发布流程会打包所需运行库 DLL |
| 游戏客户端 | 明日方舟 PC 版，或 MuMu 中的 Android 12 / Android 15 版 |
| 游戏画面 | 原有识别布局以 16:9 为基础，实际识别仍受赛季界面和分辨率影响 |

### MuMu / ADB 连接

1. 在 MuMu 中打开明日方舟，将模拟器画面设置为横屏 16∶9，支持 1920×1080 和 1280×720（240 DPI，平板模式）。
2. 打开辅助侧栏的设置，点击“游戏连接…”，选择“MuMu / ADB”。
3. 点击“发现设备”并选择目标实例，也可以手动填写模拟器的 `adb.exe` 路径和连接地址。
4. 选择与该设备对应的 MuMu 游戏窗口，点击“测试并应用”。多开时需核对实例与窗口。
5. 选择当前游戏的商店主题并添加追踪目标。绿色标记会随 MuMu 内部游戏画面移动和缩放。

ADB 模式使用 MaaFramework 读取游戏截图，游戏被其他窗口遮挡时仍可识别。覆盖层在游戏或辅助侧栏处于前台时显示，最小化时隐藏。截图失败时清除旧结果并每 5 秒重试。关闭后重新打开的模拟器窗口可在“游戏连接…”中重新选择。连接设置保存在本机用户数据目录，原有安装默认继续使用 PC 截图。

当前实测范围为 MuMu 12 的 1920×1080 终端主题商店，已读出四格商品并验证坐标。ADB 模式拒绝竖屏与非 16∶9 画面。其他商店等级、主题和模拟器版本仍需更多实机验证。

开发验证可通过 `ASPASSISTANT_SETTINGS_DIR` 环境变量指定独立设置目录，使用 `dotnet ASPAssistant.App.dll` 启动已构建的托管入口。

## 获取与构建

无需编译的 Windows x64 独立运行包可在 [Releases](https://github.com/bingyunhe/asp-assistant-arknights/releases/latest) 获取。

1. 下载 `ASPAssistant-v1.7.1-win-x64.zip`，不要选择 `Source code` 源码压缩包。
2. 右键“全部解压”到独立目录，保留全部文件和 `data`、`runtimes` 文件夹。
3. 双击 `ASPAssistant.App.exe` 启动。无需另外安装 .NET；包内带有识别模型和所需运行库。
4. 更新时先退出旧程序，再解压新版。个人追踪与阵容设置存放于本机用户数据目录。

发布页同时提供 `SHA256SUMS.txt`，可用 `Get-FileHash .\ASPAssistant-v1.7.1-win-x64.zip -Algorithm SHA256` 核对下载完整性；`PACKAGE-MANIFEST.txt` 记录 CI 解包检查后的版本、资源数量及 ZIP 哈希。详细说明见 [本版发行说明](RELEASE_NOTES.md)。

如果需要从源码构建：

安装 .NET 8 SDK 后，在 PowerShell 中运行：

```powershell
git clone https://github.com/bingyunhe/asp-assistant-arknights.git
cd asp-assistant-arknights
dotnet restore ASPAssistant.sln
dotnet test src/ASPAssistant.Tests/ASPAssistant.Tests.csproj -c Release
dotnet run --project src/ASPAssistant.App
```

仓库已包含当前 JSON 数据、图标和 OCR 模型，首次运行不需要重新抓取数据。

```powershell
# 生成需要 .NET Desktop Runtime 的本地版本
dotnet publish src/ASPAssistant.App/ASPAssistant.App.csproj -c Release --self-contained false -o publish/local

# 生成 Windows x64 独立版本
dotnet publish src/ASPAssistant.App/ASPAssistant.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

维护者更新发行说明后，推送 `v` 开头的版本标签即可触发 `.github/workflows/release.yml`。该流程先运行测试，再构建独立运行包、打包运行库与许可证，生成校验文件并发布到 GitHub Releases。

## 更新游戏数据

脚本使用 Python 3.10+；处理图片需要 Pillow。

```powershell
python -m pip install Pillow
cd data
# 只更新本赛季干员、装备、盟约，保留首领和敌人数据
python fetch_spdatabase.py --skip-bosses --skip-enemies
```

已有图标会保留，只下载缺失图标。大量下载可能耗时，建议在游戏空闲时运行。

| 命令 | 功能 |
| --- | --- |
| `python fetch_spdatabase.py --skip-bosses --skip-enemies` | 更新当前干员、装备和盟约数据 |
| `python fetch_spdatabase.py --download-skins` | 下载皮肤头像并重建映射 |
| `python fetch_spdatabase.py --apply-mask` | 为已有皮肤头像补白色背景 |
| `python fetch_spdatabase.py --skip-icons` | 跳过图标下载 |

数据脚本的输出格式和来源沿用原项目，并增加当前赛季读取逻辑；更新数据不会自动完成识别区域适配。

## 数据与项目结构

```text
├── data/
│   ├── operators.json           # 干员、盟约与词条
│   ├── equipment.json           # 装备与普通 / 精锐效果
│   ├── covenants.json           # 盟约与核心转职装备映射
│   ├── skin_avatar_map.json     # 皮肤头像到干员名的映射
│   ├── garrison_maps.json       # 站位地图：可部署格、敌人路线、特殊地块与镜头
│   ├── icons/                  # 干员、装备、敌人等图标
│   ├── model/                  # OCR 模型
│   └── fetch_spdatabase.py      # 数据与图片获取脚本
├── src/
│   ├── ASPAssistant.App/        # WPF 界面
│   ├── ASPAssistant.Core/       # 业务逻辑与数据层
│   └── ASPAssistant.Tests/      # 数据、视图模型和 WPF 回归测试
├── docs/images/                # 无个人信息的界面截图
├── LICENSE                     # Apache 官方标准 License 2.0 正文
├── LICENSE.upstream            # 保留上游许可原文及版权声明
├── NOTICE                      # 上游与第三方归属
```

个人追踪、阵容、窗口及主题设置存放在本机用户数据目录，不随源码分发。不要把个人配置、未经脱敏的游戏截图、日志、密钥或构建缓存提交到仓库。

## 开发依赖与致谢

- [Moonfair/ASP_Assistant](https://github.com/Moonfair/ASP_Assistant)：本项目的上游代码与设计基础。
- [MaaFramework](https://github.com/MaaXYZ/MaaFramework)：OCR、模板与特征匹配。
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)：MVVM 基础设施。
- [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC)：内置界面字体（子集），SIL Open Font License 1.1，许可文本随字体文件分发。
- [PRTS Wiki](https://prts.wiki/) 与 [SPDatabase](https://github.com/Yanstory/SPDatabase)：游戏数据和图片来源，具体接口保留在数据脚本中。
- xUnit、Moq、FluentAssertions 等测试依赖遵循各自许可证。

## 许可证

本项目的修改部分以 **[Apache License 2.0](LICENSE)** 开源。`LICENSE` 采用 [Apache 官方标准正文](https://www.apache.org/licenses/LICENSE-2.0.txt)；上游仓库原许可文本的部分措辞与标准正文不同，已逐字保留于 [LICENSE.upstream](LICENSE.upstream)，其中的原始版权声明及上游许可条件继续保留。

分发源码或构建产物时，请一并保留 `LICENSE`、`LICENSE.upstream`、`NOTICE` 以及相关文件中的版权和变更声明。

软件许可证不改变《明日方舟》游戏数据、名称、图像和其他第三方资源的权利归属；各依赖遵循其各自的许可证。本项目仅供学习和个人使用，与 Hypergryph / 鹰角网络无关。
