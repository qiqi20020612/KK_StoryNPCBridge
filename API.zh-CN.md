# KK_StoryNPCBridge API 1.0.0

[English](API.md) | 简体中文

## 传输与认证

通过 IPv4 TCP 本地回环连接传输 UTF-8 编码、以换行分隔的 JSON。默认端口为 `64321`；端口和认证 token 位于 `BepInEx/config/KK_StoryNPCBridge.cfg`，请勿公开 token。`tools/bridge_client.py` 中仅依赖 Python 标准库的客户端负责认证、请求 ID 和心跳。

```json
{"id":"1","op":"auth","token":"<local token>"}
{"id":"2","op":"status"}
```

成功回复格式为 `{ "id": "2", "ok": true, "result": ... }`。错误回复格式为 `{ "id": "2", "ok": false, "error": { "code": "...", "message": "English description" } }`。协议版本为 `2`，插件版本为 `1.0.0`。

认证和状态回复通过 `pluginName="KK_StoryNPCBridge"` 与 `pluginGuid="KK_StoryNPCBridge"` 标识插件。单个 DLL 直接安装为 `BepInEx/plugins/KK_StoryNPCBridge.dll`。

最多允许四个客户端同时连接；每条传入命令帧上限为 64 KiB，每帧最多处理八条 Unity 命令，原生任务队列最多保留 32 项。请每三秒发送一次 `ping`。控制器断开连接或约 12 秒未发送心跳后，控制权会被释放。尚未执行的排队命令在等待五秒后过期。快照订阅默认以 5 Hz 推送，可配置为 1 至 20 Hz。

API 中的描述、命令说明及插件生成的汇报文本均为英文。角色名称、外部提供的台词、原生菜单文字、`nativeName` 和原生资源标识来自调用方或游戏，会保留原文而不翻译。UI 语言不会改变 API 描述、ID 或 key。本页仅提供文档的中文说明。

## 角色控制权

只有 `bridge:main` 可以被写入。通过 `status`、`list` 或 `get` 获取当前 `generation`。写入角色前必须提供 `target`、`generation`，并由当前连接成功执行 `claim`。过期的 generation 以及其他连接在未持有控制权时发起的写入会被拒绝。多数写入需要处于自由漫游状态；对话操作也支持桥接角色的原生会话。加载场景时可能返回 `busy` 或 `not_ready`。

未被接管的角色使用原生 AI。`claim` 会取消自主任务，并冻结此次交接的行动报告。`release`、`release_all` 或控制器连接丢失后，会在场景／对话清理结束后恢复原生调度。`stop` 会保留控制权。不能根据 `nativeAI=true` 判断角色是否自主行动：它只表示存在原生执行器；应通过 `nativeAutonomy` 判断由谁选择行动。

## 目录与引用参数

`catalog` 返回 `actions`、`maps`、`expressions`、`animations`、`idDomains`、`ui`、`generation` 和 `descriptionLanguage="en"`，无需持有控制权。目录根据当前加载的游戏安装内容发现。表情和动画目录要求角色已加载；若对应目录为空或不可用，应先让角色走到玩家附近。

每个离散目录项都包含整数 `id`、稳定的 `key`、英文 `name` 和英文 `description`，并按需提供原生来源名称或准确的形变目标。带有 `request`／`queueRequest` 的目录项包含当前角色 generation 的命令参数；接管角色后，可直接传给 `BridgeClient.request`。客户端会补充传输层请求 ID；直接使用 TCP 的调用方必须自行添加 `id`。这些请求不会自动接管角色。重新读取故事存档或加载其他角色模型后，应刷新目录。

在目录项存在时，以下写法等价：

```python
phone = next(x for x in catalog['actions'] if x['key'] == 'use_phone')
bridge.request('set_action', action=10, **p)
bridge.request('set_action', action='use_phone', **p)
bridge.request('set_action', action=phone, **p)
bridge.request(**phone['request'])
bridge.request(**phone['queueRequest'])
```

引用对象可以包含 `id`、`key`，或同时包含两者；同时提供时，必须指向同一个目录项。显示名称只用于说明，不能作为选择参数。未知 key、不匹配的引用，以及用于错误字段的表情目录项，都会在修改相应状态前被拒绝。

| 编号领域 | 含义 | 参数位置 | 需要区分的概念 |
| --- | --- | --- | --- |
| 调度行动 | 原生行动选择 ID，例如 `10 = Use phone`（使用手机） | `set_action.action`、`queue_action.action`、`set_desire.action` | 队列任务也使用同一组行动 ID；它不是动画哈希 |
| 表情模式 | 当前角色眼睛、眉毛或嘴部的 FBS 索引 | `set_expression.changes.<field>` | 每个面部字段都有独立的编号空间 |
| Animator 状态 | 第 0 层的有符号 `Animator.StringToHash` ID，或对应状态 key | `play_animation.state` | 仅控制视觉播放，不产生调度器行为 |
| 地图 | 原生故事／事件地图 ID | 任务的 `map`；开发命令的 `game.map.map` | `-1`／`automatic` 只可用于任务自动选图 |
| 原生计划 | 只读的原生子计划值 | `native.plansNo`／`plansName` | 仅用于观测，不是另一组可调用的任务 ID |

编号表见[中文编号参考](docs/REFERENCE.zh-CN.md)（[English](docs/REFERENCE.md)）。模型、已安装内容及当前可用性始终以运行时目录为准。

### 表情

`expression_catalog` 返回 `eyes`、`eyebrow`、`mouth` 数组，以及连续值／布尔值 `controls`。英文表情名称根据原生形变目标标签解释。`morphTargets` 记录实际网格及其张开／闭合混合形状名称。对于未知的自定义表情，会明确标注为未命名，而不会套用其他模型的表情含义。

```python
eyes = catalog['expressions']['eyes'][2]  # Smiling eyes on the default model
bridge.request('set_expression', changes={
    'eyes': eyes,
    'eyebrow': catalog['expressions']['eyebrow'][0],
    'mouth': catalog['expressions']['mouth'][1],
    'eyesOpen': 1.0, 'mouthOpen': 0.3, 'blink': False,
}, **p)
```

`eyesOpen` 和 `mouthOpen` 为 `[0,1]` 范围内的数值；`blink` 为布尔值。`clear_expression` 会清除覆盖，将表情管理交回原生游戏。实时角色对象包含数值型 `expression`、带说明的 `expressionDetails`，以及表示模式数量的 `expressionLimits`。

### 原生任务与地图

`native_actions` 包含名称、原生层、欲求值、`mapsWithPoints`、带说明的 `mapDetails` 及可执行请求模板。`map_catalog` 包含地图名称和 `canSchedule`。仅供事件使用的地图可能被列出，但不能用于自由漫游任务。自动选图的 `id=-1`、`key="automatic"`。

```python
destination = next(x for x in catalog['maps'] if x['key'] == 'map/34')
result = bridge.request('set_action', action=phone, map=destination,
                        duration=30, **p)
queued = bridge.request('queue_action', action=phone, map=destination,
                        duration=0, **p)['queue'][-1]
bridge.request('clear_queue', **p)
bridge.request(**queued['request'])       # new queue task, new taskId
bridge.request('set_action', task=queued, **p)  # replay returned task directly
```

队列项包含 `taskId`、`action`、`actionInfo`、`map`、`mapInfo`、`duration` 和可再次调用的 `request`。`set_action` 与 `queue_action` 接受通过 `task` 参数传入的已返回队列项；存在 `task` 时，使用其中的行动／地图／时长字段。再次调用会创建新任务，而不会复用原任务的标识。`duration` 为 `0` 或省略时使用原生时长；正值表示到达后执行的秒数，上限为 3600。即使 ID 有效，游戏条件和行动点占用仍可能导致 `action_unavailable`。

### 动画

`animation_catalog` 列出从原生等待点发现的状态，以及经当前 Animator 确认存在于第 0 层的常见状态。它不是 Studio 动画数据库，也不保证穷尽所有 Unity 状态。接口仍接受有效的原始状态字符串。每个返回项都会说明该状态，并提供有符号哈希和可执行的 `play_animation` 请求。

```python
idle = next(x for x in catalog['animations'] if x['key'] == 'Idle')
bridge.request('play_animation', state=idle, **p)
bridge.request('play_animation', state=idle['id'], **p)
```

播放 `Reading` 不会自动寻找座位、添加书本道具或执行行动 `9`。需要原生游戏行为时，应使用调度行动。实时动画状态通过 `animationDetails` 描述。

## 命令

除非另有说明，角色命令需要 `target`；写入命令还需要 `generation`，并要求当前连接持有控制权。

| 命令 | 参数／返回结果 |
| --- | --- |
| `auth` | `token`；认证当前连接 |
| `ping` | 心跳；返回 `pong=true` |
| `subscribe` | 为当前连接启用快照推送；关闭连接即可停止 |
| `status`／`list` | 世界、玩家、一个桥接角色、事件、英文说明语言及所配置的 UI 语言 |
| `get` | 角色对象，包含完整 `activityReport` 和当前编号的说明 |
| `catalog` | 所有运行时编号目录；无需 target 或控制权 |
| `native_actions` | 角色的完整原生行动目录 |
| `map_catalog` | 故事和事件地图引用；无需 target |
| `expression_catalog` | 当前角色的表情模式目录及控制参数 |
| `animation_catalog` | 已发现的可播放 Animator 状态 |
| `claim` | 角色对象、完整 `activityReport` 及内容相同的 `handoverReport`；同一持有者重复接管是幂等的 |
| `release` | 可选布尔值 `restorePosition`；恢复原生 AI，并返回角色／当前报告区间 |
| `release_all` | 释放当前连接持有的控制权；回复直接包含当前 `activityReport` |
| `activity_report` | 可选的已保留 `reportId`；普通调用已经包含完整明细 |
| `walk_to_player`／`summon` | 原生步行请求；通过 `approach` 观察完成情况 |
| `set_action` | `action`，可选 `map`、`duration`；或传入返回过的 `task` |
| `queue_action` | 与上述任务字段相同；追加到队列 |
| `clear_queue` | 移除等待中的任务；返回原生任务状态 |
| `set_desire` | `action` 引用及 0 至 100 的整数 `value`；仅支持原生可修改的欲求 |
| `move` | 当前已加载地图 NavMesh 上的世界坐标 `position=[x,y,z]` |
| `teleport` | 显式位置操作；使用相同的 `position` 格式 |
| `turn` | `yaw`，范围为 -3600 至 3600 度 |
| `stop` | 取消当前任务／移动，并保留控制权 |
| `set_stats` | `changes`：范围为 `[0,100]` 的整数 `favor`、`intimacy`、`anger`，以及布尔值 `isAnger` |
| `set_expression`／`clear_expression` | 设置上述表情引用和控制参数／清除覆盖 |
| `play_animation` | `state` 引用、原始状态 key 或有符号状态哈希 |
| `native_talk` | 开启原生对话场景；实际的原生对话／菜单仍可使用 |
| `say_native` | `text`；在插件管理的对话流程之外显示一句原生 ADV 台词 |
| `dialogue_open` | 可选 `text`、`initiator='player'/'npc'`、`requestInput`、`timeoutSeconds`、`afterInteractionId` |
| `say` | `text`，可选 `requestInput`、`afterInteractionId`；开启／继续插件管理的对话流程 |
| `dialogue_status` | 完整会话状态、请求 ID 和已显示的历史 |
| `dialogue_reply` | 用户文本：`conversationId`、`turn`、`text`；无需接管控制权 |
| `dialogue_request_input` | `conversationId`；就绪时请求用户输入文本 |
| `dialogue_wait` | `conversationId`；就绪时请求外部响应 |
| `dialogue_respond` | `conversationId`、`requestId`、`text`；提交外部响应 |
| `dialogue_fail` | `conversationId`、`requestId`、`error`；将外部响应请求标记为失败 |
| `dialogue_retry` | `conversationId`；使用新的请求 ID 重试失败／超时的请求 |
| `dialogue_close` | `conversationId`；结束插件管理的对话流程并解除 ADV 输入拦截 |
| `native_interaction_status` | 当前／最近一次触碰或注视的生命周期及原生反应结果 |
| `panel_open`／`panel_close` | 仅供开发：显式打开／关闭调试面板 |

`model_reply`、`model_error` 和 `dialogue_wait_model` 分别是 `dialogue_respond`、`dialogue_fail` 和 `dialogue_wait` 的别名。模型 SDK、提示词构造和外部服务均不属于本项目。`spawn`／`despawn`／`respawn` 会被拒绝，因为角色生命周期由原生故事加载流程管理。

## 行动报告返回值

以角色为值的回复以及快照中的角色对象都包含 `activityReport`。未受控期间，它表示当前仍在累积的区间；接管后，它是该区间的冻结报告。`claim.handoverReport` 是内容相同的完整报告。`entries` 直接随回复返回，无需二次查询。

| 字段 | 含义 |
| --- | --- |
| `reportId`、`generation`、`world` | 报告片段标识及来源世界 |
| `reason` | 恢复原生控制的原因，例如 `released` 或 `controller_disconnected` |
| `startedUtc`、`endedUtc` | ISO 8601 格式的 UTC 时间；尚未结束的区间中 `endedUtc` 为 null |
| `durationSeconds` | 已观察到的区间时长 |
| `startState`、`endState` | 原生行动／地图引用、目标、数值与会话状态；不包含移动坐标 |
| `entries` | 有序记录，字段为 `seq`、`utc`、`kind`、`data` |
| `entryCount`、`omittedEntries`、`counts` | 保留数量、被截断的数量及按类型统计的总数 |

移动记录类型为 `map_changed`，包含 `fromMap`、`toMap`、`fromMapInfo`、`toMapInfo`。同地图内的移动不会生成轨迹或到达记录。行动记录包含英文含义及保留的原生名称。触碰／注视事件中的位置表示交互命中点，不用于追踪移动。只保留最近 256 条记录；报告并不是无限期的历史档案。

## 对话状态与结构化事件

`phase` 包括 `closed`、`inviting`、`opening`、`npc_speaking`、`awaiting_input`、`waiting_reply`、`reply_error`、`ready`、`native_reaction` 和 `ending`。`waitingReply`、`mouseBlocked`、`requestId`、`requestKind`、`conversationId`、`turn` 及 `errorCode` 说明当前响应流程。返回值也提供 `state=waiting_model/model_error` 形式的名称；新客户端可使用 `phase` 判断响应状态。

`dialogue_request` 包含标识、请求类型，以及原生菜单命令或用户输入。原生按钮选择的 `userText=null`；桥接插件不会虚构玩家说出的话，也不会为外部应用打包提示词或历史上下文。原生菜单文字保留源内容。`commandId`、`category` 和可选 `eventId` 保留原生编号；已知邀请示例包括学习的 `eventId=8` 和跟随的 `eventId=11`。这些表示所观察到的 UI 意图，不是调度行动 ID。

触碰／注视事件包含 `interactionId`、`kind`、`nativeMode`、`region`、语音／姿势数据和生命周期。`nativeMode=0` 表示触碰，`1` 表示注视。原生语音、动画和计数照常执行。反应结束后，外部应用可以显式调用带 `afterInteractionId` 的 `say` 或 `dialogue_open`；插件不会自动创建对话请求。

其他事件包括控制权接管／释放、任务排队／开始／到达／完成／失败／取消、对话取消，以及原生邀请接受／取消。事件和快照包含序号。重连后应刷新 generation 和请求标识；不要盲目重放过期的写入命令。

## 开发辅助功能与限制

`Development.AllowGameCommands` 控制 `game.*` 辅助命令是否可用，包括故事加载、地图切换、故事推进／继续、截图、存档导出探针、原生菜单／指针探针及原生部位交互探针。`game.map` 接受返回的地图对象／key／ID，但它改变的是玩家地图，属于开发工具，不是 NPC 步行接口。

错误示例包括 `unauthorized`、`invalid_request`、`invalid_argument`、`not_plugin_actor`、`not_owner`、`owned`、`generation_required`、`stale_generation`、`not_loaded`、`not_ready`、`unknown_reference`、`reference_mismatch`、`unknown_animation`、`unknown_map`、`action_unavailable`、`native_interaction_busy`、`stale_interaction`，以及对话请求／上下文错误。重试写入前，请先检查错误码和当前状态。
