# KK_StoryNPCBridge API 1.3.0

[English](API.md) | 简体中文

## 传输与认证

通过 IPv4 TCP 本地回环连接传输 UTF-8 编码、以换行分隔的 JSON。默认端口为 `64321`；端口和认证 token 位于 `BepInEx/config/KK_StoryNPCBridge.cfg`，请勿公开 token。`tools/bridge_client.py` 中仅依赖 Python 标准库的客户端负责认证、请求 ID 和心跳。

```json
{"id":"1","op":"auth","token":"<local token>"}
{"id":"2","op":"status"}
```

成功回复格式为 `{ "id": "2", "ok": true, "result": ... }`。错误回复格式为 `{ "id": "2", "ok": false, "error": { "code": "...", "message": "English description" } }`。协议版本为 `2`，插件版本为 `1.3.0`。

认证和状态回复通过 `pluginName="KK_StoryNPCBridge"` 与 `pluginGuid="KK_StoryNPCBridge"` 标识插件。单个 DLL 直接安装为 `BepInEx/plugins/KK_StoryNPCBridge.dll`。

最多允许四个客户端同时连接；每条传入命令帧上限为 64 KiB，每帧最多处理八条 Unity 命令，原生任务队列最多保留 32 项。请每三秒发送一次 `ping`。连接丢失或约 12 秒未发送心跳后，会进入下述恢复等待期。尚未执行的排队命令在等待五秒后过期。快照订阅默认以 5 Hz 推送，可配置为 1 至 20 Hz。

API 中的描述、命令说明及插件生成的汇报文本均为英文。角色名称、外部提供的台词、原生菜单文字、`nativeName` 和原生资源标识来自调用方或游戏，会保留原文而不翻译。UI 语言不会改变 API 描述、ID 或 key。本页仅提供文档的中文说明。

## 角色控制权

只有 `bridge:main` 可以被写入。通过 `status`、`list` 或 `get` 获取当前 `generation`。写入角色前必须提供 `target`、`generation`，并由当前连接成功执行 `claim`。过期的 generation 以及其他连接在未持有控制权时发起的写入会被拒绝。故事已加载时，`claim`、`release`、`set_stats` 和 `adjust_stats` 不限制场景，也不要求角色模型已加载，可在切图期间使用。导航和调度写入要求漫游；表情和动画也支持桥接角色的原生对话。其他依赖场景的操作可能返回 `busy` 或 `not_ready`。

未被接管的角色使用原生 AI。`claim` 会取消自主任务，并冻结此次交接的行动报告。`release`、`release_all` 或正常 `disconnect` 会在场景／对话清理结束后恢复原生调度。意外断线会先原地等待恢复，超时才交回原生调度。`stop` 会保留控制权。不能根据 `nativeAI=true` 判断角色是否自主行动：它只表示存在原生执行器；应通过 `nativeAutonomy` 判断由谁选择行动。

### 意外断线与恢复

`claim` 和 `resume` 的回复包含私有 `resumeToken`、`world`、当前角色 generation 及完整交接报告。控制器应保留这些凭据；快照、事件和 profile 文件均不包含恢复 token。每条新连接仍须单独认证。

```python
claimed = bridge.request('claim', **p)
resume_token = claimed['resumeToken']
# 意外断线后，新建并认证 replacement BridgeClient。
recovered = replacement.request('resume', resumeToken=resume_token, **p)
resume_token = recovered['resumeToken']  # 已轮换，旧 token 失效。
```

连接／心跳丢失时，角色进入 `recovery.state='waiting_reconnect'`，取消导航／队列任务、原地等待，并保留当前会话与待回复请求 ID。其他连接无法抢占预留角色。cfg 中 `Bridge.ReconnectGraceSeconds` 默认为 `60`，限制为 `5–600` 秒。`recovery` 返回 `deadlineUtc`、`remainingSeconds`、`graceSeconds` 和 `interruptedTaskState`。恢复要求仍在同一运行中的故事、generation 有效、token 正确且等待期未过。成功后轮换 token，并将会话／请求超时顺延断线期间的时长。已中断的行动需要显式重新下令，不会自动重放。

等待期内，除结束对话外，ADV 交互保持禁用。原生对话不插入重连提示；连接状态可在可选的开发面板中查看。超时产生 `controller_recovery_expired`，以 `reconnect_timeout` 为原因释放控制，并在清理后恢复原生 AI。切换故事或重启游戏会使恢复凭据失效。

正常结束应在关闭 TCP 前发送 `release`、`release_all` 或 `disconnect`。`disconnect` 会先确认释放再关闭连接，无需 target。仅关闭 socket 无法表达主动结束的意图，会按意外断线处理。`BridgeClient.close()` 和上下文退出默认发送 `disconnect`；`close(graceful=False)` 表示连接意外丢失。客户端库不包含自动重连循环或模型调用。

### 按存档隔离的桥接状态

`actor.profile` 提供 `save`、SHA-256 `key`、`scope='story_save_slot'` 和 `initializedThisLoad`。key 根据原生存档文件名生成，不区分大小写。外置文件位于 `BepInEx/config/KK_StoryNPCBridge/profiles/<key>/`：`owned-actor.json`、`owned-native.bin`、`activity-journal.json`。每个槽位首次创建时，数值／计数为 `0`、公开状态标志为 `false`、`relation=-1`；后续加载保留该槽位进度。归零不会清除引擎内部身份、角色卡和调度初始化数据。原生“另存为”成功后，当前桥接进度会写入目标槽位。请将外置文件与原生存档一起备份。旧版未分存档的数据会保留，不会自动导入每个槽位。

## 目录与引用参数

`catalog` 返回 `actions`、`maps`、`expressions`、`animations`、`stats`、`time`、`nativeEvents`、`idDomains`、`ui`、`generation` 和 `descriptionLanguage="en"`，无需持有控制权。目录根据当前加载的游戏安装内容发现。表情和动画要求有活动模型，可查看 `visualAvailable` 和 `visualContext`（`roaming`／`native_talk`）。进入或退出原生对话后应刷新视觉目录，对话使用独立模型／Animator。

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
| 关系 | 原生派生关系，-1 至 2 | `set_stats.changes.relation` | 接受返回的关系对象、key 或 ID |
| 游戏时间 | 原生阶段、UI 时间段及星期 | `gameTime`、`time_catalog` | 仅观测；正式接口不提供修改玩家时钟的命令 |
| 原生活动事件 | 原生对话事件 ID | `trigger_native_event.event` | 直接执行效果；与调度行动 ID、`commandId=eventId+10` 分属不同编号 |

编号表见[中文编号参考](docs/REFERENCE.zh-CN.md)（[English](docs/REFERENCE.md)）。模型、已安装内容及当前可用性始终以运行时目录为准。

### 表情

`expression_catalog` 返回 `eyes`、`eyebrow`、`mouth` 数组，以及连续值／布尔值 `controls`。英文表情名称根据原生形变目标标签解释。`morphTargets` 记录实际网格及其张开／闭合混合形状名称。对于未知的自定义表情，会明确标注为未命名，而不会套用其他模型的表情含义。

```python
eyes = catalog['expressions']['eyes'][2]  # Smiling eyes on the default model
bridge.request('set_expression', changes={
    'eyes': eyes,
    'eyebrow': catalog['expressions']['eyebrow'][0],
    'mouth': catalog['expressions']['mouth'][1],
    'eyesOpen': 1.0, 'blink': False,
}, **p)
```

`eyesOpen` 为 `[0,1]` 范围内的数值；`blink` 为布尔值。嘴巴张开度由原生游戏控制：`expression.mouthOpen` 仅供只读上报，不列入可写 `controls`。在 `set_expression.changes` 中传入 `mouthOpen` 会返回 `unsupported_field`；混合提交中包含该字段时，整次修改在应用任何字段前被拒绝。旧配置中保存的张开度会被忽略。嘴型模式（`mouth`）仍可写；应用表情时插件不再写入嘴部张开上限或固定张开比例。`clear_expression` 会清除覆盖，将表情管理交回原生游戏。实时角色对象包含数值型 `expression`、带说明的 `expressionDetails`，以及表示模式数量的 `expressionLimits`。

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

### 原生 AI 规划观测

`native_plan` 只需 `target="bridge:main"`，无需控制权或 generation。返回 `live` 和 `beforeControl`；角色状态中的 `actor.nativePlanning` 包含同样的对象。每份快照提供 `generation`、`world`、`capturedUtc`、`gameTime`、`selection`、`available` 和 `unavailableReason`。原生调度器尚未就绪时，`available=false`，行动／候选数组为空。

首次成功 `claim` 的 `handoverReport.nativePlan`（也是 `activityReport.nativePlan`）直接包含**清理前**的原生 AI 规划快照，无需二次调用。随后取消原生已选行动，释放预留行动点和伙伴，清理优先选择记忆及插件任务。保留欲望值、角色属性和近期行动历史。漫游中立即进入受控待机，其他场景则在返回漫游时生效。同一控制者重复 `claim` 或认证后 `resume` 会保留原快照；释放后重新接管才生成新快照。`beforeControl` 为当前 generation 对应的最近接管快照，没有则为 `null`，不表示应自动重放被中断的行动。

原生 AI 每次只选择一个行动，因此 `futureQueueAvailable=false`，没有预先排好全部未来任务的 FIFO 队列。具体观测字段如下：

| 字段 | 含义 |
| --- | --- |
| `plannedActions` | 当前已选行动，通常为零或一项：`action`、`actionInfo`、`source`、`isPriority`、`destination`、`partner`、`plansNo`、`plansName`。受控期间描述插件所选的原生执行器状态。 |
| `reservedDestination` | 实际预留行动点，包含地图引用和坐标，未预留则为 `null`。这是当前预留快照，不是移动历史事件。 |
| `candidates` | 全部原生欲望条目，包含行动引用、`desire`、`isPriority`、原生 `priority`、`selectionKind`、`weight`、`priorityThresholdMet`、`eligibility` 及可执行 `request`／`queueRequest`。 |
| `recentActions` | 按最旧到最新排列的原生近期行动历史，**不是未来任务**。 |
| `recentPrioritySelections` | 用于抑制重复选择的原生优先行动记忆，**不是未来任务**。 |
| `deferredPriorityActions` | 按行动 ID 排序的原生延后优先标记，不代表执行顺序。 |

候选先按是否优先分组，再按欲望、原生 priority 降序，最后按 ID 排序。优先行动通常需要欲望至少 `100`；普通随机行动的当前 `weight` 为原生基础概率加欲望。`selectionKind` 为 `priority`、`weighted_random` 或 `special`，没有普通随机概率时 `weight=null`。此排序仅用于观测，不是预测的执行序列。`eligibility="not_evaluated"`：读取不会增长欲望、消耗随机数、调用行动选择或预留目标，也不保证地图／目标可用。时间、服装、排除行动、目标和行动点占用，以及后续欲望变化都可能改变原生选择。

```python
observed = bridge.request('native_plan', target='bridge:main')
claimed = bridge.request('claim', **p)
original_plan = claimed['handoverReport']['nativePlan']
candidate = next(x for x in original_plan['candidates'] if x['key'] == 'use_phone')
bridge.request(**candidate['request'])  # 显式选定；仍受原生执行条件限制。
```

generation 改变后需刷新返回的请求。行动 ID／key 沿用现有行动目录；`plansNo` 仍是只读的原生子计划值，不能作为行动编号调用。手动客户端命令 `plan` 在接管前后均可使用。

### 随机参考其他原生 NPC 的当前任务

`sample_native_action` 除 `op`（以及传输请求 ID）外无需参数。需要认证，但无需 target、generation 或控制权。每次调用均从当前故事中已初始化、有原生 AI 结果且行动 ID 非负的女孩中，按不同角色等概率独立抽取一位。始终排除桥接角色，即使她尚未受控；包含其他地图的 NPC，跳过没有当前原生任务的角色。连续调用可能抽到同一位。抽样使用独立随机源，不推进 AI、不改变原生队列／预留，也不消耗游戏的随机状态。仅显式调用时抽样，不随订阅快照自动刷新。

返回 `available`、`unavailableReason` 和 `task`。可用时，`task` 包含 `action`、带说明的 `actionInfo`、目标 `map`／`mapInfo`、`duration=0`，以及以桥接角色为目标的 `request`／`queueRequest`。不返回被抽中角色的 ID、姓名、伙伴、外观、属性或坐标。`plansNo`／`plansName` 为当前原生子计划，`arrival` 和 `elapsedSeconds` 为执行状态。行动和地图编号沿用现有目录；没有原生目标点时为 `map=-1`（`automatic`）。复用时使用桥接角色自己的原生时长，不复制来源角色已执行或剩余的时长；仍需控制权、当前 generation 和合适的原生条件。不会复制交互对象或伙伴，因此来源角色可执行的任务，桥接角色也可能无法执行。

```python
sample = bridge.request('sample_native_action')
if sample['available']:
    bridge.request('queue_action', task=sample['task'], **p)
    # 或使用 bridge.request(**sample['task']['request']) 立即执行。
```

不可用时 `task=null`，`unavailableReason` 为 `story_not_ready`、`scene_busy` 或 `no_eligible_native_npc`。原生对话准备就绪后仍可观测其他 NPC；导航和行动写入继续遵守现有场景限制。手动客户端命令为 `sample_action`，接管前也可调用。

### 动画

`animation_catalog` 列出从原生等待点、对话动作、控制器动画片段及常见状态中发现，并经活动 Animator 确认存在于第 0 层的状态。它不是 Studio 动画数据库，也不保证穷尽所有 Unity 状态。接口仍接受有效的原始状态字符串。每项包含 `context`、英文说明、有符号哈希及可执行的 `play_animation` 请求。与受控角色对话时，视觉命令作用于实际显示的对话模型。

```python
idle = bridge.request('animation_catalog', **p)[0]  # 在当前场景中发现。
bridge.request('play_animation', state=idle, **p)
bridge.request('play_animation', state=idle['id'], **p)
```

播放 `Reading` 不会自动寻找座位、添加书本道具或执行行动 `9`。需要原生游戏行为时，应使用调度行动。实时动画状态通过 `animationDetails` 描述；`animation.state`／哈希返回实际播放状态，`requestedState` 表示显式设置的覆盖状态。

## 原生属性与关系

`stat_catalog`（也包含于 `catalog.stats`）返回字段类型、写入范围、`adjustable`、英文含义、`relationships` 及 `adjustment` 策略。`actor.stats` 是实际原生值，`actor.relationship` 是附说明的关系引用。原生效果可能产生超出接口写入范围的值（例如负数生气值）；观测与桥接存档进度会保留这些实际值。`set_stats.changes` 在修改前验证全部字段，写入原生状态后重新读取，并返回完整角色。`stats_changed` 包含 `requested`、`before` 和 `actual`。两种属性操作都要求当前故事角色存在、调用方持有控制权和当前 generation，不限制漫游／ADV／地图／模型状态。

| 字段 | 类型／范围 | 含义 |
| --- | --- | --- |
| `favor`、`intimacy` | 整数 0–100 | 对玩家的好感度、亲密度 |
| `anger`、`lewdness` | 整数 0–100 | 原生生气程度、淫乱度 |
| `hCount` | 整数 0–2147483647 | 原生亲密行为次数 |
| `nickNameTalkCount` | 整数 0–2147483647 | 昵称对话次数 |
| `myRoomCount` | 整数 0–2147483647 | 玩家房间访问次数 |
| `eventAfterDay` | 整数 0–2147483647 | 原生事件后经过天数计数 |
| `hasMet`、`isFriend` | 布尔值 | 初次见面标记（talk events 0/1）、朋友标记（2） |
| `isGirlfriend`、`isStaff`、`isDate` | 布尔值 | 恋人、恋活部成员、已约定约会 |
| `isAnger`、`confessed`、`isFirstGirlfriend` | 布尔值 | 原生生气、告白及首位恋人状态标志 |
| `isTaked`、`isLunch`、`isTalkPoint`、`isKiss` | 布尔值 | 以这些名称独立保留的原生状态标志 |
| `isVirgin`、`isAnalVirgin` | 布尔值 | 原生阴道／肛门处女状态标志 |
| `relation` | 引用 | -1 `stranger`（尚未认识）、0 `acquaintance`（认识）、1 `friend`（朋友）、2 `girlfriend`（恋人） |

设置 `relation` 会一起更新见面、朋友和恋人标志；同时显式提供冲突标志会返回 `reference_mismatch`。也可独立修改各标志，返回的关系遵循游戏原生优先级。单独修改 `anger` 时会同时推导 `isAnger = anger > 0`；如需其他结果，请显式提供 `isAnger`。修改分数／标志不会运行对应剧情事件，也不保证当前能执行某种邀请。

```python
friend = next(x for x in bridge.request('stat_catalog')['relationships']
              if x['key'] == 'friend')
actual = bridge.request('set_stats', changes={
    'relation': friend, 'favor': 30, 'intimacy': 20,
    'isStaff': True, 'isDate': True,
}, **p)
assert actual['stats']['relation'] == 1
```

### 增量修改属性

`adjust_stats` 接受非空 `deltas` 对象和调用方生成的 `mutationId`。上表中仅八个整数分数／计数字段支持增量（`adjustable=true`）；关系和布尔标志仍通过 `set_stats` 绝对赋值。每个增量须为 -2147483648 至 2147483647 的整数；布尔值、字符串、小数和未知字段会在写入前被拒绝。可直接使用 `stat_catalog` 返回的字段 `key` 作为 `deltas` 的键。

操作先读取当前原生值，以 64 位整数累加，再将结果限制到该字段的写入范围：分数为 0–100，计数为 0–2147483647；即使起始观测值超出该范围，也按此处理最终值。复用 `set_stats` 的验证／规范化、原生同步、外置数据保存和整批失败回滚，包括原生 talk-event 标记。所有字段基于同一份快照计算，在 Unity 主线程同步提交。与 `set_stats` 一样，I/O 故障时回滚后的外置文件保存仍为尽力恢复。

`set_stats` 继续执行绝对赋值，越界值仍被拒绝。增量修改 `anger` 沿用 `isAnger = 实际 anger > 0` 的既有推导；不会新增情绪评分、修改关系规则，也不会在好感度满值后转加亲密度。

```python
# 每次逻辑修改只生成／保存一次 ID；重试时保持不变。
change = {'mutationId': 'chat-42-stats',
          'deltas': {'favor': 4, 'intimacy': -2, 'lewdness': 4}}
result = bridge.request('adjust_stats', **change, **p)
print(result['before']['favor'], result['actual']['favor'])
retry = bridge.request('adjust_stats', **change, **p)
assert retry['replayed'] is True  # 返回首次结果，不会再次累加。
```

返回增量报告，而非完整角色：

| 字段 | 含义 |
| --- | --- |
| `operation`、`mutationId`、`target`、`generation` | `adjust_stats`、逻辑修改标识及角色身份 |
| `requestedDeltas` | 已验证的调用方有符号增量 |
| `requested` | 交给共用写入流程的规范化绝对值，按需包含派生的 `isAnger` |
| `before`、`actual` | 此次提交紧邻前后的完整原生属性快照 |
| `appliedDeltas` | 各请求数值字段实际的 `actual - before`，已考虑范围限制 |
| `clampedFields` | 计算总值受合法范围限制的字段 key |
| `replayed` | 首次成功为 `false`，返回缓存结果时为 `true` |

例如起始值为 `favor=98`、`intimacy=1`、`lewdness=100`，上述增量得到 `100`、`0`、`100`；`appliedDeltas` 分别为 `2`、`-1`、`0`，三个 key 均出现在 `clampedFields` 中。这只是数值示例，不是情绪评分规则。首次成功发送一条 `stats_changed`，包含除 `replayed` 外的报告字段；重试不再发送修改事件。缓存快照描述首次操作，之后发生其他修改时，应通过 `get` 查询当前状态。

`mutationId` 是区分大小写、非空白且最长 128 字符的字符串，与 TCP 请求 `id` 不同，可使用 UUID。同一 ID 和规范化后相同的字段／增量会返回首次结果，不受字段顺序、后续写入、正常释放后重新接管或认证后 `resume` 影响。同一 ID 改用其他增量返回 `adjustment_conflict`。重放前仍检查控制权、当前故事和 generation；超时重试保留原 ID 与增量，重连后先恢复或接管控制。客户端不会自动生成替代 ID，也不会自动重试。

记录保留在当前角色 generation 的内存中，不跨故事重建／游戏重启；这些操作会产生新 generation 并拒绝旧命令。最多保留 4096 次开始执行的记录，不淘汰旧记录；达到上限时，新 ID 在写入前返回 `adjustment_limit`，旧 ID 仍可查询。输入验证失败不记入记录；开始执行后失败则记录为 `adjustment_failed`，同一 ID 只会返回相同错误，不再执行。应先用 `get` 检查状态并排除故障，再有意使用新 ID，防止回滚／保存结果不明确时自动重复累加。

## 玩家与原生游戏时间

`status.player` 提供 `name`、`lastName`、`firstName`、`nickname`，以及 `loaded`、`map`、`mapInfo` 和 `position`。玩家模型未加载时，仍可从已加载故事读取姓名；正式接口不控制玩家。

`status.gameTime` 包含 `available`、`stage`、`period`、`day`、`timerSeconds` 和英文说明。`time_catalog`（也包含于 `catalog.time`）解释所有阶段／时间段／星期编号。stage 是精确的原生周期阶段，period 是原生 UI 的粗粒度分组；完整编号见[编号参考](docs/REFERENCE.zh-CN.md)。

`day` 包含 `id`、`observedDayIndex`、`weekday`、`calendarDate=null`、`dateSource='native_weekday_with_observed_day_counter'`、`isDayEnding` 和 `isNewDay`。游戏只提供星期，没有绝对年月日。每槽位计数只累加插件运行期间观测到的原生日历推进，不代表历史游玩总天数。`day.id` 由当前 world 与该计数组成。`isNewDay` 标记原生日历推进／起床阶段，`isDayEnding` 标记进入下一天之前的夜晚阶段。

| 全局事件 | 触发条件／内容 |
| --- | --- |
| `game_time_initialized` | 故事就绪后建立基线；完整 `gameTime` |
| `game_stage_changed` | 精确的原生周期变化；`from`、`to`、`time`、`world` |
| `game_time_period_changed` | UI 时间段变化；`from`、`to`、`time`、`world` |
| `game_day_ending` | 进入原生夜晚；`day`、`reason='native_night_entered'`、`world` |
| `game_day_ended` | 原生日历实际推进；之前的 `day`、`nextWeekday`、`daysAdvanced`、`reason`、`world` |
| `game_day_started` | 紧接日终事件；新 `day`、`previousWeekday`、`daysAdvanced`、`world` |

这些事件的 `target=null`、`generation=null`。初次加载只建立基线，不宣告完成了一天；进入夜晚与真正推进日历是不同事件。`available=false`／`day=null` 表示尚未建立就绪故事的时间基线。

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
| `native_plan` | 只读的当前原生规划与接管前快照，无需控制权 |
| `sample_native_action` | 独立随机抽取其他原生女孩的当前任务，匿名返回；无需 target／generation／控制权 |
| `map_catalog` | 故事和事件地图引用；无需 target |
| `expression_catalog` | 当前角色的表情模式目录及控制参数 |
| `animation_catalog` | 已发现的可播放 Animator 状态 |
| `stat_catalog`／`time_catalog` | 原生属性／关系定义，或阶段、时间段和星期引用 |
| `claim` | 角色、完整 `activityReport`、相同的 `handoverReport`、私有 `resumeToken` 与 `world`；同一持有者重复接管是幂等的 |
| `resume` | `resumeToken`、当前 target／generation；恢复预留的控制权并轮换 token |
| `release` | 可选布尔值 `restorePosition`；恢复原生 AI，并返回角色／当前报告区间 |
| `release_all` | 释放当前连接持有的控制权；回复直接包含当前 `activityReport` |
| `disconnect` | 正常结束：释放控制、确认后关闭当前连接；无需 target |
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
| `set_stats` | `changes`：上表的字段／引用；在任意场景返回实际原生状态 |
| `adjust_stats` | 稳定的 `mutationId` 和整数 `deltas`；原子地累加／限制数值字段，返回前后实际值并防止重复应用 |
| `set_expression`／`clear_expression` | 设置上述表情引用和控制参数／清除覆盖 |
| `play_animation` | `state` 引用、原始状态 key 或有符号状态哈希 |
| `native_talk` | 开启原生对话场景；实际的原生对话／菜单仍可使用 |
| `native_event_catalog` | 原生事件效果、可用性及可直接调用的引用；无需 target |
| `trigger_native_event` | `event` 对象／key／ID；直接执行效果，返回执行状态、实际属性与跟随绑定 |
| `set_waiting_indicator` | 布尔值 `visible`；显示／关闭 ADV 等待提示，无需会话／请求 ID |
| `say_native` | `text`；在托管会话之外分页显示原生 ADV 回复，最多 1000 个 UTF-16 代码单元 |
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
| `nativePlan` | 接管时添加：控制清理之前的原生 AI 选择、预留目标和候选快照 |
| `entryCount`、`omittedEntries`、`counts` | 保留数量、被截断的数量及按类型统计的总数 |

移动记录类型为 `map_changed`，包含 `fromMap`、`toMap`、`fromMapInfo`、`toMapInfo`。同地图内的移动不会生成轨迹或到达记录。行动记录包含英文含义及保留的原生名称。触碰／注视事件中的位置表示交互命中点，不用于追踪移动。只保留最近 256 条记录；报告并不是无限期的历史档案。

## 对话状态与结构化事件

### 玩家输入与等待提示

与已接管的桥接角色处于就绪原生对话时，**输入文字对话**按钮可主动打开文字输入，即使尚未建立托管会话。打开输入会创建托管会话，但只有提交文字后才请求外部响应。现有 `dialogue_reply` 接口与输入界面使用同一提交流程。

控制方通过 `set_waiting_indicator` 显式控制提示：

```python
bridge.request('set_waiting_indicator', visible=True, **p)
# 外部连接器在插件之外处理请求。
bridge.request('set_waiting_indicator', visible=False, **p)
```

显示提示要求桥接角色的原生对话已经打开或正在进入。该操作不创建台词、模型请求或提示词，也无需 `conversationId`／`requestId`。`dialogue.ui` 返回 `playerInputAvailable`、`waitingIndicatorRequested`、`waitingIndicatorVisible`。提示显示期间，除**结束对话**外，原生 ADV 输入均被阻止。关闭提示不会解除待回复请求或重连等待造成的锁定。对话结束、释放控制或切换故事会清除提示；重连等待期内暂时隐藏，成功恢复后可重新出现。提示属于 UI，不会作为 `game_dialogue` 记录。

### 实际显示的原生台词记录

`game_dialogue` 只记录原生 ADV 文字命令，并等待其对话框实际显示。记录范围由原生会话参与者决定：包含桥接角色的会话保留已显示的上下文，包括桥接角色与玩家、玩家与桥接角色、桥接角色与其他 NPC 的台词及相关旁白。玩家与无关 NPC 的会话整场排除。纯语音触碰／注视反应和非 ADV 的文字更新不记录。桥接角色由原生 AI 管理时仍会记录，相关台词作为 `dialogue` 条目进入未受控行动报告。

| 字段 | 含义 |
| --- | --- |
| `speaker`、`text` | 原生显示姓名与实际显示的文字；长回复按显示页记录 |
| `speakerActor` | 说话者 `id`、`role`、`name`、身份来源，以及可解析时的原生班级／座位 |
| `conversationId` | 原生会话标识；同一聊天场景的不同 ADV 脚本共用，独立 ADV 按单次会话区分 |
| `dialogueConversationId` | 可选的托管流程 ID；不存在托管会话时为 null |
| `conversation` | 原生 `id`、`kind`（`native_talk`／`native_adv_event`）、实际参与者引用和可选的托管 ID |
| `source`、`nativeLine`、`gameTime` | `native_adv_text`、原生脚本行号及观测游戏时间 |
| `bundle`、`asset` | 可用时的原生脚本来源 |

说话者角色包括 `bridge`、`player`、`npc`、`narrator`、`unknown`。ID 为 `bridge:main`、`player:main`，其他 NPC 使用当前运行会话内的 `native:<world>:<roster-index>`。其他 NPC 引用仅用于观测，不授予控制权。原生角色参数与实际运行对象优先于姓名；字面姓名有重名歧义时，返回 `role='unknown'`、null `id` 和 `candidates`，不会猜测身份。`actor.nativeConversation` 保留最近记录的原生会话。这些观测记录与托管请求历史、完整回复事件相互独立。

### 托管对话流程

`phase` 包括 `closed`、`inviting`、`opening`、`npc_speaking`、`user_speaking`、`awaiting_input`、`waiting_reply`、`reply_error`、`ready`、`native_reaction`、`native_event` 和 `ending`。`native_event` 表示从托管会话转入活动的过渡阶段。`waitingReply`、`mouseBlocked`、`requestId`、`requestKind`、`conversationId`、`turn` 及 `errorCode` 说明当前响应流程。返回值也提供 `state=waiting_model/model_error` 形式的名称；新客户端可使用 `phase` 判断响应状态。

`dialogue_request` 包含标识、请求类型，以及原生菜单命令或用户输入。原生按钮选择的 `userText=null`；桥接插件不会虚构玩家说出的话，也不会为外部应用打包提示词或历史上下文。原生菜单文字保留源内容。`commandId`、`category` 和可选 `eventId` 保留原生编号；已知邀请示例包括学习的 `eventId=8` 和跟随的 `eventId=11`。这些表示所观察到的 UI 意图，不是调度行动 ID。

触碰／注视事件包含 `interactionId`、`kind`、`nativeMode`、`region`、语音／姿势数据和生命周期。`nativeMode=0` 表示触碰，`1` 表示注视。原生语音、动画和计数照常执行。反应结束后，外部应用可以显式调用带 `afterInteractionId` 的 `say` 或 `dialogue_open`；插件不会自动创建对话请求。

外部提供的回复通过原生 ADV 分页显示，每页额度等价于 **60 个汉字或 100 个 ASCII 字符**。以 ASCII 开头的 Unicode 文本元素占 3 个宽度单位，其余文本元素占 5 个；因此英文字母、数字、半角空格和半角标点占 3 个，汉字、全角标点及其他非 ASCII 文本元素占 5 个。中英混排共用每页 300 单位的额度，例如 30 个汉字加 50 个英文字母。若加入下一个文本元素会超出额度，则将整个元素放到下一页，分页步骤不拆开组合字符序列或代理对。换行也会结束当前页。托管回复最多接受 4000 个 UTF-16 代码单元。无论分成几页，历史和 `npc_dialogue` 中只记录一次完整原文。

`dialogue.pagination` 返回 `page`、`pageCount`、`maxCharacters=100`（文本元素数量的绝对上限）、`maxCjkCharacters=60`、`maxWidthUnits=300`、`countUnit='weighted_text_element'`、`asciiWidthUnits=3`、`nonAsciiWidthUnits=5` 和 `nonBmpFallbacks`。每个 `dialogue_page` 事件包含相同的限额／计数元数据，以及 `conversationId`、`turn`、从 1 开始的 `page`、`pageCount`、原文页 `text` 和实际 `displayText`。这些是单页显示限额，不会缩减整条回复允许的最大长度。原生自动换行对异常长、没有空格的 ASCII 连续字符串仍可能横向溢出。

Unity 5 的原生打字机无法安全测量补充平面 Unicode 字符（如 Emoji），仅在原生显示时将每个此类字符替换为 `□`，接口历史和分页原文仍完整保留。`nonBmpFallbacks` 返回整条回复的替换次数。无效的未配对 UTF-16 代理项会在显示前被拒绝。

其他事件包括控制权接管／释放、任务排队／开始／到达／完成／失败／取消、对话取消，以及原生邀请接受／取消。事件和快照包含序号。重连后应刷新 generation 和请求标识；不要盲目重放过期的写入命令。

## 直接执行原生活动效果

`native_event_catalog` 和 `catalog.nativeEvents` 返回支持的效果。`trigger_native_event` 接受返回的目录对象、key 或数值 ID，要求控制权与当前 generation，无需 `conversationId`、`requestId`、预先聊天或邀请／接受判定。目标须在当前地图加载，处于漫游或她自己的原生聊天场景。调用前应刷新 `available`、`unavailableReason`。目录项包含 `supported=true`、英文 `description`、原生 `commandId`、`result`、可执行 `request`、`executionKind`（`state_effect`／`native_scene_flow`）和结构化 `requirements`。

```python
event = next(x for x in bridge.request('native_event_catalog') if x['key'] == 'follow_player')
execution = bridge.request('trigger_native_event', event=event, **p)
# 活动通过 actor.nativeEvent 或 native_event_* 事件跟踪。
```

事件列表包含以下效果。“朋友以上”指原生关系为 `friend`（`1`）或 `girlfriend`（`2`），不是好感度数值门槛。

| 事件 ID／key | 关系或部员限制 | 效果 |
| --- | --- | --- |
| `1 / confess` | 朋友或恋人 | 建立原生恋人关系与标记，更新首次恋人排行及玩家计数；已是恋人时幂等，不重复累加。 |
| `2 / break_up` | 仅恋人 | 解除恋人和约会约定，清除此角色的原生日程登记，应用原生分手状态（`anger=100`、`isAnger=true`、`talkTime=0`），进入原生分手／离开流程。 |
| `3 / invite_h` | 朋友或恋人 | 在满足原生地点条件时进入 H 流程；执行前在原生宿主中再次检查地点。 |
| `4 / eat_lunch` | 朋友或恋人 | 进入原生午餐；另需午餐阶段 `5`、`isLunch=false`。 |
| `5 / club_activity` | 仅部员 | 在社团阶段 `8` 进入原生社团活动。 |
| `7 / invite_date` | 朋友或恋人 | 设置约会约定，并登记原生日程／存档中的角色身份；已有其他角色的约会时拒绝覆盖。 |
| `8 / study_together` | 无关系／部员限制 | 在自由阶段 `5`、`8`、`9` 进入原生学习。 |
| `9 / exercise_together` | 无关系／部员限制 | 在自由阶段 `5`、`8`、`9` 进入原生运动。 |
| `10 / invite_club` | 仅非部员 | 设置原生部员和事件标记，更新成员排行和累加式社团报告。 |
| `11 / follow_player` | 无关系／部员限制 | 建立原生跟随关系。 |

原有 `6 / go_home_together` 继续在放学阶段 `9` 可用。准确命令 ID、原生结果编号见[编号参考](docs/REFERENCE.zh-CN.md#原生活动效果)。`requirements` 提供 `minimumRelation`／`exactRelation` 引用（无要求时为 `null`）、`clubMember`（`true`／`false`／不限的 `null`）、允许的 `stages`（空数组表示无额外阶段限制）、`sameMap`、`nativeHLocation`、`unusedLunch`、`unoccupiedDateSlot`。这些是执行条件，不是二次接受判定。漫游中调用 H 时，地点检查可能在内部准备的原生宿主中完成。不可用原因包括 `requires_friend_or_girlfriend`、`requires_girlfriend`、`requires_club_membership`、`already_club_member`、`another_date_scheduled`、`requires_native_h_location`，以及现有的场景／时间／午餐原因。尚未应用的场景效果在实际派发前会再次检查条件。

告白、约会登记和入部属于立即执行的 `state_effect`：不打开聊天场景，不消耗邀请轮次，不取消待处理的外部回复，也不中断导航／对话；直接返回完成状态及实际结果。仍可使用 `set_stats` 写入状态，但单独修改关系／约会／部员标志，不会执行对应事件的日程、排行和社团报告效果。

跟随直接建立游戏原生的玩家跟随关系；`stop` 或新任务会清除此关系。正常释放控制会将正在进行的跟随交给游戏管理，重新接管会连同自主行为一起取消。分手、H 和活动效果进入原生场景流程，按相应事件保留原生活动 ADV、属性变化、地图与时间推进。内部准备宿主时跳过普通问候和邀请菜单。场景流程以 `native_event` 原因取消已有托管回复请求，不会请求模型回复。请先完成原生选项或触碰／注视反应。学习、运动、跟随和原有一起回家功能不额外增加关系门槛。

返回值及 `actor.nativeEvent` 包含 `executionId`、事件引用、`state`（`opening`、`preparing`、`running`、`completed`、`failed`、`cancelled`）、`error`、`effectApplied`、数值 `nativeResult`、实际 `followingPlayer`、`generation`、`world`、当前 `stats`。`before` 和 `actual` 分别上报派发前和应用／完成时的原生效果状态：`stats`、带说明的 `relationship`、此角色的 `dateScheduled`，以及原生 `girlfriendCount`、`staffCount`、`playerGirlfriendedCount`、`clubReportStaffAdd`。应用或终止前 `actual=null`；`stats` 始终为实时读回值。原生结果 `-1 / continue_talk` 表示状态效果没有终结场景的结果，与调度行动的 `-1` 无关。

生命周期事件为 `native_event_requested`、`native_event_started`、`native_event_completed`、`native_event_failed`、`native_event_cancelled`。完成表示状态效果已应用，或派发的原生流程已结束；跟随仍持续到停止或交还游戏。场景流程期间其他动作／对话写入返回 `native_event_busy`；`set_stats`、`adjust_stats`、观测、释放控制仍可使用。效果应用前可取消派发；应用后允许原生流程完成，不回滚，也不在重连后自动重放。

## 开发辅助功能与限制


`Development.AllowGameCommands` 控制 `game.*` 辅助命令是否可用，包括故事加载、地图切换、故事推进／继续、截图、存档导出探针、原生菜单／指针探针及原生部位交互探针。`game.map` 接受返回的地图对象／key／ID，但它改变的是玩家地图，属于开发工具，不是 NPC 步行接口。

错误示例包括 `unauthorized`、`invalid_request`、`invalid_argument`、`not_plugin_actor`、`not_owner`、`owned`、`generation_required`、`stale_generation`、`not_loaded`、`not_ready`、`unknown_reference`、`reference_mismatch`、`unknown_animation`、`unknown_map`、`action_unavailable`、`native_interaction_busy`、`stale_interaction`、`adjustment_conflict`、`adjustment_limit`、`adjustment_failed`，以及对话请求／上下文错误。重试写入前，请先检查错误码和当前状态。
