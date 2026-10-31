# 运行时编号参考

[English](REFERENCE.md) | 简体中文

以下编号映射基于日版原版 Koikatu 与默认女性模型（`ill_Default_Female`）。可用 ID、模型特有表情及游戏当前条件以实时 `catalog` 为准。原生形变标识是源数据；英文表情标签用于解释这些标识，本页提供对应的中文说明。接口返回的名称和描述仍为英文。

[API 命令与引用参数规则](../API.zh-CN.md)（[English](../API.md)）。可调用的引用接受对应参数领域中的返回对象、`key` 或数值 `id`；游戏时间引用仅供读取。队列任务使用相同的行动与地图编号；任务 ID 本身则是每次执行的唯一标识。

## 关系

用于 `set_stats.changes.relation`。关系引用会一并更新见面、朋友和恋人标志；原生 getter 根据这些标志派生关系。字段类型、范围和所有可写状态见[原生属性与关系](../API.zh-CN.md#原生属性与关系)。

`adjust_stats.deltas` 接受 `stat_catalog.fields[].adjustable=true` 的数值字段 key，按返回的 `min`／`max` 限制最终值。关系引用和布尔标志仍仅支持绝对赋值；重试标识与返回字段见[增量修改属性](../API.zh-CN.md#增量修改属性)。

| ID | Key | 中文说明 |
| --- | --- | --- |
| -1 | `stranger` | 尚未认识 |
| 0 | `acquaintance` | 认识 |
| 1 | `friend` | 朋友 |
| 2 | `girlfriend` | 恋人 |

## 游戏时间

这些编号用于解释 `gameTime` 观测结果，不是调度行动编号或修改时钟的命令。游戏提供星期，没有绝对日历日期。计数与事件见[玩家与原生游戏时间](../API.zh-CN.md#玩家与原生游戏时间)。

### 原生阶段

| ID | Key | 中文说明 |
| --- | --- | --- |
| 0 | `wake_up` | 起床 |
| 1 | `morning` | 早晨 |
| 2 | `going_to_school` | 上学 |
| 3 | `homeroom_1` | 上午班会 |
| 4 | `lesson_1` | 上午课程 |
| 5 | `lunch_time` | 午休 |
| 6 | `lesson_2` | 下午课程 |
| 7 | `homeroom_2` | 下午班会 |
| 8 | `staff_time` | 社团时间 |
| 9 | `after_school` | 放学后 |
| 10 | `going_home` | 回家 |
| 11 | `my_house` | 在家／夜晚 |

### UI 时间段

阶段 0–4 对应时间段 0，5–7 对应 1，8 对应 2，9–10 对应 3，11 对应 4。

| ID | Key | 中文说明 |
| --- | --- | --- |
| 0 | `morning` | 上午 |
| 1 | `midday` | 午休／下午课程 |
| 2 | `staff_time` | 社团时间 |
| 3 | `after_school` | 放学后 |
| 4 | `night` | 夜晚 |

### 星期

| ID | Key | 中文说明 |
| --- | --- | --- |
| 0 | `monday` | 星期一 |
| 1 | `tuesday` | 星期二 |
| 2 | `wednesday` | 星期三 |
| 3 | `thursday` | 星期四 |
| 4 | `friday` | 星期五 |
| 5 | `saturday` | 星期六 |
| 6 | `holiday` | 星期日／假日 |

## 原生活动效果

`trigger_native_event.event` 接受 `native_event_catalog`／`catalog.nativeEvents` 返回的对象、key 或数值 ID。这些属于原生对话事件编号，与日常调度行动不同。`commandId` 为原生 UI 命令（事件 ID + 10）。接口直接应用成功的原生效果，跳过邀请／拒绝判定，无需预先开启聊天。请检查实时可用性；NPC 须在当前地图加载。

| 事件 ID | Key | 效果 | 命令 ID | 原生结果 | 附加条件 |
| --- | --- | --- | --- | --- | --- |
| 1 | `confess` | 告白 | 11 | `-1 / continue_talk` | 朋友或恋人 |
| 2 | `break_up` | 分手 | 12 | `8 / divorce` | 仅恋人 |
| 3 | `invite_h` | 邀请 H | 13 | `2 / h` | 朋友或恋人；满足原生 H 地点条件 |
| 4 | `eat_lunch` | 一起吃午餐 | 14 | `3 / lunch` | 朋友或恋人；午餐阶段 5；`isLunch=false` |
| 5 | `club_activity` | 一起参加社团活动 | 15 | `4 / club` | 仅部员；社团阶段 8 |
| 6 | `go_home_together` | 一起回家 | 16 | `5 / gohome` | 放学阶段 9 |
| 7 | `invite_date` | 邀请约会 | 17 | `-1 / continue_talk` | 朋友或恋人；没有其他角色的约会登记 |
| 8 | `study_together` | 一起学习 | 18 | `6 / study` | 自由阶段 5、8、9 |
| 9 | `exercise_together` | 一起运动 | 19 | `7 / exercise` | 自由阶段 5、8、9 |
| 10 | `invite_club` | 邀请入部 | 20 | `-1 / continue_talk` | 仅非部员 |
| 11 | `follow_player` | 跟随玩家 | 21 | `1 / chase` | 漫游或该 NPC 的原生对话已就绪 |

活动场景保留原生 ADV 及时间／状态效果；跟随使用原生玩家跟随绑定。告白／约会／入部直接应用状态效果，不打开或打断对话；原生结果 `-1 / continue_talk` 表示没有终结场景的结果，不是调度待机。约会登记会更新原生日程／存档身份；入部和告白会更新相应排行、玩家计数及报告。`set_stats` 仅修改标志，不会执行这些关联事件效果。`requirements` 说明关系／部员／阶段／场景条件；原生按钮拦截仍仅上报意图，外部控制方显式调用后才执行效果。

## 原生调度行动

`sample_native_action` 每次独立随机抽取其他原生女孩的当前任务，不返回来源角色信息。返回的 `task.actionInfo` 和 `mapInfo` 沿用本节行动和地图编号；`task` 可直接传给 `set_action`／`queue_action`。抽样不保证复用时可执行。详见 [随机参考其他原生 NPC 的当前任务](../API.zh-CN.md#随机参考其他原生-npc-的当前任务)。

`native_plan` 使用同一套引用观测已选行动及当前欲望权重候选；`claim` 的 `handoverReport.nativePlan` 直接返回接管前快照。原生 AI 每次选择一个行动，近期行动及优先选择记忆不是未来任务队列；候选顺序只作说明，不保证可执行或实际执行。详见 [原生 AI 规划观测](../API.zh-CN.md#原生-ai-规划观测)。

用于 `set_action.action`、`queue_action.action` 或 `set_desire.action`。行动 -1 表示插件空闲状态，不能作为可调度任务，请使用 `stop`。有效的行动 ID 仍可能因游戏条件、行动点占用或缺少原生目标而不可用。实时结果中的 `mapsWithPoints` 数组列出具有相应行动点层的地图，`mapDetails` 则提供各地图的说明。

| ID | Key | 中文说明 | 原生行动点层（资源名） |
| --- | --- | --- | --- |
| 0 | `change_clothes` | 换衣服 | Dress |
| 1 | `use_toilet` | 上厕所 | Toilet |
| 2 | `shower` | 淋浴 | Shower |
| 3 | `eat` | 吃饭 | Dinner |
| 4 | `solo_intimacy` | 独自亲密行为 | Masturbation |
| 5 | `seek_player_intimacy` | 寻求与玩家亲密互动 | WishH |
| 6 | `club_activity` | 社团活动 | Club |
| 7 | `talk_to_npc` | 与其他 NPC 交谈 | Talk |
| 8 | `talk_to_player` | 与玩家交谈 | Talk |
| 9 | `read` | 阅读 | Reading |
| 10 | `use_phone` | 使用手机 | Phone |
| 11 | `play_phone_game` | 玩手机游戏 | Game |
| 12 | `take_selfie` | 自拍 | Shooting |
| 13 | `listen_to_music` | 听音乐 | Music |
| 14 | `practice_dance` | 练习舞蹈 | Dance |
| 15 | `groom` | 整理仪容 | Appearance |
| 16 | `drink` | 喝饮料 | Drink |
| 17 | `take_break` | 休息 | ChangeMind |
| 18 | `exercise` | 锻炼 | Sport |
| 19 | `personality_activity` | 性格特有行动 | Personality |
| 20 | `flee` | 逃离 | Wait |
| 21 | `study` | 学习 | Study |
| 22 | `sleep` | 睡觉 | Sleep |
| 23 | `follow_player` | 跟随玩家 | Talk |
| 24 | `respond_to_call` | 回应玩家呼叫 | Talk |
| 25 | `shy_response` | 害羞回应 | Talk |
| 26 | `paired_intimacy` | 双人亲密行为 | Lesbian |
| 27 | `paired_intimacy_partner` | 双人亲密行为的配合方 | Lesbian |
| 28 | `await_talk_response` | 等待交谈回应 | ResponseTalk |
| 29 | `await_intimacy_response` | 等待亲密互动回应 | ResponseH |
| 30 | `hesitate` | 犹豫等待 | HesitantlyWait |

## 地图

在任务的 `map` 中使用地图引用。`automatic`（-1）表示让调度器自动选择。地图名称只标识地点，并不表示当前可用。选择任务目的地前，应检查实时结果中的 `canSchedule`；仅供事件使用的地图和私人房间可能不可用。

| ID | Key | 中文说明 |
| --- | --- | --- |
| -1 | `automatic` | 自动选择地图 |
| 0 | `map/0` | 玩家房间 |
| 2 | `map/2` | 校舍一楼 |
| 3 | `map/3` | 校舍二楼 |
| 4 | `map/4` | 校舍三楼 |
| 5 | `map/5` | 1-1 教室 |
| 8 | `map/8` | 2-1 教室 |
| 9 | `map/9` | 2-2 教室 |
| 11 | `map/11` | 3-1 教室 |
| 14 | `map/14` | 一楼女厕 |
| 15 | `map/15` | 二楼女厕 |
| 16 | `map/16` | 三楼女厕 |
| 17 | `map/17` | 教职员室 |
| 18 | `map/18` | 教职员男厕 |
| 20 | `map/20` | 医务室 |
| 21 | `map/21` | 图书馆 |
| 22 | `map/22` | 社团活动室 |
| 26 | `map/26` | 漫画社活动室 |
| 28 | `map/28` | 和室 |
| 31 | `map/31` | 体育仓库 |
| 32 | `map/32` | 体育馆 |
| 33 | `map/33` | 中庭 |
| 34 | `map/34` | 操场 |
| 36 | `map/36` | 屋顶 |
| 37 | `map/37` | 游泳池 |
| 38 | `map/38` | 食堂 |
| 45 | `map/45` | 淋浴室 |
| 46 | `map/46` | 更衣室 |
| 47 | `map/47` | 后院 |
| 50 | `map/50` | 体育馆演出 |
| 51 | `map/51` | 厕所事件场景 |
| 52 | `map/52` | 淋浴事件场景 |
| 53 | `map/53` | 更衣室事件场景 |
| 60 | `map/60` | 情侣酒店 |
| 61 | `map/61` | 卡拉 OK |
| 62 | `map/62` | 教堂 |
| 63 | `map/63` | 拥挤电车 |
| 64 | `map/64` | 电车 |
| 65 | `map/65` | 公园 |
| 66 | `map/66` | 教堂事件场景 |
| 70 | `map/70` | 器材仓库 |

## 表情模式

索引仅适用于当前角色模型及对应面部字段。嘴部引用不能用于眼睛。请使用 `set_expression.changes.eyes`、`.eyebrow` 或 `.mouth`。下表展示第一个网格准确的闭合／张开形变目标；实时目录项包含所有网格的目标。视觉强度还受开合上限、嘴部固定及眨眼影响。参数范围和限制见[表情控制与语义预设](../API.zh-CN.md#表情)。未知的自定义形变组合会被明确标注，不会被猜测为默认模型的某种表情。


### 眼睛（eyes）

| ID | Key | 中文说明 | 闭合目标 | 张开目标 |
| --- | --- | --- | --- | --- |
| 0 | `eyes/0` | 自然表情 | `eye_face.f00_def_cl` | `eye_face.f00_def_op` |
| 1 | `eyes/1` | 自然闭眼 | `eye_face.f00_def_cl` | `eye_face.f00_def_cl` |
| 2 | `eyes/2` | 微笑眼 | `eye_face.f00_egao_cl` | `eye_face.f00_egao_op` |
| 3 | `eyes/3` | 微笑闭眼 | `eye_face.f00_egao_cl` | `eye_face.f00_egao_cl` |
| 4 | `eyes/4` | 浅笑 | `eye_face.f00_egao_cl` | `eye_face.f00_bisyou_op` |
| 5 | `eyes/5` | 左眼眨眼 | `eye_face.f00_winkl_op` | `eye_face.f00_winkl_op` |
| 6 | `eyes/6` | 右眼眨眼 | `eye_face.f00_winkr_op` | `eye_face.f00_winkr_op` |
| 7 | `eyes/7` | 惆怅 | `eye_face.f00_def_cl` | `eye_face.f00_setunai_op` |
| 8 | `eyes/8` | 害羞 | `eye_face.f00_def_cl` | `eye_face.f00_tere_op` |
| 9 | `eyes/9` | 生气 | `eye_face.f00_def_cl` | `eye_face.f00_ikari_op` |
| 10 | `eyes/10` | 认真 | `eye_face.f00_def_cl` | `eye_face.f00_sinken_op` |
| 11 | `eyes/11` | 无聊 | `eye_face.f00_def_cl` | `eye_face.f00_tumara_op` |
| 12 | `eyes/12` | 痛苦 | `eye_face.f00_def_cl` | `eye_face.f00_kurusi_op` |
| 13 | `eyes/13` | 厌恶 | `eye_face.f00_def_cl` | `eye_face.f00_keno_op` |
| 14 | `eyes/14` | 思考 | `eye_face.f00_def_cl` | `eye_face.f00_sian_op` |
| 15 | `eyes/15` | 悲伤 | `eye_face.f00_def_cl` | `eye_face.f00_kanasi_op` |
| 16 | `eyes/16` | 哭泣 | `eye_face.f00_def_cl` | `eye_face.f00_naki_op` |
| 17 | `eyes/17` | 焦急 | `eye_face.f00_def_cl` | `eye_face.f00_aseri_op` |
| 18 | `eyes/18` | 沮丧 | `eye_face.f00_def_cl` | `eye_face.f00_rakutan_op` |
| 19 | `eyes/19` | 困扰 | `eye_face.f00_def_cl` | `eye_face.f00_komaru_op` |
| 20 | `eyes/20` | 得意 | `eye_face.f00_def_cl` | `eye_face.f00_doya_op` |
| 21 | `eyes/21` | 搞怪眼 | `eye_face.f00_gag` | `eye_face.f00_gag` |
| 22 | `eyes/22` | 左眼用力眯起 | `eye_face.f00_gyul_op` | `eye_face.f00_gyul_op` |
| 23 | `eyes/23` | 右眼用力眯起 | `eye_face.f00_gyur_op` | `eye_face.f00_gyur_op` |
| 24 | `eyes/24` | 双眼紧闭 | `eye_face.f00_gyu_cl` | `eye_face.f00_gyu_cl` |
| 25 | `eyes/25` | 左眼用力眯起（变体 2） | `eye_face.f00_gyul02_op` | `eye_face.f00_gyul02_op` |
| 26 | `eyes/26` | 右眼用力眯起（变体 2） | `eye_face.f00_gyur02_op` | `eye_face.f00_gyur02_op` |
| 27 | `eyes/27` | 双眼紧闭（变体 2） | `eye_face.f00_gyu02_cl` | `eye_face.f00_gyu02_cl` |

### 眉毛（eyebrow）

| ID | Key | 中文说明 | 闭合目标 | 张开目标 |
| --- | --- | --- | --- | --- |
| 0 | `eyebrow/0` | 自然眉 | `mayuge.mayu00_def_cl` | `mayuge.mayu00_def_op` |
| 1 | `eyebrow/1` | 生气眉 | `mayuge.mayu00_oko_cl` | `mayuge.mayu00_oko_op` |
| 2 | `eyebrow/2` | 困扰眉 | `mayuge.mayu00_koma_cl` | `mayuge.mayu00_koma_op` |
| 3 | `eyebrow/3` | 无聊眉 | `mayuge.mayu00_tmara_cl` | `mayuge.mayu00_tmara_op` |
| 4 | `eyebrow/4` | 左侧疑问眉 | `mayuge.mayu00_gimoL_cl` | `mayuge.mayu00_gimoL_op` |
| 5 | `eyebrow/5` | 右侧疑问眉 | `mayuge.mayu00_gimoR_cl` | `mayuge.mayu00_gimoR_op` |
| 6 | `eyebrow/6` | 左侧思考眉 | `mayuge.mayu00_sianL_cl` | `mayuge.mayu00_sianL_op` |
| 7 | `eyebrow/7` | 右侧思考眉 | `mayuge.mayu00_sianR_cl` | `mayuge.mayu00_sianR_op` |
| 8 | `eyebrow/8` | 左侧生气眉 | `mayuge.mayu00_oko2L_cl` | `mayuge.mayu00_oko2L_op` |
| 9 | `eyebrow/9` | 右侧生气眉 | `mayuge.mayu00_oko2R_cl` | `mayuge.mayu00_oko2R_op` |
| 10 | `eyebrow/10` | 认真眉 | `mayuge.mayu00_sinken_cl` | `mayuge.mayu00_sinken_op` |
| 11 | `eyebrow/11` | 担忧眉 | `mayuge.mayu00_huan_cl` | `mayuge.mayu00_huan_op` |
| 12 | `eyebrow/12` | 惊讶眉 | `mayuge.mayu00_odoro_cl` | `mayuge.mayu00_odoro_op` |
| 13 | `eyebrow/13` | 沮丧眉 | `mayuge.mayu00_rakutan_cl` | `mayuge.mayu00_rakutan_op` |
| 14 | `eyebrow/14` | 得意眉 | `mayuge.mayu00_doya_cl` | `mayuge.mayu00_doya_op` |
| 15 | `eyebrow/15` | 配合左眼眨眼的眉形 | `mayuge.mayu00_winkl_cl` | `mayuge.mayu00_winkl_op` |
| 16 | `eyebrow/16` | 配合右眼眨眼的眉形 | `mayuge.mayu00_winkr_cl` | `mayuge.mayu00_winkr_op` |

### 嘴部（mouth）

| ID | Key | 中文说明 | 闭合目标 | 张开目标 |
| --- | --- | --- | --- | --- |
| 0 | `mouth/0` | 自然嘴形 | `kuti_face.f00_def_cl` | `kuti_face.f00_def_op` |
| 1 | `mouth/1` | 微笑 | `kuti_face.f00_bisyou_op` | `kuti_face.f00_egao_op` |
| 2 | `mouth/2` | 喜悦微笑 | `kuti_face.f00_uresi_cl` | `kuti_face.f00_uresi_op` |
| 3 | `mouth/3` | 小幅喜悦微笑 | `kuti_face.f00_bisyou_op` | `kuti_face.f00_uresi_s_op` |
| 4 | `mouth/4` | 极小幅喜悦微笑 | `kuti_face.f00_bisyou_op` | `kuti_face.f00_uresi_ss_op` |
| 5 | `mouth/5` | 心动嘴形 | `kuti_face.f00_doki_cl` | `kuti_face.f00_doki_s_op` |
| 6 | `mouth/6` | 小幅心动嘴形 | `kuti_face.f00_doki_s_cl` | `kuti_face.f00_doki_s_op` |
| 7 | `mouth/7` | 极小幅心动嘴形 | `kuti_face.f00_doki_ss_cl` | `kuti_face.f00_doki_ss_op` |
| 8 | `mouth/8` | 生气嘴形 | `kuti_face.f00_ikari_cl` | `kuti_face.f00_ikari_op` |
| 9 | `mouth/9` | 生气嘴形（变体 2） | `kuti_face.f00_ikari_cl` | `kuti_face.f00_ikari02_op` |
| 10 | `mouth/10` | 认真收窄嘴形 | `kuti_face.f00_doki_ss_cl` | `kuti_face.f00_sinken02_op` |
| 11 | `mouth/11` | 认真抿嘴 | `kuti_face.f00_sinken03_cl` | `kuti_face.f00_sinken03_op` |
| 12 | `mouth/12` | 厌恶嘴形 | `kuti_face.f00_sinken03_cl` | `kuti_face.f00_keno_op` |
| 13 | `mouth/13` | 寂寞嘴形 | `kuti_face.f00_doki_ss_cl` | `kuti_face.f00_sabisi_op` |
| 14 | `mouth/14` | 焦急嘴形 | `kuti_face.f00_sinken03_cl` | `kuti_face.f00_aseri_op` |
| 15 | `mouth/15` | 不满张嘴 | `kuti_face.f00_human_cl` | `kuti_face.f00_o_l_op` |
| 16 | `mouth/16` | 无奈嘴形 | `kuti_face.f00_doki_ss_cl` | `kuti_face.f00_akire_op` |
| 17 | `mouth/17` | 惊讶张嘴 | `kuti_face.f00_def_cl` | `kuti_face.f00_odoro_op` |
| 18 | `mouth/18` | 小幅惊讶嘴形 | `kuti_face.f00_def_cl` | `kuti_face.f00_odoro_s_op` |
| 19 | `mouth/19` | 得意微笑 | `kuti_face.f00_doya_cl` | `kuti_face.f00_doya_op` |
| 20 | `mouth/20` | 伸舌 | `kuti_face.f00_pero_cl` | `kuti_face.f00_pero_cl` |
| 21 | `mouth/21` | 舔舐嘴形 | `kuti_face.f00_def_cl` | `kuti_face.f00_name_op` |
| 22 | `mouth/22` | 圆拢嘴形 | `kuti_face.f00_def_cl` | `kuti_face.f00_kuwae_op` |
| 23 | `mouth/23` | 噘嘴 | `kuti_face.f00_kisu_op` | `kuti_face.f00_kisu_op` |
| 24 | `mouth/24` | 舔舐嘴形（变体 2） | `kuti_face.f00_name02_op` | `kuti_face.f00_name02_op` |
| 25 | `mouth/25` | 元音 A（小幅） | `kuti_face.f00_n_s_cl` | `kuti_face.f00_a_s_op` |
| 26 | `mouth/26` | 元音 A（大幅） | `kuti_face.f00_n_l_cl` | `kuti_face.f00_a_l_op` |
| 27 | `mouth/27` | 元音 I（小幅） | `kuti_face.f00_n_s_cl` | `kuti_face.f00_i_s_op` |
| 28 | `mouth/28` | 元音 I（大幅） | `kuti_face.f00_n_l_cl` | `kuti_face.f00_i_l_op` |
| 29 | `mouth/29` | 元音 U（小幅） | `kuti_face.f00_n_s_cl` | `kuti_face.f00_u_s_op` |
| 30 | `mouth/30` | 元音 U（大幅） | `kuti_face.f00_n_l_cl` | `kuti_face.f00_u_l_op` |
| 31 | `mouth/31` | 元音 E（小幅） | `kuti_face.f00_n_s_cl` | `kuti_face.f00_e_s_op` |
| 32 | `mouth/32` | 元音 E（大幅） | `kuti_face.f00_n_l_cl` | `kuti_face.f00_e_l_op` |
| 33 | `mouth/33` | 元音 O（小幅） | `kuti_face.f00_n_s_cl` | `kuti_face.f00_o_s_op` |
| 34 | `mouth/34` | 元音 O（大幅） | `kuti_face.f00_n_l_cl` | `kuti_face.f00_o_l_op` |
| 35 | `mouth/35` | 闭口辅音嘴形（小幅） | `kuti_face.f00_n_s_cl` | `kuti_face.f00_n_s_cl` |
| 36 | `mouth/36` | 闭口辅音嘴形（大幅） | `kuti_face.f00_n_l_cl` | `kuti_face.f00_n_l_cl` |
| 37 | `mouth/37` | 猫嘴 | `kuti_face.f00_neko_cl` | `kuti_face.f00_neko_op` |
| 38 | `mouth/38` | 三角嘴 | `kuti_face.f00_san_op` | `kuti_face.f00_san_op` |
| 39 | `mouth/39` | 灿烂微笑 | `kuti_face.f00_niko_cl` | `kuti_face.f00_niko_op` |
| 40 | `mouth/40` | 进食嘴形 | `kuti_face.f00_mogu_op` | `kuti_face.f00_tabe_op` |
| 41 | `mouth/41` | 咀嚼嘴形 | `kuti_face.f00_mogu_cl` | `kuti_face.f00_mogu_op` |
| 42 | `mouth/42` | 不满闭嘴 | `kuti_face.f00_human_cl` | `kuti_face.f00_human_cl` |

### 表情控制参数

| Key | 类型／范围 | 含义 |
| --- | --- | --- |
| eyesOpen | 数值 0-1 | 眼睑张开上限；眨眼独立控制 |
| blink | 布尔值 | 启用原生自动眨眼 |
| eyebrowOpen | 数值 0-1 | 眉毛开合上限；实际效果取决于形变及眨眼 |
| mouthOpen | 数值 0-1 | 嘴部张开上限；不自动固定嘴巴 |
| mouthFixed | 布尔值 | 固定在 mouthOpen；false 恢复原生运动与口型同步 |
| blush | 数值 0-1 | 原生脸红强度；遵守游戏脸红设置 |
| highlight | 布尔值 | 显示／隐藏原生眼睛高光 |
| tears | 整数 0-3 | 无、轻、中、重三级原生泪水网格；不是连续量 |
| gaze | 引用 | 当前原生视线目录中的模式；不提供任意角度和坐标 |

### 原生视线模式

在 `set_expression.changes.gaze` 中传入返回条目、key 或 ID。模式属于当前控制器；不提供任意角度／目标。CONTROL 模式不可用。

| ID | Key | 原生类型 | 可用 | 含义 |
| --- | --- | --- | --- | --- |
| 0 | gaze/0 | FORWARD | True | 相对头部向前看 |
| 1 | gaze/1 | TARGET | True | 看向当前原生眼神目标，不选择新目标 |
| 2 | gaze/2 | CONTROL | False | 需要原生固定角度数据 |
| 3 | gaze/3 | AWAY | True | 从当前原生目标移开视线 |
| 4 | gaze/4 | FORWARD | True | 相对头部向前看 |
| 5 | gaze/5 | TARGET | True | 看向当前原生眼神目标，不选择新目标 |
| 6 | gaze/6 | CONTROL | False | 需要原生固定角度数据 |
| 7 | gaze/7 | AWAY | True | 从当前原生目标移开视线 |

### 语义表情预设

使用 `set_expression.changes.preset`。模式 ID 按实时形变标签解析；缺失／含糊的形变或不可用参数会拒绝预设。需检查实时可用性。

| Key | 含义 |
| --- | --- |
| neutral | 普通表情 |
| smile | 笑容 |
| shy | 害羞 |
| crying | 哭泣 |
| angry | 生气 |
| surprised | 惊讶 |

## Animator 状态

用于 `play_animation.state`。有符号 ID 是第 0 层的 `Animator.StringToHash(key)`，不是调度行动 ID。播放动画不会执行游戏任务、添加道具或进行导航。运行时会确认所列状态存在；这一发现列表不是完整的 Studio 动画库。这些示例使用漫游模型；原生对话使用另一套 Animator，进入或退出对话后请刷新 `animation_catalog`，并检查每项的 `context`。`animation.state` 和哈希报告实际播放状态。

| 有符号状态哈希 | 状态 key | 中文说明 |
| --- | --- | --- |
| -437354176 | `Appearance` | 整理仪容（Appearance） |
| 1827410204 | `Appearance2` | 整理仪容（Appearance2） |
| 468394378 | `Appearance3` | 整理仪容（Appearance3） |
| -225934145 | `Appearance5` | 整理仪容（Appearance5） |
| 1803670789 | `Appearance6` | 整理仪容（Appearance6） |
| 1838035980 | `ChangeMind` | 休息（ChangeMind） |
| 744289238 | `ChangeMind10` | 休息（ChangeMind10） |
| 1532740416 | `ChangeMind11` | 休息（ChangeMind11） |
| -1034775814 | `ChangeMind12` | 休息（ChangeMind12） |
| 319716370 | `ChangeMind2` | 休息（ChangeMind2） |
| 1678331012 | `ChangeMind3` | 休息（ChangeMind3） |
| -93464281 | `ChangeMind4` | 休息（ChangeMind4） |
| -1922373199 | `ChangeMind5` | 休息（ChangeMind5） |
| 342079499 | `ChangeMind6` | 休息（ChangeMind6） |
| 1667532957 | `ChangeMind7` | 休息（ChangeMind7） |
| -203713268 | `ChangeMind8` | 休息（ChangeMind8） |
| -2065914470 | `ChangeMind9` | 休息（ChangeMind9） |
| 541959475 | `Club_Cheer` | 啦啦队练习（Club_Cheer） |
| 1009566468 | `Club_Cheer1` | 啦啦队练习（Club_Cheer1） |
| -1524264258 | `Club_Cheer2` | 啦啦队练习（Club_Cheer2） |
| -769482200 | `Club_Cheer3` | 啦啦队练习（Club_Cheer3） |
| 1972151244 | `Club_Comic` | 漫画社活动（Club_Comic） |
| -2005746635 | `Club_Comic2` | 漫画社活动（Club_Comic2） |
| 1838661728 | `Club_Koikatu` | 恋活社活动（Club_Koikatu） |
| 1460291733 | `Club_Koikatu2` | 恋活社活动（Club_Koikatu2） |
| 537753603 | `Club_Koikatu3` | 恋活社活动（Club_Koikatu3） |
| -301256027 | `Club_Land` | 田径社练习（Club_Land） |
| -934837043 | `Club_Swim` | 游泳社练习（Club_Swim） |
| -3651708 | `Club_Swim2` | 游泳社练习（Club_Swim2） |
| -320875747 | `Club_Tea` | 茶道社活动（Club_Tea） |
| 2034175239 | `Club_Tea2` | 茶道社活动（Club_Tea2） |
| 238566801 | `Club_Tea3` | 茶道社活动（Club_Tea3） |
| -645213589 | `Dance` | 舞蹈练习（Dance） |
| -1066628081 | `Dance2` | 舞蹈练习（Dance2） |
| -1911024371 | `Dinner` | 吃饭（Dinner） |
| 1693112997 | `Dinner2` | 吃饭（Dinner2） |
| 334359091 | `Dinner3` | 吃饭（Dinner3） |
| -864063531 | `Drink` | 喝饮料（Drink） |
| -1683499089 | `Drink3` | 喝饮料（Drink3） |
| 96682508 | `Drink4` | 喝饮料（Drink4） |
| -2095472974 | `Game` | 玩手机游戏（Game） |
| 1639850708 | `Houshi2` | 亲密行为（Houshi2） |
| 381227586 | `Houshi3` | 亲密行为（Houshi3） |
| -1998747679 | `Houshi4` | 亲密行为（Houshi4） |
| -2443401 | `Houshi5` | 亲密行为（Houshi5） |
| 2081823275 | `Idle` | 站姿待机（Idle） |
| -1304433860 | `kotei_tachi` | 站姿待机（kotei_tachi） |
| -129426149 | `Lesbian` | 双人亲密行为（Lesbian） |
| -1874313476 | `Lesbian2` | 双人亲密行为（Lesbian2） |
| -414249366 | `Lesbian3` | 双人亲密行为（Lesbian3） |
| -2089788878 | `Locomotion` | 行走（Locomotion） |
| -1807062758 | `Locomotion 0` | 行走（Locomotion 0） |
| -1439380330 | `Locomotion_Anger` | 行走（Locomotion_Anger） |
| 1634715586 | `Masturbation` | 独自亲密行为（Masturbation） |
| 1876849238 | `Masturbation2` | 独自亲密行为（Masturbation2） |
| 416891584 | `Masturbation3` | 独自亲密行为（Masturbation3） |
| -2034378909 | `Masturbation4` | 独自亲密行为（Masturbation4） |
| 210963790 | `Music` | 听音乐（Music） |
| -1950531177 | `Music2` | 听音乐（Music2） |
| -54890239 | `Music3` | 听音乐（Music3） |
| -2054244135 | `Phone` | 使用手机（Phone） |
| -446735675 | `Phone2` | 使用手机（Phone2） |
| -1839699373 | `Phone3` | 使用手机（Phone3） |
| 205324272 | `Phone4` | 使用手机（Phone4） |
| 245876189 | `Reading` | 阅读（Reading） |
| -491145311 | `Reading2` | 阅读（Reading2） |
| -1782675657 | `Reading3` | 阅读（Reading3） |
| 198840980 | `Reading4` | 阅读（Reading4） |
| 1877016939 | `Shooting` | 自拍（Shooting） |
| 115405060 | `Shower` | 淋浴（Shower） |
| -822940248 | `Sleep` | 睡觉（Sleep） |
| 1528772224 | `Sleep1` | 睡觉（Sleep1） |
| -1038719174 | `Sleep2` | 睡觉（Sleep2） |
| -1257154644 | `Sleep3` | 睡觉（Sleep3） |
| 729137679 | `Sleep4` | 睡觉（Sleep4） |
| -616251178 | `Sport` | 锻炼（Sport） |
| 1975422252 | `Sport2` | 锻炼（Sport2） |
| 45727162 | `Sport3` | 锻炼（Sport3） |
| -1663227879 | `Sport4` | 锻炼（Sport4） |
| -338028401 | `Sport5` | 锻炼（Sport5） |
| 1926449461 | `Sport6` | 锻炼（Sport6） |
| 666810445 | `Study` | 学习（Study） |
| 666486598 | `suwari_Idle` | 坐姿待机 |
| -1624975941 | `talk` | 对话：说话 |
| -1951916837 | `talk_uke` | 对话：倾听 |
| 1975430100 | `Toilet` | 上厕所（Toilet） |

## 重新生成编号参考

加载角色后，保存最新的 `catalog` 返回结果，然后运行：

```powershell
python -X utf8 tools/export_reference.py --catalog catalog.json
```

此命令重新生成[英文编号参考](REFERENCE.md)。更新后请同步本中文参考中的对应表格，保持 ID、key、哈希和形变目标一致，仅翻译说明文字。

运行时请求模板包含当前故事的 generation。调用 API 时，应重新获取最新模板。
