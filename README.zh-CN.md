# KK_StoryNPCBridge

[English](README.md) | 简体中文

这是一个适用于《恋活》（Koikatu）和 Koikatsu Party 的 BepInEx 5 插件，通过本地 API 读取和控制故事模式中的 NPC。外部应用可以提供手动控制，也可以接入 AI；模型调用、提示词、上下文组织和行为决策由外部应用负责。

## 当前支持范围

**目前插件会向游戏中注入一个新的桥接角色，且只有这个新角色可以被控制。** 她的角色 ID 为 `bridge:main`，初始外观使用游戏默认女性模型 `ill_Default_Female`。

当前不能选择或接管故事中已有的 NPC，也不控制玩家。未接管桥接角色时，她由游戏原生 AI 管理。正常释放后恢复原生 AI；意外断线会先保留一段有时限的重连等待期。

## 后续 NPC 控制计划

未来将支持故事模式中的已有 NPC，并提供以下控制范围：

- 指定一个 NPC。
- 指定某几个 NPC。
- 控制全部 NPC。

这些是后续计划，不代表当前版本已经支持。

## 功能

- 提供玩家名字、原生游戏时间段、星期，以及明确的日终／新一天事件。
- 只读采集 H 场景参与者、完整原生统计、结束状态及结算后属性变化，支持普通 NPC 的独立／多人场景；通过 `game_h_*` 事件与 `lastHScene` 提供报告。
- 受控期间保留原生阶段的位置和穿着初始化，随后在新落点等待，并上报任务中断及重定位结果。
- 在任意场景读取和修改原生数值、关系、社团成员和约会约定等状态，返回实际读取结果。
- 按有符号增量调整数值属性，限制到原生范围并返回前后实际值；稳定的修改 ID 防止重试时重复应用。
- 每个故事存档独立保留桥接角色进度；首次创建时数值／计数归零，公开的状态标志设为 false。
- 在漫游和原生对话中控制表情及身体动作。
- 在就绪的对话中通过原生 `CharaMotion` 临时演出，支持控制器、IK 和摇晃资源的实时验证及恢复；已加载状态继续使用轻量的 `play_animation`。详见[机制对比及参数支持范围](API.zh-CN.md#高级原生-adv-动作)。
- 只读获取当前原生穿着栏位类型，包括校服、体操服、泳装、社团服、私服和睡衣。
- 立即执行原生行动，或将接下来的任务加入队列。
- 观测原生 AI 已选行动、预留目标及欲望权重候选；接管时先返回原生规划快照，再清理原生任务。
- 每次独立随机抽取其他原生女孩的当前任务作为匿名行为参考，并提供可复用行动请求。
- 使用原生导航走到玩家附近，支持跨地图路线。
- 使用游戏原生 ADV 对话，支持文字输入、等待外部响应、NPC 主动邀聊，长回复按实际原生字体及文本区域分页，并对过长单词断行。
- 使用 `play_adv_sequence` 在桥接角色已就绪的原生聊天中连续播放台词／旁白、表情、已加载动作及延迟，并提供进度、取消和恢复。
- 控制原生脸红、泪水等级、视线、高光及面部开合，并按当前模型形变动态解析 `smile`、`shy`、`crying` 等语义预设。
- 上报原生对话选项，区分触碰与注视，并保留原有部位交互反应。
- 记录涉及桥接角色且实际显示的原生 ADV 台词，包含说话者身份和会话参与者；排除无关会话和纯语音反应。
- 在原生对话中通过按钮主动输入文字；外部连接器可显式显示／关闭等待提示，无需插入台词。
- 通过独立接口执行告白、分手、H、午餐、约会、社团活动／入部、学习、运动、跟随及一起回家等原生效果，明确检查关系／部员条件，跳过邀请／拒绝判定。
- 接管时直接返回未受控期间的行动报告；移动历史只记录地图切换。
- 返回英文编号说明，并接受返回的目录对象、key、数值 ID 或调用模板作为参数。
- 意外断线后原地等待，可在配置的等待期内安全恢复控制，默认 60 秒。

正式集成通过接口调用、返回结果和事件完成。游戏内仅保留可选的开发调试面板和必要的对话控件，不增加状态弹窗或行动报告界面。

## 环境要求

- Windows 日版原版 Koikatu，或 Koikatsu Party。
- Koikatsu Party 需购买 After Party DLC，并安装官网补丁后才可使用本插件。
- BepInEx 5。
- 官方 [KKAPI / KK_ModdingAPI](https://github.com/IllusionMods/IllusionModdingAPI/releases/tag/v1.48.1) 1.48.1 或更新版本，以及其必需的 [BepisPlugins](https://github.com/IllusionMods/BepisPlugins/releases) 中的 ExtensibleSaveFormat。
- 使用可选客户端时需要 Python 3，无额外 Python 依赖。

KKAPI 为必需依赖，CheatTools 仍非必需。项目不包含游戏文件或第三方 DLL，详见[依赖说明](docs/DEPENDENCIES.md)。

2.0.0 引入 KKAPI 必需依赖和只读 H 场景报告；Bridge 协议仍为 2，原有命令保持兼容。详见 [2.0.0 说明](docs/releases/2.0.0.zh-CN.md)及 [H 场景字段文档](API.zh-CN.md#只读-h-场景报告)。

## 安装

请先安装官方 **KK** Modding API 安装包及其依赖，不要使用 KKS、AI 或 HS2 版。按官方压缩包保留目录结构，并确保 `BepInEx/plugins` 下只有一份 `KKAPI.dll`。BepInEx 根据硬依赖 `marco.kkapi >= 1.48.1` 先加载 KKAPI；缺失或版本过低时会跳过 Bridge。

从 [Releases](https://github.com/qiqi20020612/KK_StoryNPCBridge/releases/latest) 下载。ZIP 包含 Bridge DLL 的安装目录结构、MIT 许可证及依赖说明，可直接解压到游戏目录。`SHA256SUMS.txt` 提供 DLL 和 ZIP 的校验值。

将单个 DLL 直接放入游戏的插件目录：

```text
<Koikatu>/BepInEx/plugins/KK_StoryNPCBridge.dll
```

无需创建插件子目录。插件使用以下配置和数据路径：

- `BepInEx/config/KK_StoryNPCBridge.cfg`：配置和本地认证 token。
- `BepInEx/config/KK_StoryNPCBridge/profiles/<save-key>/`：每个故事存档槽位独立的桥接角色数据与行动日志，首次在原生故事保存时写入。

profile key 根据原生存档文件名生成。每个槽位首次创建时，公开的数值／计数为 `0`，状态标志为 `false`，关系为 `stranger`（`-1`，尚未认识）；后续加载保留该槽位的桥接角色进度。请将这些外置数据与故事存档一起备份。旧版未分存档的数据文件会保留，但不会被导入所有存档。

桥接数据跟随游戏故事存档保存。修改和行动报告先保留在内存中，仅在原生保存成功后写盘（包含原生自动保存）；另存为写入目标栏位。属性／表情修改、结束对话、时间变化、释放控制和退出本身均不独立保存。未进行原生保存就重新读档或退出，会丢弃未保存进度。接口提供 `profile.savePolicy='native_save_only'` 及保存完成／失败事件。

## 界面语言

插件 UI 默认使用英文。需要简体中文时，修改 cfg 并重启游戏：

```ini
[UI]
Language = zh-CN
```

设置 `Language = en` 可切回英文。两种模式下，API 说明和汇报文本均保持英文；本体原名、角色名及实际对话内容保留原文。

按 **左 Ctrl + 左 Shift + F10** 打开开发调试面板，面板默认关闭。F8 仍为游戏自身的快捷键。

## 快速开始

读取故事存档并进入自由漫游。在项目目录运行客户端，显式提供游戏目录：

```powershell
python -X utf8 tools/bridge_client.py --game-root "<Koikatu 游戏目录>"
```

客户端会从游戏 cfg 中读取端口和 token。在交互式命令行中可以执行：

```text
list
player
time
use 1
stats
stat relation=friend isStaff=true isDate=true favor=30
adjust demo-stats-1 favor=4 intimacy=-2 lewdness=4
summon
get
report
release
quit
```

`use 1` 接管新注入的桥接角色并打印完整交接报告。`stat` 绝对赋值，`adjust` 按增量调整；新修改使用新 ID，超时重试保留原 ID／增量，详见[增量修改属性](API.zh-CN.md#增量修改属性)。`summon` 会让她沿原生路线走到玩家附近，不会瞬移。任务执行期间需要保持连接；正常关闭 Python 客户端会立即释放控制权。意外断线或心跳丢失会进入重连等待期，可使用 `claim` 返回的私有凭据恢复，详见 API 文档。

应用程序也可以设置 `KOIKATU_GAME_ROOT`，或向 `BridgeClient` 传入 `game_root`：

```python
from tools.bridge_client import BridgeClient

with BridgeClient(game_root="<Koikatu 游戏目录>") as bridge:
    actor = bridge.request("status")["actors"][0]
    target = {"target": actor["id"], "generation": actor["generation"]}
    claimed = bridge.request("claim", **target)
    report = claimed["activityReport"]  # 本次回复已包含完整明细，无需二次查询。
    catalog = bridge.request("catalog")
    # 保持连接期间，可使用返回的目录项继续发起调用。
    bridge.request("release_all")
```

## 文档

- [API 参考](API.zh-CN.md)（[English](API.md)）：通信协议、命令、控制权、返回值、行动报告与对话事件。
- [编号参考](docs/REFERENCE.zh-CN.md)（[English](docs/REFERENCE.md)）：行动、地图、表情分组和 Animator 状态。
- [1.5.0 更新日志](docs/releases/1.5.0.zh-CN.md)（[English](docs/releases/1.5.0.md)）：相对于 1.4.0 的变更及已验证的动作支持范围。
- `catalog`：以游戏当前加载的模型、内容和状态为准的运行时目录。

文档默认展示英文，并通过语言链接提供对应的简体中文版。无论文档或 UI 使用哪种语言，API 返回的说明、命令名、字段名和编号标识均保持英文。

## 从源码构建

安装 `global.json` 指定的 .NET SDK，然后运行：

```powershell
dotnet restore KK_StoryNPCBridge.csproj --locked-mode
dotnet build KK_StoryNPCBridge.csproj --no-restore --configuration Release --output artifacts/build
dotnet run --project tests/KK_StoryNPCBridge.Tests.csproj --configuration Release
```

输出为 `artifacts/build/KK_StoryNPCBridge.dll`，目标框架为 .NET Framework 3.5。此方式使用 [IllusionLibs](https://github.com/IllusionMods/IllusionLibs) 提供的公开精简编译引用，无需本机游戏目录。依赖版本及内容哈希记录在 `packages.lock.json` 中，插件发布包不包含引用程序集。

`NuGet.Config` 提供 nuget.org、官方 IllusionMods 源（包含锁定的 `IllusionModdingAPI.KKAPI` 1.48.1 包），以及其传递构建依赖需要的 BepInEx 源。构建排除上游附带的分析器，维持现有编译器检查规则；仍启用 C# 3、net35 和警告视为错误。安装 ZIP 附带 `DEPENDENCIES.md`，不包含 KKAPI 或其他依赖 DLL。

生成发布 DLL、安装 ZIP 和 SHA-256 校验文件：

```powershell
pwsh -File tools/package-release.ps1 -Tag v2.0.0
```

报告测试使用真实生产模块和数据替身，验证生命周期去重、身份隔离及快照，不能代替游戏内验收。

GitHub Actions 会在代码推送和拉取请求时构建。推送 `v<版本号>` 标签后，会在核对标签与 DLL 版本一致后构建并发布 Release。

也可以使用 Windows .NET Framework 3.5 的 C# 编译器，引用本机游戏与 BepInEx 中的程序集：

```powershell
.\build.ps1 -GameRoot "<Koikatu 游戏目录>"
```

输出为 `bin/KK_StoryNPCBridge.dll`。需要复制到游戏时，先关闭游戏，再执行：

```powershell
.\build.ps1 -GameRoot "<Koikatu 游戏目录>" -Deploy
```

也可通过 `KOIKATU_GAME_ROOT` 提供游戏目录。构建产物和运行时配置不进入版本控制。

本机编译脚本会递归查找游戏 `BepInEx/plugins` 下的 `KKAPI.dll`，并检查程序集标识与版本。使用单独的官方引用 DLL 时可传入 `-KkapiPath "<KKAPI.dll 路径>"`。`-Deploy` 仅复制 Bridge DLL，KKAPI 需要单独安装。

[迁移评估（英文）](docs/KKAPI-MIGRATION.md) 记录了实际替换、保留的 Hook 和集成验证范围。

## 行为与限制

- 写入角色前需要接管，并提供当前 generation；重新读档后应刷新状态。
- 主动释放控制后，会在必要的对话、切场景清理结束后恢复原生 AI。意外断线时原地等待 `Bridge.ReconnectGraceSeconds`（默认 60 秒，范围 5–600），超时后才恢复原生 AI。恢复仅适用于同一运行中的故事／generation，不跨游戏重启。
- 目录项标识有效编号；行动点占用、目标和故事条件仍可能阻止行动执行。
- 表情取决于已加载的角色模型；进入或退出对话后应刷新动画目录，原生对话使用独立的 Animator。Animator 状态哈希与原生调度行动 ID 属于不同编号领域。
- 游戏原生提供星期，而没有绝对日历日期；插件为每个存档提供观测到的天数计数。原生 ADV 中 Emoji 等补充平面 Unicode 字符会显示为 `□`，以避免 Unity 文本错误；接口历史和事件仍保留原文。
- 行动报告保留最近 256 条明细，并提供截断计数；不记录同地图步行轨迹。
- 插件只上报结构化事件和展示外部提供的台词，不调用模型或构造提示词。

## 许可证

[MIT](LICENSE)。游戏与第三方依赖保留各自的许可证。
