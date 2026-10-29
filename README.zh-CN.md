# KK_StoryNPCBridge

[English](README.md) | 简体中文

这是一个适用于《恋活》（Koikatu）和 Koikatsu Party 的 BepInEx 5 插件，通过本地 API 读取和控制故事模式中的 NPC。外部应用可以提供手动控制，也可以接入 AI；模型调用、提示词、上下文组织和行为决策由外部应用负责。

## 当前支持范围

**目前插件会向游戏中注入一个新的桥接角色，且只有这个新角色可以被控制。** 她的角色 ID 为 `bridge:main`，初始外观使用游戏默认女性模型 `ill_Default_Female`。

当前不能选择或接管故事中已有的 NPC，也不控制玩家。未接管桥接角色时，她由游戏原生 AI 管理；接管后，她的行动由控制器决定，直到主动释放或控制连接断开。

## 后续 NPC 控制计划

未来将支持故事模式中的已有 NPC，并提供以下控制范围：

- 指定一个 NPC。
- 指定某几个 NPC。
- 控制全部 NPC。

这些是后续计划，不代表当前版本已经支持。

## 功能

- 读取和修改角色数值、表情、原生行动、动画和位置。
- 立即执行原生行动，或将接下来的任务加入队列。
- 使用原生导航走到玩家附近，支持跨地图路线。
- 使用游戏原生 ADV 对话，支持文字输入、等待外部响应和 NPC 主动邀聊。
- 上报原生对话选项，区分触碰与注视，并保留原有部位交互反应。
- 接管时直接返回未受控期间的行动报告；移动历史只记录地图切换。
- 返回英文编号说明，并接受返回的目录对象、key、数值 ID 或调用模板作为参数。

正式集成通过接口调用、返回结果和事件完成。游戏内仅保留可选的开发调试面板和必要的对话控件，不增加状态弹窗或行动报告界面。

## 环境要求

- Windows 日版原版 Koikatu，或 Koikatsu Party。
- Koikatsu Party 需购买 After Party DLC，并安装官网补丁后才可使用本插件。
- BepInEx 5。
- 使用可选客户端时需要 Python 3，无额外 Python 依赖。

不要求安装 KKAPI 或 CheatTools。项目不包含游戏文件或 BepInEx 二进制文件。

## 安装

从 [Releases](https://github.com/qiqi20020612/KK_StoryNPCBridge/releases/latest) 下载 `KK_StoryNPCBridge.dll`。ZIP 内包含相同 DLL 的安装目录结构和 MIT 许可证，可直接解压到游戏目录。`SHA256SUMS.txt` 提供 DLL 和 ZIP 的校验值。

将单个 DLL 直接放入游戏的插件目录：

```text
<Koikatu>/BepInEx/plugins/KK_StoryNPCBridge.dll
```

无需创建插件子目录。首次启动后，插件会生成：

- `BepInEx/config/KK_StoryNPCBridge.cfg`：配置和本地认证 token。
- `BepInEx/config/KK_StoryNPCBridge/`：桥接角色数据与行动日志。

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
use 1
summon
get
report
release
quit
```

`use 1` 接管新注入的桥接角色并打印完整交接报告。`summon` 会让她沿原生路线走到玩家附近，不会瞬移。任务执行期间需要保持控制连接；关闭控制器后，角色交回原生 AI。

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
- `catalog`：以游戏当前加载的模型、内容和状态为准的运行时目录。

文档默认展示英文，并通过语言链接提供对应的简体中文版。无论文档或 UI 使用哪种语言，API 返回的说明、命令名、字段名和编号标识均保持英文。

## 从源码构建

安装 `global.json` 指定的 .NET SDK，然后运行：

```powershell
dotnet restore KK_StoryNPCBridge.csproj --locked-mode
dotnet build KK_StoryNPCBridge.csproj --no-restore --configuration Release --output artifacts/build
```

输出为 `artifacts/build/KK_StoryNPCBridge.dll`，目标框架为 .NET Framework 3.5。此方式使用 [IllusionLibs](https://github.com/IllusionMods/IllusionLibs) 提供的公开精简编译引用，无需本机游戏目录。依赖版本及内容哈希记录在 `packages.lock.json` 中，插件发布包不包含引用程序集。

生成发布 DLL、安装 ZIP 和 SHA-256 校验文件：

```powershell
pwsh -File tools/package-release.ps1 -Tag v1.0.0
```

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

## 行为与限制

- 写入角色前需要接管，并提供当前 generation；重新读档后应刷新状态。
- 释放控制或控制连接断开后，会在必要的对话、切场景清理结束后恢复原生 AI。
- 目录项标识有效编号；行动点占用、目标和故事条件仍可能阻止行动执行。
- 表情取决于当前角色模型；Animator 状态哈希与原生行动 ID 不属于同一种编号。
- 行动报告保留最近 256 条明细，并提供截断计数；不记录同地图步行轨迹。
- 插件只上报结构化事件和展示外部提供的台词，不调用模型或构造提示词。

## 许可证

[MIT 英文原文](LICENSE) | [中文参考译文](LICENSE.zh-CN.md)。许可证以英文原文为准。游戏与第三方依赖保留各自的许可证。
